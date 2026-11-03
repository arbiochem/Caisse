using System.Net;
using System.Text;
using CaisseReseau.Web.Models;

namespace CaisseReseau.Web.Services;

/// <summary>Reçu 80 mm (port de printableHtml). Le HTML est autonome : il est affiché dans une iframe.</summary>
public static class ReceiptRenderer
{
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

    public static string Render(CashOperation op, bool duplicate)
    {
        var rows = new List<(string, string)>
        {
            ("Date opération", Fmt.DateShort(op.TransactionDate)),
            ("Caissier", string.IsNullOrEmpty(op.CashierName) ? op.CreatedBy : op.CashierName),
            ("Client / libellé", op.CustomerName),
            ("Paiement", Domain.ChannelLabel(op.Channel)),
            ("Nature", Domain.NatureLabel(op.TransactionNature)),
            ("Sens", op.Direction == "ENTREE" ? "Encaissement" : "Sortie / versement"),
        };
        if (!string.IsNullOrEmpty(op.InvoiceNumber)) rows.Add(("Facture SAGE", op.InvoiceNumber));
        if (!string.IsNullOrEmpty(op.Reference)) rows.Add(("Référence", op.Reference));
        if (!string.IsNullOrEmpty(op.BankName)) rows.Add(("Banque", op.BankName));
        if (!string.IsNullOrEmpty(op.DocumentNumber)) rows.Add(("N° document", op.DocumentNumber));
        if (op.MaturityDate.HasValue) rows.Add(("Échéance", Fmt.DateShort(op.MaturityDate.Value)));
        if (!string.IsNullOrEmpty(op.Notes)) rows.Add(("Observation", op.Notes));

        var detail = new StringBuilder();
        foreach (var (label, value) in rows)
            detail.Append("<div class=\"row\"><span>").Append(E(label)).Append("</span><strong>").Append(E(value)).Append("</strong></div>");

        var status = duplicate ? "DUPLICATA" : "ORIGINAL";
        var border = duplicate ? "2px" : "1px";
        var created = Fmt.DateTime(op.CreatedAt);

        var css = """
            @page { size: 80mm auto; margin: 2mm; }
            * { box-sizing: border-box; }
            html, body { margin: 0; padding: 0; background: #fff; color: #000; }
            body { width: 76mm; font-family: Arial, Helvetica, sans-serif; font-size: 10.5px; line-height: 1.3; }
            .receipt { width: 76mm; padding: 2mm 1.5mm 5mm; break-inside: avoid; }
            header { text-align: center; }
            .company { font-size: 17px; font-weight: 900; letter-spacing: .4px; }
            .agency { margin-top: 2px; font-size: 10px; }
            h1 { margin: 7px 0 3px; font-size: 13px; letter-spacing: 1.4px; }
            .ticket-number { overflow-wrap: anywhere; margin: 8px 0 5px; font-family: "Courier New", monospace; font-size: 14px; font-weight: 900; text-align: center; }
            .rule { margin: 6px 0; border-top: 1px dashed #000; }
            .row { display: grid; grid-template-columns: 27mm 1fr; gap: 2mm; padding: 2px 0; }
            .row span { color: #333; }
            .row strong { overflow-wrap: anywhere; text-align: right; }
            .total { display: flex; align-items: baseline; justify-content: space-between; gap: 4mm; }
            .total span { font-size: 12px; font-weight: 900; }
            .total strong { font-size: 18px; white-space: nowrap; }
            .meta, footer { margin-top: 4px; font-size: 8.5px; text-align: center; }
            footer { margin-top: 8px; }
            """;

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>").Append(E(op.TicketNumber)).Append("</title><style>");
        sb.Append(css);
        sb.Append(".copy { display: inline-block; border: ").Append(border).Append(" solid #000; padding: 2px 8px; font-size: 9px; font-weight: 900; }");
        sb.Append("</style></head><body><section class=\"receipt\"><header><div class=\"company\">")
          .Append(E(Domain.CompanyLabel(op.Company))).Append("</div><div class=\"agency\">").Append(E(op.AgencyName))
          .Append("</div><h1>TICKET DE CAISSE</h1><div class=\"copy\">").Append(status).Append("</div></header>");
        sb.Append("<div class=\"ticket-number\">").Append(E(op.TicketNumber)).Append("</div><div class=\"rule\"></div>");
        sb.Append(detail);
        sb.Append("<div class=\"rule\"></div><div class=\"total\"><span>MONTANT</span><strong>").Append(E(Fmt.Money(op.Amount)))
          .Append("</strong></div><div class=\"meta\">Créé le ").Append(E(created))
          .Append("</div><div class=\"meta\">Impression n° ").Append(op.PrintCount)
          .Append("</div><footer>Ticket généré par Direction Crédit et Recouvrement<br>Merci de conserver ce justificatif.</footer></section></body></html>");
        return sb.ToString();
    }
}
