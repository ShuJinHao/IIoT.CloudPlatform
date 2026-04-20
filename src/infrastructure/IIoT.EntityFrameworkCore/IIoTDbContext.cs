using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.SharedKernel.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore;

public class IIoTDbContext(DbContextOptions<IIoTDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<MfgProcess> MfgProcesses => Set<MfgProcess>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RefreshTokenSession> RefreshTokenSessions => Set<RefreshTokenSession>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public bool HasPendingDomainEvents => ChangeTracker.Entries<BaseEntity<Guid>>()
        .Any(e => e.Entity.DomainEvents.Count > 0);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var trackedEntities = ChangeTracker.Entries<BaseEntity<Guid>>()
            .Where(e => e.Entity.DomainEvents.Any())
            .ToList();

        var domainEvents = trackedEntities.SelectMany(e => e.Entity.DomainEvents).ToList();
        trackedEntities.ForEach(e => e.Entity.ClearDomainEvents());

        if (domainEvents.Count > 0)
        {
            OutboxMessages.AddRange(domainEvents.Select(OutboxMessage.FromDomainEvent));
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    public async Task FlushDomainEventsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
    }

    public void DiscardPendingDomainEvents()
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity<Guid>>())
        {
            entry.Entity.ClearDomainEvents();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IIoTDbContext).Assembly);
    }
}

