using HashKeyChain.Domain;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Data;

/// <summary>
/// EF Core context scoped to the dedicated <c>hashkeychain</c> schema. It only
/// ever reads/writes its own tables; existing tables and data in the shared
/// database are never touched.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public const string Schema = "hashkeychain";

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // Localization demo table (kept for the TradeDemo page).
    public DbSet<TradeRequestRecord> TradeRequests => Set<TradeRequestRecord>();

    // Core business domain.
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradeDocument> TradeDocuments => Set<TradeDocument>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<VerificationRun> VerificationRuns => Set<VerificationRun>();
    public DbSet<DocumentCheck> DocumentChecks => Set<DocumentCheck>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ChainTransaction> ChainTransactions => Set<ChainTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TradeRequestRecord>(e =>
            e.Property(p => p.Amount).HasColumnType("decimal(18,2)"));

        modelBuilder.Entity<Trade>(e =>
        {
            e.HasIndex(t => t.TradeReference).IsUnique();
            e.Property(t => t.PaymentAmount).HasColumnType("decimal(38,18)");
            e.Property(t => t.ExpectedQuantity).HasColumnType("decimal(38,18)");

            e.HasOne(t => t.BuyerCompany).WithMany()
                .HasForeignKey(t => t.BuyerCompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.SellerCompany).WithMany()
                .HasForeignKey(t => t.SellerCompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.VerifierUser).WithMany()
                .HasForeignKey(t => t.VerifierUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.BuyerApproverUser).WithMany()
                .HasForeignKey(t => t.BuyerApproverUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TradeDocument>(e =>
        {
            e.Ignore(d => d.CurrentVersion);
            e.HasOne(d => d.Trade).WithMany(t => t.Documents)
                .HasForeignKey(d => d.TradeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.TradeId, d.DocumentType }).IsUnique();
        });

        modelBuilder.Entity<DocumentVersion>(e =>
        {
            e.HasOne(v => v.TradeDocument).WithMany(d => d.Versions)
                .HasForeignKey(v => v.TradeDocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(v => v.Sha256);
        });

        modelBuilder.Entity<VerificationRun>()
            .HasOne(r => r.Trade).WithMany(t => t.VerificationRuns)
            .HasForeignKey(r => r.TradeId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentCheck>()
            .HasOne(c => c.VerificationRun).WithMany(r => r.Checks)
            .HasForeignKey(c => c.VerificationRunId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.HasOne(a => a.Trade).WithMany(t => t.AuditEntries)
                .HasForeignKey(a => a.TradeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.TimestampUtc);
        });

        modelBuilder.Entity<ChainTransaction>()
            .HasOne(c => c.Trade).WithMany(t => t.ChainTransactions)
            .HasForeignKey(c => c.TradeId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserRole>()
            .HasOne(r => r.User).WithMany(u => u.Roles)
            .HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AppUser>()
            .HasOne(u => u.Company).WithMany(c => c.Users)
            .HasForeignKey(u => u.CompanyId).OnDelete(DeleteBehavior.SetNull);
    }
}
