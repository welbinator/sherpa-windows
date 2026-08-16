using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sherpa.Models;
using Sherpa.Services;
using Sherpa.Support;

namespace Sherpa.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly AppServices _svc;

    public MainViewModel() : this(new AppServices()) { }

    public MainViewModel(AppServices services)
    {
        _svc = services;
        _svc.Notifications.Raised += (title, body) =>
        {
            ToastTitle = title;
            ToastBody = body;
            ShowToast = true;
        };
        ReloadSites();
        RefreshRuntimeStatus();
        LoadSettingsFields();
    }

    public ObservableCollection<Site> Sites { get; } = new();
    public ObservableCollection<ConflictAdvice> Advice { get; } = new();
    public ObservableCollection<PackageRow> Packages { get; } = new();
    public ObservableCollection<DeploymentRecord> Deployments { get; } = new();

    [ObservableProperty] private Site? selectedSite;
    [ObservableProperty] private int selectedNavIndex; // 0 sites, 1 hosts, 2 settings
    [ObservableProperty] private int selectedDetailTab; // overview git packages commands deploy
    [ObservableProperty] private bool isSitesNav = true;
    [ObservableProperty] private bool isHostsNav;
    [ObservableProperty] private bool isSettingsNav;
    [ObservableProperty] private bool newSiteBlank = true;
    [ObservableProperty] private bool newSiteStatamic;
    [ObservableProperty] private string logText = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string busyLabel = "";
    [ObservableProperty] private string statusLine = "Sherpa for Windows · 0.1.0";
    [ObservableProperty] private string runtimeStatus = "";
    [ObservableProperty] private string gitStatusText = "";
    [ObservableProperty] private string gitLogText = "";
    [ObservableProperty] private string packageQuery = "";
    [ObservableProperty] private string requirePackage = "";
    [ObservableProperty] private string requireVersion = "";
    [ObservableProperty] private bool showComposerSheet;
    [ObservableProperty] private bool showNewSiteWizard;
    [ObservableProperty] private bool showImportSheet;
    [ObservableProperty] private bool showToast;
    [ObservableProperty] private string toastTitle = "";
    [ObservableProperty] private string toastBody = "";
    [ObservableProperty] private string newSiteName = "";
    [ObservableProperty] private int newSiteMode; // 0 blank 1 statamic
    [ObservableProperty] private string importPath = "";
    [ObservableProperty] private string defaultSitesFolder = "";
    [ObservableProperty] private string preferredPhp = "";
    [ObservableProperty] private string preferredComposer = "";
    [ObservableProperty] private string preferredGit = "";
    [ObservableProperty] private string gitHubTokenInput = "";
    [ObservableProperty] private string gitHubStatus = "Not connected.";
    [ObservableProperty] private string cfTokenInput = "";
    [ObservableProperty] private string cfAccountId = "";
    [ObservableProperty] private string cfStatus = "Not connected.";
    [ObservableProperty] private string emptyTitle = "Create your first site";
    [ObservableProperty] private string emptyBody = "Start blank or create a Statamic project. You can also import a folder you already have.";

    partial void OnSelectedNavIndexChanged(int value)
    {
        IsSitesNav = value == 0;
        IsHostsNav = value == 1;
        IsSettingsNav = value == 2;
    }

    partial void OnNewSiteBlankChanged(bool value)
    {
        if (value) { NewSiteStatamic = false; NewSiteMode = 0; }
    }

    partial void OnNewSiteStatamicChanged(bool value)
    {
        if (value) { NewSiteBlank = false; NewSiteMode = 1; }
    }

    partial void OnSelectedSiteChanged(Site? value)
    {
        Deployments.Clear();
        if (value?.Deployments != null)
            foreach (var d in value.Deployments.OrderByDescending(x => x.At))
                Deployments.Add(d);
        _ = RefreshSitePanelsAsync();
    }

    private void ReloadSites()
    {
        Sites.Clear();
        foreach (var s in _svc.Sites.Load().OrderBy(s => s.Name))
            Sites.Add(s);
        if (SelectedSite is not null)
            SelectedSite = Sites.FirstOrDefault(s => s.Id == SelectedSite.Id);
    }

    private void PersistSites() => _svc.Sites.Save(Sites);

    private void LoadSettingsFields()
    {
        var p = _svc.Preferences.Load();
        DefaultSitesFolder = p.DefaultSitesFolder;
        PreferredPhp = p.PreferredPhpPath ?? "";
        PreferredComposer = p.PreferredComposerPath ?? "";
        PreferredGit = p.PreferredGitPath ?? "";
        if (_svc.Secrets.Has("github"))
            GitHubStatus = "GitHub token saved in the Windows secret store.";
        if (_svc.Secrets.Has("cloudflare"))
            CfStatus = "Cloudflare token saved in the Windows secret store.";
        var host = p.Hosts.FirstOrDefault(h => h.Provider == HostProviderKind.CloudflarePages);
        if (host?.Extra is string acct) CfAccountId = acct;
    }

    private void RefreshRuntimeStatus() => RuntimeStatus = _svc.Runtime.StatusSummary();

    private void AppendLog(string line)
    {
        if (string.IsNullOrEmpty(LogText)) LogText = line;
        else LogText += "\n" + line;
    }

    private void ClearLog()
    {
        LogText = "";
        Advice.Clear();
    }

    private void ShowAdviceFrom(string output)
    {
        Advice.Clear();
        foreach (var a in ConflictTranslator.Translate(output))
            Advice.Add(a);
    }

    private async Task RunJobAsync(string label, Func<CancellationToken, Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        BusyLabel = label;
        ClearLog();
        AppendLog(label + "…");
        try
        {
            await work(CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
            ShowAdviceFrom(ex.Message);
            StatusLine = "Something needed your attention.";
        }
        finally
        {
            IsBusy = false;
            BusyLabel = "";
        }
    }

    private async Task RefreshSitePanelsAsync()
    {
        if (SelectedSite is null) return;
        try
        {
            var st = await _svc.Git.StatusAsync(SelectedSite.Path);
            GitStatusText = st.Success ? st.Combined : st.Combined;
            var log = await _svc.Git.LogAsync(SelectedSite.Path);
            GitLogText = log.Combined;
        }
        catch (Exception ex)
        {
            GitStatusText = ex.Message;
            GitLogText = "";
        }

        try
        {
            Packages.Clear();
            var show = await _svc.Composer.ShowAsync(SelectedSite.Path);
            if (show.Success && !string.IsNullOrWhiteSpace(show.StdOut))
            {
                using var doc = JsonDocument.Parse(show.StdOut);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        Packages.Add(new PackageRow
                        {
                            Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Version = el.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                            Description = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                        });
                    }
                }
            }
        }
        catch
        {
            Packages.Clear();
        }
    }

    [RelayCommand]
    private void SelectNav(string index)
    {
        if (int.TryParse(index, out var i)) SelectedNavIndex = i;
    }

    [RelayCommand]
    private void OpenNewSiteWizard()
    {
        NewSiteName = "";
        NewSiteMode = 0;
        ShowNewSiteWizard = true;
    }

    [RelayCommand]
    private void CloseNewSiteWizard() => ShowNewSiteWizard = false;

    [RelayCommand]
    private void OpenImportSheet()
    {
        ImportPath = "";
        ShowImportSheet = true;
    }

    [RelayCommand]
    private void CloseImportSheet() => ShowImportSheet = false;

    [RelayCommand]
    private void OpenComposerSheet()
    {
        RequirePackage = "";
        RequireVersion = "";
        ShowComposerSheet = true;
    }

    [RelayCommand]
    private void CloseComposerSheet() => ShowComposerSheet = false;

    [RelayCommand]
    private void DismissToast() => ShowToast = false;

    [RelayCommand]
    private async Task BrowseImportFolderAsync()
    {
        var path = await PickFolderAsync("Choose a site folder");
        if (path is not null) ImportPath = path;
    }

    [RelayCommand]
    private async Task BrowseSitesFolderAsync()
    {
        var path = await PickFolderAsync("Default sites folder");
        if (path is not null) DefaultSitesFolder = path;
    }

    [RelayCommand]
    private async Task ImportSiteAsync()
    {
        var path = ImportPath.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            StatusLine = "Pick a folder that exists.";
            return;
        }
        if (Sites.Any(s => string.Equals(s.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
        {
            StatusLine = "That site is already in Sherpa.";
            ShowImportSheet = false;
            return;
        }
        var site = SiteDetector.FromPath(path);
        Sites.Add(site);
        PersistSites();
        SelectedSite = site;
        SelectedNavIndex = 0;
        ShowImportSheet = false;
        StatusLine = $"Imported {site.Name}.";
        await RefreshSitePanelsAsync();
    }

    [RelayCommand]
    private async Task CreateSiteAsync()
    {
        var prefs = _svc.Preferences.Load();
        var parent = string.IsNullOrWhiteSpace(DefaultSitesFolder) ? prefs.DefaultSitesFolder : DefaultSitesFolder;
        if (string.IsNullOrWhiteSpace(parent))
            parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Sites");

        await RunJobAsync(NewSiteStatamic ? "Creating Statamic project" : "Creating blank site", async ct =>
        {
            if (NewSiteStatamic)
            {
                var (site, result, error) = await _svc.Install.CreateStatamicAsync(parent, NewSiteName.Trim(), AppendLog, ct);
                if (result is not null && !result.Success)
                {
                    AppendLog(result.Combined);
                    ShowAdviceFrom(result.Combined);
                }
                if (error is not null)
                {
                    AppendLog(error);
                    ShowAdviceFrom(error + "\n" + (result?.Combined ?? ""));
                    return;
                }
                if (site is not null)
                {
                    Sites.Add(site);
                    PersistSites();
                    SelectedSite = site;
                    ShowNewSiteWizard = false;
                    _svc.Notifications.Notify("Site ready", $"{site.Name} is ready to open.");
                    StatusLine = $"Created {site.Name}.";
                }
            }
            else
            {
                var (site, _, error) = await _svc.Install.CreateBlankAsync(parent, NewSiteName.Trim(), AppendLog, ct);
                if (error is not null)
                {
                    AppendLog(error);
                    ShowAdviceFrom(error);
                    return;
                }
                if (site is not null)
                {
                    Sites.Add(site);
                    PersistSites();
                    SelectedSite = site;
                    ShowNewSiteWizard = false;
                    StatusLine = $"Created blank folder {site.Name}. Drop a Statamic/Laravel app in, or run Composer.";
                }
            }
        });
    }

    [RelayCommand]
    private void RemoveSiteFromList()
    {
        if (SelectedSite is null) return;
        var name = SelectedSite.Name;
        Sites.Remove(SelectedSite);
        SelectedSite = null;
        PersistSites();
        StatusLine = $"Removed {name} from Sherpa. Files on disk were left alone.";
    }

    [RelayCommand]
    private async Task OpenSiteFolderAsync()
    {
        if (SelectedSite is null) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = SelectedSite.Path,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusLine = ex.Message;
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshGitAsync()
    {
        if (SelectedSite is null) return;
        await RefreshSitePanelsAsync();
    }

    [RelayCommand]
    private async Task GitInitAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Initializing Git", async ct =>
        {
            var r = await _svc.Git.InitAsync(SelectedSite.Path, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else StatusLine = "Git repository initialized.";
            await RefreshSitePanelsAsync();
        });
    }

    [RelayCommand]
    private async Task ComposerInstallAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Composer install", async ct =>
        {
            var r = await _svc.Composer.InstallAsync(SelectedSite.Path, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else
            {
                StatusLine = "Composer install finished.";
                _svc.Notifications.Notify("Composer", "Install finished.");
            }
            await RefreshSitePanelsAsync();
        });
    }

    [RelayCommand]
    private async Task ComposerUpdateAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Composer update", async ct =>
        {
            var r = await _svc.Composer.UpdateAsync(SelectedSite.Path, null, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else
            {
                StatusLine = "Composer updated.";
                _svc.Notifications.Notify("Composer", "Update finished.");
            }
            await RefreshSitePanelsAsync();
        });
    }

    [RelayCommand]
    private async Task ComposerRequireAsync()
    {
        if (SelectedSite is null) return;
        if (string.IsNullOrWhiteSpace(RequirePackage))
        {
            StatusLine = "Enter a package name (vendor/package).";
            return;
        }
        await RunJobAsync("Composer require", async ct =>
        {
            var r = await _svc.Composer.RequireAsync(SelectedSite.Path, RequirePackage.Trim(),
                string.IsNullOrWhiteSpace(RequireVersion) ? null : RequireVersion.Trim(), AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else
            {
                ShowComposerSheet = false;
                StatusLine = $"Required {RequirePackage}.";
                _svc.Notifications.Notify("Composer", $"{RequirePackage} installed.");
            }
            await RefreshSitePanelsAsync();
        });
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Clear Cache", async ct =>
        {
            var r = await _svc.Commands.ClearLaravelCacheAsync(SelectedSite.Path, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else StatusLine = "Laravel cache cleared.";
        });
    }

    [RelayCommand]
    private async Task ClearStacheAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Clear Stache", async ct =>
        {
            var r = await _svc.Commands.ClearStacheAsync(SelectedSite.Path, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else StatusLine = "Stache cleared. Statamic will rebuild indexes on the next request.";
        });
    }

    [RelayCommand]
    private async Task WarmStacheAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Warm Stache", async ct =>
        {
            var r = await _svc.Commands.WarmStacheAsync(SelectedSite.Path, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else StatusLine = "Stache warmed.";
        });
    }

    [RelayCommand]
    private async Task ClearGlideAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Clear Glide Cache", async ct =>
        {
            var r = await _svc.Commands.ClearGlideAsync(SelectedSite.Path, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else StatusLine = "Glide cache cleared. Transforms regenerate on the next request.";
        });
    }

    [RelayCommand]
    private async Task CopyErrorsAsync()
    {
        var text = new StringBuilder();
        if (Advice.Count > 0)
        {
            foreach (var a in Advice)
                text.AppendLine($"• {a.Title}: {a.Detail}");
            text.AppendLine();
        }
        text.Append(LogText);
        var clipboard = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Clipboard
            : null;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text.ToString());
            StatusLine = "Copied errors to the clipboard.";
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var p = _svc.Preferences.Load();
        p.DefaultSitesFolder = DefaultSitesFolder.Trim();
        p.PreferredPhpPath = string.IsNullOrWhiteSpace(PreferredPhp) ? null : PreferredPhp.Trim();
        p.PreferredComposerPath = string.IsNullOrWhiteSpace(PreferredComposer) ? null : PreferredComposer.Trim();
        p.PreferredGitPath = string.IsNullOrWhiteSpace(PreferredGit) ? null : PreferredGit.Trim();
        _svc.Preferences.Save(p);
        RefreshRuntimeStatus();
        StatusLine = "Settings saved.";
    }

    [RelayCommand]
    private async Task SaveGitHubTokenAsync()
    {
        var token = GitHubTokenInput.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            GitHubStatus = "Add a GitHub token first. Create a classic token at GitHub → Settings → Developer settings.";
            return;
        }
        var (ok, message, _) = await _svc.GitHub.ValidateTokenAsync(token);
        if (!ok)
        {
            GitHubStatus = message;
            return;
        }
        _svc.Secrets.Set("github", token);
        var p = _svc.Preferences.Load();
        if (!p.Hosts.Any(h => h.Provider == HostProviderKind.GitHub))
        {
            p.Hosts.Add(new HostAccount
            {
                Provider = HostProviderKind.GitHub,
                Label = "GitHub",
                SecretKey = "github",
            });
            _svc.Preferences.Save(p);
        }
        GitHubTokenInput = "";
        GitHubStatus = message + " Token stays in the Windows secret store.";
    }

    [RelayCommand]
    private void ClearGitHubToken()
    {
        _svc.Secrets.Delete("github");
        GitHubStatus = "GitHub token removed.";
    }

    [RelayCommand]
    private async Task SaveCloudflareAsync()
    {
        var token = CfTokenInput.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            CfStatus = "Add a Cloudflare API token first.";
            return;
        }
        var (ok, message) = await _svc.Cloudflare.ValidateAsync(token, CfAccountId.Trim());
        CfStatus = message;
        if (!ok) return;
        _svc.Secrets.Set("cloudflare", token);
        var p = _svc.Preferences.Load();
        var host = p.Hosts.FirstOrDefault(h => h.Provider == HostProviderKind.CloudflarePages);
        if (host is null)
        {
            host = new HostAccount
            {
                Provider = HostProviderKind.CloudflarePages,
                Label = "Cloudflare Pages",
                SecretKey = "cloudflare",
                Extra = CfAccountId.Trim(),
            };
            p.Hosts.Add(host);
        }
        else host.Extra = CfAccountId.Trim();
        _svc.Preferences.Save(p);
        CfTokenInput = "";
        CfStatus = message + " Token stays in the Windows secret store.";
    }

    [RelayCommand]
    private async Task DeployPrerequisiteCheckAsync()
    {
        ClearLog();
        if (SelectedSite is null)
        {
            AppendLog("Pick a site first.");
            return;
        }
        if (!_svc.Secrets.Has("github"))
        {
            AppendLog("Add a GitHub token in Settings first.");
            ShowAdviceFrom("Add a GitHub token in Settings first.");
            SelectedNavIndex = 2;
            return;
        }
        if (!_svc.Secrets.Has("cloudflare"))
        {
            AppendLog("Connect Cloudflare Pages under Hosts/Settings first.");
            ShowAdviceFrom("Connect Netlify or Cloudflare Pages under Hosts first.");
            SelectedNavIndex = 1;
            return;
        }
        AppendLog("Prerequisites look good.");
        AppendLog("Deploy wizards for Forge / Laravel Cloud / full Pages publish are next — this build validates connections and prepares your local site.");
        AppendLog("Your site path: " + SelectedSite.Path);
        StatusLine = "Hosts connected. Full one-click deploy ships in the next builds.";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task BackupToGitHubAsync()
    {
        if (SelectedSite is null) return;
        if (!_svc.Secrets.Has("github"))
        {
            StatusLine = "Add a GitHub token in Settings first, then back up this site.";
            SelectedNavIndex = 2;
            return;
        }
        await RunJobAsync("Back up to GitHub", async ct =>
        {
            var token = _svc.Secrets.Get("github")!;
            var (ok, message, url) = await _svc.GitHub.CreatePrivateRepoAsync(token, SelectedSite.Name, ct);
            AppendLog(message);
            if (!ok)
            {
                ShowAdviceFrom(message);
                return;
            }
            AppendLog("Creates a private repo. Push from the Git tab or your terminal with the remote GitHub shows you.");
            if (url is not null) AppendLog(url);
            StatusLine = "Private GitHub repo created. Token stayed in the secret store.";
            _svc.Notifications.Notify("GitHub", $"Private repo ready for {SelectedSite.Name}");
        });
    }

    private static async Task<string?> PickFolderAsync(string title)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        var window = desktop.MainWindow;
        if (window is null) return null;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}

public partial class PackageRow : ObservableObject
{
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string version = "";
    [ObservableProperty] private string description = "";
}
