using CaisseReseau.Web.Services;

namespace CaisseReseau.Web.Models;

public sealed class AuthVm
{
    public string Screen { get; set; } = "LOGIN"; // LOGIN | REQUEST_RESET | UPDATE_PASSWORD
    public string Email { get; set; } = "";
    public string? Token { get; set; }
    public string? Error { get; set; }
    public string? Notice { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class DashboardVm
{
    public string AgencyCode { get; init; } = "ALL";
    public string AgencyName { get; init; } = "Toutes les agences";
    public bool CanManage { get; init; }
    public bool CanRegularize { get; init; }
    public int LateOpenCount { get; init; }
    public List<CaisseReseau.Web.Models.CashOperation> Operations { get; init; } = new();
    public List<CashSession> Sessions { get; init; } = new();
    public List<CompanyDailyClosure> Closures { get; init; } = new();
}

public sealed class EntryVm
{
    public string AgencyCode { get; init; } = "";
    public string AgencyName { get; init; } = "";
    public CashSession? Session { get; init; }
    public string Today { get; init; } = "";
}

public sealed class JournalVm
{
    public List<CashOperation> Operations { get; init; } = new();
    public CashOperation? NewTicket { get; init; }
    public AccountInfo Account { get; init; } = new();
}

public sealed class ClosureVm
{
    public string AgencyCode { get; init; } = "";
    public string AgencyName { get; init; } = "";
    public string Company { get; init; } = "ARBIOCHEM";
    public CashSession? Session { get; init; }
    public CashSessionCompanyBalance? Balance { get; init; }
    public List<CashOperation> AgencyOps { get; init; } = new();
    public List<CompanyDailyClosure> Closures { get; init; } = new();
    public string WindowStatus { get; init; } = "OPEN";
    public string Today { get; init; } = "";

    public IEnumerable<CashOperation> CompanyOps => AgencyOps.Where(o => o.Company == Company);
    public long OpeningCash => (Balance?.CarriedBalance ?? 0) + (Balance?.CashFund ?? 0);
    public long CashMovement => CompanyOps.Where(o => o.Channel == "ESPECE").Sum(o => o.Direction == "ENTREE" ? o.Amount : -o.Amount);
    public long Theoretical => OpeningCash + CashMovement;
    public CompanyDailyClosure? Existing => Closures.FirstOrDefault(c => c.Company == Company);
    public bool IsDone(string company) => Closures.Any(c => c.Company == company);

    public List<CompanyDef> Required => Domain.Companies.Where(c =>
    {
        var b = Session?.CompanyBalances.FirstOrDefault(x => x.Company == c.Code);
        return (b?.CarriedBalance ?? 0) + (b?.CashFund ?? 0) > 0 || AgencyOps.Any(o => o.Company == c.Code);
    }).ToList();

    public bool AllClosed => Session != null && Required.All(c => IsDone(c.Code));
}

public sealed class RegularizationVm
{
    public List<CashSession> Pending { get; init; } = new();
    public CashSession? Session { get; init; }
    public string Company { get; init; } = "";
    public List<CashOperation> SessionOps { get; init; } = new();
    public List<CompanyDailyClosure> Closures { get; init; } = new();

    public List<CompanyDef> Required => Domain.Companies.Where(c =>
    {
        var b = Session?.CompanyBalances.FirstOrDefault(x => x.Company == c.Code);
        return (b?.CarriedBalance ?? 0) + (b?.CashFund ?? 0) > 0 || SessionOps.Any(o => o.Company == c.Code);
    }).ToList();

    public CashSessionCompanyBalance? Balance => Session?.CompanyBalances.FirstOrDefault(b => b.Company == Company);
    public long OpeningCash => (Balance?.CarriedBalance ?? 0) + (Balance?.CashFund ?? 0);
    public long CashMovement => SessionOps.Where(o => o.Company == Company && o.Channel == "ESPECE").Sum(o => o.Direction == "ENTREE" ? o.Amount : -o.Amount);
    public long Theoretical => OpeningCash + CashMovement;
    public CompanyDailyClosure? ClosureOf(string company) => Closures.FirstOrDefault(c => c.Company == company);
    public bool AllClosed => Session != null && Required.All(c => ClosureOf(c.Code) != null);
}

public sealed class ReportRow
{
    public string Key { get; init; } = "";
    public int Level { get; init; }
    public string Label { get; init; } = "";
    public int Count { get; init; }
    public long Entries { get; init; }
    public long Exits { get; init; }
}

public sealed class ReportsVm
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Agency { get; init; } = "ALL";
    public string Company { get; init; } = "ALL";
    public string Channel { get; init; } = "ALL";
    public string Nature { get; init; } = "ALL";
    public List<(string Code, string Name)> Agencies { get; init; } = new();
    public List<CashOperation> Operations { get; init; } = new();
    public List<CashSession> Sessions { get; init; } = new();
    public List<CompanyDailyClosure> Closures { get; init; } = new();
    public List<ReportRow> Hierarchy { get; init; } = new();
}

public sealed class AdminVm
{
    public List<AccountAccess> Accounts { get; init; } = new();
    public List<(string Code, string Name)> Agencies { get; init; } = new();
    public string? Error { get; init; }
    public string? Notice { get; init; }
}
