using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaisseReseau.Web.Data;
using CaisseReseau.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CaisseReseau.Web.Services;

public sealed class CreateOperationInput
{
    public string? AgencyCode { get; set; }
    public string? Company { get; set; }
    public string? Channel { get; set; }
    public string? Direction { get; set; }
    public string? TransactionNature { get; set; }
    public string? TransactionDate { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? CustomerName { get; set; }
    public long Amount { get; set; }
    public string? Reference { get; set; }
    public string? BankName { get; set; }
    public string? DocumentNumber { get; set; }
    public string? MaturityDate { get; set; }
    public string? Notes { get; set; }
    public string? AuditCategory { get; set; }
    public string? ClientOperationId { get; set; }
}

public sealed class OpenSessionResult
{
    public CashSession Session { get; init; } = null!;
    public bool Handover { get; init; }
}

/// <summary>
/// Règles métier de la caisse. Reprend, en C# / LINQ, les fonctions SQL de l'application d'origine
/// (open_cash_session_v3, create_cash_operation_v3, close_company_cash_session_core, finalize_cash_day,
/// regularize_company_cash_closure, finalize_late_cash_day, mark_cash_operation_imputed,
/// record_ticket_print_v2, allocate_customer_deposit_v2).
/// </summary>
public sealed class CashService
{
    private const string CashWriteDenied = "Ce compte est réservé à la consultation et ne peut pas modifier une caisse.";

    private readonly CaisseDbContext _db;

    public CashService(CaisseDbContext db)
    {
        _db = db;
    }

    // ------------------------------------------------------------------ utilitaires

    private static string Clean(string? value, int max = 240)
    {
        var text = (value ?? "").Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static void AssertAgencyWrite(AccountInfo account, string agencyCode)
    {
        if (!account.HasAgencyWriteAccess(agencyCode)) throw new DomainException(CashWriteDenied, 403);
    }

    private static void AssertRegularization(AccountInfo account, string agencyCode)
    {
        if (!account.HasClosureRegularizationAccess(agencyCode))
            throw new DomainException("Droit de régularisation des clôtures requis.", 403);
    }

    private async Task<T> RetryAsync<T>(Func<Task<T>> work)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await work();
            }
            catch (DbUpdateException) when (attempt < 5)
            {
                // Conflit d'écriture simultané (numéro de ticket, ouverture, etc.) : on recommence proprement.
                _db.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                throw new DomainException("Plusieurs écritures simultanées ont empêché l’enregistrement. Réessayez dans un instant.", 409);
            }
        }
    }

    // ------------------------------------------------------------------ ouverture / relève

    public Task<OpenSessionResult> OpenSessionAsync(
        AccountInfo account, string agencyCodeInput, string cashierNameInput, IDictionary<string, long>? openingFunds)
    {
        var agencyCode = Clean(agencyCodeInput, 30).ToUpperInvariant();
        var cashierName = Clean(cashierNameInput, 100);
        if (agencyCode.Length == 0 || cashierName.Length < 2)
            throw new DomainException("Agence, date et nom du caissier sont obligatoires.");
        AssertAgencyWrite(account, agencyCode);

        return RetryAsync(async () =>
        {
            var now = MadagascarTime.Now;
            var today = MadagascarTime.Today;
            if (now.TimeOfDay < new TimeSpan(6, 0, 0))
                throw new DomainException("L’ouverture de la caisse est autorisée à partir de 06 h 00.");

            var agency = await _db.Agencies.FirstOrDefaultAsync(a => a.Code == agencyCode && a.Active);
            if (agency == null) throw new DomainException("Agence inconnue ou inactive.");

            await using var tx = await _db.Database.BeginTransactionAsync();

            var existing = await _db.CashSessions
                .Include(s => s.CompanyBalances)
                .FirstOrDefaultAsync(s => s.AgencyCode == agencyCode && s.BusinessDate == today);

            if (existing != null && existing.Status == "CLOSED")
                throw new DomainException("Cette caisse est définitivement clôturée. La prochaine ouverture sera possible demain à partir de 06 h 00.");

            if (existing == null)
            {
                var previous = await _db.CashSessions
                    .Where(s => s.AgencyCode == agencyCode && s.BusinessDate < today)
                    .OrderByDescending(s => s.BusinessDate)
                    .FirstOrDefaultAsync();
                if (previous != null && previous.Status != "CLOSED")
                    throw new DomainException("La journée précédente doit être fermée avant une nouvelle ouverture.");
            }

            var handover = existing != null;
            CashSession session;
            if (existing == null)
            {
                session = new CashSession
                {
                    AgencyCode = agencyCode,
                    BusinessDate = today,
                    CashierName = cashierName,
                    OpeningBalance = 0,
                    StartedBy = account.Email,
                    StartedAt = now,
                    UpdatedBy = account.Email,
                    UpdatedAt = now,
                    Status = "OPEN",
                };
                _db.CashSessions.Add(session);
                await _db.SaveChangesAsync();
            }
            else
            {
                session = existing;
                session.CashierName = cashierName;
                session.UpdatedBy = account.Email;
                session.UpdatedAt = now;
            }

            var balances = session.CompanyBalances.ToList();
            if (balances.Count == 0)
            {
                var companies = await _db.Companies
                    .Where(c => c.Active)
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
                    .ToListAsync();

                foreach (var company in companies)
                {
                    long fund = 0;
                    if (openingFunds != null && openingFunds.TryGetValue(company.Code, out var requested))
                    {
                        if (requested < 0)
                            throw new DomainException("Un fond de caisse doit être un montant entier positif ou nul.");
                        fund = requested;
                    }

                    var last = await _db.CompanyDailyClosures
                        .Where(c => c.AgencyCode == agencyCode && c.Company == company.Code && c.ClosureDate < today)
                        .OrderByDescending(c => c.ClosureDate).ThenByDescending(c => c.SubmittedAt)
                        .FirstOrDefaultAsync();

                    var balance = new CashSessionCompanyBalance
                    {
                        CashSessionId = session.Id,
                        AgencyCode = agencyCode,
                        BusinessDate = today,
                        Company = company.Code,
                        CarriedBalance = last?.CashCounted ?? 0,
                        CashFund = fund,
                        SourceClosureDate = last?.ClosureDate,
                        CreatedAt = now,
                    };
                    _db.CashSessionCompanyBalances.Add(balance);
                    balances.Add(balance);
                }
            }

            session.OpeningBalance = balances.Sum(b => b.CarriedBalance + b.CashFund);
            session.SourceClosureDate = balances.Max(b => b.SourceClosureDate);
            session.UpdatedBy = account.Email;
            session.UpdatedAt = now;

            _db.CashierShifts.Add(new CashierShift
            {
                CashSessionId = session.Id,
                AgencyCode = agencyCode,
                BusinessDate = today,
                CashierName = cashierName,
                Action = handover ? "RELEVE" : "OUVERTURE",
                RecordedBy = account.Email,
                RecordedAt = now,
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return new OpenSessionResult { Session = session, Handover = handover };
        });
    }

    // ------------------------------------------------------------------ saisie d'une opération + premier ticket

    private async Task<string> NextTicketNumberAsync(string agencyCode, Company company, int year, DateTimeOffset now)
    {
        var sequence = await _db.TicketSequences
            .FirstOrDefaultAsync(s => s.AgencyCode == agencyCode && s.Company == company.Code && s.CalendarYear == year);
        if (sequence == null)
        {
            sequence = new TicketSequence { AgencyCode = agencyCode, Company = company.Code, CalendarYear = year, LastNumber = 0 };
            _db.TicketSequences.Add(sequence);
        }
        sequence.LastNumber += 1;
        sequence.UpdatedAt = now;
        return $"{company.TicketPrefix}-{agencyCode}-{year}-{sequence.LastNumber:D6}";
    }

    public async Task<CashOperation> CreateOperationAsync(AccountInfo account, CreateOperationInput input)
    {
        var agencyCode = Clean(input.AgencyCode, 30).ToUpperInvariant();
        var company = Clean(input.Company, 40).ToUpperInvariant();
        var channel = Clean(input.Channel, 40).ToUpperInvariant();
        var direction = Clean(input.Direction, 10).ToUpperInvariant();
        var nature = Clean(input.TransactionNature, 40).ToUpperInvariant();
        var invoice = Clean(input.InvoiceNumber, 120).ToUpperInvariant();
        var customer = Clean(input.CustomerName, 160).ToUpperInvariant();
        var reference = Clean(input.Reference, 160);
        var bank = Clean(input.BankName, 80).ToUpperInvariant();
        var document = Clean(input.DocumentNumber, 80).ToUpperInvariant();
        var notes = Clean(input.Notes, 400);
        var clientOperationId = Clean(input.ClientOperationId, 100);

        DateOnly date;
        if (agencyCode.Length == 0 || customer.Length == 0 || !TryDate(input.TransactionDate, out date))
            throw new DomainException("Agence, date et client sont obligatoires.");
        AssertAgencyWrite(account, agencyCode);

        if (!Domain.Companies.Any(c => c.Code == company)
            || !Domain.Channels.Any(c => c.Value == channel)
            || (direction != "ENTREE" && direction != "SORTIE"))
            throw new DomainException("Société, mode, nature ou sens de l’opération invalide.");

        var isDocument = channel == "CHEQUE" || channel == "TRAITE";
        if (isDocument && direction != "ENTREE")
            throw new DomainException("Un chèque ou une traite se saisit en encaissement. Pour un retour de chèque, transmettez le justificatif au superviseur.");

        var natureValid = direction == "ENTREE"
            ? Domain.EntryNatures.Contains(nature)
            : nature == "VERSEMENT_DEGAGEMENT";
        if (!natureValid) throw new DomainException("Nature de l’opération invalide.");
        if (input.Amount <= 0) throw new DomainException("Le montant doit être un nombre entier positif en ariary.");
        if (direction == "ENTREE" && nature != "ACOMPTE" && invoice.Length == 0)
            throw new DomainException("La référence facture est obligatoire pour une entrée.");
        if (channel != "ESPECE" && reference.Length == 0)
            throw new DomainException("La référence de paiement est obligatoire.");

        DateOnly? maturity = null;
        if (isDocument)
        {
            DateOnly parsedMaturity;
            var maturityOk = TryDate(input.MaturityDate, out parsedMaturity);
            if (document.Length == 0 || !maturityOk)
                throw new DomainException("Le document et son échéance sont obligatoires.");
            maturity = parsedMaturity;
        }

        if (date != MadagascarTime.Today)
            throw new DomainException("Les nouvelles opérations doivent être saisies dans la journée de caisse ouverte.");

        var isFr = direction == "SORTIE" && (input.AuditCategory == "FR" || Domain.IsFrInvoice(invoice));
        if (isFr && !Regex.IsMatch(notes, @"^\[FR\](?:\s|$)", RegexOptions.IgnoreCase))
            notes = Clean($"[FR] {notes}", 400);

        if (clientOperationId.Length > 0)
        {
            var already = await _db.CashOperations.FirstOrDefaultAsync(o => o.ClientOperationId == clientOperationId);
            if (already != null)
            {
                if (already.AgencyCode == agencyCode && already.Company == company && already.Amount == input.Amount)
                    return already;
                throw new DomainException("Identifiant déjà utilisé pour une autre opération : régularisation nécessaire.");
            }
        }

        return await RetryAsync(async () =>
        {
            var now = MadagascarTime.Now;
            await using var tx = await _db.Database.BeginTransactionAsync();

            var session = await _db.CashSessions.FirstOrDefaultAsync(s => s.AgencyCode == agencyCode && s.BusinessDate == date);
            if (session == null) throw new DomainException("Ouvrez d’abord la caisse et renseignez le caissier en poste.");
            if (session.Status != "OPEN") throw new DomainException("La caisse est clôturée : régularisation par un superviseur nécessaire.");

            var companyRow = await _db.Companies.FirstOrDefaultAsync(c => c.Code == company && c.Active);
            if (companyRow == null) throw new DomainException("Société invalide.");

            var agency = await _db.Agencies.FirstOrDefaultAsync(a => a.Code == agencyCode && a.Active);
            if (agency == null) throw new DomainException("Agence inconnue ou inactive.");

            var ticket = await NextTicketNumberAsync(agencyCode, companyRow, date.Year, now);

            var operation = new CashOperation
            {
                CashSessionId = session.Id,
                AgencyCode = agencyCode,
                AgencyName = agency.Name,
                Company = company,
                Channel = channel,
                Direction = direction,
                TransactionNature = nature,
                TransactionDate = date,
                TicketNumber = ticket,
                InvoiceNumber = invoice.Length == 0 ? null : invoice,
                CustomerName = customer,
                Amount = input.Amount,
                Reference = reference.Length == 0 ? null : reference,
                BankName = bank.Length == 0 ? null : bank,
                DocumentNumber = document.Length == 0 ? null : document,
                MaturityDate = maturity,
                SageStatus = Domain.ToImpute,
                Notes = notes.Length == 0 ? null : notes,
                CashierName = session.CashierName,
                ClientOperationId = clientOperationId.Length == 0 ? null : clientOperationId,
                CreatedBy = account.Email,
                CreatedAt = now,
                // Le premier exemplaire est imprimé immédiatement : l'impression est enregistrée avec l'opération.
                PrintCount = 1,
                LastPrintedAt = now,
                LastPrintedBy = account.Email,
            };
            _db.CashOperations.Add(operation);
            _db.TicketPrintEvents.Add(new TicketPrintEvent
            {
                Operation = operation,
                ClientPrintId = "print-" + Guid.NewGuid().ToString("D"),
                PrintedBy = account.Email,
                PrintedAt = now,
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return operation;
        });
    }

    // ------------------------------------------------------------------ impression, imputation

    public async Task<CashOperation> RecordPrintAsync(AccountInfo account, long operationId, string clientPrintId)
    {
        clientPrintId = Clean(clientPrintId, 100);
        if (clientPrintId.Length < 16) throw new DomainException("Identifiant d’impression invalide.");

        var repeated = await _db.TicketPrintEvents
            .Where(e => e.ClientPrintId == clientPrintId)
            .Select(e => e.Operation!)
            .FirstOrDefaultAsync();
        if (repeated != null)
        {
            AssertAgencyWrite(account, repeated.AgencyCode);
            return repeated;
        }

        var operation = await _db.CashOperations.FirstOrDefaultAsync(o => o.Id == operationId);
        if (operation == null) throw new DomainException("Ticket introuvable.", 404);
        AssertAgencyWrite(account, operation.AgencyCode);

        var now = MadagascarTime.Now;
        _db.TicketPrintEvents.Add(new TicketPrintEvent
        {
            OperationId = operation.Id,
            ClientPrintId = clientPrintId,
            PrintedBy = account.Email,
            PrintedAt = now,
        });
        operation.PrintCount += 1;
        operation.LastPrintedAt = now;
        operation.LastPrintedBy = account.Email;
        await _db.SaveChangesAsync();
        return operation;
    }

    public async Task<CashOperation> MarkImputedAsync(AccountInfo account, long operationId)
    {
        var operation = await _db.CashOperations.FirstOrDefaultAsync(o => o.Id == operationId);
        if (operation == null) throw new DomainException("Opération introuvable.", 404);
        AssertAgencyWrite(account, operation.AgencyCode);

        if (operation.SageStatus == Domain.ToImpute)
        {
            operation.SageStatus = Domain.Imputed;
            operation.SageStatusUpdatedAt = MadagascarTime.Now;
            operation.SageStatusUpdatedBy = account.Email;
            await _db.SaveChangesAsync();
        }
        return operation;
    }

    // ------------------------------------------------------------------ affectation d'un acompte

    public Task<(CashOperation Source, CashOperation TransferOut, CashOperation TransferIn)> AllocateDepositAsync(
        AccountInfo account, long sourceOperationId, string targetCompanyInput, string invoiceInput, long amount)
    {
        var targetCompany = Clean(targetCompanyInput, 40).ToUpperInvariant();
        var invoice = Clean(invoiceInput, 120).ToUpperInvariant();
        if (targetCompany.Length == 0 || invoice.Length == 0)
            throw new DomainException("Acompte, société cible et facture sont obligatoires.");
        if (amount <= 0) throw new DomainException("Le montant à affecter doit être positif.");

        return RetryAsync(async () =>
        {
            var now = MadagascarTime.Now;
            var today = MadagascarTime.Today;
            await using var tx = await _db.Database.BeginTransactionAsync();

            var source = await _db.CashOperations.FirstOrDefaultAsync(o => o.Id == sourceOperationId);
            if (source == null) throw new DomainException("Acompte introuvable.", 404);
            AssertAgencyWrite(account, source.AgencyCode);
            if (source.Direction != "ENTREE" || source.TransactionNature != "ACOMPTE")
                throw new DomainException("Cette opération n’est pas un acompte client.");

            var targetRow = await _db.Companies.FirstOrDefaultAsync(c => c.Code == targetCompany && c.Active);
            if (targetRow == null) throw new DomainException("Société destinataire invalide.");
            var sourceRow = await _db.Companies.FirstOrDefaultAsync(c => c.Code == source.Company && c.Active);
            if (sourceRow == null) throw new DomainException("Société invalide.");

            var remaining = source.Amount - source.AllocatedAmount;
            if (amount > remaining) throw new DomainException("Le montant dépasse le solde disponible de l’acompte.");

            var session = await _db.CashSessions.FirstOrDefaultAsync(s => s.AgencyCode == source.AgencyCode && s.BusinessDate == today);
            if (session == null) throw new DomainException("Ouvrez la caisse du jour avant d’affecter cet acompte.");
            if (session.Status != "OPEN")
                throw new DomainException("La caisse est clôturée : aucun transfert d’acompte ne peut être ajouté.");

            var allocationId = "allocation-" + Guid.NewGuid().ToString("D");
            var outTicket = await NextTicketNumberAsync(source.AgencyCode, sourceRow, today.Year, now);
            var inTicket = await NextTicketNumberAsync(source.AgencyCode, targetRow, today.Year, now);

            var transferOut = new CashOperation
            {
                CashSessionId = session.Id,
                AgencyCode = source.AgencyCode,
                AgencyName = source.AgencyName,
                Company = source.Company,
                Channel = source.Channel,
                Direction = "SORTIE",
                TransactionNature = "TRANSFERT_ACOMPTE",
                TransactionDate = today,
                TicketNumber = outTicket,
                InvoiceNumber = null,
                CustomerName = source.CustomerName,
                Amount = amount,
                Reference = $"TRANSFERT ACOMPTE {source.TicketNumber} VERS {targetCompany}",
                BankName = source.BankName,
                DocumentNumber = source.DocumentNumber,
                MaturityDate = source.MaturityDate,
                SageStatus = Domain.ToImpute,
                Notes = "Décaissement interne généré automatiquement lors de l’affectation d’un acompte.",
                CashierName = session.CashierName,
                SourceDepositId = source.Id,
                ClientOperationId = allocationId + "-OUT",
                CreatedBy = account.Email,
                CreatedAt = now,
            };
            var transferIn = new CashOperation
            {
                CashSessionId = session.Id,
                AgencyCode = source.AgencyCode,
                AgencyName = source.AgencyName,
                Company = targetCompany,
                Channel = source.Channel,
                Direction = "ENTREE",
                TransactionNature = "REGLEMENT",
                TransactionDate = today,
                TicketNumber = inTicket,
                InvoiceNumber = invoice,
                CustomerName = source.CustomerName,
                Amount = amount,
                Reference = $"ACOMPTE {source.TicketNumber} / {invoice}",
                BankName = source.BankName,
                DocumentNumber = source.DocumentNumber,
                MaturityDate = source.MaturityDate,
                SageStatus = Domain.ToImpute,
                Notes = "Encaissement généré automatiquement depuis un acompte client.",
                CashierName = session.CashierName,
                SourceDepositId = source.Id,
                ClientOperationId = allocationId + "-IN",
                CreatedBy = account.Email,
                CreatedAt = now,
            };
            _db.CashOperations.Add(transferOut);
            _db.CashOperations.Add(transferIn);
            source.AllocatedAmount += amount;

            _db.DepositAllocations.Add(new DepositAllocation
            {
                SourceOperation = source,
                TransferOutOperation = transferOut,
                TransferInOperation = transferIn,
                AgencyCode = source.AgencyCode,
                SourceCompany = source.Company,
                TargetCompany = targetCompany,
                AllocatedAmount = amount,
                InvoiceNumber = invoice,
                AllocationDate = today,
                CreatedBy = account.Email,
                CreatedAt = now,
                ClientAllocationId = allocationId,
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return (source, transferOut, transferIn);
        });
    }

    // ------------------------------------------------------------------ clôtures par société

    private static string BreakdownJson(IDictionary<string, long>? breakdown)
    {
        var clean = new SortedDictionary<string, long>(StringComparer.Ordinal);
        if (breakdown != null)
        {
            foreach (var pair in breakdown)
            {
                if (pair.Value < 0) throw new DomainException("Le détail de comptage est invalide.");
                if (pair.Value > 0) clean[pair.Key] = pair.Value;
            }
        }
        return JsonSerializer.Serialize(clean);
    }

    /// <summary>Espèces entrantes moins sortantes d'une société pour une journée (toutes opérations de la journée).</summary>
    private async Task<long> CashMovementAsync(string agencyCode, string company, DateOnly date)
    {
        var rows = await _db.CashOperations
            .Where(o => o.AgencyCode == agencyCode && o.Company == company && o.TransactionDate == date && o.Channel == "ESPECE")
            .Select(o => new { o.Direction, o.Amount })
            .ToListAsync();
        return rows.Sum(o => o.Direction == "ENTREE" ? o.Amount : -o.Amount);
    }

    public Task<CompanyDailyClosure> CloseCompanyAsync(
        AccountInfo account, string agencyCodeInput, string companyInput, long cashCounted,
        IDictionary<string, long>? breakdown, string? commentInput)
    {
        var agencyCode = Clean(agencyCodeInput, 30).ToUpperInvariant();
        var company = Clean(companyInput, 40).ToUpperInvariant();
        if (agencyCode.Length == 0 || company.Length == 0)
            throw new DomainException("Agence, société et date de clôture obligatoires.");
        AssertAgencyWrite(account, agencyCode);
        if (cashCounted < 0) throw new DomainException("Le montant compté est invalide.");
        var comment = Clean(commentInput, 400);
        var breakdownJson = BreakdownJson(breakdown);

        return RetryAsync(async () =>
        {
            var now = MadagascarTime.Now;
            var date = MadagascarTime.Today;
            await using var tx = await _db.Database.BeginTransactionAsync();

            if (!await _db.Companies.AnyAsync(c => c.Code == company && c.Active))
                throw new DomainException("Société invalide.");

            var session = await _db.CashSessions.FirstOrDefaultAsync(s => s.AgencyCode == agencyCode && s.BusinessDate == date);
            if (session == null) throw new DomainException("La caisse doit être ouverte avant sa clôture.");
            if (session.Status != "OPEN") throw new DomainException("La caisse est déjà définitivement clôturée.");
            if (!MadagascarTime.IsRegularClosureWindow(date, now))
                throw new DomainException("La clôture normale est autorisée uniquement de 11 h 00 à 18 h 30, heure de Madagascar.");

            var balance = await _db.CashSessionCompanyBalances
                .FirstOrDefaultAsync(b => b.CashSessionId == session.Id && b.Company == company);
            if (balance == null) throw new DomainException("Les soldes par société doivent être initialisés à l’ouverture.");

            if (await _db.CompanyDailyClosures.AnyAsync(c => c.AgencyCode == agencyCode && c.Company == company && c.ClosureDate == date))
                throw new DomainException("Cette société est déjà clôturée et ne peut plus être modifiée.");

            var agency = await _db.Agencies.FirstAsync(a => a.Code == agencyCode);
            var movement = await CashMovementAsync(agencyCode, company, date);
            var opening = balance.CarriedBalance + balance.CashFund;
            var theoretical = opening + movement;
            var variance = cashCounted - theoretical;
            if (variance != 0 && comment.Length == 0)
                throw new DomainException("Un commentaire est obligatoire en cas d’écart.");

            var closure = new CompanyDailyClosure
            {
                AgencyCode = agencyCode,
                AgencyName = agency.Name,
                Company = company,
                ClosureDate = date,
                OpeningCash = opening,
                CashFund = balance.CashFund,
                CashTheoretical = theoretical,
                CashCounted = cashCounted,
                Variance = variance,
                Breakdown = breakdownJson,
                Comment = comment.Length == 0 ? null : comment,
                CashierName = session.CashierName,
                Status = "Transmis",
                SubmittedBy = account.Email,
                SubmittedAt = now,
            };
            _db.CompanyDailyClosures.Add(closure);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return closure;
        });
    }

    /// <summary>Noms des sociétés actives de la journée qui n'ont pas encore de clôture.</summary>
    private async Task<string> MissingClosuresAsync(CashSession session)
    {
        var companies = await _db.Companies.Where(c => c.Active).OrderBy(c => c.SortOrder).ToListAsync();
        var balances = await _db.CashSessionCompanyBalances.Where(b => b.CashSessionId == session.Id).ToListAsync();
        var withOperations = await _db.CashOperations
            .Where(o => o.CashSessionId == session.Id)
            .Select(o => o.Company).Distinct().ToListAsync();
        var closed = await _db.CompanyDailyClosures
            .Where(c => c.AgencyCode == session.AgencyCode && c.ClosureDate == session.BusinessDate)
            .Select(c => c.Company).ToListAsync();

        var missing = companies
            .Where(c => balances.Any(b => b.Company == c.Code && b.CarriedBalance + b.CashFund > 0) || withOperations.Contains(c.Code))
            .Where(c => !closed.Contains(c.Code))
            .Select(c => c.DisplayName);
        return string.Join(", ", missing);
    }

    public Task<CashSession> FinalizeDayAsync(AccountInfo account, string agencyCodeInput)
    {
        var agencyCode = Clean(agencyCodeInput, 30).ToUpperInvariant();
        AssertAgencyWrite(account, agencyCode);

        return RetryAsync(async () =>
        {
            var now = MadagascarTime.Now;
            var date = MadagascarTime.Today;
            await using var tx = await _db.Database.BeginTransactionAsync();

            var session = await _db.CashSessions.FirstOrDefaultAsync(s => s.AgencyCode == agencyCode && s.BusinessDate == date);
            if (session == null) throw new DomainException("Caisse introuvable.", 404);
            if (session.Status == "CLOSED") return session;

            var missing = await MissingClosuresAsync(session);
            if (missing.Length > 0) throw new DomainException("Clôturez d’abord les caisses société suivantes : " + missing + ".");
            if (!MadagascarTime.IsRegularClosureWindow(date, now))
                throw new DomainException("La clôture normale est autorisée uniquement de 11 h 00 à 18 h 30, heure de Madagascar.");
            if (now < session.StartedAt) throw new DomainException("L’heure de fermeture est invalide.");

            session.Status = "CLOSED";
            session.ClosedAt = now;
            session.ClosedBy = account.Email;
            session.UpdatedBy = account.Email;
            session.UpdatedAt = now;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return session;
        });
    }

    // ------------------------------------------------------------------ régularisation des clôtures en retard

    public Task<CompanyDailyClosure> RegularizeCompanyAsync(
        AccountInfo account, string agencyCodeInput, string companyInput, string closureDateInput,
        long cashCounted, IDictionary<string, long>? breakdown, string reasonInput)
    {
        var agencyCode = Clean(agencyCodeInput, 30).ToUpperInvariant();
        var company = Clean(companyInput, 40).ToUpperInvariant();
        var reason = Clean(reasonInput, 400);
        DateOnly date;
        if (agencyCode.Length == 0 || company.Length == 0 || !TryDate(closureDateInput, out date))
            throw new DomainException("Agence, société et date de clôture obligatoires.");
        AssertRegularization(account, agencyCode);
        if (cashCounted < 0) throw new DomainException("Le montant compté est invalide.");
        if (reason.Length < 5) throw new DomainException("Indiquez un motif de régularisation d’au moins 5 caractères.");
        var breakdownJson = BreakdownJson(breakdown);

        return RetryAsync(async () =>
        {
            var now = MadagascarTime.Now;
            if (!MadagascarTime.IsCashDayOverdue(date, now))
                throw new DomainException("La régularisation concerne une journée antérieure ou la journée du jour après 18 h 30, heure de Madagascar.");
            await using var tx = await _db.Database.BeginTransactionAsync();

            if (!await _db.Companies.AnyAsync(c => c.Code == company && c.Active))
                throw new DomainException("Société invalide.");

            var session = await _db.CashSessions.FirstOrDefaultAsync(s => s.AgencyCode == agencyCode && s.BusinessDate == date);
            if (session == null) throw new DomainException("Journée de caisse introuvable.", 404);
            if (session.Status != "OPEN") throw new DomainException("Cette journée est déjà définitivement clôturée.");

            var existing = await _db.CompanyDailyClosures
                .FirstOrDefaultAsync(c => c.AgencyCode == agencyCode && c.Company == company && c.ClosureDate == date);
            if (existing != null) return existing;

            var balance = await _db.CashSessionCompanyBalances
                .FirstOrDefaultAsync(b => b.CashSessionId == session.Id && b.Company == company);
            if (balance == null) throw new DomainException("Les soldes par société ne sont pas initialisés.");

            var agency = await _db.Agencies.FirstAsync(a => a.Code == agencyCode);
            var movementRows = await _db.CashOperations
                .Where(o => o.CashSessionId == session.Id && o.Company == company && o.Channel == "ESPECE")
                .Select(o => new { o.Direction, o.Amount })
                .ToListAsync();
            var movement = movementRows.Sum(o => o.Direction == "ENTREE" ? o.Amount : -o.Amount);
            var opening = balance.CarriedBalance + balance.CashFund;
            var theoretical = opening + movement;
            var variance = cashCounted - theoretical;

            var closure = new CompanyDailyClosure
            {
                AgencyCode = agencyCode,
                AgencyName = agency.Name,
                Company = company,
                ClosureDate = date,
                OpeningCash = opening,
                CashFund = balance.CashFund,
                CashTheoretical = theoretical,
                CashCounted = cashCounted,
                Variance = variance,
                Breakdown = breakdownJson,
                Comment = variance != 0 ? "Clôture tardive — écart constaté. " + reason : "Clôture tardive. " + reason,
                CashierName = session.CashierName,
                Status = "Transmis",
                SubmittedBy = account.Email,
                SubmittedAt = now,
                IsLate = true,
                LateReason = reason,
                RegularizedAt = now,
                RegularizedBy = account.Email,
            };
            _db.CompanyDailyClosures.Add(closure);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return closure;
        });
    }

    public Task<CashSession> FinalizeLateDayAsync(
        AccountInfo account, string agencyCodeInput, string businessDateInput, string reasonInput)
    {
        var agencyCode = Clean(agencyCodeInput, 30).ToUpperInvariant();
        var reason = Clean(reasonInput, 400);
        DateOnly date;
        if (agencyCode.Length == 0 || !TryDate(businessDateInput, out date))
            throw new DomainException("Agence et date de caisse obligatoires.");
        AssertRegularization(account, agencyCode);
        if (reason.Length < 5) throw new DomainException("Indiquez un motif de régularisation d’au moins 5 caractères.");

        return RetryAsync(async () =>
        {
            var now = MadagascarTime.Now;
            if (!MadagascarTime.IsCashDayOverdue(date, now))
                throw new DomainException("La régularisation concerne une journée antérieure ou la journée du jour après 18 h 30, heure de Madagascar.");
            await using var tx = await _db.Database.BeginTransactionAsync();

            var session = await _db.CashSessions.FirstOrDefaultAsync(s => s.AgencyCode == agencyCode && s.BusinessDate == date);
            if (session == null) throw new DomainException("Journée de caisse introuvable.", 404);
            if (session.Status == "CLOSED") return session;

            var missing = await MissingClosuresAsync(session);
            if (missing.Length > 0) throw new DomainException("Clôturez d’abord les caisses société suivantes : " + missing + ".");

            session.Status = "CLOSED";
            session.ClosedAt = now;
            session.ClosedBy = account.Email;
            session.ClosedLate = true;
            session.LateClosureReason = reason;
            session.RegularizedAt = now;
            session.RegularizedBy = account.Email;
            session.UpdatedBy = account.Email;
            session.UpdatedAt = now;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return session;
        });
    }
}
