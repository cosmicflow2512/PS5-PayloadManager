using System.Windows;
using System.Windows.Media;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Views;

namespace PS5AutoPayloadTool;

public partial class MainWindow : Window
{
    // ── Static shared state ──────────────────────────────────────────────────
    public static AppConfig Config { get; private set; } = new();
    public static GitHubClient GitHub { get; private set; } = new();
    public static PayloadManager PayloadMgr { get; private set; } = new(new GitHubClient());

    // ── Page instances (created once) ────────────────────────────────────────
    private readonly SourcesPage      _sourcesPage      = new();
    private readonly PayloadsPage     _payloadsPage     = new();
    private readonly FlowBuilderPage  _flowBuilderPage  = new();
    private readonly ProfilesPage     _profilesPage     = new();
    private readonly SettingsPage     _settingsPage     = new();

    // ── Port state brushes ───────────────────────────────────────────────────
    private static readonly SolidColorBrush _dotOff = new(Color.FromRgb(69,  71,  90));
    private static readonly SolidColorBrush _dotOn  = new(Color.FromRgb(166, 227, 161));

    public MainWindow()
    {
        // Load config before InitializeComponent so pages can read it
        Config       = ConfigManager.Load();
        GitHub       = new GitHubClient(string.IsNullOrWhiteSpace(Config.GitHubToken)
                           ? null : Config.GitHubToken);
        PayloadMgr   = new PayloadManager(GitHub);

        InitializeComponent();

        // Show sidebar host/device
        UpdateSidebarDevice();

        // Default page
        ContentArea.Content = _sourcesPage;
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private void NavSources_Checked(object sender, RoutedEventArgs e)
    {
        if (ContentArea == null) return;
        ContentArea.Content = _sourcesPage;
        _sourcesPage.Refresh();
    }

    private void NavPayloads_Checked(object sender, RoutedEventArgs e)
    {
        if (ContentArea == null) return;
        ContentArea.Content = _payloadsPage;
        _payloadsPage.Refresh();
    }

    private void NavFlow_Checked(object sender, RoutedEventArgs e)
    {
        if (ContentArea == null) return;
        ContentArea.Content = _flowBuilderPage;
        _flowBuilderPage.Refresh();
    }

    private void NavProfiles_Checked(object sender, RoutedEventArgs e)
    {
        if (ContentArea == null) return;
        ContentArea.Content = _profilesPage;
        _profilesPage.Refresh();
    }

    private void NavSettings_Checked(object sender, RoutedEventArgs e)
    {
        if (ContentArea == null) return;
        ContentArea.Content = _settingsPage;
        _settingsPage.Refresh();
    }

    // ── Public helpers called by pages ───────────────────────────────────────

    /// <summary>
    /// Persist the current config and refresh the sidebar host display.
    /// </summary>
    public static void SaveConfig()
    {
        ConfigManager.Save(Config);
    }

    /// <summary>
    /// Called by SettingsPage when the host or token changes.
    /// Re-creates the GitHub client if the token changed.
    /// </summary>
    public void OnConfigChanged()
    {
        UpdateSidebarDevice();
        GitHub     = new GitHubClient(string.IsNullOrWhiteSpace(Config.GitHubToken)
                         ? null : Config.GitHubToken);
        PayloadMgr = new PayloadManager(GitHub);
        ConfigManager.Save(Config);
    }

    private void UpdateSidebarDevice()
    {
        var host   = Config.PS5Host;
        var device = Config.Devices.FirstOrDefault(d => d.Ip == host);
        SidebarDeviceName.Text = device?.DisplayName ?? (Config.Devices.Count > 0
            ? Config.Devices[0].DisplayName : "No device");
        SidebarHost.Text = host;
    }

    /// <summary>Update the sidebar port indicator dots.</summary>
    public void SetPortIndicators(bool luaOpen, bool elfOpen)
    {
        Dispatcher.Invoke(() =>
        {
            SidebarLuaDot.Fill = luaOpen ? _dotOn : _dotOff;
            SidebarElfDot.Fill = elfOpen ? _dotOn : _dotOff;
        });
    }
}
