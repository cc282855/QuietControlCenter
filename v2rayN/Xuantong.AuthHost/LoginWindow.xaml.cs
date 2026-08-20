using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using Microsoft.Web.WebView2.Core;

namespace Xuantong.AuthHost;

public partial class LoginWindow : Window
{
    private const int MaxBodyCharacters = 8 * 1024 * 1024;
    private readonly AuthTicket _ticket;
    private readonly Func<bool> _ownerAlive;
    private readonly TaskCompletionSource<AuthResponse> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<string> _confirmedExternalOrigins = new(StringComparer.Ordinal);
    private string? _userDataFolder;
    private bool _ready;
    private bool _aborted;
    private bool _busy;
    private SessionStore.PendingSession? _pendingSession;

    internal LoginWindow(AuthTicket ticket, Func<bool> ownerAlive)
    {
        _ticket = ticket;
        _ownerAlive = ownerAlive;
        InitializeComponent();
        OriginText.Text = "受信任官网：" + ticket.Origin;
        Loaded += Window_Loaded;
        Closed += Window_Closed;
    }

    internal Task<AuthResponse> Completion => _completion.Task;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        var maximumWidth = Math.Max(320, work.Width * 0.92);
        var maximumHeight = Math.Max(360, work.Height * 0.92);
        MinWidth = Math.Min(640, maximumWidth);
        MinHeight = Math.Min(480, maximumHeight);
        Width = Math.Min(980, maximumWidth);
        Height = Math.Min(720, maximumHeight);
        try
        {
            _userDataFolder = OwnedUdfStore.Create(_ticket);
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = BrowserSecurityOptions.BuildProxyArguments(_ticket.SocksPort)
            };
            var environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder, options);
            await Browser.EnsureCoreWebView2Async(environment);
            ConfigureBrowser(Browser.CoreWebView2);
            await SessionStore.LoadAsync(Browser.CoreWebView2.CookieManager, _ticket);
            _ready = true;
            SetStatus("安全浏览器已就绪，请完成登录后重新查询。");
            UpdateNavigationControls();
            Browser.Source = new Uri(_ticket.OfficialUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            SetStatus("未安装 Microsoft Edge WebView2 Runtime，请安装后重试。");
            Complete(new("WebView2RuntimeMissing"));
        }
        catch
        {
            SetStatus("安全登录组件初始化失败。");
            Complete(new("AuthHostUnavailable"));
        }
    }

    private void ConfigureBrowser(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.NavigationStarting += Core_NavigationStarting;
        core.NavigationCompleted += (_, _) => UpdateNavigationControls();
        core.HistoryChanged += (_, _) => UpdateNavigationControls();
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += Core_WebResourceRequested;
    }

    private void Core_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var origin = UriPolicy.Origin(e.Request.Uri);
        if (origin is not null && (origin == _ticket.Origin || _confirmedExternalOrigins.Contains(origin))) return;
        var core = (CoreWebView2)sender!;
        e.Response = core.Environment.CreateWebResourceResponse(
            new MemoryStream(Array.Empty<byte>()), 403, "Blocked", "Content-Type: text/plain\r\nCache-Control: no-store");
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!UriPolicy.TryNormalize(e.Uri, out _))
        {
            e.Cancel = true;
            return;
        }
        var origin = UriPolicy.Origin(e.Uri)!;
        if (string.Equals(origin, _ticket.Origin, StringComparison.Ordinal)
            || _confirmedExternalOrigins.Contains(origin))
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"官网请求跳转到第三方登录域：\n{origin}\n\n仅在确认这是您的登录服务时继续。该域 Cookie 不会被保存。",
            "确认第三方登录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
        {
            _confirmedExternalOrigins.Add(origin);
        }
        else
        {
            e.Cancel = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Complete(new("Cancelled"));
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_ready && !_busy && Browser.CanGoBack) Browser.GoBack();
    }
    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_ready && !_busy) Browser.Reload();
    }
    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (_ready && !_busy) Browser.Source = new Uri(_ticket.OfficialUrl);
    }
    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _busy) return;
        _busy = true;
        UpdateNavigationControls();
        SetStatus("正在清除登录信息…");
        try
        {
            Browser.CoreWebView2.CookieManager.DeleteAllCookies();
            await Browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
            SetStatus(SessionStore.ClearSavedSessions(_ticket)
                ? "登录信息已清除"
                : "清除失败，请关闭占用后重试");
        }
        catch { SetStatus("清除失败，请重试"); }
        finally
        {
            _busy = false;
            UpdateNavigationControls();
        }
    }

    private async void Query_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _busy) return;
        _busy = true;
        UpdateNavigationControls();
        SetStatus("正在安全查询…");
        try
        {
            var result = await FetchQuotaAsync();
            if (!_aborted && _ownerAlive() && result.Status is ("Success" or "AuthenticatedUnsupported"))
            {
                _pendingSession = await SessionStore.PrepareAsync(Browser.CoreWebView2.CookieManager, _ticket);
            }
            if (_aborted || !_ownerAlive())
            {
                RollbackPendingSession();
                Complete(new("Cancelled"));
                return;
            }
            switch (result.Status)
            {
                case "LoginRequired":
                    SetStatus("尚未完成登录，请继续登录后重试。");
                    break;
                case "NetworkError":
                    SetStatus("网络查询失败，请检查网络后重试。");
                    break;
                case "HttpError":
                    SetStatus("官网暂时无法完成查询，请稍后重试。");
                    break;
                case "BodyTooLarge":
                    SetStatus("官网返回内容超出安全限制，请稍后重试。");
                    break;
                default:
                    Complete(result);
                    break;
            }
        }
        catch
        {
            if (!_aborted && _ownerAlive())
                SetStatus("网络查询失败，请检查网络后重试。");
            else
            {
                RollbackPendingSession();
                Complete(new("Cancelled"));
            }
        }
        finally
        {
            _busy = false;
            UpdateNavigationControls();
        }
    }

    private async Task<AuthResponse> FetchQuotaAsync()
    {
        var urlLiteral = JsonSerializer.Serialize(_ticket.OfficialUrl);
        var script = $$"""
            (async () => {
              try {
                const response = await fetch({{urlLiteral}}, {credentials:'include', redirect:'follow', cache:'no-store'});
                const length = Number(response.headers.get('content-length') || '0');
                if (length > {{MaxBodyCharacters}}) return JSON.stringify({status:'BodyTooLarge'});
                const reader = response.body && response.body.getReader();
                if (!reader) return JSON.stringify({status:'NetworkError'});
                const decoder = new TextDecoder('utf-8', {fatal:true});
                let size = 0, body = '';
                while (true) {
                  const part = await reader.read();
                  if (part.done) break;
                  size += part.value.byteLength;
                  if (size > {{MaxBodyCharacters}}) { await reader.cancel(); return JSON.stringify({status:'BodyTooLarge'}); }
                  body += decoder.decode(part.value, {stream:true});
                }
                body += decoder.decode();
                return JSON.stringify({status:String(response.status), url:response.url,
                  userinfo:response.headers.get('subscription-userinfo'), body});
              } catch (_) { return JSON.stringify({status:'NetworkError'}); }
            })()
            """;
        var raw = await Browser.ExecuteScriptAsync(script);
        var payloadText = JsonSerializer.Deserialize<string>(raw);
        if (string.IsNullOrEmpty(payloadText) || payloadText.Length > MaxBodyCharacters + 4096)
            return new("BodyTooLarge");
        using var payload = JsonDocument.Parse(payloadText, new JsonDocumentOptions { MaxDepth = 8 });
        var root = payload.RootElement;
        var statusText = root.GetProperty("status").GetString();
        if (statusText is "NetworkError" or "BodyTooLarge") return new(statusText);
        if (!int.TryParse(statusText, out var status)) return new("NetworkError");
        var finalUrl = root.GetProperty("url").GetString();
        var body = root.GetProperty("body").GetString() ?? string.Empty;
        if (status is 401 or 403 || status is >= 300 and < 400
            || !UriPolicy.IsExactOrigin(finalUrl, _ticket.Origin)
            || LooksLikeLogin(finalUrl, body))
        {
            return new("LoginRequired");
        }
        if (status is < 200 or >= 300) return new("HttpError");
        if (root.TryGetProperty("userinfo", out var header)
            && AuthenticatedQuotaParser.TryParseHeader(header.GetString(), out var headerResponse))
        {
            return headerResponse;
        }
        return new("AuthenticatedUnsupported");
    }

    private static bool LooksLikeLogin(string? url, string body)
        => url?.Contains("login", StringComparison.OrdinalIgnoreCase) == true
           || url?.Contains("signin", StringComparison.OrdinalIgnoreCase) == true
           || (body.Contains("type=\"password\"", StringComparison.OrdinalIgnoreCase)
               && body.Contains("<form", StringComparison.OrdinalIgnoreCase));

    private void Complete(AuthResponse response)
    {
        if (_completion.TrySetResult(response)) Close();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _completion.TrySetResult(new("Cancelled"));
    }

    internal async Task<bool> CleanupAsync()
    {
        var success = true;
        if (Browser.CoreWebView2 is not null)
        {
            try { await Browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile); }
            catch { success = false; }
        }
        try { Browser.Dispose(); } catch { success = false; }
        if (_userDataFolder is null) return success;
        for (var attempt = 0; attempt < 15 && Directory.Exists(_userDataFolder); attempt++)
        {
            OwnedUdfStore.DeleteOwnedPath(_userDataFolder);
            if (Directory.Exists(_userDataFolder)) await Task.Delay(200);
        }
        var cleaned = success && !Directory.Exists(_userDataFolder);
        if (!cleaned) RollbackPendingSession();
        return cleaned;
    }

    internal void AbortWithoutSaving()
    {
        _aborted = true;
        RollbackPendingSession();
        if (!Dispatcher.CheckAccess()) Dispatcher.Invoke(AbortWithoutSaving);
        else if (IsVisible) Close();
    }

    internal bool HasPendingSession => _pendingSession is not null;

    internal Task<bool> CommitPendingSessionAsync()
        => _pendingSession?.CommitAsync() ?? Task.FromResult(true);

    internal bool RollbackPendingSession()
    {
        var pending = Interlocked.Exchange(ref _pendingSession, null);
        return pending?.Rollback() ?? true;
    }

    internal void FinalizePendingSession()
    {
        var pending = Interlocked.Exchange(ref _pendingSession, null);
        pending?.FinalizeCommit();
    }

    private void UpdateNavigationControls()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdateNavigationControls);
            return;
        }
        var enabled = _ready && !_busy && Browser.CoreWebView2 is not null;
        ReloadButton.IsEnabled = enabled;
        HomeButton.IsEnabled = enabled;
        ClearButton.IsEnabled = enabled;
        QueryButton.IsEnabled = enabled;
        BackButton.IsEnabled = enabled && Browser.CanGoBack;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        UIElementAutomationPeer.CreatePeerForElement(StatusText)?
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

}
