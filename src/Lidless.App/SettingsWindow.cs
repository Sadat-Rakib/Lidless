using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace Lidless.App;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow()
    {
        Title = "Lidless Settings";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Background;
        Foreground = Ui.Text;

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(Ui.Label("How keep-awake works on Windows", 16, FontWeights.SemiBold));
        root.Children.Add(Paragraph(
            "Idle sleep is blocked with SetThreadExecutionState and a PowerCreateRequest " +
            "(system + away-mode). That does not, by itself, override “When I close the lid”."));
        root.Children.Add(Paragraph(
            "While keep-awake is on, Lidless also sets the current power scheme’s lid-close " +
            "action to Do nothing on AC and DC, then restores the previous values when you " +
            "turn it off, quit, or the watchdog notices a crash or hang (90 seconds without a heartbeat)."));
        root.Children.Add(Ui.Label("Limits you should know", 14, FontWeights.SemiBold));
        root.Children.Add(Paragraph(
            "• OEM utilities (Lenovo Vantage, Dell Power Manager, HP, Surface) can override lid actions.\n" +
            "• Group Policy / MDM can lock the power scheme so the change fails.\n" +
            "• Some firmware still sleeps on lid close regardless of Windows settings.\n" +
            "• The built-in display usually turns off when the lid is closed. That is expected; the PC should keep running.\n" +
            "• Unlike macOS SleepDisabled, Windows lid actions persist across reboot. If Lidless and its watchdog both die (hard power loss), “Do nothing” can stick until you open Lidless again or change it in Windows Settings ▸ System ▸ Power."));
        root.Children.Add(Ui.Label("Safety", 14, FontWeights.SemiBold));
        root.Children.Add(Paragraph(
            "Running closed under load can heat the chassis and drain the battery. Keep it plugged in and ventilated. " +
            "Safety guards pause keep-awake on heat, on battery (optional), and below the cutoff you choose."));
        root.Children.Add(Ui.Label("About", 14, FontWeights.SemiBold));
        root.Children.Add(Paragraph(
            "Lidless is a Windows tray app under the MIT License. Original keep-awake idea and macOS implementation " +
            "© 2026 Nghia Luong. Windows port © 2026 Sadat-Rakib."));

        var close = new Button
        {
            Content = "Close",
            Width = 88,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        close.Click += (_, _) => Close();
        root.Children.Add(close);
        Content = root;
    }

    private static TextBlock Paragraph(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = Ui.Secondary,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 8),
    };
}

internal sealed class OnboardingWindow : Window
{
    private int _step;
    private readonly Action _done;
    private readonly StackPanel _body = new();
    private readonly Button _next = new() { Width = 120, Height = 32 };

    public OnboardingWindow(Action done)
    {
        _done = done;
        Title = "Welcome to Lidless";
        Width = 460;
        Height = 520;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Background;
        ShowInTaskbar = true;

        var root = new DockPanel { Margin = new Thickness(0) };
        var footer = new DockPanel { Margin = new Thickness(24, 12, 24, 16) };
        DockPanel.SetDock(footer, Dock.Bottom);
        _next.Click += (_, _) => Advance();
        DockPanel.SetDock(_next, Dock.Right);
        footer.Children.Add(_next);
        var skip = new Button { Content = "Skip", Width = 80, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        skip.Click += (_, _) => Finish();
        footer.Children.Add(skip);

        _body.Margin = new Thickness(36, 36, 36, 12);
        root.Children.Add(footer);
        root.Children.Add(_body);
        Content = root;
        Render();
    }

    private void Advance()
    {
        if (_step >= 2)
        {
            Finish();
            return;
        }

        _step++;
        Render();
    }

    private void Finish()
    {
        _done();
        Close();
    }

    private void Render()
    {
        _body.Children.Clear();
        switch (_step)
        {
            case 0:
                _body.Children.Add(Ui.Label("Welcome to Lidless", 22, FontWeights.SemiBold));
                _body.Children.Add(Block("Keep your PC awake — even with the lid closed."));
                _body.Children.Add(Block("Built for coding agents, downloads, and long builds while you close the lid and walk away. Closing the lid is a Windows power-plan action; Lidless changes that plan for you and restores it afterwards."));
                _next.Content = "Continue";
                break;
            case 1:
                _body.Children.Add(Ui.Label("How it works", 22, FontWeights.SemiBold));
                _body.Children.Add(Block("1. Blocks idle sleep with a Windows power request."));
                _body.Children.Add(Block("2. Sets “When I close the lid” to Do nothing, then puts it back."));
                _body.Children.Add(Block("3. A watchdog process restores lid behavior if Lidless crashes or hangs."));
                _body.Children.Add(Block("Keep the PC plugged in and ventilated under heavy use. Some OEM firmware still sleeps on lid close — try it once before you rely on it."));
                _next.Content = "Continue";
                break;
            default:
                _body.Children.Add(Ui.Label("You’re set", 22, FontWeights.SemiBold));
                _body.Children.Add(Block("Lidless lives in the notification area. Click the laptop icon to toggle keep-awake, set a timer, and edit safety guards."));
                _body.Children.Add(Block("If you don’t see the icon, open the ^ overflow next to the clock. Launch at login is optional from the menu."));
                _next.Content = "Open Lidless";
                break;
        }
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text,
        FontSize = 14,
        Foreground = Ui.Secondary,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 12, 0, 0),
    };
}
