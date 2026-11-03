using CaisseReseau.Web.Data;
using CaisseReseau.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CaisseReseau.Web.Services;

public sealed class CashSnapshot
{
    public List<CashOperation> Operations { get; init; } = new();
    public List<CompanyDailyClosure> Closures { get; init; } = new();
    public List<CashSession> Sessions { get; init; } = new();
}

/// <summary>Lectures LINQ (équivalent des vues/RPC de lecture de l'application d'origine).</summary>
public sealed class CashQueries
{
    private readonly CaisseDbContext _db;

    public CashQueries(CaisseDbContext db)
    {
        _db = db;
    }

    /// <summary>Agences visibles par le compte (une seule pour un compte agence).</summary>
    public async Task<List<(string Code, string Name)>> VisibleAgenciesAsync(AccountInfo account)
    {
        var query = _db.Agencies.Where(a => a.Active);
        if (!account.CanViewAll) query = query.Where(a => a.Code == account.AgencyCode);
        var rows = await query.OrderBy(a => a.Name).Select(a => new { a.Code, a.Name }).ToListAsync();
        return rows.Select(r => (r.Code, r.Name)).ToList();
    }

    public async Task<CashSnapshot> LoadAsync(AccountInfo account, DateOnly from, DateOnly to, string? agencyCode = null)
    {
        var allowed = (await VisibleAgenciesAsync(account)).Select(a => a.Code).ToList();
        if (!string.IsNullOrEmpty(agencyCode) && agencyCode != "ALL")
            allowed = allowed.Where(c => c == agencyCode).ToList();

        var operations = await _db.CashOperations.AsNoTracking()
            .Where(o => allowed.Contains(o.AgencyCode) && o.TransactionDate >= from && o.TransactionDate <= to)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        var closures = await _db.CompanyDailyClosures.AsNoTracking()
            .Where(c => allowed.Contains(c.AgencyCode) && c.ClosureDate >= from && c.ClosureDate <= to)
            .OrderByDescending(c => c.ClosureDate)
            .ToListAsync();
        var sessions = await _db.CashSessions.AsNoTracking()
            .Include(s => s.CompanyBalances)
            .Include(s => s.Agency)
            .Where(s => allowed.Contains(s.AgencyCode) && s.BusinessDate >= from && s.BusinessDate <= to)
            .OrderByDescending(s => s.BusinessDate)
            .ToListAsync();
        return new CashSnapshot { Operations = operations, Closures = closures, Sessions = sessions };
    }

    /// <summary>Caisses restées ouvertes en retard (journées passées ou jour courant après 18 h 30).</summary>
    public async Task<List<CashSession>> OverdueSessionsAsync(AccountInfo account)
    {
        var agencies = (await VisibleAgenciesAsync(account))
            .Where(a => account.HasClosureRegularizationAccess(a.Code))
            .Select(a => a.Code).ToList();
        if (agencies.Count == 0) return new List<CashSession>();

        var now = MadagascarTime.Now;
        var open = await _db.CashSessions.AsNoTracking()
            .Include(s => s.CompanyBalances)
            .Include(s => s.Agency)
            .Where(s => s.Status == "OPEN" && agencies.Contains(s.AgencyCode))
            .ToListAsync();
        return open
            .Where(s => MadagascarTime.IsCashDayOverdue(s.BusinessDate, now))
            .OrderBy(s => s.BusinessDate).ThenBy(s => s.Agency!.Name)
            .ToList();
    }

    public async Task<CashOperation?> FindOperationAsync(AccountInfo account, long id)
    {
        var op = await _db.CashOperations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (op == null) return null;
        if (!account.CanViewAll && op.AgencyCode != account.AgencyCode) return null;
        return op;
    }

    /// <summary>Pièces FR saisies en sortie, à contrôler par l'audit.</summary>
    public async Task<List<CashOperation>> FrPiecesAsync(AccountInfo account, string? agency, DateOnly from, DateOnly to)
    {
        var allowed = (await VisibleAgenciesAsync(account)).Select(a => a.Code).ToList();
        if (!string.IsNullOrEmpty(agency) && agency != "ALL") allowed = allowed.Where(c => c == agency).ToList();
        var fromStamp = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), MadagascarTime.Offset);
        var toStamp = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), MadagascarTime.Offset);
        var rows = await _db.CashOperations.AsNoTracking()
            .Where(o => allowed.Contains(o.AgencyCode) && o.Direction == "SORTIE"
                        && o.CreatedAt >= fromStamp && o.CreatedAt < toStamp)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return rows.Where(o => (o.Notes != null && o.Notes.StartsWith("[FR]", StringComparison.OrdinalIgnoreCase))
                               || Domain.IsFrInvoice(o.InvoiceNumber)).ToList();
    }
}
