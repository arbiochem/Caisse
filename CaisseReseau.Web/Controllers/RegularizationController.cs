using CaisseReseau.Web.Models;
using CaisseReseau.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CaisseReseau.Web.Controllers;

public class RegularizationController : BaseController
{
    [HttpGet]
    public async Task<IActionResult> Index(long? session = null, string? company = null)
    {
        if (!Account.HasClosureRegularizationAccess())
        {
            TempData["Toast"] = "Droit de régularisation des clôtures requis.";
            return RedirectToAction("Index", "Home");
        }
        ResolveAgency();
        var pending = await Queries.OverdueSessionsAsync(Account);
        var selected = pending.FirstOrDefault(s => s.Id == session) ?? pending.FirstOrDefault();

        var ops = new List<CashOperation>();
        var closures = new List<CompanyDailyClosure>();
        if (selected != null)
        {
            var snapshot = await Queries.LoadAsync(Account, selected.BusinessDate, selected.BusinessDate, selected.AgencyCode);
            ops = snapshot.Operations;
            closures = snapshot.Closures;
        }

        var vm = new RegularizationVm { Pending = pending, Session = selected, SessionOps = ops, Closures = closures };
        var required = vm.Required;
        var chosen = required.Any(c => c.Code == company) ? company! : required.FirstOrDefault()?.Code ?? Domain.Companies[0].Code;

        ViewData["View"] = "regularization";
        return View(new RegularizationVm { Pending = pending, Session = selected, SessionOps = ops, Closures = closures, Company = chosen });
    }

    [HttpPost]
    public async Task<IActionResult> CloseCompany([FromBody] RegularizeDto dto)
    {
        try
        {
            var closure = await Cash.RegularizeCompanyAsync(Account, dto.AgencyCode ?? "", dto.Company ?? "", dto.ClosureDate ?? "", dto.CashCounted, dto.Breakdown, dto.Reason ?? "");
            return Json(new { company = closure.Company, variance = closure.Variance });
        }
        catch (DomainException ex) { return Fail(ex); }
    }

    [HttpPost]
    public async Task<IActionResult> Finalize([FromBody] FinalizeDto dto)
    {
        try
        {
            var session = await Cash.FinalizeLateDayAsync(Account, dto.AgencyCode ?? "", dto.BusinessDate ?? "", dto.Reason ?? "");
            return Json(new { status = session.Status });
        }
        catch (DomainException ex) { return Fail(ex); }
    }
}
