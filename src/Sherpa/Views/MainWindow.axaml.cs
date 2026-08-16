using System;
using System.ComponentModel;
using Avalonia.Controls;
using AvaloniaWebView;
using Sherpa.ViewModels;

namespace Sherpa.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachVm(DataContext as MainViewModel);
        Opened += (_, _) => ApplyPreview(force: true);
    }

    private void AttachVm(MainViewModel? vm)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
        ApplyPreview(force: true);
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

        var web = this.FindControl<WebView>("SitePreviewWebView");
        if (web is null) return;

        var url = _vm.PreviewUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        try
        {
            // Re-assigning the same Uri may not reload; null first when forcing.
            if (force)
                web.Url = null;
            web.Url = uri;
        }
        catch
        {
            // WebView may not be ready yet; next property change will retry
        }
    }
}
