using CaisseReseau.Web.Models;
using CaisseReseau.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace CaisseReseau.Web.Controllers;

public class HomeController : BaseController
{
    public async Task<IActionResult> Index()
    {
        var agency = ResolveAgency();
        var today = MadagascarTime.Today;
        var snapshot = await Queries.LoadAsync(Account, today, today, agency);
        var overdue = Account.HasClosureRegularizationAccess() ? (await Queries.OverdueSessionsAsync(Account)).Count : 0;
        var name = VisibleAgencies.FirstOrDefault(a => a.Code == agency).Name ?? "Toutes les agences";

        ViewData["View"] = "dashboard";
        return View(new DashboardVm
        {
            AgencyCode = agency,
            AgencyName = name,
            CanManage = Account.CanWrite && agency != "ALL" && Account.HasAgencyWriteAccess(agency),
            CanRegularize = Account.HasClosureRegularizationAccess(),
            LateOpenCount = overdue,
            Operations = snapshot.Operations,
            Sessions = snapshot.Sessions,
            Closures = snapshot.Closures,
        });
    }

    [HttpPost]
    public IActionResult SelectAgency(string? code)
    {
        if (Account.CanViewAll && !string.IsNullOrEmpty(code))
        {
            Response.Cookies.Append(AgencyCookie, code, new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                MaxAge = TimeSpan.FromDays(30),
            });
        }
        return Ok();
    }

    [AllowAnonymous]
    public IActionResult Error() => Content("Une erreur est survenue. Réessayez ou contactez la Direction Crédit et Recouvrement.");
}
