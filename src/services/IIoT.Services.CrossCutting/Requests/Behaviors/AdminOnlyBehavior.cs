using System.Text.Json;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Exceptions;
using MediatR;

namespace IIoT.Services.CrossCutting.Behaviors;

/// <summary>
/// 人员端管理员专属操作守卫。
/// </summary>
public sealed class AdminOnlyBehavior<TRequest, TResponse>(
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requiresAdmin = typeof(TRequest)
            .GetCustomAttributes(typeof(AdminOnlyAttribute), true)
            .Length > 0;

        if (!requiresAdmin)
            return await next(cancellationToken);

        if (!SystemRoles.IsAuthenticatedHumanAdmin(
                currentUser.IsAuthenticated,
                currentUser.ActorType,
                currentUser.Roles))
        {
            if (request is IAdminOnlyAuditRequest auditRequest)
            {
                await auditTrailService.TryWriteAsync(
                    new AuditTrailEntry(
                        ParseActorUserId(currentUser.Id),
                        currentUser.UserName,
                        auditRequest.AdminAuditOperationType,
                        auditRequest.AdminAuditTargetType,
                        auditRequest.AdminAuditTargetIdOrKey,
                        DateTime.UtcNow,
                        false,
                        JsonSerializer.Serialize(new
                        {
                            action = "AdminOnlyDenied",
                            reasonCode = "AdminRequired",
                            requestType = typeof(TRequest).Name
                        }),
                        "拒绝访问：只有管理员可以执行该操作"),
                    cancellationToken);
            }

            throw new ForbiddenException("拒绝访问：只有管理员可以执行该操作");
        }

        return await next(cancellationToken);
    }

    private static Guid? ParseActorUserId(string? rawUserId)
        => Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;
}
