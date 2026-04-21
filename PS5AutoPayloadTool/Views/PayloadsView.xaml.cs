using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Views;

public partial class PayloadsView : UserControl
{
    public ObservableCollection<PayloadRow> Rows { get; } = [];
    private string _search = "", _filter = "all";

    public PayloadsView()
    {
        InitializeComponent();
        PayloadsList.ItemsSource = Rows;
        Refresh();
    }

    public void Refresh()
    {
        var list = Storage.ListPayloads();
        Rows.Clear();
        foreach (var p in list)
        {
            var ext = Path.GetExtension(p.Name).ToLowerInvariant();
            if (_filter == "elf" && ext != ".elf") continue;
            if (_filter == "lua" && ext != ".lua") continue;
            if (!string.IsNullOrEmpty(_search) && !p.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)) continue;

            var info = string.IsNullOrEmpty(p.Version)
                ? $"{FormatBytes(p.Size)} • {p.Modified.ToLocalTime():g}"
                : $"{p.Version} • {FormatBytes(p.Size)} • {(string.IsNullOrEmpty(p.Repo) ? "local" : p.Repo)}";
            Rows.Add(new PayloadRow { Name = p.Name, Info = info });
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text ?? "";
        Refresh();
    }

    private void OnFilter(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb) { _filter = rb.Tag?.ToString() ?? "all"; Refresh(); }
    }

    private void OnUpload(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Payloads (*.elf;*.lua;*.bin)|*.elf;*.lua;*.bin",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames)
        {
            var dest = Path.Combine(AppPaths.PayloadsDir, Path.GetFileName(f));
            File.Copy(f, dest, true);
        }
        LogBus.Log($"Uploaded {dlg.FileNames.Length} payload(s)", LogLevel.Success);
        Refresh();
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        var ip = Storage.LoadUiState()["ps5_ip"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(ip))
        {
            MessageBox.Show("Set PS5 IP in Settings first.", "No IP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var port = PayloadSender.ResolvePort(name);
        LogBus.Log($"Sending {name} -> {ip}:{port}", LogLevel.Info);
        var (ok, msg, _) = await PayloadSender.SendAsync(ip, port, name);
        LogBus.Log(msg, ok ? LogLevel.Success : LogLevel.Error);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        if (MessageBox.Show($"Delete {name}?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var path = Path.Combine(AppPaths.PayloadsDir, name);
        if (File.Exists(path)) File.Delete(path);
        var meta = Storage.LoadPayloadMeta();
        meta.Remove(name);
        Storage.SavePayloadMeta(meta);
        LogBus.Log($"Deleted {name}", LogLevel.Info);
        Refresh();
    }

    private static string FormatBytes(long b)
    {
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:F1} KB";
        return $"{b / (1024.0 * 1024):F1} MB";
    }
}

public class PayloadRow
{
    public string Name { get; set; } = "";
    public string Info { get; set; } = "";
}
