using System;
using System.Drawing;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;
using Sherpa.ViewModels;
using Sherpa.Views;

namespace Sherpa;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void RegisterServices()
    {
        base.RegisterServices();

        // Preview stack is best-effort. If WebView2 bits are blocked/missing, Sherpa
        // must still open so the user can manage sites.
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sherpa",
                "WebView2");
            Directory.CreateDirectory(userData);

            AvaloniaWebViewBuilder.Initialize(props =>
            {
                props.UserDataFolder = userData;
                props.AreDevToolEnabled = true;
                props.DefaultWebViewBackgroundColor = Color.White;
            });
        }
        catch (Exception ex)
        {
            CrashLog.Write("AvaloniaWebViewBuilder.Initialize", ex, showDialog: false);
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new AppServices();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(services),
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
