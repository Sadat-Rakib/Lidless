using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Lidless.App;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private HeartbeatServer? _heartbeat;
    private WindowsPowerSession? _power;
    private KeepAwakeController? _controller;
    private Process? _watchdog;
    private Forms.NotifyIcon? _tray;
    private TrayPopupWindow? _popup;
    private SettingsWindow? _settings;
    private readonly LoginItemService _login = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _seconds = new() { Interval = TimeSpan.FromSeconds(1) };
    private System.Drawing.Icon? _iconOff;
    private System.Drawing.Icon? _iconOn;

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--watchdog", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(WatchdogProgram.Run());
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, @"Local\Lidless.SingleInstance", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        WindowsPowerSession.RestoreOrphanedSession();

        _heartbeat = HeartbeatServer.Create();
        _power = new WindowsPowerSession(_heartbeat);
        var settingsPath = Path.Combine(SessionStore.DirectoryPath, "settings.json");
        var store = new SettingsStore(new JsonFileKeyValueStore(settingsPath));

        _controller = new KeepAwakeController(
            _power,
            new WindowsBatterySource(),
            new WmiThermalSource(),
            store,
            new WpfAlertPresenter());

        try
        {
            _watchdog = WatchdogHost.Start();
            _controller.WatchdogRunning = !_watchdog.HasExited;
        }
        catch (Exception ex)
        {
            _controller.WatchdogRunning = false;
            Debug.WriteLine(ex);
        }

        _iconOff = TrayIconFactory.Create(false);
        _iconOn = TrayIconFactory.Create(true);
        _popup = new TrayPopupWindow(_controller, _login, ShowSettings, ExitApp);

        _tray = new Forms.NotifyIcon
        {
            Icon = _controller.IsEnabled ? _iconOn : _iconOff,
            Text = "Lidless",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.MouseUp += TrayOnMouseUp;

        _controller.Changed += (_, _) => Dispatcher.Invoke(RefreshTray);
        _poll.Tick += (_, _) =>
        {
            _controller.WatchdogRunning = _watchdog is { HasExited: false };
            _power.PulseHeartbeat();
            _controller.Tick();
        };
        _seconds.Tick += (_, _) =>
        {
            if (_controller.AutoOffDeadline is not null)
            {
                _controller.AutoOffTick();
            }
        };
        _poll.Start();
        _seconds.Start();
        _power.PulseHeartbeat();

        if (!_controller.OnboardingComplete)
        {
            var onboarding = new OnboardingWindow(() => _controller.CompleteOnboarding());
            onboarding.Show();
        }
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var toggle = new Forms.ToolStripMenuItem("Keep awake with lid closed");
        toggle.Click += (_, _) => _controller!.SetMasterToggle(!_controller.MasterToggleOn);
        menu.Items.Add(toggle);
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Quit Lidless", null, (_, _) => ExitApp());
        menu.Opening += (_, _) =>
        {
            toggle.Checked = _controller!.MasterToggleOn;
        };
        return menu;
    }

    private void TrayOnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Left || _popup is null)
        {
            return;
        }

        var pos = Forms.Control.MousePosition;
        _popup.ShowNearTray(pos.X, pos.Y);
    }

    private void RefreshTray()
    {
        if (_tray is null || _controller is null)
        {
            return;
        }

        _tray.Icon = _controller.IsEnabled ? _iconOn : _iconOff;
        _tray.Text = _controller.IsEnabled ? "Lidless — keep-awake on" : "Lidless";
        _popup?.Refresh();
        if (_controller.IsEnabled)
        {
            _power?.PulseHeartbeat();
        }
    }

    private void ShowSettings()
    {
        if (_settings is { IsVisible: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow();
        _settings.Show();
        _settings.Activate();
    }

    private void ExitApp()
    {
        try
        {
            _controller?.Shutdown();
            _power?.RequestWatchdogShutdown();
            if (_watchdog is { HasExited: false })
            {
                if (!_watchdog.WaitForExit(2000))
                {
                    _watchdog.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // Still quit.
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _poll.Stop();
        _seconds.Stop();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _iconOn?.Dispose();
        _iconOff?.Dispose();
        _power?.Dispose();
        _heartbeat?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
