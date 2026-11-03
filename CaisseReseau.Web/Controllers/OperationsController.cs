using CaisseReseau.Web.Data;
using CaisseReseau.Web.Models;
using CaisseReseau.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CaisseReseau.Web.Controllers;

public class OperationsController : BaseController
{
    [HttpGet]
    public async Task<IActionResult> New()
    {
        if (!Account.CanWrite)
        {
            TempData["Toast"] = "Ce compte est en lecture seule.";
            return RedirectToAction("Index", "Home");
        }
        var agency = ResolveAgency(forWrite: true);
        var today = MadagascarTime.Today;
        var session = await Db.CashSessionsReadOnly(agency, today);
        ViewData["View"] = "entry";
        return View(new EntryVm { AgencyCode = agency, AgencyName = Account.AgencyName, Session = session, Today = Fmt.Iso(today) });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOperationInput input)
    {
        try
        {
            var op = await Cash.CreateOperationAsync(Account, input);
            return Json(new { id = op.Id, ticketNumber = op.TicketNumber });
        }
        catch (DomainException ex) { return Fail(ex); }
    }

    [HttpGet]
    public async Task<IActionResult> Journal(long? ticket = null)
    {
        var agency = ResolveAgency();
        var today = MadagascarTime.Today;
        var snapshot = await Queries.LoadAsync(Account, today.AddDays(-45), today, agency);
        CashOperation? newTicket = null;
        if (ticket.HasValue)
        {
            newTicket = snapshot.Operations.FirstOrDefault(o => o.Id == ticket.Value && Account.HasAgencyWriteAccess(o.AgencyCode));
        }
        ViewData["View"] = "journal";
        return View(new JournalVm { Operations = snapshot.Operations, NewTicket = newTicket, Account = Account });
    }

    [HttpPost]
    public async Task<IActionResult> Impute([FromBody] ImputeDto dto)
    {
        try
        {
            var op = await Cash.MarkImputedAsync(Account, dto.Id);
            return Json(new { id = op.Id, sageStatus = op.SageStatus });
        }
        catch (DomainException ex) { return Fail(ex); }
    }

    [HttpPost]
    public async Task<IActionResult> RecordPrint([FromBody] PrintDto dto)
    {
        try
        {
            var op = await Cash.RecordPrintAsync(Account, dto.Id, dto.ClientPrintId ?? "");
            return Json(new { id = op.Id, printCount = op.PrintCount });
        }
        catch (DomainException ex) { return Fail(ex); }
    }

    [HttpPost]
    public async Task<IActionResult> Allocate([FromBody] AllocateDto dto)
    {
        try
        {
            var result = await Cash.AllocateDepositAsync(Account, dto.Id, dto.TargetCompany ?? "", dto.InvoiceNumber ?? "", dto.Amount);
            return Json(new { id = result.Source.Id, allocated = result.Source.AllocatedAmount });
        }
        catch (DomainException ex) { return Fail(ex); }
    }

    /// <summary>Reçu 80 mm (HTML autonome) affiché dans l'aperçu d'impression.</summary>
    [HttpGet]
    public async Task<IActionResult> Receipt(long id)
    {
        var op = await Queries.FindOperationAsync(Account, id);
        if (op == null) return NotFound();
        return Content(ReceiptRenderer.Render(op, op.PrintCount > 1), "text/html; charset=utf-8");
    }
}
