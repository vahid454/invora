using Invora.Domain.Modules.Businesses;
using Invora.Domain.Modules.Identity;
using Invora.Domain.Modules.Retail;
using Invora.Domain.Modules.Licensing;
using Microsoft.EntityFrameworkCore;
namespace Invora.Infrastructure.Persistence;

public sealed class InvoraDbContext(DbContextOptions<InvoraDbContext> options) : DbContext(options)
{
    public DbSet<BusinessProfile> Businesses => Set<BusinessProfile>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<StaffBranch> StaffBranches => Set<StaffBranch>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshCredential> RefreshCredentials => Set<RefreshCredential>();
    public DbSet<AccessAudit> AccessAudits => Set<AccessAudit>();
    private void ValidateAuditChanges()
    {
        foreach(var sale in ChangeTracker.Entries<Sale>().Where(x=>x.State==EntityState.Modified && x.OriginalValues.GetValue<string>(nameof(Sale.Status))!="Draft"))
            if(sale.Properties.Any(p=>p.IsModified && p.Metadata.Name!=nameof(Sale.Status)))throw new InvalidOperationException("Posted invoice values are immutable.");
        if(ChangeTracker.Entries<Sale>().Any(x=>x.State==EntityState.Deleted && x.OriginalValues.GetValue<string>(nameof(Sale.Status))!="Draft"))throw new InvalidOperationException("Posted invoices cannot be deleted.");
        if(ChangeTracker.Entries<Purchase>().Any(x=>x.State is EntityState.Modified or EntityState.Deleted && x.OriginalValues.GetValue<string>(nameof(Purchase.Status))!="Draft"))throw new InvalidOperationException("Posted purchases are immutable.");

        if (ChangeTracker.Entries().Any(x => (x.Entity is DeviceCorrection or DeviceInspection or BusinessLicense or PaymentFollowUp or DocumentFile or SaleItem or PurchaseItem or LedgerEntry or InventoryMovement or OperationReceipt or Payment or PaymentAllocation or AllocationReversal or SaleReturn or SaleReturnItem or SaleNonCashSettlement or PurchaseReturn or PurchaseReturnItem or NonCashSettlementReversal) && x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Posted history is append-only.");
        if (ChangeTracker.Entries<AccessAudit>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Access audit records are append-only.");
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateAuditChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateAuditChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    protected override void OnModelCreating(ModelBuilder model)
    {
        RetailModel.Configure(model);
        model.Entity<StaffUser>(b =>
        {
            b.HasKey(x => x.Id); b.HasIndex(x => x.Login).IsUnique(); b.Property(x => x.Login).HasMaxLength(100);
            b.Property(x => x.DisplayName).HasMaxLength(200); b.Property(x => x.PasswordHash).HasMaxLength(1000);
            b.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<StaffBranch>(b =>
        {
            b.HasKey(x => new { x.UserId, x.BranchId });
            b.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AuthSession>(b => { b.HasKey(x => x.Id); b.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<RefreshCredential>(b =>
        {
            b.HasKey(x => x.Id); b.Property(x => x.TokenHash).HasMaxLength(64); b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasOne<AuthSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AccessAudit>(b => { b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(100); b.Property(x => x.DetailsJson).HasColumnType("jsonb"); });
        model.Entity<BusinessProfile>(b =>
        {
            b.ToTable("Businesses", table => table.HasCheckConstraint("CK_Business_Singleton", "\"SingletonKey\" = 1"));
            b.HasKey(x => x.Id); b.Property(x => x.TradeName).HasMaxLength(200).IsRequired();
            b.HasIndex(x => x.SingletonKey).IsUnique();
            b.Property(x => x.Currency).HasMaxLength(3); b.Property(x => x.TimeZone).HasMaxLength(100);
            b.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<Branch>(b =>
        {
            b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(20).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<BusinessProfile>().WithMany().HasForeignKey(x => x.BusinessProfileId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
