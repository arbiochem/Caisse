using CaisseReseau.Web.Models;
using CaisseReseau.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaisseReseau.Web.Controllers;

/// <summary>Gestion des comptes (remplace Supabase Authentication) — réservée à la Direction.</summary>
public class AdminController : BaseController
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!Account.IsDirection) return RedirectToAction("Index", "Home");
        return await Render();
    }

    [HttpPost]
    public async Task<IActionResult> Save(string? email, string? role, string? agencyCode, bool canRegularize, string? excludedAgency, string? password, bool active)
    {
        if (!Account.IsDirection) return RedirectToAction("Index", "Home");

        var normalized = (email ?? "").Trim().ToLowerInvariant();
        if (normalized.Length < 5 || !normalized.Contains('@')) return await Render(error: "Adresse e-mail invalide.");
        if (role == null || !Roles.All.Contains(role)) return await Render(error: "Rôle invalide.");
        var needsAgency = role == Roles.Agency || role == Roles.SupervisorAgency;
        var agency = string.IsNullOrWhiteSpace(agencyCode) ? null : agencyCode.Trim().ToUpperInvariant();
        if (needsAgency && (agency == null || !await Db.Agencies.AnyAsync(a => a.Code == agency)))
            return await Render(error: "Une agence valide est obligatoire pour ce rôle.");
        if (!needsAgency) agency = null;
        var excluded = string.IsNullOrWhiteSpace(excludedAgency) ? null : excludedAgency.Trim().ToUpperInvariant();
        if (excluded != null && !await Db.Agencies.AnyAsync(a => a.Code == excluded))
            return await Render(error: "Agence exclue de la régularisation inconnue.");

        var row = await Db.Accounts.FirstOrDefaultAsync(a => a.Email == normalized);
        var creating = row == null;
        if (row == null)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 10)
                return await Render(error: "Un mot de passe d’au moins 10 caractères est obligatoire pour un nouveau compte.");
            row = new AccountAccess { Email = normalized };
            Db.Accounts.Add(row);
        }
        row.Role = role;
        row.AgencyCode = agency;
        row.CanRegularizeClosures = canRegularize;
        row.RegularizationExcludedAgencyCode = excluded;
        row.Active = active;
        if (!string.IsNullOrEmpty(password))
        {
            if (password.Length < 10) return await Render(error: "Le mot de passe doit contenir au moins 10 caractères.");
            row.PasswordHash = PasswordHasher.Hash(password);
            row.ResetTokenHash = null;
            row.ResetTokenExpiresAt = null;
        }
        if (!creating && normalized == Account.Email && (!active || role != Roles.Direction))
            return await Render(error: "Vous ne pouvez pas retirer vos propres droits de Direction.");

        await Db.SaveChangesAsync();
        return await Render(notice: creating ? "Compte créé." : "Compte mis à jour.");
    }

    private async Task<IActionResult> Render(string? error = null, string? notice = null)
    {
        ResolveAgency();
        ViewData["View"] = "admin";
        var accounts = await Db.Accounts.AsNoTracking().OrderBy(a => a.Email).ToListAsync();
        return View("Index", new AdminVm { Accounts = accounts, Agencies = VisibleAgencies, Error = error, Notice = notice });
    }
}
