using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskMan.Domain.Entities;

namespace TaskMan.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.UserName).HasMaxLength(32).IsRequired();
        builder.Property(x => x.NormalizedUserName).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PasswordHash).HasColumnType("text").IsRequired();
        builder.Property(x => x.SecurityStamp).IsRequired();
        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.LastLoginAt).HasColumnType("timestamp with time zone");

        builder.HasIndex(x => x.NormalizedUserName).IsUnique();
    }
}
