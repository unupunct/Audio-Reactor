using System.Windows;
using System.Windows.Forms;

namespace AudioReactor.App.Services;

/// <summary>System tray icon + Windows notifications (toasts), each category switchable in Settings. Also raises the in-app toast.</summary>
public sealed class NotificationService : IDisposable
{
    private readonly NotifyIcon _icon;
    public event Action<string>? InAppToast;
    private string _lastTrack = "";

    public NotificationService(Action showMain, Action togglePlay, Action next, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Audio Reactor", null, (_, _) => showMain());
        menu.Items.Add("Play / Pause", null, (_, _) => togglePlay());
        menu.Items.Add("Next track", null, (_, _) => next());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        System.Drawing.Icon? ico = null;
        try { var uri = new Uri("pack://application:,,,/Assets/AudioReactor.ico"); using var s = System.Windows.Application.GetResourceStream(uri)?.Stream; if (s != null) ico = new System.Drawing.Icon(s); } catch { }
        _icon = new NotifyIcon { Icon = ico ?? System.Drawing.SystemIcons.Application, Text = "Audio Reactor", Visible = true, ContextMenuStrip = menu };
        _icon.DoubleClick += (_, _) => showMain();
    }

    /// <summary>Handles a player event. kind: track | radio | error.</summary>
    public void FromPlayer(string kind, string msg)
    {
        var s = App.Settings;
        switch (kind)
        {
            case "track": if (s.NotifyTrack && msg != _lastTrack) Toast("Now playing", msg); _lastTrack = msg; break;
            case "radio": if (s.NotifyRadio) Toast("Internet radio", msg); break;
            case "error": if (s.NotifyErrors) Toast("Playback error", msg, ToolTipIcon.Warning); break;
        }
        InAppToast?.Invoke(kind == "track" ? "Now playing: " + msg : msg);
    }

    public void Device(string msg) { if (App.Settings.NotifyDevices) Toast("Audio device", msg); InAppToast?.Invoke(msg); }
    public void Recording(string msg) { if (App.Settings.NotifyRecording) Toast("Recording", msg); InAppToast?.Invoke(msg); }

    private void Toast(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        // Only pop a Windows notification when the app is not in the foreground; the in-app toast covers the focused case.
        var w = System.Windows.Application.Current?.MainWindow;
        if (w != null && w.IsActive && w.IsVisible) return;
        try { _icon.ShowBalloonTip(4000, title, text.Length > 200 ? text[..200] : text, icon); } catch { }
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
