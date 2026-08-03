using System.Net;
using System.Net.Http;
using ServiceLib.Services;
using Xunit;

namespace ServiceLib.Tests.Services;

public class ProxyPingClientTests
{
    [Fact]
    public async Task ProbeAsync_ReusesClientUntilProxyPortChanges()
    {
        var port = 10808;
        var factoryCalls = 0;
        using var client = new ProxyPingClient(
            () => port,
            () => "https://example.test/generate_204",
            _ =>
            {
                factoryCalls++;
                return new ImmediateHandler();
            });

        Assert.True(await client.ProbeAsync(CancellationToken.None) > 0);
        Assert.True(await client.ProbeAsync(CancellationToken.None) > 0);
        Assert.Equal(1, factoryCalls);

        port = 10809;
        Assert.True(await client.ProbeAsync(CancellationToken.None) > 0);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task ProbeAsync_PropagatesCallerCancellation()
    {
        using var client = new ProxyPingClient(
            () => 10808,
            () => "https://example.test/generate_204",
            _ => new WaitingHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ProbeAsync(cancellation.Token));
    }

    private sealed class ImmediateHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
