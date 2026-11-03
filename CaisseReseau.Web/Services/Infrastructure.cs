using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using CaisseReseau.Web.Data;
using CaisseReseau.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CaisseReseau.Web.Services;

/// <summary>Limite les tentatives de connexion répétées (par e-mail), en mémoire.</summary>
public sealed class LoginThrottle
{
    private const int MaxAttempts = 8;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _attempts = new();

    public bool IsBlocked(string key)
    {
        if (!_attempts.TryGetValue(key, out var list)) return false;
        lock (list)
        {
            list.RemoveAll(t => t < DateTimeOffset.UtcNow - Window);
            return list.Count >= MaxAttempts;
        }
    }

    public void Fail(string key)
    {
        var list = _attempts.GetOrAdd(key, _ => new List<DateTimeOffset>());
        lock (list) list.Add(DateTimeOffset.UtcNow);
    }

    public void Reset(string key) => _attempts.TryRemove(key, out _);
}

/// <summary>Envoi d'e-mails via SMTP (réinitialisation du mot de passe).</summary>
public sealed class EmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration config, ILogger<EmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool Configured => !string.IsNullOrWhiteSpace(_config["Smtp:Host"]);

    public async Task<bool> SendAsync(string to, string subject, string body)
    {
        if (!Configured)
        {
            _logger.LogWarning("SMTP non configuré : e-mail « {Subject} » non envoyé.", subject);
            return false;
        }
        try
        {
            using var client = new SmtpClient(_config["Smtp:Host"], _config.GetValue("Smtp:Port", 587))
            {
                EnableSsl = _config.GetValue("Smtp:EnableSsl", true),
            };
            var user = _config["Smtp:User"];
            if (!string.IsNullOrEmpty(user))
                client.Credentials = new NetworkCredential(user, _config["Smtp:Password"]);
            using var message = new MailMessage(_config["Smtp:From"] ?? "caisse@localhost", to, subject, body);
            await client.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec d'envoi de l'e-mail à {To}", to);
            return false;
        }
    }
}

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaisseDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (config.GetValue("Database:AutoCreate", false))
            await db.Database.EnsureCreatedAsync();

        if (!await db.Companies.AnyAsync())
        {
            var order = 0;
            foreach (var c in Domain.Companies)
                db.Companies.Add(new Company { Code = c.Code, DisplayName = c.Label, TicketPrefix = c.Prefix, SortOrder = ++order });
        }

        foreach (var (code, name) in Domain.Agencies)
        {
            if (!await db.Agencies.AnyAsync(a => a.Code == code))
                db.Agencies.Add(new Agency { Code = code, Name = name, LoginEmail = $"{code.ToLowerInvariant()}@a-configurer.invalid" });
        }
        await db.SaveChangesAsync();

        var adminEmail = (config["Seed:AdminEmail"] ?? "").Trim().ToLowerInvariant();
        var adminPassword = config["Seed:AdminPassword"] ?? "";
        if (adminEmail.Length > 0 && adminPassword.Length >= 10 && !await db.Accounts.AnyAsync(a => a.Email == adminEmail))
        {
            db.Accounts.Add(new AccountAccess
            {
                Email = adminEmail,
                Role = Roles.Direction,
                CanRegularizeClosures = true,
                PasswordHash = PasswordHasher.Hash(adminPassword),
            });
            await db.SaveChangesAsync();
        }
    }
}
