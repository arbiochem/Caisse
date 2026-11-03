using System.Text;
using CaisseReseau.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CaisseReseau.Web.Data;

public class CaisseDbContext : DbContext
{
    public CaisseDbContext(DbContextOptions<CaisseDbContext> options) : base(options) { }

    public DbSet<Agency> Agencies => Set<Agency>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<AccountAccess> Accounts => Set<AccountAccess>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashSessionCompanyBalance> CashSessionCompanyBalances => Set<CashSessionCompanyBalance>();
    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();
    public DbSet<CashOperation> CashOperations => Set<CashOperation>();
    public DbSet<CompanyDailyClosure> CompanyDailyClosures => Set<CompanyDailyClosure>();
    public DbSet<DepositAllocation> DepositAllocations => Set<DepositAllocation>();
    public DbSet<TicketPrintEvent> TicketPrintEvents => Set<TicketPrintEvent>();
    public DbSet<TicketSequence> TicketSequences => Set<TicketSequence>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Agency>(e =>
        {
            e.ToTable("agencies");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.LoginEmail).HasMaxLength(200);
            e.HasIndex(x => x.LoginEmail).IsUnique();
        });

        b.Entity<Company>(e =>
        {
            e.ToTable("companies");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.TicketPrefix).HasMaxLength(10);
        });

        b.Entity<AccountAccess>(e =>
        {
            e.ToTable("account_access");
            e.HasKey(x => x.Email);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Role).HasMaxLength(30);
            e.Property(x => x.AgencyCode).HasMaxLength(20);
            e.Property(x => x.RegularizationExcludedAgencyCode).HasMaxLength(20);
            e.Property(x => x.PasswordHash).HasMaxLength(300);
            e.Property(x => x.ResetTokenHash).HasMaxLength(100);
            e.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyCode).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Agency>().WithMany().HasForeignKey(x => x.RegularizationExcludedAgencyCode).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CashSession>(e =>
        {
            e.ToTable("cash_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.AgencyCode).HasMaxLength(20);
            e.Property(x => x.CashierName).HasMaxLength(100);
            e.Property(x => x.StartedBy).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.ClosedBy).HasMaxLength(200);
            e.Property(x => x.LateClosureReason).HasMaxLength(400);
            e.Property(x => x.RegularizedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.AgencyCode, x.BusinessDate }).IsUnique();
            e.HasOne(x => x.Agency).WithMany().HasForeignKey(x => x.AgencyCode).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.CompanyBalances).WithOne(x => x.CashSession).HasForeignKey(x => x.CashSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CashSessionCompanyBalance>(e =>
        {
            e.ToTable("cash_session_company_balances");
            e.HasKey(x => new { x.CashSessionId, x.Company });
            e.Property(x => x.AgencyCode).HasMaxLength(20);
            e.Property(x => x.Company).HasMaxLength(40);
        });

        b.Entity<CashierShift>(e =>
        {
            e.ToTable("cashier_shifts");
            e.HasKey(x => x.Id);
            e.Property(x => x.AgencyCode).HasMaxLength(20);
            e.Property(x => x.CashierName).HasMaxLength(100);
            e.Property(x => x.Action).HasMaxLength(20);
            e.Property(x => x.RecordedBy).HasMaxLength(200);
            e.HasOne<CashSession>().WithMany().HasForeignKey(x => x.CashSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CashOperation>(e =>
        {
            e.ToTable("cash_operations");
            e.HasKey(x => x.Id);
            e.Property(x => x.AgencyCode).HasMaxLength(20);
            e.Property(x => x.AgencyName).HasMaxLength(100);
            e.Property(x => x.Company).HasMaxLength(40);
            e.Property(x => x.Channel).HasMaxLength(40);
            e.Property(x => x.Direction).HasMaxLength(10);
            e.Property(x => x.TicketNumber).HasMaxLength(120);
            e.Property(x => x.InvoiceNumber).HasMaxLength(120);
            e.Property(x => x.CustomerName).HasMaxLength(160);
            e.Property(x => x.Reference).HasMaxLength(160);
            e.Property(x => x.BankName).HasMaxLength(80);
            e.Property(x => x.DocumentNumber).HasMaxLength(80);
            e.Property(x => x.SageStatus).HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(400);
            e.Property(x => x.CashierName).HasMaxLength(100);
            e.Property(x => x.LastPrintedBy).HasMaxLength(200);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.Property(x => x.TransactionNature).HasMaxLength(40);
            e.Property(x => x.SageStatusUpdatedBy).HasMaxLength(200);
            e.Property(x => x.ClientOperationId).HasMaxLength(100);
            e.HasIndex(x => x.TicketNumber).IsUnique();
            e.HasIndex(x => x.ClientOperationId).IsUnique().HasFilter("[client_operation_id] IS NOT NULL");
            e.HasIndex(x => new { x.AgencyCode, x.TransactionDate });
            e.HasOne(x => x.CashSession).WithMany().HasForeignKey(x => x.CashSessionId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CompanyDailyClosure>(e =>
        {
            e.ToTable("company_daily_closures");
            e.HasKey(x => x.Id);
            e.Property(x => x.AgencyCode).HasMaxLength(20);
            e.Property(x => x.AgencyName).HasMaxLength(100);
            e.Property(x => x.Company).HasMaxLength(40);
            e.Property(x => x.Comment).HasMaxLength(500);
            e.Property(x => x.CashierName).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(30);
            e.Property(x => x.SubmittedBy).HasMaxLength(200);
            e.Property(x => x.LateReason).HasMaxLength(400);
            e.Property(x => x.RegularizedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.AgencyCode, x.Company, x.ClosureDate }).IsUnique();
        });

        b.Entity<DepositAllocation>(e =>
        {
            e.ToTable("deposit_allocations");
            e.HasKey(x => x.Id);
            e.Property(x => x.AgencyCode).HasMaxLength(20);
            e.Property(x => x.SourceCompany).HasMaxLength(40);
            e.Property(x => x.TargetCompany).HasMaxLength(40);
            e.Property(x => x.InvoiceNumber).HasMaxLength(120);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.Property(x => x.ClientAllocationId).HasMaxLength(100);
            e.HasIndex(x => x.ClientAllocationId).IsUnique().HasFilter("[client_allocation_id] IS NOT NULL");
            e.HasOne(x => x.SourceOperation).WithMany().HasForeignKey(x => x.SourceOperationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.TransferOutOperation).WithMany().HasForeignKey(x => x.TransferOutOperationId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.TransferInOperation).WithMany().HasForeignKey(x => x.TransferInOperationId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<TicketPrintEvent>(e =>
        {
            e.ToTable("ticket_print_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.ClientPrintId).HasMaxLength(100);
            e.Property(x => x.PrintedBy).HasMaxLength(200);
            e.HasIndex(x => x.ClientPrintId).IsUnique();
            e.HasOne(x => x.Operation).WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TicketSequence>(e =>
        {
            e.ToTable("ticket_sequences");
            e.HasKey(x => new { x.AgencyCode, x.Company, x.CalendarYear });
            e.Property(x => x.AgencyCode).HasMaxLength(20);
            e.Property(x => x.Company).HasMaxLength(40);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        // Noms de colonnes en snake_case, comme dans la base d'origine.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 6);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

public static class CaisseDbContextExtensions
{
    /// <summary>Session du jour avec ses soldes par société (lecture seule).</summary>
    public static Task<CashSession?> CashSessionsReadOnly(this CaisseDbContext db, string agencyCode, DateOnly date) =>
        db.CashSessions.AsNoTracking().Include(s => s.CompanyBalances)
            .FirstOrDefaultAsync(s => s.AgencyCode == agencyCode && s.BusinessDate == date);
}
