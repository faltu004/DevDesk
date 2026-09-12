using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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

        builder.Property(c => c.Description)
            .IsRequired(false)
            .HasMaxLength(1000);

        // Windows long paths supported without legacy 260 character limitation
        builder.Property(c => c.Executable)
            .IsRequired()
            .HasMaxLength(2048);

        // Value converter and comparer for JSON array argument persistence
        var argumentsConverter = new ValueConverter<IReadOnlyList<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => string.IsNullOrWhiteSpace(v)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? (IReadOnlyList<string>)Array.Empty<string>());

        var argumentsComparer = new ValueComparer<IReadOnlyList<string>>(
            (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v != null ? v.GetHashCode() : 0)),
            c => c.ToList());

        builder.Property(c => c.Arguments)
            .IsRequired()
            .HasConversion(argumentsConverter, argumentsComparer)
            .HasMaxLength(8192);

        builder.Property(c => c.WorkingDirectory)
            .IsRequired(false)
            .HasMaxLength(2048);

        builder.Property(c => c.Category)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.Property(c => c.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.CreatedAtUtc)
            .IsRequired();

        builder.Property(c => c.UpdatedAtUtc)
            .IsRequired();

        builder.HasOne(c => c.Project)
            .WithMany(p => p.SavedCommands)
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        builder.HasIndex(c => c.ProjectId);
    }
}
