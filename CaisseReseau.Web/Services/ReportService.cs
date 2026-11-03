using System.IO.Compression;
using System.Globalization;
using System.Text;
using CaisseReseau.Web.Models;
using ClosedXML.Excel;

namespace CaisseReseau.Web.Services;

/// <summary>
/// Rapports Excel : un classeur par agence et par société (onglet « Synthèse » + 11 onglets de détail),
/// regroupés dans une archive zip. Port de lib/excel-report.ts (mêmes onglets, colonnes, couleurs et formats).
/// </summary>
public sealed class ReportService
{
    private const string Ink = "#17342D", Muted = "#66766F", Green = "#155E4B", GreenDark = "#0F493B",
        GreenSoft = "#EAF4F0", GreenPale = "#F5F9F7", Amber = "#9A6700", AmberSoft = "#FFF4D6",
        Red = "#B42318", RedDark = "#8F1D16", RedSoft = "#FFE9E7", White = "#FFFFFF", Line = "#D9E3DE";

    private const string MoneyFormat = "#,##0 \"Ar\";[Red](#,##0) \"Ar\";-";
    private const int DetailColumns = 18;

    private sealed class St
    {
        public double Size = 9;
        public bool Bold, Italic, Wrap;
        public string Color = Ink;
        public string? Bg;
        public string Align = "left";
        public string? Format;
        public double? Height;
        public XLBorderStyleValues? Bottom;
        public string? BottomColor;
        public XLBorderStyleValues? Top;
        public string? TopColor;
        public XLBorderStyleValues? Right;
        public string? RightColor;

        public St Clone() => (St)MemberwiseClone();
    }

    private static St Title => new() { Size = 18, Bold = true, Color = White, Bg = GreenDark, Height = 31 };
    private static St Subtitle => new() { Size = 10, Bold = true, Color = Green, Bg = GreenSoft, Height = 23 };
    private static St Context => new() { Size = 9, Color = Muted, Height = 21, Bottom = XLBorderStyleValues.Hair, BottomColor = Line };
    private static St Header => new()
    {
        Size = 8, Bold = true, Color = White, Bg = Green, Align = "center", Wrap = true, Height = 32,
        Right = XLBorderStyleValues.Thin, RightColor = "#4E8776", Bottom = XLBorderStyleValues.Medium, BottomColor = GreenDark,
    };
    private static St Body => new() { Size = 9, Color = Ink, Height = 23, Bottom = XLBorderStyleValues.Hair, BottomColor = Line };
    private static St TotalSt => new()
    {
        Size = 9, Bold = true, Color = White, Bg = Ink, Height = 29, Top = XLBorderStyleValues.Medium, TopColor = Green,
    };

    private static XLAlignmentHorizontalValues H(string a) => a switch
    {
        "center" => XLAlignmentHorizontalValues.Center,
        "right" => XLAlignmentHorizontalValues.Right,
        _ => XLAlignmentHorizontalValues.Left,
    };

    private static void Put(IXLWorksheet ws, int row, int col, int span, object? value, St s)
    {
        var range = ws.Range(row, col, row, col + span - 1);
        if (span > 1) range.Merge();
        var cell = ws.Cell(row, col);
        switch (value)
        {
            case null: break;
            case string text: cell.Value = text; break;
            case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); break;
            case long l: cell.Value = l; break;
            case int i: cell.Value = i; break;
            default: cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""; break;
        }
        var style = range.Style;
        style.Font.FontName = "Arial";
        style.Font.FontSize = s.Size;
        style.Font.Bold = s.Bold;
        style.Font.Italic = s.Italic;
        style.Font.FontColor = XLColor.FromHtml(s.Color);
        if (s.Bg != null) style.Fill.BackgroundColor = XLColor.FromHtml(s.Bg);
        style.Alignment.Horizontal = H(s.Align);
        style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        style.Alignment.WrapText = s.Wrap;
        if (s.Format != null) style.NumberFormat.Format = s.Format;
        if (s.Bottom.HasValue)
        {
            style.Border.BottomBorder = s.Bottom.Value;
            if (s.BottomColor != null) style.Border.BottomBorderColor = XLColor.FromHtml(s.BottomColor);
        }
        if (s.Top.HasValue)
        {
            style.Border.TopBorder = s.Top.Value;
            if (s.TopColor != null) style.Border.TopBorderColor = XLColor.FromHtml(s.TopColor);
        }
        if (s.Right.HasValue)
        {
            style.Border.RightBorder = s.Right.Value;
            if (s.RightColor != null) style.Border.RightBorderColor = XLColor.FromHtml(s.RightColor);
        }
        if (s.Height.HasValue && ws.Row(row).Height < s.Height.Value) ws.Row(row).Height = s.Height.Value;
    }

    private static St Money(St baseStyle) { var c = baseStyle.Clone(); c.Format = MoneyFormat; c.Align = "right"; return c; }

    private sealed class Sheet
    {
        public IXLWorksheet Ws = null!;
        public int Row = 1;
        public int Columns;

        public void Skip() { Row++; }

        public void Line(string text, St style) { Put(Ws, Row, 1, Columns, text, style); Row++; }
    }

    private sealed record ReportDef(string Sheet, string Title, string Description, Func<CashOperation, bool> Filter);

    private static readonly ReportDef[] Defs =
    {
        new("Espèces comptant", "Paiements en espèces au comptant", "Toutes les opérations au comptant réglées en espèces.", o => o.Channel == "ESPECE" && o.TransactionNature == "AU_COMPTANT"),
        new("Espèces règlement", "Règlements en espèces", "Tous les règlements de factures reçus en espèces.", o => o.Channel == "ESPECE" && o.TransactionNature == "REGLEMENT"),
        new("Toutes espèces", "Toutes les transactions en espèces", "Vue complète des entrées et sorties réalisées en espèces.", o => o.Channel == "ESPECE"),
        new("Orange Money", "Transactions Orange Money", "Toutes les natures d’opérations payées par Orange Money.", o => o.Channel == "ORANGE_MONEY"),
        new("MVola", "Transactions MVola", "Toutes les natures d’opérations payées par MVola.", o => o.Channel == "MVOLA"),
        new("TPE", "Transactions TPE / carte", "Toutes les natures d’opérations payées par carte ou TPE.", o => o.Channel == "TPE"),
        new("Virement", "Virements et versements", "Toutes les natures d’opérations payées par virement ou versement.", o => o.Channel == "VIREMENT_VERSEMENT"),
        new("Chèque", "Transactions par chèque", "Toutes les natures d’opérations payées par chèque.", o => o.Channel == "CHEQUE"),
        new("Traite", "Transactions par traite", "Toutes les natures d’opérations payées par traite.", o => o.Channel == "TRAITE"),
        new("Tous comptants", "Tous les paiements au comptant", "Paiements au comptant, tous modes de paiement confondus.", o => o.TransactionNature == "AU_COMPTANT"),
        new("Tous règlements", "Tous les règlements", "Règlements de factures, tous modes de paiement confondus.", o => o.TransactionNature == "REGLEMENT"),
    };

    public sealed class Input
    {
        public DateOnly From { get; init; }
        public DateOnly To { get; init; }
        public (string Code, string Name) Agency { get; init; }
        public string Company { get; init; } = "";
        public List<CashOperation> Operations { get; init; } = new();
        public List<CashSession> Sessions { get; init; } = new();
        public List<CompanyDailyClosure> Closures { get; init; } = new();
    }

    private static (int Count, long Entries, long Exits, long Net) Summarize(IEnumerable<CashOperation> ops)
    {
        var list = ops.ToList();
        var entries = list.Where(o => o.Direction == "ENTREE").Sum(o => o.Amount);
        var exits = list.Where(o => o.Direction == "SORTIE").Sum(o => o.Amount);
        return (list.Count, entries, exits, entries - exits);
    }

    private static string Period(Input i) => $"{Fmt.DateShort(i.From)} au {Fmt.DateShort(i.To)}";

    private static string Context_(Input i) => $"{i.Agency.Name} · {Domain.CompanyLabel(i.Company)} · période du {Period(i)}";

    // ------- journée de référence

    private static DateOnly ReferenceDate(Input i)
    {
        var dates = new HashSet<DateOnly>();
        foreach (var o in i.Operations.Where(o => o.AgencyCode == i.Agency.Code && o.Company == i.Company && o.TransactionDate >= i.From && o.TransactionDate <= i.To))
            dates.Add(o.TransactionDate);
        foreach (var s in i.Sessions.Where(s => s.AgencyCode == i.Agency.Code && s.BusinessDate >= i.From && s.BusinessDate <= i.To))
            dates.Add(s.BusinessDate);
        foreach (var c in i.Closures.Where(c => c.AgencyCode == i.Agency.Code && c.Company == i.Company && c.ClosureDate >= i.From && c.ClosureDate <= i.To))
            dates.Add(c.ClosureDate);
        return dates.Count == 0 ? i.To : dates.Max();
    }

    private static long? ReferencePreviousBalance(Input i, DateOnly date)
    {
        var session = i.Sessions.FirstOrDefault(s => s.AgencyCode == i.Agency.Code && s.BusinessDate == date);
        var balance = session?.CompanyBalances.FirstOrDefault(b => b.Company == i.Company);
        if (balance != null) return balance.CarriedBalance;
        var closure = i.Closures.FirstOrDefault(c => c.AgencyCode == i.Agency.Code && c.Company == i.Company && c.ClosureDate == date);
        if (closure != null) return Math.Max(0L, closure.OpeningCash - closure.CashFund);
        var previous = i.Closures
            .Where(c => c.AgencyCode == i.Agency.Code && c.Company == i.Company && c.ClosureDate < date)
            .OrderByDescending(c => c.ClosureDate).FirstOrDefault();
        return previous?.CashCounted;
    }

    private static long ReferenceEntries(Input i, DateOnly date) =>
        i.Operations.Where(o => o.AgencyCode == i.Agency.Code && o.Company == i.Company && o.TransactionDate == date && o.Direction == "ENTREE")
            .Sum(o => o.Amount);

    private static void HighlightedDaily(Sheet sh, Input input, int columns)
    {
        var dateSpan = columns <= 11 ? 3 : 4;
        var balanceSpan = (columns - dateSpan) / 2;
        var entriesSpan = columns - dateSpan - balanceSpan;
        var date = ReferenceDate(input);
        var previous = ReferencePreviousBalance(input, date);

        Put(sh.Ws, sh.Row, 1, dateSpan, "JOURNÉE DE RÉFÉRENCE", Header);
        Put(sh.Ws, sh.Row, 1 + dateSpan, balanceSpan, "SOLDE CLÔTURE VEILLE", Header);
        Put(sh.Ws, sh.Row, 1 + dateSpan + balanceSpan, entriesSpan, "TOTAL ENTRÉES DU JOUR (TOUS MODES)", Header);
        sh.Row++;

        St Value() { var v = Body; v.Height = 36; v.Align = "center"; v.Size = 12; v.Bold = true; return v; }

        var dateStyle = Value(); dateStyle.Format = "dd/mm/yyyy"; dateStyle.Bg = GreenPale;
        Put(sh.Ws, sh.Row, 1, dateSpan, date, dateStyle);

        if (previous.HasValue)
        {
            var s = Value(); s.Format = MoneyFormat; s.Bg = AmberSoft;
            Put(sh.Ws, sh.Row, 1 + dateSpan, balanceSpan, previous.Value, s);
        }
        else
        {
            var s = Value(); s.Bg = AmberSoft; s.Color = Amber; s.Italic = true; s.Size = 10;
            Put(sh.Ws, sh.Row, 1 + dateSpan, balanceSpan, "Non disponible", s);
        }

        var e = Value(); e.Format = MoneyFormat; e.Bg = GreenSoft; e.Color = GreenDark;
        Put(sh.Ws, sh.Row, 1 + dateSpan + balanceSpan, entriesSpan, ReferenceEntries(input, date), e);
        sh.Row++;
    }

    private static void TitleRows(Sheet sh, string title, string subtitle)
    {
        sh.Line(title, Title);
        sh.Line(subtitle, Subtitle);
    }

    // ------- onglets de détail

    private static readonly double[] DetailWidths =
    {
        13, 9, 25, 20, 33, 11, 23, 22, 17, 17, 19, 17, 21, 20, 20, 19, 14, 32,
    };

    private static void DetailSheet(IXLWorkbook wb, Input input, ReportDef def)
    {
        var ws = wb.Worksheets.Add(def.Sheet);
        var sh = new Sheet { Ws = ws, Columns = DetailColumns };
        var ops = input.Operations.Where(def.Filter)
            .OrderBy(o => o.TransactionDate).ThenBy(o => o.CreatedAt).ThenBy(o => o.TicketNumber, StringComparer.Ordinal)
            .ToList();
        var total = Summarize(ops);

        TitleRows(sh, def.Title, "DIRECTION CRÉDIT ET RECOUVREMENT");
        var ctx = Context; ctx.Bold = true; ctx.Color = Ink;
        sh.Line(Context_(input), ctx);
        sh.Line(def.Description, Context);
        sh.Skip();
        HighlightedDaily(sh, input, DetailColumns);
        sh.Skip();

        Put(ws, sh.Row, 1, 3, "OPÉRATIONS", Header);
        Put(ws, sh.Row, 4, 3, "ENTRÉES", Header);
        Put(ws, sh.Row, 7, 3, "SORTIES", Header);
        Put(ws, sh.Row, 10, 3, "SOLDE NET", Header);
        Put(ws, sh.Row, 13, 6, "LECTURE", Header);
        sh.Row++;

        St Kpi() { var k = Body; k.Align = "center"; k.Bold = true; k.Bg = GreenSoft; k.Height = 32; return k; }
        var countStyle = Kpi(); countStyle.Size = 12;
        var entriesStyle = Kpi(); entriesStyle.Size = 11; entriesStyle.Format = MoneyFormat;
        var exitsStyle = Kpi(); exitsStyle.Size = 11; exitsStyle.Format = MoneyFormat; exitsStyle.Color = Red; exitsStyle.Bg = RedSoft;
        var netStyle = Kpi(); netStyle.Size = 11; netStyle.Format = MoneyFormat; netStyle.Color = total.Net < 0 ? RedDark : GreenDark;
        var readStyle = Kpi(); readStyle.Color = Red; readStyle.Bg = RedSoft;
        Put(ws, sh.Row, 1, 3, total.Count, countStyle);
        Put(ws, sh.Row, 4, 3, total.Entries, entriesStyle);
        Put(ws, sh.Row, 7, 3, total.Exits, exitsStyle);
        Put(ws, sh.Row, 10, 3, total.Net, netStyle);
        Put(ws, sh.Row, 13, 6, "Sorties en rouge", readStyle);
        sh.Row++;
        sh.Skip();

        var headers = new[]
        {
            "DATE", "HEURE", "TICKET", "FACTURE", "CLIENT / LIBELLÉ", "SENS", "NATURE", "MODE DE PAIEMENT",
            "ENTRÉE", "SORTIE", "SOLDE CUMULÉ", "STATUT SAGE", "CAISSIER", "RÉFÉRENCE", "BANQUE", "N° PIÈCE",
            "ÉCHÉANCE", "OBSERVATION",
        };
        for (var c = 0; c < headers.Length; c++) Put(ws, sh.Row, c + 1, 1, headers[c], Header);
        sh.Row++;
        var freezeRows = sh.Row - 1;

        long running = 0;
        for (var index = 0; index < ops.Count; index++)
        {
            var op = ops[index];
            var outgoing = op.Direction == "SORTIE";
            running += outgoing ? -op.Amount : op.Amount;
            var bg = outgoing ? RedSoft : index % 2 == 1 ? GreenPale : White;
            St Shared() { var s = Body; s.Bg = bg; return s; }
            var r = sh.Row;

            var dateStyle = Shared(); dateStyle.Format = "dd/mm/yyyy"; dateStyle.Align = "center";
            Put(ws, r, 1, 1, op.TransactionDate, dateStyle);
            var timeStyle = Shared(); timeStyle.Align = "center";
            Put(ws, r, 2, 1, Fmt.Time(op.CreatedAt), timeStyle);
            var ticketStyle = Shared(); ticketStyle.Bold = true;
            Put(ws, r, 3, 1, op.TicketNumber, ticketStyle);
            Put(ws, r, 4, 1, op.InvoiceNumber, Shared());
            Put(ws, r, 5, 1, op.CustomerName, Shared());
            var senseStyle = Shared(); senseStyle.Align = "center"; senseStyle.Bold = true; senseStyle.Color = outgoing ? RedDark : GreenDark;
            Put(ws, r, 6, 1, outgoing ? "Sortie" : "Entrée", senseStyle);
            Put(ws, r, 7, 1, Domain.NatureLabel(op.TransactionNature), Shared());
            Put(ws, r, 8, 1, Domain.ChannelLabel(op.Channel), Shared());
            if (!outgoing) Put(ws, r, 9, 1, op.Amount, Money(Shared())); else Put(ws, r, 9, 1, null, Shared());
            if (outgoing)
            {
                var m = Money(Shared()); m.Bold = true; m.Color = RedDark;
                Put(ws, r, 10, 1, op.Amount, m);
            }
            else Put(ws, r, 10, 1, null, Shared());
            var runStyle = Money(Shared()); runStyle.Bold = true; runStyle.Color = running < 0 ? RedDark : Ink;
            Put(ws, r, 11, 1, running, runStyle);
            var sage = Shared(); sage.Align = "center"; sage.Bold = true;
            if (op.SageStatus == Domain.ToImpute) { sage.Bg = AmberSoft; sage.Color = Amber; }
            else { sage.Bg = GreenSoft; sage.Color = Green; }
            Put(ws, r, 12, 1, op.SageStatus, sage);
            Put(ws, r, 13, 1, op.CashierName, Shared());
            Put(ws, r, 14, 1, op.Reference, Shared());
            Put(ws, r, 15, 1, op.BankName, Shared());
            Put(ws, r, 16, 1, op.DocumentNumber, Shared());
            var matStyle = Shared(); matStyle.Format = "dd/mm/yyyy"; matStyle.Align = "center";
            Put(ws, r, 17, 1, op.MaturityDate, matStyle);
            var noteStyle = Shared(); noteStyle.Wrap = true;
            Put(ws, r, 18, 1, op.Notes, noteStyle);
            sh.Row++;
        }

        if (ops.Count == 0)
        {
            var empty = Body; empty.Align = "center"; empty.Italic = true; empty.Color = Muted; empty.Bg = GreenPale; empty.Height = 35;
            Put(ws, sh.Row, 1, DetailColumns, "Aucune opération pour cette rubrique sur la période sélectionnée.", empty);
            sh.Row++;
        }

        for (var c = 1; c <= 7; c++) Put(ws, sh.Row, c, 1, null, TotalSt);
        var totalLabel = TotalSt; totalLabel.Align = "right";
        Put(ws, sh.Row, 8, 1, "TOTAL", totalLabel);
        Put(ws, sh.Row, 9, 1, total.Entries, Money(TotalSt));
        var totalExit = Money(TotalSt); totalExit.Color = "#FF9A92";
        Put(ws, sh.Row, 10, 1, total.Exits, totalExit);
        Put(ws, sh.Row, 11, 1, total.Net, Money(TotalSt));
        for (var c = 12; c <= DetailColumns; c++) Put(ws, sh.Row, c, 1, null, TotalSt);

        for (var c = 0; c < DetailWidths.Length; c++) ws.Column(c + 1).Width = DetailWidths[c];
        ws.ShowGridLines = false;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.SheetView.ZoomScale = 78;
        ws.SheetView.FreezeRows(Math.Min(freezeRows, 12));
        ws.SheetView.FreezeColumns(3);
    }

    // ------- synthèse

    private static string SessionStatus(CashSession s) => s.Status == "OPEN" ? "En cours" : s.ClosedLate ? "Fermée tardivement" : "Fermée";

    private static void SummarySheet(IXLWorkbook wb, Input input)
    {
        const int columns = 11;
        var ws = wb.Worksheets.Add("Synthèse");
        var sh = new Sheet { Ws = ws, Columns = columns };
        var total = Summarize(input.Operations);
        var pending = input.Operations.Where(o => o.SageStatus == Domain.ToImpute).ToList();

        TitleRows(sh, $"Rapport {Domain.CompanyLabel(input.Company)}", "SYNTHÈSE DE CAISSE");
        var ctx = Context; ctx.Bold = true; ctx.Color = Ink;
        sh.Line(Context_(input), ctx);
        sh.Line($"Édité le {Fmt.DateTime(MadagascarTime.Now)} · les vues ci-dessous se recoupent et ne doivent pas être additionnées.", Context);
        sh.Skip();
        HighlightedDaily(sh, input, columns);
        sh.Skip();

        Put(ws, sh.Row, 1, 2, "OPÉRATIONS", Header);
        Put(ws, sh.Row, 3, 2, "ENTRÉES", Header);
        Put(ws, sh.Row, 5, 2, "SORTIES", Header);
        Put(ws, sh.Row, 7, 2, "SOLDE NET", Header);
        Put(ws, sh.Row, 9, 3, "À IMPUTER SAGE", Header);
        sh.Row++;

        St Kpi() { var k = Body; k.Align = "center"; k.Bold = true; k.Bg = GreenSoft; k.Height = 34; return k; }
        var a = Kpi(); a.Size = 12;
        var b = Kpi(); b.Size = 11; b.Format = MoneyFormat;
        var c2 = Kpi(); c2.Size = 11; c2.Format = MoneyFormat; c2.Color = Red; c2.Bg = RedSoft;
        var d = Kpi(); d.Size = 11; d.Format = MoneyFormat; d.Color = total.Net < 0 ? RedDark : GreenDark;
        var e = Kpi(); e.Size = 12; e.Bg = AmberSoft; e.Color = Amber;
        Put(ws, sh.Row, 1, 2, total.Count, a);
        Put(ws, sh.Row, 3, 2, total.Entries, b);
        Put(ws, sh.Row, 5, 2, total.Exits, c2);
        Put(ws, sh.Row, 7, 2, total.Net, d);
        Put(ws, sh.Row, 9, 3, pending.Count, e);
        sh.Row++;
        sh.Skip();

        var section = Context; section.Size = 11; section.Bold = true; section.Color = GreenDark; section.Bg = GreenSoft; section.Height = 27;
        sh.Line("Répartition des onglets du classeur", section);
        Put(ws, sh.Row, 1, 7, "RUBRIQUE", Header);
        Put(ws, sh.Row, 8, 1, "OPÉRATIONS", Header);
        Put(ws, sh.Row, 9, 1, "ENTRÉES", Header);
        Put(ws, sh.Row, 10, 1, "SORTIES", Header);
        Put(ws, sh.Row, 11, 1, "SOLDE NET", Header);
        sh.Row++;

        for (var index = 0; index < Defs.Length; index++)
        {
            var def = Defs[index];
            var summary = Summarize(input.Operations.Where(def.Filter));
            var bg = index % 2 == 1 ? GreenPale : White;
            St Shared() { var s = Body; s.Bg = bg; return s; }
            var titleStyle = Shared(); titleStyle.Bold = true;
            Put(ws, sh.Row, 1, 7, def.Title, titleStyle);
            var count = Shared(); count.Align = "center";
            Put(ws, sh.Row, 8, 1, summary.Count, count);
            Put(ws, sh.Row, 9, 1, summary.Entries, Money(Shared()));
            var ex = Money(Shared()); ex.Color = summary.Exits != 0 ? RedDark : Ink;
            Put(ws, sh.Row, 10, 1, summary.Exits, ex);
            var net = Money(Shared()); net.Bold = true; net.Color = summary.Net < 0 ? RedDark : Ink;
            Put(ws, sh.Row, 11, 1, summary.Net, net);
            sh.Row++;
        }

        sh.Skip();
        sh.Line("Clôtures journalières et comptage des espèces", section);
        sh.Line("Recettes espèces du jour : total des comptants, règlements et acomptes reçus en espèces, hors transferts internes d’acompte.", Context);
        var headers = new[]
        {
            "DATE", "CAISSIER", "OUVERTURE", "CLÔTURE", "STATUT / RÉGULARISATION", "SOLDE CLÔTURE VEILLE",
            "RECETTES ESPÈCES DU JOUR", "CAISSE THÉORIQUE", "MONTANT COMPTAGE ESPÈCES", "ÉCART", "EXPLICATION ÉCART",
        };
        for (var c = 0; c < headers.Length; c++) Put(ws, sh.Row, c + 1, 1, headers[c], Header);
        sh.Row++;
        var freezeRows = Math.Min(sh.Row - 1, 13);

        var sessions = input.Sessions.OrderBy(s => s.BusinessDate).ToList();
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            var bg = index % 2 == 1 ? GreenPale : White;
            St Shared() { var s = Body; s.Bg = bg; return s; }
            var status = SessionStatus(session);
            var regularization = session.RegularizedBy != null
                ? $"{status} · {session.RegularizedBy} · {Fmt.DateTime(session.RegularizedAt)}"
                : status;
            var closure = input.Closures.FirstOrDefault(x => x.AgencyCode == session.AgencyCode && x.Company == input.Company && x.ClosureDate == session.BusinessDate);
            var balance = session.CompanyBalances.FirstOrDefault(x => x.Company == input.Company);
            long? previous = null;
            if (balance != null) previous = balance.CarriedBalance;
            else if (closure != null) previous = Math.Max(0L, closure.OpeningCash - closure.CashFund);
            var receipts = input.Operations
                .Where(o => o.AgencyCode == session.AgencyCode && o.Company == input.Company && o.TransactionDate == session.BusinessDate
                            && o.Channel == "ESPECE" && o.Direction == "ENTREE" && o.TransactionNature != "TRANSFERT_ACOMPTE")
                .Sum(o => o.Amount);

            var r = sh.Row;
            var dateStyle = Shared(); dateStyle.Format = "dd/mm/yyyy"; dateStyle.Align = "center";
            Put(ws, r, 1, 1, session.BusinessDate, dateStyle);
            Put(ws, r, 2, 1, session.CashierName, Shared());
            var t1 = Shared(); t1.Align = "center";
            Put(ws, r, 3, 1, Fmt.Time(session.StartedAt), t1);
            var t2 = Shared(); t2.Align = "center";
            Put(ws, r, 4, 1, Fmt.Time(session.ClosedAt), t2);
            var st = Shared(); st.Bold = true;
            st.Color = session.Status == "OPEN" ? Amber : session.ClosedLate ? Red : Green;
            Put(ws, r, 5, 1, regularization, st);
            var prev = Money(Shared()); prev.Bold = true;
            Put(ws, r, 6, 1, previous, prev);
            var rec = Money(Shared()); rec.Bold = true; rec.Color = GreenDark;
            Put(ws, r, 7, 1, receipts, rec);
            Put(ws, r, 8, 1, closure?.CashTheoretical, Money(Shared()));
            Put(ws, r, 9, 1, closure?.CashCounted, Money(Shared()));
            var variance = Money(Shared());
            if (closure != null)
            {
                variance.Bold = true;
                variance.Color = closure.Variance == 0 ? GreenDark : RedDark;
                if (closure.Variance != 0) variance.Bg = RedSoft;
            }
            Put(ws, r, 10, 1, closure?.Variance, variance);
            var comment = Shared(); comment.Wrap = true;
            Put(ws, r, 11, 1, string.IsNullOrEmpty(closure?.Comment) ? null : closure!.Comment, comment);
            sh.Row++;
        }

        if (sessions.Count == 0)
        {
            var empty = Body; empty.Align = "center"; empty.Italic = true; empty.Color = Muted;
            Put(ws, sh.Row, 1, columns, "Aucune ouverture de caisse sur cette période.", empty);
        }

        var widths = new double[] { 14, 22, 14, 14, 34, 22, 26, 21, 21, 18, 42 };
        for (var c = 0; c < widths.Length; c++) ws.Column(c + 1).Width = widths[c];
        ws.ShowGridLines = false;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.SheetView.ZoomScale = 90;
        ws.SheetView.FreezeRows(freezeRows);
    }

    public static byte[] CompanyWorkbook(Input input)
    {
        using var wb = new XLWorkbook();
        SummarySheet(wb, input);
        foreach (var def in Defs) DetailSheet(wb, input, def);
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private static string FilenamePart(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsAsciiLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }
        var text = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
        return text.Length == 0 ? "export" : text;
    }

    /// <summary>Archive zip : {agence}/{société}_{du}_{au}.xlsx pour chaque agence et chaque société.</summary>
    public (byte[] Content, string FileName, int FileCount) BuildArchive(
        DateOnly from, DateOnly to, IEnumerable<(string Code, string Name)> agencies, CashSnapshot snapshot)
    {
        var list = agencies.ToList();
        if (list.Count == 0) throw new DomainException("Aucune agence disponible pour l’export.");

        using var output = new MemoryStream();
        var count = 0;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var agency in list)
            {
                var ops = snapshot.Operations.Where(o => o.AgencyCode == agency.Code).ToList();
                var sessions = snapshot.Sessions.Where(s => s.AgencyCode == agency.Code).ToList();
                var closures = snapshot.Closures.Where(c => c.AgencyCode == agency.Code).ToList();
                foreach (var company in Domain.Companies)
                {
                    var bytes = CompanyWorkbook(new Input
                    {
                        From = from,
                        To = to,
                        Agency = agency,
                        Company = company.Code,
                        Operations = ops.Where(o => o.Company == company.Code).ToList(),
                        Sessions = sessions,
                        Closures = closures.Where(c => c.Company == company.Code).ToList(),
                    });
                    var path = $"{FilenamePart(agency.Name)}/{FilenamePart(company.Label)}_{Fmt.Iso(from)}_{Fmt.Iso(to)}.xlsx";
                    var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    entryStream.Write(bytes, 0, bytes.Length);
                    count++;
                }
            }
        }
        return (output.ToArray(), $"rapports-caisse-par-agence_{Fmt.Iso(from)}_{Fmt.Iso(to)}.zip", count);
    }
}
