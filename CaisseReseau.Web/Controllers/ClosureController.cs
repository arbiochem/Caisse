using CaisseReseau.Web.Data;
using CaisseReseau.Web.Models;
using CaisseReseau.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CaisseReseau.Web.Controllers;

public class ClosureController : BaseController
{
    [HttpGet]
    public async Task<IActionResult> Index(string? company = null)
    {
        if (!Account.CanWrite)
        {
            TempData["Toast"] = "Ce compte est en lecture seule.";
            return RedirectToAction("Index", "Home");
        }
        var agency = ResolveAgency(forWrite: true);
        var now = MadagascarTime.Now;
        var today = MadagascarTime.Today;
        var selected = Domain.Companies.Any(c => c.Code == company) ? company! : Domain.Companies[0].Code;

        var session = await Db.CashSessionsReadOnly(agency, today);
        var snapshot = await Queries.LoadAsync(Account, today, today, agency);

        ViewData["View"] = "closure";
        return View(new ClosureVm
        {
            AgencyCode = agency,
            AgencyName = Account.AgencyName,
            Company = selected,
            Session = session,
            Balance = session?.CompanyBalances.FirstOrDefault(b => b.Company == selected),
            AgencyOps = snapshot.Operations,
            Closures = snapshot.Closures,
            WindowStatus = MadagascarTime.ClosureWindowStatus(now),
            Today = Fmt.Iso(today),
        });
    }

    [HttpPost]
    public async Task<IActionResult> CloseCompany([FromBody] CloseCompanyDto dto)
    {
        try
        {
            var closure = await Cash.CloseCompanyAsync(Account, dto.AgencyCode ?? "", dto.Company ?? "", dto.CashCounted, dto.Breakdown, dto.Comment);
            return Json(new { company = closure.Company, variance = closure.Variance });
        }
        catch (DomainException ex) { return Fail(ex); }
    }

    [HttpPost]
    public async Task<IActionResult> Finalize([FromBody] FinalizeDto dto)
    {
        try
        {
            var session = await Cash.FinalizeDayAsync(Account, dto.AgencyCode ?? "");
            return Json(new { status = session.Status });
        }
        catch (DomainException ex) { return Fail(ex); }
    }
}
