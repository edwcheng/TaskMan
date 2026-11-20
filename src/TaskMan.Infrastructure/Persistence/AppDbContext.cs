using Microsoft.EntityFrameworkCore;
using TaskMan.Domain.Entities;

namespace TaskMan.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Forum> Forums => Set<Forum>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Reply> Replies => Set<Reply>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("task_number_seq")
            .StartsAt(1)
            .IncrementsBy(1);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
