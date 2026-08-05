using Xunit;

namespace ServiceLib.Tests.Services;

public sealed class SubscriptionUpdateCoordinatorTests
{
    [Fact]
    public void FirstUpdatePolicy_AllowsOnlyUnconsumedEnabledHttpCreates()
    {
        const string opaqueId = "opaque-test-id";
        const string secureUrl = "https://subscription.invalid/source";

        Assert.True(FirstSubscriptionUpdatePolicy.ShouldUpdate(true, false, opaqueId, true, secureUrl));
        Assert.True(FirstSubscriptionUpdatePolicy.ShouldUpdate(true, false, opaqueId, true, "http://subscription.invalid/source"));
        Assert.False(FirstSubscriptionUpdatePolicy.ShouldUpdate(false, false, opaqueId, true, secureUrl));
        Assert.False(FirstSubscriptionUpdatePolicy.ShouldUpdate(true, true, opaqueId, true, secureUrl));
        Assert.False(FirstSubscriptionUpdatePolicy.ShouldUpdate(true, false, string.Empty, true, secureUrl));
        Assert.False(FirstSubscriptionUpdatePolicy.ShouldUpdate(true, false, opaqueId, false, secureUrl));
        Assert.False(FirstSubscriptionUpdatePolicy.ShouldUpdate(true, false, opaqueId, true, string.Empty));
        Assert.False(FirstSubscriptionUpdatePolicy.ShouldUpdate(true, false, opaqueId, true, "ftp://subscription.invalid/source"));
        Assert.False(FirstSubscriptionUpdatePolicy.ShouldUpdate(true, false, opaqueId, true, "not-a-url"));
    }

    [Fact]
    public async Task SameInitialId_ConcurrentRequestsShareOneExecution()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var coordinator = new SubscriptionUpdateCoordinator(async _ =>
        {
            Interlocked.Increment(ref invocationCount);
            entered.SetResult();
            await release.Task;
            return new SubscriptionUpdateResult(true, 1, 1);
        });
        var request = AutomaticRequest("opaque-dedupe-id");

        var first = coordinator.UpdateAsync(request);
        await entered.Task;
        var second = coordinator.UpdateAsync(request with { UseProxy = false });

        Assert.Same(first, second);
        Assert.Equal(1, Volatile.Read(ref invocationCount));
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task DifferentIds_HaveGlobalMaximumConcurrencyOfOne()
    {
        var active = 0;
        var maximum = 0;
        var coordinator = new SubscriptionUpdateCoordinator(async _ =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maximum, current);
            await Task.Yield();
            Interlocked.Decrement(ref active);
            return new SubscriptionUpdateResult(true, 1, 1);
        });

        var tasks = Enumerable.Range(0, 24)
            .Select(index => coordinator.UpdateAsync(AutomaticRequest($"opaque-serial-{index}")))
            .ToArray();

        await Task.WhenAll(tasks);
        Assert.Equal(1, maximum);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task DelegateFailure_ReturnsStructuredFailureAndReleasesGate()
    {
        var callCount = 0;
        var coordinator = new SubscriptionUpdateCoordinator(request =>
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                throw new InvalidOperationException("synthetic failure");
            }

            return Task.FromResult(new SubscriptionUpdateResult(true, 1, 1));
        });

        var failed = await coordinator.UpdateAsync(AutomaticRequest("opaque-failure-id"));
        var succeeded = await coordinator.UpdateAsync(AutomaticRequest("opaque-retry-id"));

        Assert.False(failed.Success);
        Assert.Equal(0, failed.AttemptedCount);
        Assert.Equal(0, failed.SucceededCount);
        Assert.True(succeeded.Success);
    }

    [Fact]
    public async Task AutomaticRequest_PreservesProxyOnlyFlagsAndOpaqueId()
    {
        SubscriptionUpdateRequest? observed = null;
        var coordinator = new SubscriptionUpdateCoordinator(request =>
        {
            observed = request;
            return Task.FromResult(new SubscriptionUpdateResult(true, 1, 1));
        });
        var request = AutomaticRequest("opaque-flags-id");

        var result = await coordinator.UpdateAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(observed);
        Assert.Equal(request.SubscriptionId, observed.SubscriptionId);
        Assert.True(observed.UseProxy);
        Assert.False(observed.AllowDirectFallback);
        Assert.True(observed.IsAutomatic);
    }

    private static SubscriptionUpdateRequest AutomaticRequest(string opaqueId)
    {
        return new SubscriptionUpdateRequest(
            opaqueId,
            UseProxy: true,
            AllowDirectFallback: false,
            IsAutomatic: true);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int candidate)
        {
            var current = Volatile.Read(ref target);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(ref target, candidate, current);
                if (observed == current)
                {
                    return;
                }
                current = observed;
            }
        }
    }
}
