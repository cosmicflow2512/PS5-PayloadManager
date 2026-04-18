using System.Windows;
using System.Windows.Media;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Core;
using PS5AutoPayloadTool.Modules.Devices;
using PS5AutoPayloadTool.Modules.Export;
using PS5AutoPayloadTool.Modules.Payloads;
using PS5AutoPayloadTool.Modules.Sources;
using PS5AutoPayloadTool.Views;

namespace PS5AutoPayloadTool;

public partial class MainWindow : Window
{
    // ── Core state (always loaded) ───────────────────────────────────────────

    public static AppConfig      Config     { get; private set; } = new();
    public static GitHubClient   GitHub     { get; private set; } = new();
    public static PayloadManager PayloadMgr { get; private set; } = new(new GitHubClient());

    // ── Module services (lazy — instantiated on first access) ────────────────

    private static SourceService?  _sourceSvc;
    private static PayloadService? _payloadSvc;
    private static ExportService?  _exportSvc;

    public static SourceService  SourceSvc  => _sourceSvc  ??= new SourceService(GitHub, PayloadMgr);
    public static PayloadService PayloadSvc => _payloadSvc ??= new PayloadService(PayloadMgr);
    public static ExportService  ExportSvc  => _exportSvc  ??= new ExportService(PayloadMgr);

    // ── Page instances ───────────────────────────────────────────────────────

    private readonly SourcesPage     _sourcesPage     = new();
    private readonly PayloadsPage    _payloadsPage    = new();
    private readonly FlowBuilderPage _flowBuilderPage = new();
    private readonly ProfilesPage    _profilesPage    = new();
    private readonly SettingsPage    _settingsPage    = new();
    private readonly LogsPage        _logsPage        = new();

    // ── Port indicator brushes ───────────────────────────────────────────────

    private static readonly SolidColorBrush _dotOff = new(Color.FromRgb(69,  71,  90));
    private static readonly SolidColorBrush _dotOn  = new(Color.FromRgb(166, 227, 161));

    // ── Construction ─────────────────────────────────────────────────────────

    public MainWindow()
    {
        Config     = ConfigManager.Load();
        LogService.DebugMode = Config.State.DebugMode;

        GitHub     = new GitHubClient(string.IsNullOrWhiteSpace(Config.GitHubToken)
                         ? null : Config.GitHubToken);
        PayloadMgr = new PayloadManager(GitHub);
        InvalidateServices();

        InitializeComponent();
        UpdateSidebarDevice();
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

    private void NavLogs_Checked(object sender, RoutedEventArgs e)
    {
        if (ContentArea == null) return;
        ContentArea.Content = _logsPage;
        _logsPage.Refresh();
    }

    // ── Public helpers called by pages ───────────────────────────────────────

    public static void SaveConfig() => ConfigManager.Save(Config);

    public void OnConfigChanged()
    {
        UpdateSidebarDevice();
        LogService.DebugMode = Config.State.DebugMode;
        GitHub     = new GitHubClient(string.IsNullOrWhiteSpace(Config.GitHubToken)
                         ? null : Config.GitHubToken);
        PayloadMgr = new PayloadManager(GitHub);
        InvalidateServices();
        ConfigManager.Save(Config);
    }

    public void OpenInBuilder(List<Models.BuilderStep> steps, string? profileName = null)
    {
        NavFlow.IsChecked = true;
        _flowBuilderPage.LoadSteps(steps, profileName);
    }

    public void SetPortIndicators(bool luaOpen, bool elfOpen)
    {
        Dispatcher.Invoke(() =>
        {
            SidebarLuaDot.Fill = luaOpen ? _dotOn : _dotOff;
            SidebarElfDot.Fill = elfOpen ? _dotOn : _dotOff;
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void UpdateSidebarDevice()
    {
        var host   = Config.PS5Host;
        var device = Config.Devices.FirstOrDefault(d => d.Ip == host);
        SidebarDeviceName.Text = device?.DisplayName ?? (Config.Devices.Count > 0
            ? Config.Devices[0].DisplayName : "No device");
        SidebarHost.Text = host;
    }

    private static void InvalidateServices()
    {
        _sourceSvc  = null;
        _payloadSvc = null;
        _exportSvc  = null;
    }
}
