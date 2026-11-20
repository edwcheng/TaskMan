using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskMan.Domain.Entities;

namespace TaskMan.Infrastructure.Persistence.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments", table =>
            table.HasCheckConstraint(
                "CK_Attachments_ExactlyOneParent",
                "num_nonnulls(\"TaskId\", \"ReplyId\") = 1"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Reply>()
            .WithMany()
            .HasForeignKey(x => x.ReplyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
