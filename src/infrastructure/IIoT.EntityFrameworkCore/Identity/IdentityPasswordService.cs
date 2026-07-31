using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class IdentityPasswordService(
    UserManager<ApplicationUser> userManager,
    IIoTDbContext dbContext) : IIdentityPasswordService
{
    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(5);
    private readonly Func<IIoTDbContext> _createContext = dbContext.CreateFreshContext;

    public async Task<Result<bool>> SetPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId,
            cancellationToken);
        if (user is null)
        {
            return Result.Failure("用户不存在");
        }

        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return Result.Failure("用户已设置密码");
        }

        var validation = await ValidatePasswordAsync(user, password);
        if (!validation.Succeeded)
        {
            return Result.Failure(
                validation.Errors.Select(error => error.Description).ToArray());
        }

        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }

    public async Task<Result<bool>> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var checkedAtUtc = DateTimeOffset.UtcNow;
        PasswordSnapshot? baseline = null;
        PasswordTarget? target = null;

        return await ExecuteRecoverableAsync(
            async callbackToken =>
            {
                await using var context = _createContext();
                await using var transaction =
                    await context.Database.BeginTransactionAsync(callbackToken);
                var user = await context.Users.SingleOrDefaultAsync(
                    candidate => candidate.Id == userId,
                    callbackToken);
                if (user is null || !user.IsEnabled)
                {
                    await transaction.CommitAsync(callbackToken);
                    return Result.Success(false);
                }

                var current = PasswordSnapshot.From(user);
                if (target is not null && target.Matches(current))
                {
                    await transaction.CommitAsync(callbackToken);
                    return Result.Success(target.PasswordAccepted);
                }

                if (baseline is not null && !baseline.Matches(current))
                {
                    throw new CloudWriteConflictException();
                }

                if (user.LockoutEnabled &&
                    user.LockoutEnd is { } lockoutEnd &&
                    lockoutEnd > checkedAtUtc)
                {
                    await transaction.CommitAsync(callbackToken);
                    return Result.Success(false);
                }

                baseline ??= current;
                target ??= CreateCheckTarget(user, password, checkedAtUtc);
                if (target.Matches(current))
                {
                    await transaction.CommitAsync(callbackToken);
                    return Result.Success(target.PasswordAccepted);
                }

                var exactTarget = target ?? throw new InvalidOperationException(
                    "Password write target was not established.");
                ApplyTarget(user, exactTarget);
                await context.SaveChangesAsync(callbackToken);
                await transaction.CommitAsync(callbackToken);
                return Result.Success(exactTarget.PasswordAccepted);
            },
            async token =>
            {
                await using var context = _createContext();
                var current = await context.Users
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == userId,
                        token);
                return Observe(
                    current is null ? null : PasswordSnapshot.From(current),
                    baseline,
                    target);
            },
            targetResult: () => Result.Success(target?.PasswordAccepted ?? false),
            cancellationToken);
    }

    public async Task<Result> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var result = await SetStandalonePasswordAsync(
            userId,
            currentPassword,
            newPassword,
            requireCurrentPassword: true,
            cancellationToken);
        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.Errors?.ToArray() ?? ["密码修改失败"]);
    }

    public Task<Result<bool>> ResetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken = default)
        => SetStandalonePasswordAsync(
            userId,
            currentPassword: null,
            newPassword,
            requireCurrentPassword: false,
            cancellationToken);

    private async Task<Result<bool>> SetStandalonePasswordAsync(
        Guid userId,
        string? currentPassword,
        string newPassword,
        bool requireCurrentPassword,
        CancellationToken cancellationToken)
    {
        PasswordSnapshot? baseline = null;
        PasswordTarget? target = null;

        return await ExecuteRecoverableAsync(
            async callbackToken =>
            {
                await using var context = _createContext();
                await using var transaction =
                    await context.Database.BeginTransactionAsync(callbackToken);
                var user = await context.Users.SingleOrDefaultAsync(
                    candidate => candidate.Id == userId,
                    callbackToken);
                if (user is null)
                {
                    return Result.Failure("用户不存在");
                }

                var current = PasswordSnapshot.From(user);
                if (target is not null && target.Matches(current))
                {
                    await transaction.CommitAsync(callbackToken);
                    return Result.Success(true);
                }

                if (baseline is not null && !baseline.Matches(current))
                {
                    throw new CloudWriteConflictException();
                }

                if (baseline is null)
                {
                    if (requireCurrentPassword &&
                        !VerifyPassword(user, currentPassword ?? string.Empty))
                    {
                        return Result.Failure("当前密码不正确");
                    }

                    var validation = await ValidatePasswordAsync(user, newPassword);
                    if (!validation.Succeeded)
                    {
                        return Result.Failure(
                            validation.Errors
                                .Select(error => error.Description)
                                .ToArray());
                    }

                    baseline = current;
                    target = new PasswordTarget(
                        userManager.PasswordHasher.HashPassword(user, newPassword),
                        Guid.NewGuid().ToString("N"),
                        Guid.NewGuid().ToString("N"),
                        current.LockoutEnabled,
                        current.AccessFailedCount,
                        current.LockoutEnd,
                        PasswordAccepted: true);
                }

                var exactTarget = target ?? throw new InvalidOperationException(
                    "Password write target was not established.");
                ApplyTarget(user, exactTarget);
                await context.SaveChangesAsync(callbackToken);
                await transaction.CommitAsync(callbackToken);
                return Result.Success(true);
            },
            async token =>
            {
                await using var context = _createContext();
                var current = await context.Users
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == userId,
                        token);
                return Observe(
                    current is null ? null : PasswordSnapshot.From(current),
                    baseline,
                    target);
            },
            targetResult: () => Result.Success(true),
            cancellationToken);
    }

    private async Task<Result<bool>> ExecuteRecoverableAsync(
        Func<CancellationToken, Task<Result<bool>>> attempt,
        Func<CancellationToken, Task<WriteObservation>> observe,
        Func<Result<bool>> targetResult,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            var result = await strategy.ExecuteAsync(attempt, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudWriteConflictException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch
        {
            using var timeout = new CancellationTokenSource(ObservationTimeout);
            WriteObservation observation;
            try
            {
                observation = await observe(timeout.Token);
            }
            catch
            {
                observation = WriteObservation.Unknown;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return observation switch
            {
                WriteObservation.Target => targetResult(),
                WriteObservation.Conflict => throw new CloudWriteConflictException(),
                _ => throw new CloudWriteCommitUnknownException()
            };
        }
    }

    private PasswordTarget CreateCheckTarget(
        ApplicationUser user,
        string password,
        DateTimeOffset checkedAtUtc)
    {
        var verification = VerifyPasswordResult(user, password);
        var accepted = verification is
            PasswordVerificationResult.Success or
            PasswordVerificationResult.SuccessRehashNeeded;
        var lockoutEnabled = true;
        var accessFailedCount = accepted
            ? 0
            : checked(user.AccessFailedCount + 1);
        var lockoutEnd = user.LockoutEnd;
        if (!accepted &&
            accessFailedCount >= userManager.Options.Lockout.MaxFailedAccessAttempts)
        {
            lockoutEnd = checkedAtUtc.Add(
                userManager.Options.Lockout.DefaultLockoutTimeSpan);
            accessFailedCount = 0;
        }

        var passwordHash = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? userManager.PasswordHasher.HashPassword(user, password)
            : user.PasswordHash;
        var stateChanged =
            !string.Equals(passwordHash, user.PasswordHash, StringComparison.Ordinal) ||
            lockoutEnabled != user.LockoutEnabled ||
            accessFailedCount != user.AccessFailedCount ||
            !Nullable.Equals(lockoutEnd, user.LockoutEnd);
        return new PasswordTarget(
            passwordHash,
            user.SecurityStamp,
            stateChanged
                ? Guid.NewGuid().ToString("N")
                : user.ConcurrencyStamp,
            lockoutEnabled,
            accessFailedCount,
            lockoutEnd,
            accepted);
    }

    private Task<IdentityResult> ValidatePasswordAsync(
        ApplicationUser user,
        string password)
        => ValidatePasswordCoreAsync(user, password);

    private async Task<IdentityResult> ValidatePasswordCoreAsync(
        ApplicationUser user,
        string password)
    {
        var errors = new List<IdentityError>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, password);
            if (!result.Succeeded)
            {
                errors.AddRange(result.Errors);
            }
        }

        return errors.Count == 0
            ? IdentityResult.Success
            : IdentityResult.Failed(errors.ToArray());
    }

    private bool VerifyPassword(ApplicationUser user, string password)
        => VerifyPasswordResult(user, password) is
            PasswordVerificationResult.Success or
            PasswordVerificationResult.SuccessRehashNeeded;

    private PasswordVerificationResult VerifyPasswordResult(
        ApplicationUser user,
        string password)
    {
        return string.IsNullOrWhiteSpace(user.PasswordHash)
            ? PasswordVerificationResult.Failed
            : userManager.PasswordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                password);
    }

    private static void ApplyTarget(
        ApplicationUser user,
        PasswordTarget target)
    {
        user.PasswordHash = target.PasswordHash;
        user.SecurityStamp = target.SecurityStamp;
        user.ConcurrencyStamp = target.ConcurrencyStamp;
        user.LockoutEnabled = target.LockoutEnabled;
        user.AccessFailedCount = target.AccessFailedCount;
        user.LockoutEnd = target.LockoutEnd;
    }

    private static WriteObservation Observe(
        PasswordSnapshot? current,
        PasswordSnapshot? baseline,
        PasswordTarget? target)
    {
        if (target is not null && target.Matches(current))
        {
            return WriteObservation.Target;
        }

        if (baseline is null || target is null)
        {
            return WriteObservation.Unknown;
        }

        if (baseline.Matches(current))
        {
            return WriteObservation.Baseline;
        }

        return WriteObservation.Conflict;
    }

    private sealed record PasswordSnapshot(
        string? PasswordHash,
        string? SecurityStamp,
        string? ConcurrencyStamp,
        bool LockoutEnabled,
        int AccessFailedCount,
        DateTimeOffset? LockoutEnd)
    {
        public static PasswordSnapshot From(ApplicationUser user)
            => new(
                user.PasswordHash,
                user.SecurityStamp,
                user.ConcurrencyStamp,
                user.LockoutEnabled,
                user.AccessFailedCount,
                user.LockoutEnd);

        public bool Matches(PasswordSnapshot? other)
            => other is not null &&
               string.Equals(PasswordHash, other.PasswordHash, StringComparison.Ordinal) &&
               string.Equals(SecurityStamp, other.SecurityStamp, StringComparison.Ordinal) &&
               string.Equals(ConcurrencyStamp, other.ConcurrencyStamp, StringComparison.Ordinal) &&
               LockoutEnabled == other.LockoutEnabled &&
               AccessFailedCount == other.AccessFailedCount &&
               Nullable.Equals(LockoutEnd, other.LockoutEnd);
    }

    private sealed record PasswordTarget(
        string? PasswordHash,
        string? SecurityStamp,
        string? ConcurrencyStamp,
        bool LockoutEnabled,
        int AccessFailedCount,
        DateTimeOffset? LockoutEnd,
        bool PasswordAccepted)
    {
        public bool Matches(PasswordSnapshot? snapshot)
            => snapshot is not null &&
               string.Equals(PasswordHash, snapshot.PasswordHash, StringComparison.Ordinal) &&
               string.Equals(SecurityStamp, snapshot.SecurityStamp, StringComparison.Ordinal) &&
               string.Equals(ConcurrencyStamp, snapshot.ConcurrencyStamp, StringComparison.Ordinal) &&
               LockoutEnabled == snapshot.LockoutEnabled &&
               AccessFailedCount == snapshot.AccessFailedCount &&
               Nullable.Equals(LockoutEnd, snapshot.LockoutEnd);
    }

    private enum WriteObservation
    {
        Baseline,
        Target,
        Conflict,
        Unknown
    }
}
