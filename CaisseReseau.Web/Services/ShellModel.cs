namespace CaisseReseau.Web.Services;

public sealed class NavItem
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Url { get; init; } = "";
    public string? Badge { get; init; }
}

/// <summary>Données de la coque (menu, barre du haut) calculées une fois par requête.</summary>
public sealed class ShellModel
{
    public AccountInfo Account { get; init; } = new();
    public string ActiveView { get; set; } = "dashboard";
    public string SelectedAgency { get; set; } = "ALL";
    public List<NavItem> Nav { get; init; } = new();
    public List<(string Code, string Name)> Agencies { get; init; } = new();
    public int ClosedAgencies { get; init; }
    public SessionDialogModel? SessionDialog { get; init; }

    public string PageTitle => ActiveView == "admin" ? "Paramètres" : Nav.FirstOrDefault(n => n.Id == ActiveView)?.Label ?? "";
    public string SelectedAgencyName =>
        Agencies.FirstOrDefault(a => a.Code == SelectedAgency).Name ?? "Toutes les agences";
}

public sealed class SessionDialogModel
{
    public string AgencyCode { get; init; } = "";
    public string AgencyName { get; init; } = "";
    public bool Exists { get; init; }
    public string? Status { get; init; }
    public string Cashier { get; init; } = "";
    public long ReportTotal { get; init; }
    public Dictionary<string, long> Reports { get; init; } = new();
}
