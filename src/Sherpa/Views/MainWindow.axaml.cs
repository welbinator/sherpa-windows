using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaWebView;
using Sherpa.ViewModels;
using WebViewCore.Events;

namespace Sherpa.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private WebView? _web;
    private bool _webReady;
    private bool _wired;
    private string? _lastNavigated;
    private int _httpFallbackTries;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachVm(DataContext as MainViewModel);
        Opened += (_, _) =>
        {
            EnsureWebWired();
            // Native WebView2 often isn't ready on first Opened — retry a few times.
            ApplyPreview(force: true);
            DispatcherTimer.RunOnce(() => ApplyPreview(force: true), TimeSpan.FromMilliseconds(250));
            DispatcherTimer.RunOnce(() => ApplyPreview(force: true), TimeSpan.FromMilliseconds(1000));
            DispatcherTimer.RunOnce(() => ApplyPreview(force: true), TimeSpan.FromMilliseconds(2500));
        };
    }

    private void AttachVm(MainViewModel? vm)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
        EnsureWebWired();
        ApplyPreview(force: true);
    }

    private void EnsureWebWired()
    {
        _web ??= this.FindControl<WebView>("SitePreviewWebView");
        if (_web is null || _wired) return;
        _wired = true;

        _web.WebViewCreated += OnWebViewCreated;
        _web.NavigationCompleted += OnNavigationCompleted;
        _web.NavigationStarting += OnNavigationStarting;
    }

    private void OnWebViewCreated(object? sender, WebViewCreatedEventArgs e)
    {
        _webReady = e.IsSucceed;
        if (_vm is null) return;

        if (!e.IsSucceed)
        {
            _vm.StatusLine = "Preview engine failed: " + (string.IsNullOrWhiteSpace(e.Message)
                ? "WebView2 did not start. Install the WebView2 Runtime from Microsoft."
                : e.Message);
            _vm.SitePreviewBody =
                "Embedded preview could not start.\n\n" +
                (string.IsNullOrWhiteSpace(e.Message) ? "WebView2 Runtime missing or blocked." : e.Message) +
                "\n\nInstall “Microsoft Edge WebView2 Runtime”, fully quit Sherpa, and reopen.";
            _vm.SitePreviewIsError = true;
            return;
        }

        _vm.StatusLine = "Preview engine ready.";
        // First real navigation after native control exists.
        Dispatcher.UIThread.Post(() => ApplyPreview(force: true));
    }

    private void OnNavigationStarting(object? sender, WebViewUrlLoadingEventArg e)
    {
        if (_vm is null || e.Url is null) return;
        _vm.StatusLine = "Loading " + e.Url;
    }

    private void OnNavigationCompleted(object? sender, WebViewUrlLoadedEventArg e)
    {
        if (_vm is null) return;

        if (e.IsSuccess)
        {
            _httpFallbackTries = 0;
            _vm.SitePreviewIsError = false;
            if (!string.IsNullOrWhiteSpace(_lastNavigated))
                _vm.StatusLine = "Preview loaded " + _lastNavigated;
            return;
        }

        // HTTPS with untrusted Herd/mkcert certs often fails inside WebView2 even when
        // our HttpClient probe (which ignores certs) returns 200. Fall back to http:// once.
        if (_httpFallbackTries < 1
            && !string.IsNullOrWhiteSpace(_lastNavigated)
            && _lastNavigated.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _httpFallbackTries++;
            var http = "http://" + _lastNavigated["https://".Length..];
            _vm.StatusLine = "HTTPS preview failed — trying " + http;
            NavigateTo(http, force: true);
            return;
        }

        _vm.SitePreviewIsError = true;
        _vm.StatusLine = "Preview failed to load" +
                         (string.IsNullOrWhiteSpace(_lastNavigated) ? "." : ": " + _lastNavigated);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.PreviewUrl)
            or nameof(MainViewModel.PreviewReloadToken)
            or nameof(MainViewModel.IsDetailOverview))
        {
            ApplyPreview(force: e.PropertyName == nameof(MainViewModel.PreviewReloadToken));
        }
    }

    private void ApplyPreview(bool force)
    {
        if (_vm is null) return;
        if (!_vm.IsDetailOverview) return;

        EnsureWebWired();
        if (_web is null) return;

        var url = _vm.PreviewUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        // If the platform view isn't up yet, setting Url is often a no-op → black frame.
        if (!_webReady && _web.PlatformWebView is null)
            return;

        NavigateTo(uri.ToString(), force);
    }

    private void NavigateTo(string url, bool force)
    {
        if (_web is null) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;

        if (!force && string.Equals(_lastNavigated, url, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            _lastNavigated = url;
            // Prefer Url assignment (property-change path inside the control). On force reload
            // of the same address, also call Reload when possible.
            if (force && _web.Url is not null
                && string.Equals(_web.Url.ToString(), url, StringComparison.OrdinalIgnoreCase)
                && _webReady)
            {
                _web.Reload();
            }
            else
            {
                _web.Url = uri;
            }
        }
        catch (Exception ex)
        {
            if (_vm is not null)
                _vm.StatusLine = "Preview navigate error: " + ex.Message;
        }
    }
}
