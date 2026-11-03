using System.Security.Claims;
using CaisseReseau.Web.Data;
using CaisseReseau.Web.Models;
using CaisseReseau.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaisseReseau.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private static readonly string DummyHash = PasswordHasher.Hash("dummy-password-for-timing");
    private readonly CaisseDbContext _db;
    private readonly LoginThrottle _throttle;
    private readonly EmailSender _email;

    public AccountController(CaisseDbContext db, LoginThrottle throttle, EmailSender email)
    {
        _db = db;
        _throttle = throttle;
        _email = email;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View("Auth", new AuthVm { Screen = "LOGIN", ReturnUrl = returnUrl, Notice = TempData["Notice"] as string });
    }

    [HttpPost]
    public async Task<IActionResult> Login(string? email, string? password, string? returnUrl)
    {
        var vm = new AuthVm { Screen = "LOGIN", Email = email ?? "", ReturnUrl = returnUrl };
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        var key = normalized + "|" + HttpContext.Connection.RemoteIpAddress;
        if (_throttle.IsBlocked(key))
        {
            vm.Error = "Trop de tentatives. Patientez quelques minutes avant de réessayer.";
            return View("Auth", vm);
        }

        var account = normalized.Length == 0 ? null : await _db.Accounts.FirstOrDefaultAsync(a => a.Email == normalized && a.Active);
        var ok = PasswordHasher.Verify(password ?? "", account?.PasswordHash ?? DummyHash) && account != null;
        if (!ok)
        {
            _throttle.Fail(key);
            vm.Error = "Adresse e-mail ou mot de passe incorrect.";
            return View("Auth", vm);
        }

        _throttle.Reset(key);
        var agency = account!.AgencyCode == null ? null : await _db.Agencies.AsNoTracking().FirstOrDefaultAsync(a => a.Code == account.AgencyCode);
        var info = new AccountInfo
        {
            Email = account.Email,
            Role = account.Role,
            AgencyCode = account.AgencyCode,
            AgencyName = agency?.Name ?? "Toutes les agences",
            CanRegularizeClosures = account.CanRegularizeClosures,
            RegularizationExcludedAgencyCode = account.RegularizationExcludedAgencyCode,
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, info.ToPrincipal(CookieAuthenticationDefaults.AuthenticationScheme));
        Response.Cookies.Delete(BaseController.AgencyCookie);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Response.Cookies.Delete(BaseController.AgencyCookie);
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View("Auth", new AuthVm { Screen = "REQUEST_RESET" });

    [HttpPost]
    public async Task<IActionResult> ForgotPassword(string? email)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        var vm = new AuthVm
        {
            Screen = "REQUEST_RESET",
            Email = normalized,
            Notice = "Si cette adresse correspond à un compte actif, un nouveau lien vient d’être envoyé. Utilisez uniquement le courriel le plus récent.",
        };

        var key = "reset|" + normalized + "|" + HttpContext.Connection.RemoteIpAddress;
        if (_throttle.IsBlocked(key)) return View("Auth", vm);
        _throttle.Fail(key);

        var account = normalized.Length == 0 ? null : await _db.Accounts.FirstOrDefaultAsync(a => a.Email == normalized && a.Active);
        if (account != null)
        {
            var token = PasswordHasher.NewToken();
            account.ResetTokenHash = PasswordHasher.HashToken(token);
            account.ResetTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
            await _db.SaveChangesAsync();
            var link = Url.Action("ResetPassword", "Account", new { email = account.Email, token }, Request.Scheme);
            await _email.SendAsync(account.Email, "Caisse Réseau — nouveau mot de passe",
                "Bonjour,\n\nPour choisir un nouveau mot de passe, ouvrez ce lien (valable une heure, usage unique) :\n" + link +
                "\n\nSi vous n’êtes pas à l’origine de cette demande, ignorez ce message.");
        }
        return View("Auth", vm);
    }

    [HttpGet]
    public IActionResult ResetPassword(string? email, string? token) =>
        View("Auth", new AuthVm { Screen = "UPDATE_PASSWORD", Email = email ?? "", Token = token });

    [HttpPost]
    public async Task<IActionResult> ResetPassword(string? email, string? token, string? newPassword, string? passwordConfirmation)
    {
        var vm = new AuthVm { Screen = "UPDATE_PASSWORD", Email = email ?? "", Token = token };
        if ((newPassword ?? "").Length < 8)
        {
            vm.Error = "Le nouveau mot de passe doit contenir au moins 8 caractères.";
            return View("Auth", vm);
        }
        if (newPassword != passwordConfirmation)
        {
            vm.Error = "Les deux mots de passe ne sont pas identiques.";
            return View("Auth", vm);
        }

        var normalized = (email ?? "").Trim().ToLowerInvariant();
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Email == normalized && a.Active);
        var valid = account?.ResetTokenHash != null
                    && account.ResetTokenExpiresAt > DateTimeOffset.UtcNow
                    && !string.IsNullOrEmpty(token)
                    && PasswordHasher.HashToken(token) == account.ResetTokenHash;
        if (!valid)
        {
            vm.Error = "Ce lien n’est plus valide. Demandez un nouveau lien.";
            return View("Auth", vm);
        }

        account!.PasswordHash = PasswordHasher.Hash(newPassword!);
        account.ResetTokenHash = null;
        account.ResetTokenExpiresAt = null;
        await _db.SaveChangesAsync();
        TempData["Notice"] = "Votre mot de passe a été modifié. Vous pouvez maintenant vous connecter.";
        return RedirectToAction("Login");
    }
}
