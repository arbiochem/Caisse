namespace CaisseReseau.Web.Models;

public sealed class OpenSessionDto
{
    public string? AgencyCode { get; set; }
    public string? CashierName { get; set; }
    public Dictionary<string, long>? OpeningFunds { get; set; }
}

public sealed class ImputeDto { public long Id { get; set; } }

public sealed class PrintDto
{
    public long Id { get; set; }
    public string? ClientPrintId { get; set; }
}

public sealed class AllocateDto
{
    public long Id { get; set; }
    public string? TargetCompany { get; set; }
    public string? InvoiceNumber { get; set; }
    public long Amount { get; set; }
}

public sealed class CloseCompanyDto
{
    public string? AgencyCode { get; set; }
    public string? Company { get; set; }
    public long CashCounted { get; set; }
    public Dictionary<string, long>? Breakdown { get; set; }
    public string? Comment { get; set; }
}

public sealed class FinalizeDto
{
    public string? AgencyCode { get; set; }
    public string? BusinessDate { get; set; }
    public string? Reason { get; set; }
}

public sealed class RegularizeDto
{
    public string? AgencyCode { get; set; }
    public string? Company { get; set; }
    public string? ClosureDate { get; set; }
    public long CashCounted { get; set; }
    public Dictionary<string, long>? Breakdown { get; set; }
    public string? Reason { get; set; }
}
