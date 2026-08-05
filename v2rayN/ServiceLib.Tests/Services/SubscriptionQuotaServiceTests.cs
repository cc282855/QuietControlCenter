namespace ServiceLib.Tests.Services;

using Xunit;

public sealed class SubscriptionQuotaServiceTests
{
    [Fact]
    public async Task Fetch_PrefersValidHeaderAndDoesNotReadBody()
    {
        var content = new ThrowOnReadContent();
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            response.Headers.TryAddWithoutValidation("Subscription-Userinfo", "upload=1; download=2; total=10");
            return response;
        });
        var service = CreateService(handler);

        var result = await service.FetchAsync("https://example.invalid/sub", false, 0, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(7UL, result.Snapshot!.RemainingBytes);
        Assert.False(content.WasRead);
    }

    [Fact]
    public async Task Fetch_FallsBackToSameResponseBodyWhenHeaderIsMissingOrInvalid()
    {
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes("Remaining Flow: 3 GB\nExpiry: 2028-01-01"));
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            response.Headers.TryAddWithoutValidation("Subscription-Userinfo", "total=0");
            return response;
        });
        var service = CreateService(handler);

        var result = await service.FetchAsync("https://example.invalid/sub", false, 0, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(3UL * 1024 * 1024 * 1024, result.Snapshot!.RemainingBytes);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Fetch_ProxyUnavailableNeverCreatesDirectHandler()
    {
        var handlerCreated = false;
        var service = new SubscriptionQuotaService(
            (_, _) => Task.FromResult(false),
            _ => { handlerCreated = true; return new StubHandler(_ => new(HttpStatusCode.OK)); });

        var result = await service.FetchAsync("https://example.invalid/sub", true, 10808, null, TestContext.Current.CancellationToken);

        Assert.Equal(SubscriptionQuotaStatusCode.ProxyUnavailable, result.Status);
        Assert.False(handlerCreated);
    }

    [Theory]
    [InlineData("http://example.invalid/sub")]
    [InlineData("/relative")]
    [InlineData("https://user:pass@example.invalid/sub")]
    public async Task Fetch_AcceptsOnlyAbsoluteHttpsWithoutUserInfo(string url)
    {
        var service = CreateService(new StubHandler(_ => new(HttpStatusCode.OK)));

        var result = await service.FetchAsync(url, false, 0, null, TestContext.Current.CancellationToken);

        Assert.Equal(SubscriptionQuotaStatusCode.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task Fetch_EnforcesBoundedBodyRead()
    {
        var handler = new StubHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[SubscriptionQuotaParser.MaxBodyBytes + 1])
        });
        var service = CreateService(handler);

        var result = await service.FetchAsync("https://example.invalid/sub", false, 0, null, TestContext.Current.CancellationToken);

        Assert.Equal(SubscriptionQuotaStatusCode.BodyTooLarge, result.Status);
    }

    [Fact]
    public async Task Fetch_NetworkFailureReturnsFixedNonLeakingStatus()
    {
        const string secret = "private-host.invalid/token-value";
        var service = CreateService(new StubHandler(_ => throw new HttpRequestException(secret)));

        var result = await service.FetchAsync($"https://{secret}", false, 0, null, TestContext.Current.CancellationToken);
        var message = SubscriptionQuotaService.GetFixedChineseMessage(result.Status);

        Assert.Equal(SubscriptionQuotaStatusCode.NetworkError, result.Status);
        Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task Fetch_SendsConfiguredUserAgentAndIdentityEncoding()
    {
        string? observedUserAgent = null;
        string? observedEncoding = null;
        var handler = new StubHandler(request =>
        {
            observedUserAgent = request.Headers.UserAgent.ToString();
            observedEncoding = request.Headers.AcceptEncoding.ToString();
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Subscription-Userinfo", "upload=1; download=1; total=10");
            return response;
        });
        var service = CreateService(handler);

        var result = await service.FetchAsync(
            "https://example.invalid/sub", false, 0, "SyntheticClient/1.0", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("SyntheticClient/1.0", observedUserAgent);
        Assert.Equal("identity", observedEncoding);
    }

    [Fact]
    public async Task Fetch_UsesExistingVersionAsEmptyUserAgentFallback()
    {
        string? observedUserAgent = null;
        var handler = new StubHandler(request =>
        {
            observedUserAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var service = CreateService(handler);

        await service.FetchAsync("https://example.invalid/sub", false, 0, " ", TestContext.Current.CancellationToken);

        Assert.Equal(Utils.GetVersion(false), observedUserAgent);
    }

    [Theory]
    [InlineData("https://localhost/sub")]
    [InlineData("https://localhost./sub")]
    [InlineData("https://node.localhost/sub")]
    [InlineData("https://node.localhost./sub")]
    [InlineData("https://localhost。/sub")]
    [InlineData("https://localhost．/sub")]
    [InlineData("https://localhost｡/sub")]
    [InlineData("https://127.0.0.1/sub")]
    [InlineData("https://127.0.0.1./sub")]
    [InlineData("https://0.0.0.0/sub")]
    [InlineData("https://10.1.2.3/sub")]
    [InlineData("https://10.0.0.1./sub")]
    [InlineData("https://169.254.1.2/sub")]
    [InlineData("https://172.16.1.2/sub")]
    [InlineData("https://192.168.1.2/sub")]
    [InlineData("https://224.0.0.1/sub")]
    [InlineData("https://[::]/sub")]
    [InlineData("https://[::1]/sub")]
    [InlineData("https://[fc00::1]/sub")]
    [InlineData("https://[fe80::1]/sub")]
    [InlineData("https://[ff02::1]/sub")]
    public async Task Fetch_RejectsLocalAndNonGlobalIpDestinations(string url)
    {
        var handlerCreated = false;
        var service = new SubscriptionQuotaService(
            (_, _) => Task.FromResult(true),
            _ => { handlerCreated = true; return new StubHandler(_ => new(HttpStatusCode.OK)); });

        var result = await service.FetchAsync(url, true, 10808, null, TestContext.Current.CancellationToken);

        Assert.Equal(SubscriptionQuotaStatusCode.InvalidRequest, result.Status);
        Assert.False(handlerCreated);
    }

    [Fact]
    public void ProductionProxy_NeverBypassesLocalDestination()
    {
        var proxyType = typeof(SubscriptionQuotaService).GetNestedType(
            "ForcedLocalSocksProxy",
            BindingFlags.NonPublic);
        var proxy = Assert.IsAssignableFrom<IWebProxy>(Activator.CreateInstance(proxyType!, 10808));

        Assert.False(proxy.IsBypassed(new Uri("https://localhost/")));
        Assert.Equal("socks5", proxy.GetProxy(new Uri("https://localhost/")).Scheme);
    }

    [Fact]
    public async Task Fetch_CallerCancellationReturnsFixedCancelledStatus()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var service = CreateService(new CancellationHandler());
        var task = service.FetchAsync("https://example.invalid/sub", false, 0, null, cancellation.Token);

        cancellation.Cancel();
        var result = await task;

        Assert.Equal(SubscriptionQuotaStatusCode.Cancelled, result.Status);
        Assert.Equal("余量查询已取消", SubscriptionQuotaService.GetFixedChineseMessage(result.Status));
    }

    [Fact]
    public async Task Fetch_HttpErrorReturnsFixedNonLeakingStatus()
    {
        var service = CreateService(new StubHandler(_ => new(HttpStatusCode.BadGateway)));

        var result = await service.FetchAsync(
            "https://example.invalid/private-token", false, 0, null, TestContext.Current.CancellationToken);

        Assert.Equal(SubscriptionQuotaStatusCode.HttpError, result.Status);
        Assert.Equal("订阅服务响应异常", SubscriptionQuotaService.GetFixedChineseMessage(result.Status));
        Assert.Null(result.Snapshot);
    }

    private static SubscriptionQuotaService CreateService(HttpMessageHandler handler) => new(
        (_, _) => Task.FromResult(true),
        _ => handler,
        new FixedTimeProvider(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero)));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }

        protected override void Dispose(bool disposing)
        {
            // The service owns production handlers. Tests intentionally reuse the inert stub.
        }
    }

    private sealed class ThrowOnReadContent : HttpContent
    {
        public bool WasRead { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            WasRead = true;
            throw new InvalidOperationException("Body should not be read when the header is valid.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }
}
