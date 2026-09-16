using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lidless.App;

internal static class Ui
{
    public static readonly SolidColorBrush Background = Brush("#1C1C1E");
    public static readonly SolidColorBrush Card = Brush("#2C2C2E");
    public static readonly SolidColorBrush Text = Brush("#F5F5F7");
    public static readonly SolidColorBrush Secondary = Brush("#A1A1A6");
    public static readonly SolidColorBrush Accent = Brush("#0A84FF");
    public static readonly SolidColorBrush Green = Brush("#30D158");
    public static readonly SolidColorBrush Orange = Brush("#FF9F0A");
    public static readonly SolidColorBrush Line = Brush("#3A3A3C");

    public static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    public static TextBlock Label(string text, double size = 13, FontWeight? weight = null, Brush? foreground = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = foreground ?? Text,
            FontFamily = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    public static Border Divider() => new()
    {
        Height = 1,
        Background = Line,
        Margin = new Thickness(0, 8, 0, 8),
    };

    public static ToggleButton Switch(bool on)
    {
        var button = new ToggleButton
        {
            IsChecked = on,
            Width = 40,
            Height = 22,
            Cursor = Cursors.Hand,
            Template = SwitchTemplate(),
        };
        return button;
    }

    private static ControlTemplate SwitchTemplate()
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var track = new FrameworkElementFactory(typeof(Border));
        track.Name = "Track";
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        track.SetValue(Border.WidthProperty, 40.0);
        track.SetValue(Border.HeightProperty, 22.0);
        track.SetValue(Border.BackgroundProperty, Line);

        var knob = new FrameworkElementFactory(typeof(Ellipse));
        knob.Name = "Knob";
        knob.SetValue(FrameworkElement.WidthProperty, 16.0);
        knob.SetValue(FrameworkElement.HeightProperty, 16.0);
        knob.SetValue(Shape.FillProperty, Text);
        knob.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        knob.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 0, 0, 0));
        track.AppendChild(knob);
        template.VisualTree = track;

        var onTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        onTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Accent, "Track"));
        onTrigger.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Knob"));
        onTrigger.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 3, 0), "Knob"));
        template.Triggers.Add(onTrigger);
        return template;
    }
}

internal sealed class WpfAlertPresenter : IAlertPresenter
{
    public void PresentFailure(bool targetEnable, string message)
    {
        var title = targetEnable ? "Couldn’t keep your PC awake" : "Couldn’t turn keep-awake off";
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

internal sealed class TrayPopupWindow : Window
{
    private readonly KeepAwakeController _controller;
    private readonly LoginItemService _login;
    private readonly Action _openSettings;
    private readonly Action _exit;
    private bool _suppress;

    public TrayPopupWindow(KeepAwakeController controller, LoginItemService login, Action openSettings, Action exit)
    {
        _controller = controller;
        _login = login;
        _openSettings = openSettings;
        _exit = exit;

        Title = "Lidless";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Ui.Background;
        AllowsTransparency = false;
        Deactivated += (_, _) => Hide();

        Content = Build();
        _controller.Changed += (_, _) => Dispatcher.Invoke(Refresh);
    }

    public void ShowNearTray(int x, int y)
    {
        Refresh();
        Left = Math.Max(0, x - Width);
        Top = Math.Max(0, y - ActualHeight - 8);
        if (ActualHeight <= 0)
        {
            Show();
            Left = Math.Max(0, x - Width);
            Top = Math.Max(0, y - Height - 8);
            return;
        }

        Show();
        Activate();
    }

    private ToggleButton _master = null!;
    private TextBlock _durationValue = null!;
    private TextBlock _countdown = null!;
    private TextBlock _autoHint = null!;
    private StackPanel _warnings = null!;
    private TextBlock _watchdog = null!;
    private TextBlock _battery = null!;
    private TextBlock _notice = null!;
    private ToggleButton _charging = null!;
    private ToggleButton _thermal = null!;
    private ToggleButton _autoEnable = null!;
    private ComboBox _cutoff = null!;
    private ToggleButton _loginToggle = null!;

    private UIElement Build()
    {
        var _root = new StackPanel { Margin = new Thickness(20, 18, 20, 14) };

        var header = new DockPanel { LastChildFill = true };
        header.Children.Add(Ui.Label("Lidless", 16, FontWeights.SemiBold));
        var version = Ui.Label("v1.0.0", 12, foreground: Ui.Secondary);
        DockPanel.SetDock(version, Dock.Right);
        header.Children.Insert(0, version);
        _root.Children.Add(header);

        _root.Children.Add(new TextBlock
        {
            Text = "Keep your PC awake when the lid is closed.",
            FontSize = 12,
            Foreground = Ui.Secondary,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        _root.Children.Add(Ui.Divider());

        _master = Ui.Switch(_controller.MasterToggleOn);
        _master.Checked += (_, _) => { if (!_suppress) _controller.SetMasterToggle(true); };
        _master.Unchecked += (_, _) => { if (!_suppress) _controller.SetMasterToggle(false); };
        _root.Children.Add(Row("Keep awake with lid closed", _master, bold: true));

        var durationRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        durationRow.Children.Add(Ui.Label("Keep awake for", 13));
        var durationBtn = new Button
        {
            Content = (_durationValue = Ui.Label(AutoOff.DurationLabel(_controller.AutoOffMinutes), 13, foreground: Ui.Accent)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
        };
        durationBtn.Click += (_, e) =>
        {
            var menu = new ContextMenu { PlacementTarget = durationBtn };
            void Add(int minutes)
            {
                var item = new MenuItem { Header = AutoOff.DurationLabel(minutes) };
                item.Click += (_, _) => _controller.KeepAwakeFor(minutes);
                menu.Items.Add(item);
            }
            Add(0);
            foreach (var m in AutoOff.PresetMinutes) Add(m);
            menu.IsOpen = true;
            e.Handled = true;
        };
        DockPanel.SetDock(durationBtn, Dock.Right);
        durationRow.Children.Add(durationBtn);
        _root.Children.Add(durationRow);

        _countdown = Ui.Label("", 12, foreground: Ui.Secondary);
        _countdown.Margin = new Thickness(0, 2, 0, 0);
        _root.Children.Add(_countdown);

        _autoHint = Ui.Label("Not used while “Automatically enable when charging” is on.", 12, foreground: Ui.Secondary);
        _autoHint.Margin = new Thickness(0, 2, 0, 0);
        _root.Children.Add(_autoHint);

        _warnings = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        _root.Children.Add(_warnings);

        _root.Children.Add(Ui.Divider());

        var status = new DockPanel();
        _watchdog = Ui.Label("Watchdog inactive", 12);
        _battery = Ui.Label("Battery", 12);
        DockPanel.SetDock(_battery, Dock.Right);
        status.Children.Add(_battery);
        status.Children.Add(_watchdog);
        _root.Children.Add(status);

        _notice = Ui.Label("", 12, foreground: Ui.Orange);
        _notice.Margin = new Thickness(0, 8, 0, 0);
        _root.Children.Add(_notice);

        _root.Children.Add(Ui.Divider());
        _root.Children.Add(Ui.Label("Safety", 12, FontWeights.SemiBold, Ui.Secondary));

        _charging = Ui.Switch(_controller.Settings.OnlyWhileCharging);
        _charging.Checked += (_, _) => UpdateSafety(s => s.OnlyWhileCharging = true);
        _charging.Unchecked += (_, _) => UpdateSafety(s => s.OnlyWhileCharging = false);
        _root.Children.Add(Row("Only while charging", _charging));

        _thermal = Ui.Switch(_controller.Settings.PauseOnHighThermal);
        _thermal.Checked += (_, _) => UpdateSafety(s => s.PauseOnHighThermal = true);
        _thermal.Unchecked += (_, _) => UpdateSafety(s => s.PauseOnHighThermal = false);
        _root.Children.Add(Row("Pause when running hot", _thermal));

        _cutoff = new ComboBox { Width = 88, FontSize = 12 };
        foreach (var n in new[] { 0, 10, 20, 30, 40, 50 })
        {
            _cutoff.Items.Add(n == 0 ? "Never" : $"{n}%");
        }
        _cutoff.SelectionChanged += (_, _) =>
        {
            if (_suppress) return;
            var map = new[] { 0, 10, 20, 30, 40, 50 };
            if (_cutoff.SelectedIndex >= 0)
            {
                UpdateSafety(s => s.LowBatteryThreshold = map[_cutoff.SelectedIndex]);
            }
        };
        _root.Children.Add(Row("Low-battery cutoff", _cutoff));

        _root.Children.Add(Ui.Divider());
        _root.Children.Add(Ui.Label("Automatic", 12, FontWeights.SemiBold, Ui.Secondary));

        _autoEnable = Ui.Switch(_controller.Settings.AutoEnableWhenCharging);
        _autoEnable.Checked += (_, _) => UpdateSafety(s => s.AutoEnableWhenCharging = true);
        _autoEnable.Unchecked += (_, _) => UpdateSafety(s => s.AutoEnableWhenCharging = false);
        _root.Children.Add(Row("Enable when charging", _autoEnable));

        _loginToggle = Ui.Switch(_login.IsEnabled);
        _loginToggle.Checked += (_, _) => SetLogin(true);
        _loginToggle.Unchecked += (_, _) => SetLogin(false);
        _root.Children.Add(Row("Launch at login", _loginToggle));

        _root.Children.Add(Ui.Divider());

        var footer = new DockPanel();
        var settings = Link("Settings", _openSettings);
        var quit = Link("Quit Lidless", _exit);
        DockPanel.SetDock(quit, Dock.Right);
        footer.Children.Add(quit);
        footer.Children.Add(settings);
        _root.Children.Add(footer);

        return _root;
    }

    private void UpdateSafety(Action<SafetySettings> mutate)
    {
        if (_suppress) return;
        var next = _controller.Settings.Clone();
        mutate(next);
        _controller.UpdateSettings(next);
    }

    private void SetLogin(bool enabled)
    {
        if (_suppress) return;
        var err = _login.SetEnabled(enabled);
        if (err is not null)
        {
            MessageBox.Show(err, "Launch at login", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Refresh();
    }

    private static Button Link(string text, Action action)
    {
        var btn = new Button
        {
            Content = Ui.Label(text, 12, foreground: Ui.Accent),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    private static DockPanel Row(string title, UIElement trailing, bool bold = false)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        var label = Ui.Label(title, bold ? 14 : 13, bold ? FontWeights.SemiBold : FontWeights.Normal);
        DockPanel.SetDock(trailing, Dock.Right);
        row.Children.Add(trailing);
        row.Children.Add(label);
        return row;
    }

    public void Refresh()
    {
        _suppress = true;
        try
        {
            _master.IsChecked = _controller.MasterToggleOn;
            _durationValue.Text = AutoOff.DurationLabel(_controller.AutoOffMinutes);
            _countdown.Text = string.IsNullOrEmpty(_controller.AutoOffRemaining)
                ? ""
                : $"Turning off in {_controller.AutoOffRemaining}";
            _countdown.Visibility = string.IsNullOrEmpty(_countdown.Text) ? Visibility.Collapsed : Visibility.Visible;
            var auto = _controller.Settings.AutoEnableWhenCharging;
            _autoHint.Visibility = auto ? Visibility.Visible : Visibility.Collapsed;

            _warnings.Children.Clear();
            foreach (var reason in _controller.AutoWarningReasons)
            {
                _warnings.Children.Add(Ui.Label("• " + reason.CheckLabel, 12, foreground: Ui.Orange));
            }

            var watchdogOn = _controller.WatchdogRunning;
            _watchdog.Text = watchdogOn ? "Watchdog active" : "Watchdog inactive";
            _watchdog.Foreground = watchdogOn ? Ui.Green : Ui.Orange;
            var battery = _controller.CurrentBattery;
            _battery.Text = $"{battery.Source} · {battery.Percent}%";

            var notice = _controller.ExternalNotice ?? _controller.VerificationNotice ?? _controller.LastError;
            _notice.Text = notice ?? "";
            _notice.Visibility = string.IsNullOrEmpty(notice) ? Visibility.Collapsed : Visibility.Visible;

            _charging.IsChecked = _controller.Settings.OnlyWhileCharging;
            _thermal.IsChecked = _controller.Settings.PauseOnHighThermal;
            _autoEnable.IsChecked = _controller.Settings.AutoEnableWhenCharging;
            var cutoff = _controller.Settings.LowBatteryThreshold;
            _cutoff.SelectedIndex = cutoff switch { 10 => 1, 20 => 2, 30 => 3, 40 => 4, 50 => 5, _ => 0 };
            _loginToggle.IsChecked = _login.IsEnabled;
        }
        finally
        {
            _suppress = false;
        }
    }
}
