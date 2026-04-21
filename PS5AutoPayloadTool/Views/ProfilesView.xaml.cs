using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Views;

public partial class ProfilesView : UserControl
{
    public ObservableCollection<ProfileRow> Rows { get; } = [];
    public event Action<string>? EditRequested;

    public ProfilesView()
    {
        InitializeComponent();
        ProfilesList.ItemsSource = Rows;
        Refresh();
    }

    public void Refresh()
    {
        Rows.Clear();
        foreach (var filename in Storage.ListProfiles())
        {
            var content = Storage.ReadProfile(filename) ?? "";
            var steps = AutoloadParser.Parse(content).Count;
            var display = filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? filename[..^4] : filename;
            Rows.Add(new ProfileRow
            {
                Filename    = filename,
                DisplayName = display,
                Info        = $"{steps} step{(steps != 1 ? "s" : "")}",
            });
        }
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string name)
            EditRequested?.Invoke(name);
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        var ip = Storage.LoadUiState()["ps5_ip"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(ip))
        {
            MessageBox.Show("Set PS5 IP in Settings first.", "No IP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var content = Storage.ReadProfile(name) ?? "";
        if (string.IsNullOrWhiteSpace(content)) { MessageBox.Show("Profile is empty."); return; }
        if (ExecEngine.State is ExecEngine.Running or ExecEngine.Paused)
        {
            ExecEngine.RequestStop();
            return;
        }
        LogBus.Log($"Running profile '{name}'", LogLevel.Info);
        await ExecEngine.RunAsync(ip, content, continueOnError: false, profileName: name);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        if (MessageBox.Show($"Delete profile '{name}'?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Storage.DeleteProfile(name);
        LogBus.Log($"Deleted profile '{name}'", LogLevel.Info);
        Refresh();
    }
}

public class ProfileRow
{
    public string Filename    { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Info        { get; set; } = "";
}
