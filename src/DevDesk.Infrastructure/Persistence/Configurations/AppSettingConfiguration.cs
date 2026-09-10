using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DevDesk.Core.Models;

namespace DevDesk.Infrastructure.Persistence.Configurations;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Key)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("NOCASE");

        builder.HasIndex(s => s.Key)
            .IsUnique();

        builder.Property(s => s.Value)
            .IsRequired()
            .HasMaxLength(4096);
    }
}
