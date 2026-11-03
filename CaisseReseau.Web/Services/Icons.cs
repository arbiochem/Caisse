using Microsoft.AspNetCore.Html;

namespace CaisseReseau.Web.Services;

/// <summary>Icônes SVG identiques à celles du composant React d'origine.</summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["dashboard"] = "<rect x=\"3\" y=\"3\" width=\"7\" height=\"7\" rx=\"2\"/><rect x=\"14\" y=\"3\" width=\"7\" height=\"7\" rx=\"2\"/><rect x=\"3\" y=\"14\" width=\"7\" height=\"7\" rx=\"2\"/><rect x=\"14\" y=\"14\" width=\"7\" height=\"7\" rx=\"2\"/>",
        ["plus"] = "<path d=\"M12 5v14M5 12h14\"/>",
        ["journal"] = "<path d=\"M5 4h14v16H5z\"/><path d=\"M8 8h8M8 12h8M8 16h5\"/>",
        ["close"] = "<path d=\"M4 7h16M6 7l1 14h10l1-14M9 7V4h6v3\"/>",
        ["report"] = "<path d=\"M4 19V9M10 19V5M16 19v-7M22 19H2\"/>",
        ["settings"] = "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M19 12a7 7 0 1 1-14 0 7 7 0 0 1 14 0Z\"/>",
        ["search"] = "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m20 20-4-4\"/>",
        ["chevron"] = "<path d=\"m9 18 6-6-6-6\"/>",
        ["cash"] = "<rect x=\"3\" y=\"6\" width=\"18\" height=\"12\" rx=\"2\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>",
        ["receipt"] = "<path d=\"M5 3h14v18l-3-2-2 2-2-2-2 2-2-2-3 2V3Z\"/><path d=\"M8 8h8M8 12h6\"/>",
        ["phone"] = "<rect x=\"7\" y=\"2\" width=\"10\" height=\"20\" rx=\"2\"/><path d=\"M11 18h2\"/>",
        ["card"] = "<rect x=\"2\" y=\"5\" width=\"20\" height=\"14\" rx=\"3\"/><path d=\"M2 10h20M6 15h4\"/>",
        ["bank"] = "<path d=\"m3 10 9-6 9 6M5 10h14M6 10v8M10 10v8M14 10v8M18 10v8M3 20h18\"/>",
        ["cheque"] = "<rect x=\"2\" y=\"5\" width=\"20\" height=\"14\" rx=\"2\"/><path d=\"M6 10h8M6 14h4M16 14h2\"/>",
        ["calendar"] = "<rect x=\"3\" y=\"5\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M16 3v4M8 3v4M3 10h18\"/>",
        ["check"] = "<path d=\"m5 12 4 4L19 6\"/>",
        ["warning"] = "<path d=\"M12 3 2.5 20h19L12 3Z\"/><path d=\"M12 9v4M12 17h.01\"/>",
        ["download"] = "<path d=\"M12 3v12m0 0 5-5m-5 5-5-5M4 21h16\"/>",
        ["print"] = "<path d=\"M6 9V3h12v6M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2M6 14h12v7H6z\"/>",
        ["menu"] = "<path d=\"M4 7h16M4 12h16M4 17h16\"/>",
        ["arrow-up"] = "<path d=\"m18 15-6-6-6 6\"/>",
        ["arrow-down"] = "<path d=\"m6 9 6 6 6-6\"/>",
        ["clock"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v5l3 2\"/>",
        ["building"] = "<path d=\"M4 21V4h10v17M14 9h6v12M8 8h2M8 12h2M8 16h2\"/>",
        ["user"] = "<circle cx=\"12\" cy=\"8\" r=\"4\"/><path d=\"M4 21a8 8 0 0 1 16 0\"/>",
        ["logout"] = "<path d=\"M10 5H5v14h5M14 8l4 4-4 4M18 12H9\"/>",
        ["cloud"] = "<path d=\"M7 18h11a4 4 0 0 0 .5-8A6 6 0 0 0 7 8.5 4.5 4.5 0 0 0 7 18Z\"/><path d=\"m9 13 3 3 3-3M12 10v6\"/>",
        ["lock"] = "<rect x=\"5\" y=\"10\" width=\"14\" height=\"11\" rx=\"2\"/><path d=\"M8 10V7a4 4 0 0 1 8 0v3\"/>",
        ["swap"] = "<path d=\"M7 7h12l-3-3M17 17H5l3 3\"/>",
    };

    public static IHtmlContent Icon(string name, int size = 20)
    {
        Paths.TryGetValue(name, out var inner);
        return new HtmlString(
            $"<svg width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{inner}</svg>");
    }
}
