using AffiliateOptimizer.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AffiliateOptimizer.Repository.EntityMappings;

// One IEntityTypeConfiguration<T> per entity, applied by reflection in AppDbContext (ArchitectureSimple.md §7.6).
// Money precision and UTC datetime2(3) columns come from the context's conventions.

public sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable("Runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.ModelNamesJson).IsRequired();
        builder.Property(r => r.ModelParametersJson).IsRequired();
        builder.Property(r => r.CredentialFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(r => r.AppVersion).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => r.Status);
        builder.HasMany(r => r.Networks).WithOne().HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Merchants).WithOne().HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ClickConfiguration : IEntityTypeConfiguration<Click>
{
    public void Configure(EntityTypeBuilder<Click> builder)
    {
        builder.ToTable("Clicks");
        builder.HasKey(c => c.ClickId);
        builder.Property(c => c.ClickId).HasMaxLength(64);
        builder.Property(c => c.MerchantName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ModelName).HasMaxLength(64);
        builder.Property(c => c.ModelVersion).HasMaxLength(32);
        builder.Property(c => c.DecidedNetworkId).HasMaxLength(64);
        builder.Property(c => c.RoutedNetworkId).HasMaxLength(64);
        builder.Property(c => c.RoutedNetworkName).HasMaxLength(200);
        builder.Property(c => c.ErrorCode).HasMaxLength(64);
        builder.HasOne<Run>().WithMany().HasForeignKey(c => c.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.RunId, c.IssuedAt });
        builder.HasIndex(c => new { c.RunId, c.ModelName });
        builder.HasIndex(c => new { c.RoutedNetworkId, c.DisclosedAt });
    }
}

public sealed class FeedCursorConfiguration : IEntityTypeConfiguration<FeedCursor>
{
    public void Configure(EntityTypeBuilder<FeedCursor> builder)
    {
        builder.ToTable("FeedCursors");
        builder.HasKey(f => f.CredentialFingerprint);
        builder.Property(f => f.CredentialFingerprint).HasMaxLength(64);
    }
}

public sealed class OrphanDisclosureConfiguration : IEntityTypeConfiguration<OrphanDisclosure>
{
    public void Configure(EntityTypeBuilder<OrphanDisclosure> builder)
    {
        builder.ToTable("OrphanDisclosures");
        builder.HasKey(o => o.ClickId);
        builder.Property(o => o.ClickId).HasMaxLength(64);
        builder.Property(o => o.RoutedNetworkId).HasMaxLength(64).IsRequired();
        builder.Property(o => o.ErrorCode).HasMaxLength(64);
    }
}

public sealed class LoopEventConfiguration : IEntityTypeConfiguration<LoopEvent>
{
    public void Configure(EntityTypeBuilder<LoopEvent> builder)
    {
        builder.ToTable("LoopEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Loop).HasConversion<int>();
        builder.Property(e => e.Kind).HasConversion<int>();
        builder.Property(e => e.Code).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Message).IsRequired();
        builder.Property(e => e.ClickId).HasMaxLength(64);
        builder.HasIndex(e => new { e.DismissedAt, e.ResolvedAt, e.OccurredAt });
        builder.HasIndex(e => e.OccurredAt);
    }
}

public sealed class RunNetworkSnapshotConfiguration : IEntityTypeConfiguration<RunNetworkSnapshot>
{
    public void Configure(EntityTypeBuilder<RunNetworkSnapshot> builder)
    {
        builder.ToTable("RunNetworkSnapshots");
        builder.HasKey(s => new { s.RunId, s.NetworkId });
        builder.Property(s => s.NetworkId).HasMaxLength(64);
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.SupportedMerchantIdsJson).IsRequired();
    }
}

public sealed class RunMerchantSnapshotConfiguration : IEntityTypeConfiguration<RunMerchantSnapshot>
{
    public void Configure(EntityTypeBuilder<RunMerchantSnapshot> builder)
    {
        builder.ToTable("RunMerchantSnapshots");
        builder.HasKey(s => new { s.RunId, s.MerchantId });
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
    }
}
