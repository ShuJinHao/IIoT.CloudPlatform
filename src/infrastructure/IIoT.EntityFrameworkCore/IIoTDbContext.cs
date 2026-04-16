using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.SharedKernel.Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.EntityFrameworkCore;

public class IIoTDbContext(DbContextOptions<IIoTDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<MfgProcess> MfgProcesses => Set<MfgProcess>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Recipe> Recipes => Set<Recipe>();

    private readonly List<IDomainEvent> _pendingDomainEvents = [];
    public bool HasPendingDomainEvents => _pendingDomainEvents.Count > 0;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var trackedEntities = ChangeTracker.Entries<BaseEntity<Guid>>()
            .Where(e => e.Entity.DomainEvents.Any())
            .ToList();

        var domainEvents = trackedEntities.SelectMany(e => e.Entity.DomainEvents).ToList();
        trackedEntities.ForEach(e => e.Entity.ClearDomainEvents());

        var affected = await base.SaveChangesAsync(cancellationToken);

        if (domainEvents.Count > 0)
        {
            if (Database.CurrentTransaction is null)
            {
                await PublishDomainEventsAsync(domainEvents, cancellationToken);
            }
            else
            {
                _pendingDomainEvents.AddRange(domainEvents);
            }
        }

        return affected;
    }

    public async Task FlushDomainEventsAsync(CancellationToken cancellationToken = default)
    {
        while (_pendingDomainEvents.Count > 0)
        {
            var domainEvent = _pendingDomainEvents[0];
            await PublishDomainEventsAsync([domainEvent], cancellationToken);
            _pendingDomainEvents.RemoveAt(0);
        }
    }

    public void DiscardPendingDomainEvents()
    {
        _pendingDomainEvents.Clear();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IIoTDbContext).Assembly);
    }

    private async Task PublishDomainEventsAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken)
    {
        var mediator = this.GetService<IMediator>();
        if (mediator is null)
        {
            return;
        }

        foreach (var domainEvent in domainEvents)
        {
            await mediator.Publish(domainEvent, cancellationToken);
        }
    }
}

