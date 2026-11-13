using System.IO;
using System.Windows;
using System.Windows.Media;
using AudioReactor.App.Services;
using AudioReactor.Core.Data;
using AudioReactor.Core.Models;
using Serilog;

namespace AudioReactor.App;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static Database Db { get; private set; } = null!;
    public static PlayerService Player { get; private set; } = null!;
    public static AnalysisService Analysis { get; private set; } = null!;
    public static RecordingService Recording { get; } = new();
    public static NotificationService Notifications => ((MainWindow)Current.MainWindow).Notifications;

    protected override void OnStartup(StartupEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.DataDir);
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
            .WriteTo.File(Path.Combine(AppSettings.DataDir, "logs", "audioreactor-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7).CreateLogger();
        DispatcherUnhandledException += (_, a) => { Log.Error(a.Exception, "Unhandled UI exception"); a.Handled = true; MessageBox.Show(a.Exception.Message, "Audio Reactor", MessageBoxButton.OK, MessageBoxImage.Warning); };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Fatal(a.ExceptionObject as Exception, "Fatal");

        Settings = AppSettings.Load();
        Db = new Database(Path.Combine(AppSettings.DataDir, "library.db"));
        Player = new PlayerService(Db, Settings);
        Analysis = new AnalysisService();
        Analysis.Lighting.Settings = Settings.Lighting;
        if (Settings.HueBridgeIp != null) { var hue = Analysis.Lighting.Get<Core.Lighting.HueAdapter>(); if (hue != null) { hue.BridgeIp = Settings.HueBridgeIp; hue.Username = Settings.HueUsername; } }
        Analysis.Start(Player.Engine.Analyzer);
        ApplyAccent(Settings.AccentColor);
        base.OnStartup(e);
        Log.Information("Audio Reactor started");
    }

    public static void ApplyAccent(string hex)
    {
        try { Current.Resources["AccentColor"] = (Color)ColorConverter.ConvertFromString(hex); Settings.AccentColor = hex; } catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Analysis?.Stop(); Analysis?.Lighting.TurnOffAsync().Wait(1500); Analysis?.Lighting.Dispose(); Player?.Dispose(); Settings.Save(); Db?.Dispose(); } catch (Exception ex) { Log.Error(ex, "Shutdown error"); }
        Log.Information("Audio Reactor exited");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
