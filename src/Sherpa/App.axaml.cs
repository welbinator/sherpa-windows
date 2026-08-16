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

        // Free WebView.Avalonia stack (WebView2 on Windows) — not Avalonia Accelerate.
        // UserDataFolder MUST be a writable, stable path. Single-file / Program Files
        // defaults leave WebView2 with a black empty surface and no useful error.
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
