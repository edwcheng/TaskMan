using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskMan.Domain.Entities;

namespace TaskMan.Infrastructure.Persistence.Configurations;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Number)
            .IsRequired()
            .HasDefaultValueSql("nextval('task_number_seq')");

        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasColumnType("text").IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(x => x.Priority)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(x => x.Tag).HasMaxLength(50);
        builder.Property(x => x.DueDate).HasColumnType("date");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.LastActivityAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<Forum>()
            .WithMany()
            .HasForeignKey(x => x.ForumId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AssigneeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => new { x.ForumId, x.LastActivityAt });
        builder.HasIndex(x => new { x.Status, x.LastActivityAt });
        builder.HasIndex(x => new { x.AssigneeId, x.Status });
        builder.HasIndex(x => x.DueDate);
        builder.HasIndex(x => x.Tag);
    }
}
