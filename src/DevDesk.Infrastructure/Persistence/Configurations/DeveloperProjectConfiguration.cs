using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DevDesk.Core.Models;

namespace DevDesk.Infrastructure.Persistence.Configurations;

public sealed class DeveloperProjectConfiguration : IEntityTypeConfiguration<DeveloperProject>
{
    public void Configure(EntityTypeBuilder<DeveloperProject> builder)
    {
        builder.ToTable("DeveloperProjects", t =>
        {
            t.HasCheckConstraint("CK_DeveloperProjects_DefaultPort", "DefaultPort IS NULL OR (DefaultPort >= 1 AND DefaultPort <= 65535)");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Path)
            .IsRequired()
            .HasMaxLength(1024)
            .UseCollation("NOCASE");

        builder.HasIndex(p => p.Path)
            .IsUnique();

        builder.Property(p => p.Framework)
            .HasMaxLength(100);

        builder.Property(p => p.Language)
            .HasMaxLength(100);

        builder.Property(p => p.PackageManager)
            .HasMaxLength(100);

        builder.Property(p => p.RunCommand)
            .HasMaxLength(1024);

        builder.Property(p => p.BuildCommand)
            .HasMaxLength(1024);

        builder.Property(p => p.TestCommand)
            .HasMaxLength(1024);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.HasMany(p => p.SavedCommands)
            .WithOne(c => c.Project)
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
