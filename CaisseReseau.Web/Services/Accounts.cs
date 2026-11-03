using System.Security.Claims;
using System.Security.Cryptography;
using CaisseReseau.Web.Models;

namespace CaisseReseau.Web.Services;

/// <summary>Compte connecté (équivalent d'AppAccount / RequestAccount).</summary>
public sealed class AccountInfo
{
    public string Email { get; init; } = "";
    public string Role { get; init; } = Roles.Agency;
    public string? AgencyCode { get; init; }
    public string AgencyName { get; init; } = "Toutes les agences";
    public bool CanRegularizeClosures { get; init; }
    public string? RegularizationExcludedAgencyCode { get; init; }

    public bool IsDirection => Role == Roles.Direction;
    public bool ReadOnly => Role == Roles.Supervisor;
    public bool CanViewAll => Role != Roles.Agency;

    // lib/access-control.ts
    public bool HasCashWriteRole => Role == Roles.Agency || Role == Roles.SupervisorAgency;

    public bool HasAgencyWriteAccess(string agencyCode) =>
        HasCashWriteRole && !string.IsNullOrEmpty(AgencyCode) && AgencyCode == agencyCode;

    public bool HasClosureRegularizationAccess(string? agencyCode = null)
    {
        var granted = IsDirection || CanRegularizeClosures;
        if (!granted || string.IsNullOrEmpty(agencyCode)) return granted;
        var excluded = RegularizationExcludedAgencyCode?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(excluded) || excluded != agencyCode.Trim().ToUpperInvariant();
    }

    public string? WritableAgency =>
        !string.IsNullOrEmpty(AgencyCode) && HasAgencyWriteAccess(AgencyCode) ? AgencyCode : null;

    public bool CanWrite => WritableAgency != null;

    public bool DirectSupervisor => HasClosureRegularizationAccess() && !IsDirection;

    public ClaimsPrincipal ToPrincipal(string scheme)
    {
        var claims = new List<Claim>
        {
            new("email", Email),
            new("role", Role),
            new("agency_code", AgencyCode ?? ""),
            new("agency_name", AgencyName),
            new("can_regularize", CanRegularizeClosures ? "1" : "0"),
            new("regularization_excluded_agency", RegularizationExcludedAgencyCode ?? ""),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme, "email", "role"));
    }

    public static AccountInfo? FromPrincipal(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        var email = user.FindFirst("email")?.Value;
        var role = user.FindFirst("role")?.Value;
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(role) || !Roles.All.Contains(role)) return null;
        var agency = user.FindFirst("agency_code")?.Value;
        var excluded = user.FindFirst("regularization_excluded_agency")?.Value;
        return new AccountInfo
        {
            Email = email,
            Role = role,
            AgencyCode = string.IsNullOrEmpty(agency) ? null : agency,
            AgencyName = user.FindFirst("agency_name")?.Value ?? "Toutes les agences",
            CanRegularizeClosures = user.FindFirst("can_regularize")?.Value == "1",
            RegularizationExcludedAgencyCode = string.IsNullOrEmpty(excluded) ? null : excluded,
        };
    }
}

/// <summary>Hachage PBKDF2-SHA256 (sans dépendance externe).</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
