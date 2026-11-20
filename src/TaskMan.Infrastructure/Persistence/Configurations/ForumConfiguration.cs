using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskMan.Domain.Entities;

namespace TaskMan.Infrastructure.Persistence.Configurations;

public sealed class ForumConfiguration : IEntityTypeConfiguration<Forum>
{
    public void Configure(EntityTypeBuilder<Forum> builder)
    {
        builder.ToTable("Forums");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(x => x.Slug).IsUnique();
    }
}
