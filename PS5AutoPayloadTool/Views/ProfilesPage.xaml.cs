using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Views;

/// <summary>ViewModel for a single profile card in the list.</summary>
public class ProfileViewModel
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string Preview  { get; set; } = "";
}

public partial class ProfilesPage : UserControl
{
    private readonly ExecEngine _engine = new();

    private static readonly SolidColorBrush _clrDefault = new(Color.FromRgb(205, 214, 244));
    private static readonly SolidColorBrush _clrGreen   = new(Color.FromRgb(166, 227, 161));
    private static readonly SolidColorBrush _clrRed     = new(Color.FromRgb(243, 139, 168));
    private static readonly SolidColorBrush _clrYellow  = new(Color.FromRgb(249, 226, 175));
    private static readonly SolidColorBrush _clrSubtle  = new(Color.FromRgb(108, 112, 134));

    public ProfilesPage()
    {
        InitializeComponent();
        _engine.ProgressChanged += OnEngineProgress;
    }

    public void Refresh()
    {
        PopulateList();
    }

    // ── List ─────────────────────────────────────────────────────────────────

    private void PopulateList()
    {
        var profiles = new List<ProfileViewModel>();

        if (Directory.Exists(AppPaths.ProfilesDir))
        {
            foreach (var path in Directory.GetFiles(AppPaths.ProfilesDir, "*.txt")
                                          .OrderBy(p => p))
            {
                string preview;
                try
                {
                    var lines = File.ReadAllLines(path)
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                        .Take(3)
                        .ToArray();
                    preview = string.Join("\n", lines);
                }
                catch
                {
                    preview = "";
                }

                profiles.Add(new ProfileViewModel
                {
                    FileName = Path.GetFileName(path),
                    FullPath = path,
                    Preview  = preview
                });
            }
        }

        ProfilesList.ItemsSource = profiles;
        EmptyState.Visibility    = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Import ───────────────────────────────────────────────────────────────

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title       = "Import Profile(s)",
            Filter      = "Profile files (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var src in dlg.FileNames)
        {
            var dst = Path.Combine(AppPaths.ProfilesDir, Path.GetFileName(src));
            File.Copy(src, dst, overwrite: true);
        }

        PopulateList();
    }

    // ── Run ──────────────────────────────────────────────────────────────────

    private async void BtnRunProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string fullPath) return;

        if (!File.Exists(fullPath))
        {
            Log($"File not found: {fullPath}", _clrRed);
            return;
        }

        List<IDirective> directives;
        try
        {
            directives = ProfileParser.ParseFile(fullPath, AppPaths.PayloadsDir);
        }
        catch (Exception ex)
        {
            Log($"Parse error: {ex.Message}", _clrRed);
            return;
        }

        if (directives.Count == 0)
        {
            Log("Profile is empty — nothing to run.", _clrYellow);
            return;
        }

        Log($"Running {Path.GetFileName(fullPath)} ({directives.Count} step(s))…", _clrSubtle);

        BtnPause.IsEnabled = true;
        BtnStop.IsEnabled  = true;
        PrgProgress.Value  = 0;

        await _engine.RunAsync(MainWindow.Config.PS5Host, directives);

        BtnPause.IsEnabled = false;
        BtnStop.IsEnabled  = false;
    }

    // ── Edit ─────────────────────────────────────────────────────────────────

    private void BtnEditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string fullPath) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = fullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"Cannot open editor: {ex.Message}", _clrRed);
        }
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string fullPath) return;

        var result = MessageBox.Show(
            $"Delete \"{Path.GetFileName(fullPath)}\"?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try { File.Delete(fullPath); }
        catch (Exception ex)
        {
            Log($"Delete failed: {ex.Message}", _clrRed);
            return;
        }

        PopulateList();
    }

    // ── Pause / Stop ─────────────────────────────────────────────────────────

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.State == ExecState.Running)
        {
            _engine.RequestPause();
            BtnPause.Content = "Resume";
        }
        else if (_engine.State == ExecState.Paused)
        {
            _engine.RequestResume();
            BtnPause.Content = "Pause";
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _engine.RequestStop();
        BtnPause.IsEnabled = false;
        BtnStop.IsEnabled  = false;
        BtnPause.Content   = "Pause";
    }

    // ── Engine progress ──────────────────────────────────────────────────────

    private void OnEngineProgress(object? sender, ExecProgressEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var colour = e.IsError              ? _clrRed    :
                         e.Message.Contains("OK") ? _clrGreen :
                         e.Message.Contains("Wait") ? _clrYellow :
                         _clrDefault;

            Log(e.Message, colour);

            if (e.TotalSteps > 0 && e.StepIndex >= 0)
                PrgProgress.Value = (double)e.StepIndex / e.TotalSteps * 100.0;

            if (e.State == ExecState.Completed)
                PrgProgress.Value = 100;

            if (e.State is ExecState.Completed or ExecState.Failed or ExecState.Stopped)
            {
                BtnPause.IsEnabled = false;
                BtnStop.IsEnabled  = false;
                BtnPause.Content   = "Pause";
            }
        });
    }

    // ── Log helper ───────────────────────────────────────────────────────────

    private void Log(string message, SolidColorBrush? colour = null)
    {
        Dispatcher.Invoke(() =>
        {
            var ts   = DateTime.Now.ToString("HH:mm:ss");
            var item = new ListBoxItem
            {
                Content    = $"[{ts}] {message}",
                Foreground = colour ?? _clrDefault
            };
            LstLog.Items.Add(item);
            LstLog.ScrollIntoView(item);
        });
    }
}
