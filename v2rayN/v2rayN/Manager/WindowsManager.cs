using System.Drawing;
using System.Windows.Media.Imaging;

namespace v2rayN.Manager;

public sealed class WindowsManager
{
    private static readonly Lazy<WindowsManager> instance = new(() => new());
    public static WindowsManager Instance => instance.Value;

    public Task<Icon> GetNotifyIcon(Config config)
    {
        _ = config;
        return Task.FromResult(Properties.Resources.NotifyIcon1);
    }

    public System.Windows.Media.ImageSource GetAppIcon(Config config)
    {
        _ = config;
        return BitmapFrame.Create(new Uri("pack://application:,,,/Resources/v2rayN.ico", UriKind.RelativeOrAbsolute));
    }

    public void RegisterGlobalHotkey(Config config, Action<EGlobalHotkey> handler, Action<bool, string>? update)
    {
        HotkeyManager.Instance.UpdateViewEvent += update;
        HotkeyManager.Instance.HotkeyTriggerEvent += handler;
        HotkeyManager.Instance.Load();
    }
}
