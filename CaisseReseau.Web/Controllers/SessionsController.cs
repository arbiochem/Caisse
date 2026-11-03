using CaisseReseau.Web.Models;
using CaisseReseau.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CaisseReseau.Web.Controllers;

public class SessionsController : BaseController
{
    [HttpPost]
    public async Task<IActionResult> Open([FromBody] OpenSessionDto dto)
    {
        try
        {
            var result = await Cash.OpenSessionAsync(Account, dto.AgencyCode ?? "", dto.CashierName ?? "", dto.OpeningFunds);
            return Json(new { handover = result.Handover, cashier = result.Session.CashierName });
        }
        catch (DomainException ex) { return Fail(ex); }
    }
}
