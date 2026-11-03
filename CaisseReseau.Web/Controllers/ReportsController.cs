using CaisseReseau.Web.Models;
using CaisseReseau.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CaisseReseau.Web.Controllers;

public class ReportsController : BaseController
{
    [HttpGet]
    public async Task<IActionResult> Index(string? from, string? to, string? agency, string? company, string? channel, string? nature)
    {
        var today = MadagascarTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var dateFrom = ParseDate(from, monthStart);
        var dateTo = ParseDate(to, today);
        var selectedAgency = ResolveAgency();
        if (!string.IsNullOrEmpty(agency) && (agency == "ALL" || VisibleAgencies.Any(a => a.Code == agency))) selectedAgency = Account.CanViewAll || agency == Account.AgencyCode ? agency : selectedAgency;
        company = string.IsNullOrEmpty(company) ? "ALL" : company;
        channel = string.IsNullOrEmpty(channel) ? "ALL" : channel;
        nature = string.IsNullOrEmpty(nature) ? "ALL" : nature;

        var snapshot = await Queries.LoadAsync(Account, dateFrom, dateTo, selectedAgency);
        var filtered = snapshot.Operations
            .Where(o => (company == "ALL" || o.Company == company)
                        && (channel == "ALL" || o.Channel == channel)
                        && (nature == "ALL" || o.TransactionNature == nature))
            .ToList();
        var closures = snapshot.Closures.Where(c => company == "ALL" || c.Company == company).ToList();

        var hierarchy = new List<ReportRow>();
        var companies = company == "ALL" ? Domain.Companies : Domain.Companies.Where(c => c.Code == company).ToArray();
        foreach (var c in companies)
        {
            var companyOps = filtered.Where(o => o.Company == c.Code).ToList();
            if (companyOps.Count == 0 && company != c.Code) continue;
            hierarchy.Add(Row(c.Code, 0, c.Label, companyOps));
            foreach (var ch in Domain.Channels)
            {
                var channelOps = companyOps.Where(o => o.Channel == ch.Value).ToList();
                if (channelOps.Count == 0) continue;
                hierarchy.Add(Row($"{c.Code}-{ch.Value}", 1, ch.Label, channelOps));
                foreach (var n in Domain.AllNatures)
                {
                    var natureOps = channelOps.Where(o => o.TransactionNature == n).ToList();
                    if (natureOps.Count > 0) hierarchy.Add(Row($"{c.Code}-{ch.Value}-{n}", 2, Domain.NatureLabel(n), natureOps));
                }
            }
        }

        ViewData["View"] = "reports";
        return View(new ReportsVm
        {
            From = Fmt.Iso(dateFrom),
            To = Fmt.Iso(dateTo),
            Agency = selectedAgency,
            Company = company,
            Channel = channel,
            Nature = nature,
            Agencies = VisibleAgencies,
            Operations = filtered,
            Sessions = snapshot.Sessions,
            Closures = closures,
            Hierarchy = hierarchy,
        });
    }

    private static ReportRow Row(string key, int level, string label, List<CashOperation> ops) => new()
    {
        Key = key,
        Level = level,
        Label = label,
        Count = ops.Count,
        Entries = ops.Where(o => o.Direction == "ENTREE").Sum(o => o.Amount),
        Exits = ops.Where(o => o.Direction == "SORTIE").Sum(o => o.Amount),
    };

    /// <summary>Archive zip : un dossier par agence, un classeur Excel par société.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(string? from, string? to, string? agency)
    {
        var today = MadagascarTime.Today;
        var dateFrom = ParseDate(from, new DateOnly(today.Year, today.Month, 1));
        var dateTo = ParseDate(to, today);
        if (dateFrom > dateTo) return BadRequest("La date de début doit précéder la date de fin.");

        var agencies = VisibleAgencies.Where(a => string.IsNullOrEmpty(agency) || agency == "ALL" || a.Code == agency).ToList();
        var snapshot = await Queries.LoadAsync(Account, dateFrom, dateTo, agency);
        try
        {
            var archive = HttpContext.RequestServices.GetRequiredService<ReportService>().BuildArchive(dateFrom, dateTo, agencies, snapshot);
            return File(archive.Content, "application/zip", archive.FileName);
        }
        catch (DomainException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
