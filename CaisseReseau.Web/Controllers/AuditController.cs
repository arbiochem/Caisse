using CaisseReseau.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CaisseReseau.Web.Controllers;

public class AuditController : BaseController
{
    [HttpGet]
    public async Task<IActionResult> Fr(string? agency, string? from, string? to)
    {
        var today = MadagascarTime.Today;
        var dateFrom = ParseDate(from, today.AddDays(-6));
        var dateTo = ParseDate(to, today);
        var pieces = await Queries.FrPiecesAsync(Account, agency, dateFrom, dateTo);
        var items = pieces.Select(p => new
        {
            id = p.Id.ToString(),
            agency = p.AgencyName,
            ticket = p.TicketNumber,
            date = Fmt.Iso(p.TransactionDate),
            company = Domain.CompanyLabel(p.Company),
            customer = p.CustomerName,
            invoice = p.InvoiceNumber ?? "",
            amount = p.Amount,
            notes = p.Notes ?? "",
            receivedAt = Fmt.DateTime(p.CreatedAt),
            status = "FR à contrôler",
        });
        return Json(new { items });
    }
}
