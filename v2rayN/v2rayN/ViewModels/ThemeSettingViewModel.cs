using MaterialDesignColors;
using MaterialDesignColors.ColorManipulation;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.Windows.Media;

namespace v2rayN.ViewModels;

public class ThemeSettingViewModel : MyReactiveObject
{
    private readonly PaletteHelper _paletteHelper = new();

    private IObservableCollection<Swatch> _swatches = new ObservableCollectionExtended<Swatch>();
    public IObservableCollection<Swatch> Swatches => _swatches;

    [Reactive]
    public Swatch SelectedSwatch { get; set; }

    [Reactive] public string CurrentTheme { get; set; }

    [Reactive] public int CurrentFontSize { get; set; }

    [Reactive] public string CurrentLanguage { get; set; }

    public ThemeSettingViewModel()
    {
        _config = AppManager.Instance.Config;

        RegisterSystemColorSet(_config, ModifyTheme);

        BindingUI();
        RestoreUI();
    }

    private void RestoreUI()
    {
        ModifyTheme();
        ModifyFontSize();
        if (!_config.UiItem.ColorPrimaryName.IsNullOrEmpty())
        {
            var swatch = new SwatchesProvider().Swatches.FirstOrDefault(t => t.Name == _config.UiItem.ColorPrimaryName);
            if (swatch?.ExemplarHue?.Color is not null)
            {
                ChangePrimaryColor(swatch.ExemplarHue.Color);
            }
        }
    }

    private void BindingUI()
    {
        _swatches.AddRange(new SwatchesProvider().Swatches);
        if (!_config.UiItem.ColorPrimaryName.IsNullOrEmpty())
        {
            SelectedSwatch = _swatches.FirstOrDefault(t => t.Name == _config.UiItem.ColorPrimaryName);
        }
        CurrentTheme = _config.UiItem.CurrentTheme;
        CurrentFontSize = _config.UiItem.CurrentFontSize;
        CurrentLanguage = _config.UiItem.CurrentLanguage;

        this.WhenAnyValue(
                x => x.CurrentTheme,
                y => y != null && !y.IsNullOrEmpty())
            .Subscribe(c =>
             {
                 if (_config.UiItem.CurrentTheme != CurrentTheme)
                 {
                     _config.UiItem.CurrentTheme = CurrentTheme;
                     ModifyTheme();
                     _ = ConfigHandler.SaveConfig(_config);
                 }
             });

        this.WhenAnyValue(
          x => x.SelectedSwatch,
          y => y != null && !y.Name.IsNullOrEmpty())
             .Subscribe(c =>
             {
                 if (SelectedSwatch == null
                 || SelectedSwatch.Name.IsNullOrEmpty()
                 || SelectedSwatch.ExemplarHue == null
                 || SelectedSwatch.ExemplarHue?.Color == null)
                 {
                     return;
                 }
                 if (_config.UiItem.ColorPrimaryName != SelectedSwatch?.Name)
                 {
                     _config.UiItem.ColorPrimaryName = SelectedSwatch?.Name;
                     ChangePrimaryColor(SelectedSwatch.ExemplarHue.Color);
                     _ = ConfigHandler.SaveConfig(_config);
                 }
             });

        this.WhenAnyValue(
           x => x.CurrentFontSize,
           y => y > 0)
              .Subscribe(c =>
              {
                  if (_config.UiItem.CurrentFontSize != CurrentFontSize)
                  {
                      _config.UiItem.CurrentFontSize = CurrentFontSize;
                      ModifyFontSize();
                      _ = ConfigHandler.SaveConfig(_config);
                  }
              });

        this.WhenAnyValue(
         x => x.CurrentLanguage,
         y => y != null && !y.IsNullOrEmpty())
            .Subscribe(c =>
            {
                if (CurrentLanguage.IsNotEmpty() && _config.UiItem.CurrentLanguage != CurrentLanguage)
                {
                    _config.UiItem.CurrentLanguage = CurrentLanguage;
                    Thread.CurrentThread.CurrentUICulture = new(CurrentLanguage);
                    _ = ConfigHandler.SaveConfig(_config);
                    NoticeManager.Instance.Enqueue(ResUI.NeedRebootTips);
                }
            });
    }

    public void ModifyTheme()
    {
        var baseTheme = CurrentTheme switch
        {
            nameof(ETheme.Dark) or nameof(ETheme.Dusk) or nameof(ETheme.NightSky) => BaseTheme.Dark,
            nameof(ETheme.Light) or nameof(ETheme.Aquatic) or nameof(ETheme.Desert) => BaseTheme.Light,
            _ => BaseTheme.Inherit,
        };

        var theme = _paletteHelper.GetTheme();
        theme.SetBaseTheme(baseTheme);
        _paletteHelper.SetTheme(theme);

        var isDark = baseTheme == BaseTheme.Dark
            || (baseTheme == BaseTheme.Inherit && IsSystemDark());
        ApplyQuietControlPalette(isDark);

        WindowsUtils.SetDarkBorder(Application.Current.MainWindow, CurrentTheme);
    }

    private void ModifyFontSize()
    {
        double size = CurrentFontSize;
        if (size < Global.MinFontSize)
        {
            return;
        }

        Application.Current.Resources["StdFontSize"] = size;
        Application.Current.Resources["StdFontSize1"] = size + 1;
        Application.Current.Resources["StdFontSize-1"] = size - 1;
    }

    public void ChangePrimaryColor(System.Windows.Media.Color color)
    {
        var theme = _paletteHelper.GetTheme();

        theme.PrimaryLight = new ColorPair(color.Lighten());
        theme.PrimaryMid = new ColorPair(color);
        theme.PrimaryDark = new ColorPair(color.Darken());

        _paletteHelper.SetTheme(theme);
        ApplyQuietPrimaryColor(color);
    }

    private void ApplyQuietControlPalette(bool isDark)
    {
        if (isDark)
        {
            SetQuietColor("QccCanvasColor", Color.FromRgb(18, 22, 29));
            SetQuietColor("QccSurfaceColor", Color.FromRgb(25, 30, 39));
            SetQuietColor("QccSurfaceRaisedColor", Color.FromRgb(30, 36, 47));
            SetQuietColor("QccBorderColor", Color.FromRgb(53, 62, 76));
            SetQuietColor("QccTextColor", Color.FromRgb(235, 239, 245));
            SetQuietColor("QccMutedColor", Color.FromRgb(157, 169, 185));
            SetQuietColor("QccHoverColor", Color.FromRgb(37, 45, 58));
            SetQuietColor("QccPrimaryColor", Color.FromRgb(105, 158, 255));
            SetQuietColor("QccPrimarySoftColor", Color.FromRgb(34, 55, 91));
            SetQuietColor("QccSuccessColor", Color.FromRgb(74, 201, 126));
            SetQuietColor("QccWarningColor", Color.FromRgb(250, 184, 71));
            SetQuietColor("QccDangerColor", Color.FromRgb(255, 117, 117));
        }
        else
        {
            var isAquatic = CurrentTheme == nameof(ETheme.Aquatic);
            var isDesert = CurrentTheme == nameof(ETheme.Desert);

            SetQuietColor("QccCanvasColor", isDesert ? Color.FromRgb(250, 248, 242) : isAquatic ? Color.FromRgb(244, 250, 251) : Color.FromRgb(246, 247, 249));
            SetQuietColor("QccSurfaceColor", Color.FromRgb(255, 255, 255));
            SetQuietColor("QccSurfaceRaisedColor", isDesert ? Color.FromRgb(255, 252, 247) : Color.FromRgb(251, 252, 254));
            SetQuietColor("QccBorderColor", isDesert ? Color.FromRgb(233, 225, 210) : isAquatic ? Color.FromRgb(213, 231, 233) : Color.FromRgb(225, 229, 236));
            SetQuietColor("QccTextColor", Color.FromRgb(23, 32, 51));
            SetQuietColor("QccMutedColor", Color.FromRgb(102, 112, 133));
            SetQuietColor("QccHoverColor", isDesert ? Color.FromRgb(247, 241, 232) : isAquatic ? Color.FromRgb(234, 246, 247) : Color.FromRgb(240, 243, 248));
            SetQuietColor("QccPrimaryColor", isDesert ? Color.FromRgb(181, 112, 26) : isAquatic ? Color.FromRgb(13, 116, 132) : Color.FromRgb(37, 99, 235));
            SetQuietColor("QccPrimarySoftColor", isDesert ? Color.FromRgb(252, 239, 218) : isAquatic ? Color.FromRgb(222, 242, 244) : Color.FromRgb(232, 240, 255));
            SetQuietColor("QccSuccessColor", Color.FromRgb(22, 163, 74));
            SetQuietColor("QccWarningColor", Color.FromRgb(245, 158, 11));
            SetQuietColor("QccDangerColor", Color.FromRgb(220, 38, 38));
        }

        var customPrimary = new SwatchesProvider().Swatches
            .FirstOrDefault(t => t.Name == _config.UiItem.ColorPrimaryName)
            ?.ExemplarHue?.Color;
        if (customPrimary is not null)
        {
            ApplyQuietPrimaryColor(customPrimary.Value);
        }
    }

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme")?.ToString().ToInt() == 0;
    }

    private static void ApplyQuietPrimaryColor(Color color)
    {
        SetQuietColor("QccPrimaryColor", color);
        SetQuietColor("QccPrimarySoftColor", Soften(color, 0.88));
    }

    private static Color Soften(Color color, double amount)
    {
        byte Blend(byte channel) => (byte)Math.Round(channel + ((255 - channel) * amount));
        return Color.FromRgb(Blend(color.R), Blend(color.G), Blend(color.B));
    }

    private static void SetQuietColor(string key, Color color)
    {
        if (Application.Current?.Resources is not null)
        {
            Application.Current.Resources[key] = color;
        }
    }

    public static void RegisterSystemColorSet(Config config, Action updateFunc)
    {
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if ((e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.General)
                && config.UiItem.CurrentTheme == nameof(ETheme.FollowSystem))
            {
                updateFunc?.Invoke();
            }
        };
    }
}
