using CaisseReseau.Web.Data;
using CaisseReseau.Web.Models;
using CaisseReseau.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CaisseReseau.Web.Controllers;

[Authorize]
public abstract class BaseController : Controller
{
    public const string AgencyCookie = "caisse.agency";

    protected AccountInfo Account { get; private set; } = null!;
    private List<(string Code, string Name)> _agencies = new();
    private string _selectedAgency = "ALL";

    protected CaisseDbContext Db => HttpContext.RequestServices.GetRequiredService<CaisseDbContext>();
    protected CashQueries Queries => HttpContext.RequestServices.GetRequiredService<CashQueries>();
    protected CashService Cash => HttpContext.RequestServices.GetRequiredService<CashService>();

    protected bool IsAjax => Request.Headers["X-Requested-With"] == "fetch";

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var email = User.FindFirst("email")?.Value;
        AccountInfo? account = null;
        if (!string.IsNullOrEmpty(email))
        {
            var row = await Db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Email == email && a.Active);
            if (row != null)
            {
                var agency = row.AgencyCode == null ? null : await Db.Agencies.AsNoTracking().FirstOrDefaultAsync(a => a.Code == row.AgencyCode);
                account = new AccountInfo
                {
                    Email = row.Email,
                    Role = row.Role,
                    AgencyCode = row.AgencyCode,
                    AgencyName = agency?.Name ?? "Toutes les agences",
                    CanRegularizeClosures = row.CanRegularizeClosures,
                    RegularizationExcludedAgencyCode = row.RegularizationExcludedAgencyCode,
                };
            }
        }

        if (account == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Result = IsAjax ? new JsonResult(new { error = "Session expirée. Reconnectez-vous." }) { StatusCode = 401 } : RedirectToAction("Login", "Account");
            return;
        }

        Account = account;
        _agencies = await Queries.VisibleAgenciesAsync(account);

        var executed = await next();
        if (executed.Result is ViewResult view)
        {
            view.ViewData["Shell"] = await BuildShellAsync(view.ViewData["View"] as string ?? "dashboard");
        }
    }

    /// <summary>Agence sélectionnée : cookie pour les comptes multi-agences, agence propre sinon.</summary>
    protected string ResolveAgency(bool forWrite = false)
    {
        if (forWrite && Account.WritableAgency != null) return _selectedAgency = Account.WritableAgency;
        if (!Account.CanViewAll) return _selectedAgency = Account.AgencyCode ?? _agencies.FirstOrDefault().Code ?? "";
        var chosen = Request.Cookies[AgencyCookie];
        _selectedAgency = chosen != null && _agencies.Any(a => a.Code == chosen) ? chosen : "ALL";
        return _selectedAgency;
    }

    protected List<(string Code, string Name)> VisibleAgencies => _agencies;

    private async Task<ShellModel> BuildShellAsync(string view)
    {
        var overdue = Account.HasClosureRegularizationAccess() ? (await Queries.OverdueSessionsAsync(Account)).Count : 0;
        var today = MadagascarTime.Today;
        var codes = _agencies.Select(a => a.Code).ToList();
        var closed = await Db.CashSessions.AsNoTracking()
            .Where(s => s.BusinessDate == today && s.Status == "CLOSED" && codes.Contains(s.AgencyCode))
            .Select(s => s.AgencyCode).Distinct().CountAsync();

        var nav = new List<NavItem>
        {
            new() { Id = "dashboard", Label = "Tableau de bord", Icon = "dashboard", Url = Url.Action("Index", "Home")! },
        };
        if (Account.CanWrite)
            nav.Add(new() { Id = "entry", Label = "Nouvelle opération", Icon = "plus", Url = Url.Action("New", "Operations")! });
        nav.Add(new() { Id = "journal", Label = "Journal de caisse", Icon = "journal", Url = Url.Action("Journal", "Operations")! });
        if (Account.CanWrite)
            nav.Add(new() { Id = "closure", Label = "Clôture par société", Icon = "close", Url = Url.Action("Index", "Closure")!, Badge = "11–18h30" });
        if (Account.HasClosureRegularizationAccess())
            nav.Add(new() { Id = "regularization", Label = "Clôtures en retard", Icon = "warning", Url = Url.Action("Index", "Regularization")!, Badge = overdue > 0 ? overdue.ToString() : null });
        nav.Add(new() { Id = "reports", Label = "Rapports", Icon = "report", Url = Url.Action("Index", "Reports")! });

        SessionDialogModel? dialog = null;
        if (Account.CanWrite)
            dialog = await BuildSessionDialogAsync(today);

        return new ShellModel
        {
            Account = Account,
            ActiveView = view,
            SelectedAgency = _selectedAgency,
            Nav = nav,
            Agencies = _agencies,
            ClosedAgencies = closed,
            SessionDialog = dialog,
        };
    }

    private async Task<SessionDialogModel> BuildSessionDialogAsync(DateOnly today)
    {
        var code = Account.WritableAgency!;
        var session = await Db.CashSessions.AsNoTracking().Include(s => s.CompanyBalances)
            .FirstOrDefaultAsync(s => s.AgencyCode == code && s.BusinessDate == today);

        var reports = new Dictionary<string, long>();
        foreach (var company in Domain.Companies)
        {
            var last = await Db.CompanyDailyClosures.AsNoTracking()
                .Where(c => c.AgencyCode == code && c.Company == company.Code && c.ClosureDate < today)
                .OrderByDescending(c => c.ClosureDate).FirstOrDefaultAsync();
            reports[company.Code] = last?.CashCounted ?? 0;
        }

        return new SessionDialogModel
        {
            AgencyCode = code,
            AgencyName = Account.AgencyName,
            Exists = session != null,
            Status = session?.Status,
            Cashier = session?.CashierName ?? "",
            ReportTotal = session != null ? session.CompanyBalances.Sum(b => b.CarriedBalance) : reports.Values.Sum(),
            Reports = reports,
        };
    }

    protected IActionResult Fail(DomainException ex) =>
        new JsonResult(new { error = ex.Message }) { StatusCode = ex.Status };

    protected static DateOnly ParseDate(string? value, DateOnly fallback) =>
        DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : fallback;
}
