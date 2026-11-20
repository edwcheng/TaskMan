using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskMan.Domain.Entities;

namespace TaskMan.Infrastructure.Persistence.Configurations;

public sealed class ReplyConfiguration : IEntityTypeConfiguration<Reply>
{
    public void Configure(EntityTypeBuilder<Reply> builder)
    {
        builder.ToTable("Replies");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Body).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TaskId, x.CreatedAt });
    }
}
