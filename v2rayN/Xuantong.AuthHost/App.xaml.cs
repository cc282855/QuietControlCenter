using System.Windows;

namespace Xuantong.AuthHost;

public partial class App : Application
{
    private LoginWindow? _loginWindow;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!HostSecurity.IsMediumIntegrityInteractiveUser()
            || !AuthTicket.TryConsume(e.Args, out var ticket))
        {
            Shutdown(12);
            return;
        }

        _ = RunAsync(ticket);
    }

    private async Task RunAsync(AuthTicket ticket)
    {
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var pipe = await AuthPipeConnection.ConnectAsync(ticket, connectTimeout.Token);
        if (pipe is null) { Shutdown(13); return; }
        using var monitorCancellation = new CancellationTokenSource();
        try
        {
            var startupCleanupSucceeded = OwnedUdfStore.CleanupAllOwned() && SessionStore.CleanupPending();
            var workTask = !startupCleanupSucceeded
                ? Task.FromResult(new AuthResponse("CleanupFailed"))
                : ticket.Operation switch
            {
                "clear" => Task.FromResult(SessionStore.Clear(ticket)),
                "query-session" => AuthenticatedQuotaQuery.QueryAsync(ticket),
                _ => RunWindowAsync(ticket, pipe)
            };
            var monitor = MonitorOwnerAsync(pipe, monitorCancellation.Token);
            if (await Task.WhenAny(workTask, monitor) != workTask)
            {
                _loginWindow?.AbortWithoutSaving();
                try { await Task.WhenAny(workTask, Task.Delay(TimeSpan.FromSeconds(3))); } catch { }
                Shutdown(14);
                return;
            }
            monitorCancellation.Cancel();
            using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await workTask;
            if (!await pipe.SendAndAwaitAckAsync(response, sendTimeout.Token))
            {
                _loginWindow?.RollbackPendingSession();
                Shutdown(15);
                return;
            }

            var commitStatus = "NotRequired";
            if (_loginWindow?.HasPendingSession == true)
            {
                commitStatus = await _loginWindow.CommitPendingSessionAsync() ? "Committed" : "CommitFailed";
            }
            try
            {
                await pipe.SendCommitResultAsync(commitStatus, sendTimeout.Token);
            }
            catch
            {
                _loginWindow?.RollbackPendingSession();
                throw;
            }
            if (commitStatus == "Committed") _loginWindow?.FinalizePendingSession();
            else if (commitStatus == "CommitFailed") _loginWindow?.RollbackPendingSession();
            Shutdown(commitStatus != "CommitFailed" && response.Status is "Success" or "Cleared" ? 0 : 2);
        }
        catch
        {
            _loginWindow?.AbortWithoutSaving();
            Shutdown(15);
        }
    }

    private async Task<AuthResponse> RunWindowAsync(AuthTicket ticket, AuthPipeConnection pipe)
    {
        if (!await HostSecurity.IsLoopbackSocksAvailableAsync(ticket.SocksPort))
        {
            return new("ProxyUnavailable");
        }
        _loginWindow = new LoginWindow(ticket, pipe.IsExpectedServerAlive);
        Current.MainWindow = _loginWindow;
        _loginWindow.Show();
        var result = await _loginWindow.Completion;
        if (await _loginWindow.CleanupAsync()) return result;
        _loginWindow.RollbackPendingSession();
        return new("CleanupFailed");
    }

    private static async Task MonitorOwnerAsync(AuthPipeConnection pipe, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!pipe.IsExpectedServerAlive()) return;
            await Task.Delay(250, cancellationToken);
        }
    }
}
