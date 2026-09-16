using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MindAeonRest.Licence.Data;

/// <summary>Lisans portalı veritabanı (MindAeonRestLicence) — Identity + domain.</summary>
public class LicenceDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantInfo _tenant;

    public LicenceDbContext(DbContextOptions<LicenceDbContext> options, ITenantInfo tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Dealer> Dealers => Set<Dealer>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<PackagePrice> PackagePrices => Set<PackagePrice>();
    public DbSet<AddOnPrice> AddOnPrices => Set<AddOnPrice>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<LicenseHistory> LicenseHistories => Set<LicenseHistory>();
    public DbSet<LicenseRequest> LicenseRequests => Set<LicenseRequest>();
    public DbSet<DemoInstall> DemoInstalls => Set<DemoInstall>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentProviderConfig> PaymentProviderConfigs => Set<PaymentProviderConfig>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<UsageReport> UsageReports => Set<UsageReport>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ---- İndeksler / veri bütünlüğü ----
        // CompanyCode: silinmemiş kayıtlar arasında UNIQUE (filtered)
        b.Entity<Business>().HasIndex(x => x.CompanyCode)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        b.Entity<License>().HasIndex(x => x.LicenseCode).IsUnique().HasFilter("[IsDeleted] = 0");
        b.Entity<License>().HasIndex(x => new { x.HardwareId, x.Status });
        b.Entity<Dealer>().HasIndex(x => x.Name);

        // UsageReport (BusinessId, Period) UNIQUE
        b.Entity<UsageReport>().HasIndex(x => new { x.BusinessId, x.Period })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Period] IS NOT NULL AND [BusinessId] IS NOT NULL");

        // Payment (Provider, ProviderRef) filtered UNIQUE
        b.Entity<Payment>().HasIndex(x => new { x.Provider, x.ProviderRef })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [ProviderRef] IS NOT NULL");

        // ---- Para/oran kolonları ----
        b.Entity<Dealer>().Property(x => x.MarginPercent).HasColumnType("decimal(5,2)");
        b.Entity<PackagePrice>().Property(x => x.BasePrice).HasColumnType("decimal(18,2)");
        b.Entity<Payment>().Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Entity<Payment>().Property(x => x.MarginAmount).HasColumnType("decimal(18,2)");
        b.Entity<Payment>().Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        b.Entity<UsageReport>().Property(x => x.Revenue).HasColumnType("decimal(18,2)");

        // ---- UserId kolonları MaxLength(450) ----
        b.Entity<License>().Property(x => x.IssuedByUserId).HasMaxLength(450);
        b.Entity<LicenseHistory>().Property(x => x.ByUserId).HasMaxLength(450);
        b.Entity<Ticket>().Property(x => x.OpenerUserId).HasMaxLength(450);
        b.Entity<Ticket>().Property(x => x.AssignedToUserId).HasMaxLength(450);
        b.Entity<TicketMessage>().Property(x => x.AuthorUserId).HasMaxLength(450);

        // ---- İlişki / silme davranışları ----
        b.Entity<LicenseHistory>().HasOne(x => x.License).WithMany(x => x.History).HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TicketMessage>().HasOne(x => x.Ticket).WithMany(x => x.Messages).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Business>().HasOne(x => x.Dealer).WithMany(x => x.Businesses).HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.SetNull);

        // Finansal FK'ler: Restrict (kasıtlı silme engeli)
        b.Entity<License>().HasOne(x => x.Business).WithMany(x => x.Licenses).HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<License>().HasOne(x => x.Dealer).WithMany(x => x.Licenses).HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.Restrict);

        // ---- Global soft-delete filtresi + tenant filtresi ----
        ApplyQueryFilters(b);
    }

    private void ApplyQueryFilters(ModelBuilder b)
    {
        // Soft-delete: tüm BaseEntity türevleri
        b.Entity<Dealer>().HasQueryFilter(x => !x.IsDeleted);
        b.Entity<PackagePrice>().HasQueryFilter(x => !x.IsDeleted);
        b.Entity<LicenseHistory>().HasQueryFilter(x => !x.IsDeleted);
        b.Entity<PaymentProviderConfig>().HasQueryFilter(x => !x.IsDeleted);
        b.Entity<TicketMessage>().HasQueryFilter(x => !x.IsDeleted);

        // Soft-delete + tenant filtresi (Admin bypass; Dealer→DealerId; Customer→BusinessId)
        // Kapatma (closure) ile _tenant okunur: her sorguda güncel scoped değer.
        b.Entity<Business>().HasQueryFilter(x =>
            !x.IsDeleted &&
            (_tenant.IsAdmin
                || (_tenant.DealerId != null && x.DealerId == _tenant.DealerId)
                || (_tenant.BusinessId != null && x.Id == _tenant.BusinessId)));

        b.Entity<License>().HasQueryFilter(x =>
            !x.IsDeleted &&
            (_tenant.IsAdmin
                || (_tenant.DealerId != null && x.DealerId == _tenant.DealerId)
                || (_tenant.BusinessId != null && x.BusinessId == _tenant.BusinessId)));

        b.Entity<Payment>().HasQueryFilter(x =>
            !x.IsDeleted &&
            (_tenant.IsAdmin
                || (_tenant.DealerId != null && x.DealerId == _tenant.DealerId)
                || (_tenant.BusinessId != null && x.BusinessId == _tenant.BusinessId)));

        b.Entity<UsageReport>().HasQueryFilter(x =>
            !x.IsDeleted &&
            (_tenant.IsAdmin
                || (_tenant.BusinessId != null && x.BusinessId == _tenant.BusinessId)
                // Bayi: kendi işletmelerinin raporları (BusinessId üzerinden alt sorgu)
                || (_tenant.DealerId != null && x.BusinessId != null
                    && Businesses.Any(bz => bz.Id == x.BusinessId && bz.DealerId == _tenant.DealerId))));

        b.Entity<Ticket>().HasQueryFilter(x =>
            !x.IsDeleted &&
            (_tenant.IsAdmin
                || (_tenant.DealerId != null && x.DealerId == _tenant.DealerId)
                || (_tenant.BusinessId != null && x.BusinessId == _tenant.BusinessId)));

        b.Entity<DemoInstall>().HasQueryFilter(x =>
            !x.IsDeleted &&
            (_tenant.IsAdmin
                || (_tenant.DealerId != null && x.DealerId == _tenant.DealerId)));

        b.Entity<LicenseRequest>().HasQueryFilter(x =>
            !x.IsDeleted &&
            (_tenant.IsAdmin
                || (_tenant.DealerId != null && x.DealerId == _tenant.DealerId)
                || (_tenant.BusinessId != null && x.BusinessId == _tenant.BusinessId)));
    }
}
