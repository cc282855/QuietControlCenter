using v2rayN.ViewModels;

namespace v2rayN.Views;

/// <summary>
/// ThemeSettingView.xaml
/// </summary>
public partial class ThemeSettingView
{
    private sealed record ThemeOption(string Value, string Display);

    public ThemeSettingView()
    {
        InitializeComponent();
        ViewModel = new ThemeSettingViewModel();

        cmbCurrentTheme.ItemsSource = new[]
        {
            new ThemeOption(nameof(ETheme.FollowSystem), "跟随系统"),
            new ThemeOption(nameof(ETheme.Light), "浅色"),
            new ThemeOption(nameof(ETheme.Dark), "深色"),
            new ThemeOption(nameof(ETheme.Aquatic), "水蓝"),
            new ThemeOption(nameof(ETheme.Desert), "沙漠"),
            new ThemeOption(nameof(ETheme.Dusk), "黄昏"),
            new ThemeOption(nameof(ETheme.NightSky), "夜空"),
        };
        cmbCurrentTheme.DisplayMemberPath = nameof(ThemeOption.Display);
        cmbCurrentTheme.SelectedValuePath = nameof(ThemeOption.Value);
        cmbCurrentFontSize.ItemsSource = Enumerable.Range(Global.MinFontSize, Global.MinFontSizeCount).ToList();
        cmbCurrentLanguage.ItemsSource = Global.Languages;

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.CurrentTheme, v => v.cmbCurrentTheme.SelectedValue).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Swatches, v => v.cmbSwatches.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSwatch, v => v.cmbSwatches.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CurrentFontSize, v => v.cmbCurrentFontSize.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CurrentLanguage, v => v.cmbCurrentLanguage.Text).DisposeWith(disposables);
        });
    }
}
