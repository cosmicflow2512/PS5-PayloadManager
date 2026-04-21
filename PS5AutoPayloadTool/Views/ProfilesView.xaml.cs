using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Views;

public partial class ProfilesView : UserControl
{
    private readonly ObservableCollection<ProfileVM> _rows = [];
    private string _runningProfile = "";

    public event Action<string>? EditRequested;

    public ProfilesView()
    {
        InitializeComponent();
        ProfilesList.ItemsSource = _rows;
        LogBus.OnStateChange += (state, profile) => Dispatcher.Invoke(() => OnEngineState(state, profile));
        Refresh();
    }

    private void OnEngineState(string state, string profile)
    {
        _runningProfile = state is ExecEngine.Running or ExecEngine.Paused ? profile : "";
        bool running = !string.IsNullOrEmpty(_runningProfile);
        RunningBar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        RunningName.Text = _runningProfile;
        foreach (var r in _rows) r.UpdateRunState(_runningProfile);
    }

    public void Refresh()
    {
        var favs = Storage.LoadProfileFavs();
        _rows.Clear();
        foreach (var filename in Storage.ListProfiles())
        {
            var content = Storage.ReadProfile(filename) ?? "";
            var steps   = AutoloadParser.Parse(content).Count;
            var display = filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? filename[..^4] : filename;
            var isFav   = favs.Contains(filename);
            _rows.Add(new ProfileVM
            {
                Filename    = filename,
                DisplayName = display,
                Info        = $"{steps} step{(steps != 1 ? "s" : "")}",
                IsFav       = isFav,
                FavStar     = isFav ? "⭐" : "☆",
                FavColor    = isFav ? Brushes.Gold : (Brush)Application.Current.FindResource("TextMuted"),
            });
            _rows.Last().UpdateRunState(_runningProfile);
        }

        // Favorites first
        var sorted = _rows.OrderByDescending(r => r.IsFav).ThenBy(r => r.DisplayName).ToList();
        _rows.Clear();
        foreach (var r in sorted) _rows.Add(r);

        CountBadge.Text = $"{_rows.Count} flow{(_rows.Count != 1 ? "s" : "")}";
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnToggleFav(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        var favs = Storage.LoadProfileFavs();
        if (favs.Contains(name)) favs.Remove(name); else favs.Add(name);
        Storage.SaveProfileFavs(favs);
        Refresh();
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string name)
            EditRequested?.Invoke(name);
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;

        if (ExecEngine.State is ExecEngine.Running or ExecEngine.Paused)
        {
            ExecEngine.RequestStop();
            return;
        }

        var ip = Storage.LoadPs5Ip();
        if (string.IsNullOrEmpty(ip))
        {
            MessageBox.Show("Set PS5 IP in Settings first.", "No IP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var content = Storage.ReadProfile(name) ?? "";
        if (string.IsNullOrWhiteSpace(content)) { MessageBox.Show("Profile is empty."); return; }

        var continueOnError = ContinueOnError.IsChecked == true;
        LogBus.Log($"Running flow '{name}'", LogLevel.Info);
        await ExecEngine.RunAsync(ip, content, continueOnError: continueOnError, profileName: name);
    }

    private void OnStopCurrent(object sender, RoutedEventArgs e) => ExecEngine.RequestStop();

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        if (MessageBox.Show($"Delete flow '{name}'?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Storage.DeleteProfile(name);
        LogBus.Log($"Deleted flow '{name}'", LogLevel.Info);
        Refresh();
    }
}

public class ProfileVM : INotifyPropertyChanged
{
    private bool _isRunning;
    private string _runLabel = "▶ Run";
    private Brush _runColor = new SolidColorBrush(Color.FromRgb(59, 130, 246));

    public string Filename    { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Info        { get; set; } = "";
    public bool   IsFav       { get; set; }
    public string FavStar     { get; set; } = "☆";
    public Brush  FavColor    { get; set; } = Brushes.Gray;

    public bool IsRunning
    {
        get => _isRunning;
        set { _isRunning = value; OnPropertyChanged(); }
    }

    public string RunLabel
    {
        get => _runLabel;
        set { _runLabel = value; OnPropertyChanged(); }
    }

    public Brush RunColor
    {
        get => _runColor;
        set { _runColor = value; OnPropertyChanged(); }
    }

    public void UpdateRunState(string runningProfile)
    {
        IsRunning = runningProfile == Filename;
        if (IsRunning)
        {
            RunLabel = "⏹ Stop";
            RunColor = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }
        else if (!string.IsNullOrEmpty(runningProfile))
        {
            RunLabel = "▶ Run";
            RunColor = new SolidColorBrush(Color.FromRgb(107, 114, 128));
        }
        else
        {
            RunLabel = "▶ Run";
            RunColor = new SolidColorBrush(Color.FromRgb(59, 130, 246));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
