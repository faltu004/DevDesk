using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DevDesk.Core.Models;

namespace DevDesk.Infrastructure.Persistence.Configurations;

public sealed class SavedCommandConfiguration : IEntityTypeConfiguration<SavedCommand>
{
    public void Configure(EntityTypeBuilder<SavedCommand> builder)
    {
        builder.ToTable("SavedCommands");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Command)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(c => c.WorkingDirectory)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.HasOne(c => c.Project)
            .WithMany(p => p.SavedCommands)
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);
    }
}
