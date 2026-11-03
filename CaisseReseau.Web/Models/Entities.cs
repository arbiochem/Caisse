using System.ComponentModel.DataAnnotations;

namespace CaisseReseau.Web.Models;

// Entités mappées sur les tables d'origine (schéma public Supabase), noms de colonnes en snake_case
// (la conversion est appliquée dans CaisseDbContext).

public class Agency
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string LoginEmail { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Company
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string TicketPrefix { get; set; } = "";
    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Remplace Supabase Auth + la table account_access : un compte = une adresse e-mail.</summary>
public class AccountAccess
{
    public string Email { get; set; } = "";
    public string Role { get; set; } = Roles.Agency;
    public string? AgencyCode { get; set; }
    public bool Active { get; set; } = true;
    public bool CanRegularizeClosures { get; set; }
    public string? RegularizationExcludedAgencyCode { get; set; }
    public string? PasswordHash { get; set; }
    public string? ResetTokenHash { get; set; }
    public DateTimeOffset? ResetTokenExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class CashSession
{
    public long Id { get; set; }
    public string AgencyCode { get; set; } = "";
    public DateOnly BusinessDate { get; set; }
    public string CashierName { get; set; } = "";
    public long OpeningBalance { get; set; }
    public DateOnly? SourceClosureDate { get; set; }
    public string StartedBy { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string Status { get; set; } = "OPEN";
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
    public bool ClosedLate { get; set; }
    public string? LateClosureReason { get; set; }
    public DateTimeOffset? RegularizedAt { get; set; }
    public string? RegularizedBy { get; set; }

    public Agency? Agency { get; set; }
    public List<CashSessionCompanyBalance> CompanyBalances { get; set; } = new();
}

public class CashSessionCompanyBalance
{
    public long CashSessionId { get; set; }
    public string AgencyCode { get; set; } = "";
    public DateOnly BusinessDate { get; set; }
    public string Company { get; set; } = "";
    public long CarriedBalance { get; set; }
    public long CashFund { get; set; }
    public DateOnly? SourceClosureDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public CashSession? CashSession { get; set; }
}

public class CashierShift
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public string AgencyCode { get; set; } = "";
    public DateOnly BusinessDate { get; set; }
    public string CashierName { get; set; } = "";
    public string Action { get; set; } = "OUVERTURE";
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}

public class CashOperation
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public string AgencyCode { get; set; } = "";
    public string AgencyName { get; set; } = "";
    public string Company { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Direction { get; set; } = "";
    public DateOnly TransactionDate { get; set; }
    public string TicketNumber { get; set; } = "";
    public string? InvoiceNumber { get; set; }
    public string CustomerName { get; set; } = "";
    public long Amount { get; set; }
    public string? Reference { get; set; }
    public string? BankName { get; set; }
    public string? DocumentNumber { get; set; }
    public DateOnly? MaturityDate { get; set; }
    public string SageStatus { get; set; } = "À imputer";
    public string? Notes { get; set; }
    public string CashierName { get; set; } = "";
    public int PrintCount { get; set; }
    public DateTimeOffset? LastPrintedAt { get; set; }
    public string? LastPrintedBy { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string TransactionNature { get; set; } = "REGLEMENT";
    public long AllocatedAmount { get; set; }
    public long? SourceDepositId { get; set; }
    public DateTimeOffset? SageStatusUpdatedAt { get; set; }
    public string? SageStatusUpdatedBy { get; set; }
    public string? ClientOperationId { get; set; }

    public CashSession? CashSession { get; set; }
}

public class CompanyDailyClosure
{
    public long Id { get; set; }
    public string AgencyCode { get; set; } = "";
    public string AgencyName { get; set; } = "";
    public string Company { get; set; } = "";
    public DateOnly ClosureDate { get; set; }
    public long OpeningCash { get; set; }
    public long CashFund { get; set; }
    public long CashTheoretical { get; set; }
    public long CashCounted { get; set; }
    public long Variance { get; set; }
    /// <summary>Détail du comptage (JSON : {"20000": 3, "10000": 1, ...}).</summary>
    public string Breakdown { get; set; } = "{}";
    public string? Comment { get; set; }
    public string CashierName { get; set; } = "";
    public string Status { get; set; } = "Transmis";
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
    public bool IsLate { get; set; }
    public string? LateReason { get; set; }
    public DateTimeOffset? RegularizedAt { get; set; }
    public string? RegularizedBy { get; set; }
}

public class DepositAllocation
{
    public long Id { get; set; }
    public long SourceOperationId { get; set; }
    public long TransferOutOperationId { get; set; }
    public long TransferInOperationId { get; set; }
    public string AgencyCode { get; set; } = "";
    public string SourceCompany { get; set; } = "";
    public string TargetCompany { get; set; } = "";
    public long AllocatedAmount { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public DateOnly AllocationDate { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? ClientAllocationId { get; set; }

    public CashOperation? SourceOperation { get; set; }
    public CashOperation? TransferOutOperation { get; set; }
    public CashOperation? TransferInOperation { get; set; }
}

public class TicketPrintEvent
{
    public long Id { get; set; }
    public long OperationId { get; set; }
    public string ClientPrintId { get; set; } = "";
    public string PrintedBy { get; set; } = "";
    public DateTimeOffset PrintedAt { get; set; }

    public CashOperation? Operation { get; set; }
}

public class TicketSequence
{
    public string AgencyCode { get; set; } = "";
    public string Company { get; set; } = "";
    public int CalendarYear { get; set; }
    public long LastNumber { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public static class Roles
{
    public const string Agency = "AGENCY";
    public const string Supervisor = "SUPERVISOR";
    public const string SupervisorAgency = "SUPERVISOR_AGENCY";
    public const string Direction = "DIRECTION";

    public static readonly string[] All = { Agency, Supervisor, SupervisorAgency, Direction };
}
