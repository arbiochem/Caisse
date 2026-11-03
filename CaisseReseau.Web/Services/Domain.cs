using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CaisseReseau.Web.Services;

public sealed record CompanyDef(string Code, string Label, string Prefix);

public sealed record ChannelDef(string Value, string Label, string Short, string Icon);

/// <summary>Constantes métier (équivalent de lib/caisse-types.ts).</summary>
public static class Domain
{
    public static readonly CompanyDef[] Companies =
    {
        new("ARBIOCHEM", "ARBIOCHEM", "ARB"),
        new("ACTIVO", "ACTIVO", "ACT"),
        new("FEED", "FEED", "FED"),
        new("CAISSE_15", "Caisse 15", "C15"),
        new("AVANCE_POUSSIN", "Avance poussin", "AVP"),
        new("VENTE_INTERNE", "Vente œuf et poulet en interne", "VOI"),
        new("VETCARE_CENTRALISE", "Vetcare centralisé", "VTC"),
    };

    public static readonly ChannelDef[] Channels =
    {
        new("ESPECE", "Espèces", "Espèces", "cash"),
        new("ORANGE_MONEY", "Orange Money", "Orange Money", "phone"),
        new("MVOLA", "MVola", "MVola", "phone"),
        new("TPE", "TPE / carte", "TPE", "card"),
        new("VIREMENT_VERSEMENT", "Virement / versement", "Virement", "bank"),
        new("CHEQUE", "Chèque", "Chèque", "cheque"),
        new("TRAITE", "Traite", "Traite", "calendar"),
    };

    public static readonly (string Code, string Name)[] Agencies =
    {
        ("AMB", "Ambohimangakely"),
        ("ANA", "Analakely"),
        ("ANT", "Antanimora"),
        ("DIE", "Diégo"),
        ("FAMB", "FEED Ambohimangakely"),
        ("IME", "Imerintsiatosika"),
        ("MAH", "Mahitsy"),
        ("MAJ", "Mahajanga"),
        ("SAK", "Sakay"),
        ("TAM", "Tamatave"),
        ("TOL", "Toliara"),
        ("VAM", "Vetcare Ambatomitsangana"),
        ("VIV", "Vetcare Ivato"),
    };

    public static readonly string[] EntryNatures = { "AU_COMPTANT", "REGLEMENT", "ACOMPTE" };

    public static readonly string[] AllNatures =
    {
        "AU_COMPTANT", "REGLEMENT", "ACOMPTE", "VERSEMENT_DEGAGEMENT", "TRANSFERT_ACOMPTE",
    };

    public static readonly long[] Denominations = { 20000, 10000, 5000, 2000, 1000, 500, 200, 100 };

    public const string RegularClosureWindowLabel = "11 h 00–18 h 30";
    public const string Imputed = "Imputé";
    public const string ToImpute = "À imputer";

    public static string CompanyLabel(string code) =>
        Companies.FirstOrDefault(c => c.Code == code)?.Label ?? code;

    public static string CompanyPrefix(string code) =>
        Companies.FirstOrDefault(c => c.Code == code)?.Prefix ?? "SOC";

    public static string CompanyClass(string code) => code switch
    {
        "ARBIOCHEM" => "company-arbio",
        "ACTIVO" => "company-activo",
        "FEED" => "company-feed",
        _ => "company-other",
    };

    public static string ChannelLabel(string value)
    {
        if (value == "ESPECE_COMPTANT" || value == "ESPECE_REGLEMENT") return "Espèces";
        return Channels.FirstOrDefault(c => c.Value == value)?.Short ?? value;
    }

    public static string ChannelFullLabel(string value) =>
        Channels.FirstOrDefault(c => c.Value == value)?.Label ?? value;

    public static string ChannelIcon(string value) =>
        Channels.FirstOrDefault(c => c.Value == value)?.Icon ?? "cash";

    public static string NatureLabel(string value) => value switch
    {
        "AU_COMPTANT" => "Au comptant",
        "REGLEMENT" => "Règlement",
        "ACOMPTE" => "Acompte client",
        "VERSEMENT_DEGAGEMENT" => "Versement / dégagement",
        "TRANSFERT_ACOMPTE" => "Transfert d’acompte",
        _ => value,
    };

    private static readonly Regex FrInvoice = new(@"^FR[A-Z]{0,5}[-/ ]?[0-9]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsFrInvoice(string? value) => !string.IsNullOrWhiteSpace(value) && FrInvoice.IsMatch(value.Trim());
}

/// <summary>Formats d'affichage (équivalent de formatMoney, formatDate, etc. avec la locale fr-FR).</summary>
public static class Fmt
{
    private static readonly string[] Months =
    {
        "janv.", "févr.", "mars", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc.",
    };

    private const char GroupSeparator = ' ';

    public static string Number(long value)
    {
        var negative = value < 0;
        var digits = Math.Abs((decimal)value).ToString("0", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(GroupSeparator);
            sb.Append(digits[i]);
        }
        return (negative ? "-" : "") + sb;
    }

    public static string Money(long value, bool compact = false)
    {
        if (compact && Math.Abs(value) >= 1_000_000)
        {
            var millions = Math.Round(value / 1_000_000d, 1, MidpointRounding.AwayFromZero);
            return millions.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',') + " M Ar";
        }
        return Number(value) + " Ar";
    }

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Date(DateOnly date) =>
        $"{date.Day:00} {Months[date.Month - 1]} {date.Year}";

    /// <summary>dd/MM/yyyy (utilisé sur les tickets).</summary>
    public static string DateShort(DateOnly date) => $"{date.Day:00}/{date.Month:00}/{date.Year}";

    public static string Time(DateTimeOffset? value)
    {
        if (value is null) return "—";
        var local = value.Value.ToOffset(MadagascarTime.Offset);
        return local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public static string DateTime(DateTimeOffset? value)
    {
        if (value is null) return "—";
        var local = value.Value.ToOffset(MadagascarTime.Offset);
        return local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }
}

public static class MadagascarTime
{
    /// <summary>Madagascar (Indian/Antananarivo) : UTC+3 toute l'année, sans heure d'été.</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    public static DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(Offset);

    public static DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public static DateOnly DateOf(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.ToOffset(Offset).DateTime);

    /// <summary>BEFORE avant 11 h 00, OPEN jusqu'à 18 h 30 incluse, AFTER ensuite.</summary>
    public static string ClosureWindowStatus(DateTimeOffset value)
    {
        var minutes = value.ToOffset(Offset).Hour * 60 + value.ToOffset(Offset).Minute;
        if (minutes < 11 * 60) return "BEFORE";
        if (minutes <= 18 * 60 + 30) return "OPEN";
        return "AFTER";
    }

    public static bool IsRegularClosureWindow(DateOnly businessDate, DateTimeOffset at) =>
        DateOf(at) == businessDate && ClosureWindowStatus(at) == "OPEN";

    /// <summary>Journée antérieure, ou journée du jour après 18 h 31.</summary>
    public static bool IsCashDayOverdue(DateOnly businessDate, DateTimeOffset? at = null)
    {
        var now = at ?? Now;
        var today = DateOf(now);
        return businessDate < today || (businessDate == today && ClosureWindowStatus(now) == "AFTER");
    }
}

/// <summary>Erreur métier affichée à l'utilisateur (équivalent d'ApiError).</summary>
public sealed class DomainException : Exception
{
    public int Status { get; }

    public DomainException(string message, int status = 400) : base(message)
    {
        Status = status;
    }
}
