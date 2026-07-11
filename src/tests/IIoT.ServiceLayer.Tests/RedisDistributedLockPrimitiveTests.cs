using System.Reflection;
using IIoT.Infrastructure.Locking;
using StackExchange.Redis;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class RedisDistributedLockPrimitiveTests
{
    [Fact]
    public async Task Primitive_ShouldUseSetNxAndCompareTokenLuaScripts()
    {
        var calls = new List<DatabaseCall>();
        var database = DispatchProxy.Create<IDatabase, RecordingDatabaseProxy>();
        ((RecordingDatabaseProxy)(object)database).Handler = (method, arguments) =>
        {
            calls.Add(new DatabaseCall(method.Name, arguments ?? []));
            return method.Name switch
            {
                nameof(IDatabase.StringSetAsync) => Task.FromResult(true),
                nameof(IDatabase.ScriptEvaluateAsync) => Task.FromResult(
                    RedisResult.Create((RedisValue)1L)),
                _ => throw new NotSupportedException(method.Name)
            };
        };
        var primitive = new RedisDistributedLockPrimitive(database);

        Assert.True(await primitive.TryAcquireAsync("resource", "owner-token", TimeSpan.FromSeconds(30)));
        Assert.True(await primitive.TryRenewAsync("resource", "owner-token", TimeSpan.FromSeconds(30)));
        Assert.True(await primitive.TryReleaseAsync("resource", "owner-token"));

        var acquire = Assert.Single(calls, call => call.Name == nameof(IDatabase.StringSetAsync));
        Assert.Equal((RedisKey)"resource", Assert.IsType<RedisKey>(acquire.Arguments[0]));
        Assert.Equal((RedisValue)"owner-token", Assert.IsType<RedisValue>(acquire.Arguments[1]));
        Assert.Equal(When.NotExists, Assert.IsType<When>(acquire.Arguments[3]));

        var scripts = calls.Where(call => call.Name == nameof(IDatabase.ScriptEvaluateAsync)).ToList();
        Assert.Equal(2, scripts.Count);
        Assert.Contains("redis.call('get', KEYS[1]) == ARGV[1]", Assert.IsType<string>(scripts[0].Arguments[0]));
        Assert.Contains("redis.call('pexpire'", Assert.IsType<string>(scripts[0].Arguments[0]));
        Assert.Contains("redis.call('get', KEYS[1]) == ARGV[1]", Assert.IsType<string>(scripts[1].Arguments[0]));
        Assert.Contains("redis.call('del'", Assert.IsType<string>(scripts[1].Arguments[0]));
        Assert.All(
            scripts,
            call => Assert.Equal(
                (RedisValue)"owner-token",
                Assert.IsType<RedisValue[]>(call.Arguments[2])[0]));
    }

    private class RecordingDatabaseProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler?.Invoke(targetMethod!, args)
               ?? throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed record DatabaseCall(string Name, object?[] Arguments);
}
