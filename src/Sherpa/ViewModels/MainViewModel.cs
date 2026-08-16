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
        ResetNewSiteForm();
    }

    public ObservableCollection<Site> Sites { get; } = new();
    public ObservableCollection<ConflictAdvice> Advice { get; } = new();
    public ObservableCollection<PackageRow> Packages { get; } = new();
    public ObservableCollection<DeploymentRecord> Deployments { get; } = new();
    public ObservableCollection<GitFileItem> GitFiles { get; } = new();
    public ObservableCollection<HostAccount> HostAccounts { get; } = new();

    [ObservableProperty] private Site? selectedSite;
    [ObservableProperty] private int selectedNavIndex;
    [ObservableProperty] private int selectedDetailTab;
    [ObservableProperty] private bool isSitesNav = true;
    [ObservableProperty] private bool isHostsNav;
    [ObservableProperty] private bool isSettingsNav;
    [ObservableProperty] private string logText = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string busyLabel = "";
    [ObservableProperty] private string statusLine = "Sherpa for Windows · 0.2.0";
    [ObservableProperty] private string runtimeStatus = "";
    [ObservableProperty] private string gitBranchLine = "";
    [ObservableProperty] private string gitLogText = "";
    [ObservableProperty] private string gitCommitMessage = "Update";
    [ObservableProperty] private string gitUserName = "";
    [ObservableProperty] private string gitUserEmail = "";
    [ObservableProperty] private string requirePackage = "";
    [ObservableProperty] private string requireVersion = "";
    [ObservableProperty] private bool showComposerSheet;
    [ObservableProperty] private bool showNewSiteWizard;
    [ObservableProperty] private bool showImportSheet;
    [ObservableProperty] private bool showCreateUserSheet;
    [ObservableProperty] private bool showCommandsSheet;
    [ObservableProperty] private bool showConnectHostSheet;
    [ObservableProperty] private bool showDeleteConfirm;
    [ObservableProperty] private bool deleteSiteFilesToo;
    [ObservableProperty] private bool showToast;
    [ObservableProperty] private string toastTitle = "";
    [ObservableProperty] private string toastBody = "";

    // New Site wizard (Mac identity + options + starting point)
    [ObservableProperty] private string newSiteName = "";
    [ObservableProperty] private string newSiteFolder = "";
    [ObservableProperty] private string newSiteUrlPreview = "";
    [ObservableProperty] private string newSiteWillCreate = "";
    [ObservableProperty] private bool newSiteParkInHerd = true;
    [ObservableProperty] private bool newSiteSecureHttps = true;
    [ObservableProperty] private bool startBlank = true;
    [ObservableProperty] private bool startFreshStatamic;

    [ObservableProperty] private string importPath = "";
    [ObservableProperty] private string defaultSitesFolder = "";
    [ObservableProperty] private string preferredPhp = "";
    [ObservableProperty] private string preferredComposer = "";
    [ObservableProperty] private string preferredGit = "";
    [ObservableProperty] private bool preferHerdForNewSites = true;
    [ObservableProperty] private bool secureNewHerdSites = true;
    [ObservableProperty] private string gitHubTokenInput = "";
    [ObservableProperty] private string gitHubStatus = "Not connected.";
    [ObservableProperty] private string packagistTokenInput = "";
    [ObservableProperty] private string packagistStatus = "";
    [ObservableProperty] private string hostTokenInput = "";
    [ObservableProperty] private string hostExtraInput = "";
    [ObservableProperty] private string hostLabelInput = "";
    [ObservableProperty] private int connectHostKind; // 0 GH handled in settings; 1 forge 2 cloud 3 cf 4 netlify
    [ObservableProperty] private string connectHostStatus = "";
    [ObservableProperty] private string createUserEmail = "";
    [ObservableProperty] private string createUserPassword = "";
    [ObservableProperty] private bool createUserSuper = true;
    [ObservableProperty] private string customCommand = "";
    [ObservableProperty] private string emptyTitle = "Create your first site";
    [ObservableProperty] private string emptyBody = "Create a new Statamic site, or import a folder you already have.";

    partial void OnSelectedNavIndexChanged(int value)
    {
        IsSitesNav = value == 0;
        IsHostsNav = value == 1;
        IsSettingsNav = value == 2;
        if (value == 1) ReloadHosts();
    }

    partial void OnSelectedSiteChanged(Site? value)
    {
        Deployments.Clear();
        if (value?.Deployments != null)
            foreach (var d in value.Deployments.OrderByDescending(x => x.At))
                Deployments.Add(d);
        _ = RefreshSitePanelsAsync();
    }

    partial void OnNewSiteNameChanged(string value) => RefreshNewSitePreviews();
    partial void OnNewSiteFolderChanged(string value) => RefreshNewSitePreviews();
    partial void OnNewSiteSecureHttpsChanged(bool value) => RefreshNewSitePreviews();
    partial void OnStartBlankChanged(bool value)
    {
        if (value) StartFreshStatamic = false;
    }
    partial void OnStartFreshStatamicChanged(bool value)
    {
        if (value) StartBlank = false;
    }

    private void RefreshNewSitePreviews()
    {
        var folder = string.IsNullOrWhiteSpace(NewSiteFolder) ? DefaultSitesFolder : NewSiteFolder;
        NewSiteUrlPreview = _svc.Herd.UrlPreview(NewSiteName, NewSiteSecureHttps);
        NewSiteWillCreate = string.IsNullOrWhiteSpace(NewSiteName)
            ? ""
            : _svc.Herd.WillCreatePath(folder, NewSiteName);
    }

    private void ResetNewSiteForm()
    {
        var p = _svc.Preferences.Load();
        NewSiteName = "";
        NewSiteFolder = string.IsNullOrWhiteSpace(p.DefaultSitesFolder)
            ? _svc.Herd.DefaultSitesDirectory()
            : p.DefaultSitesFolder;
        NewSiteParkInHerd = p.PreferHerdForNewSites;
        NewSiteSecureHttps = p.SecureNewHerdSitesWithHttps;
        RefreshNewSitePreviews();
    }

    private void ReloadSites()
    {
        Sites.Clear();
        foreach (var s in _svc.Sites.Load().OrderBy(s => s.Name))
            Sites.Add(s);
        if (SelectedSite is not null)
            SelectedSite = Sites.FirstOrDefault(s => s.Id == SelectedSite.Id);
    }

    private void ReloadHosts()
    {
        HostAccounts.Clear();
        foreach (var h in _svc.Preferences.Load().Hosts)
            HostAccounts.Add(h);
    }

    private void PersistSites() => _svc.Sites.Save(Sites);

    private void LoadSettingsFields()
    {
        var p = _svc.Preferences.Load();
        DefaultSitesFolder = string.IsNullOrWhiteSpace(p.DefaultSitesFolder)
            ? _svc.Herd.DefaultSitesDirectory()
            : p.DefaultSitesFolder;
        PreferredPhp = p.PreferredPhpPath ?? "";
        PreferredComposer = p.PreferredComposerPath ?? "";
        PreferredGit = p.PreferredGitPath ?? "";
        PreferHerdForNewSites = p.PreferHerdForNewSites;
        SecureNewHerdSites = p.SecureNewHerdSitesWithHttps;
        GitUserName = p.GitUserName;
        GitUserEmail = p.GitUserEmail;
        if (_svc.Secrets.Has("github"))
            GitHubStatus = "GitHub token saved in the Windows secret store.";
        if (_svc.Secrets.Has("packagist"))
            PackagistStatus = "Packagist token saved in the Windows secret store.";
        ReloadHosts();
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
        try { await work(CancellationToken.None); }
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
        GitFiles.Clear();
        try
        {
            var st = await _svc.Git.StatusPorcelainAsync(SelectedSite.Path);
            var lines = st.Combined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("##"))
                {
                    GitBranchLine = line;
                    continue;
                }
                if (line.Length < 4) continue;
                var code = line[..2];
                var path = line[3..].Trim();
                if (path.Contains(" -> "))
                    path = path.Split(" -> ", 2)[^1];
                GitFiles.Add(new GitFileItem { Status = code.Trim(), Path = path, Selected = true });
            }
            if (string.IsNullOrEmpty(GitBranchLine))
                GitBranchLine = st.Combined;
            var log = await _svc.Git.LogAsync(SelectedSite.Path);
            GitLogText = log.Success ? log.Combined : log.Combined;
        }
        catch (Exception ex)
        {
            GitBranchLine = ex.Message;
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
        catch { Packages.Clear(); }
    }

    private IEnumerable<string> SelectedGitPaths()
        => GitFiles.Where(f => f.Selected).Select(f => f.Path);

    [RelayCommand]
    private void OpenNewSiteWizard()
    {
        ResetNewSiteForm();
        ShowNewSiteWizard = true;
    }

    [RelayCommand] private void CloseNewSiteWizard() => ShowNewSiteWizard = false;
    [RelayCommand] private void OpenImportSheet() { ImportPath = ""; ShowImportSheet = true; }
    [RelayCommand] private void CloseImportSheet() => ShowImportSheet = false;
    [RelayCommand] private void OpenComposerSheet() { RequirePackage = ""; RequireVersion = ""; ShowComposerSheet = true; }
    [RelayCommand] private void CloseComposerSheet() => ShowComposerSheet = false;
    [RelayCommand] private void OpenCreateUserSheet() { CreateUserEmail = ""; CreateUserPassword = ""; CreateUserSuper = true; ShowCreateUserSheet = true; }
    [RelayCommand] private void CloseCreateUserSheet() => ShowCreateUserSheet = false;
    [RelayCommand] private void OpenCommandsSheet() { CustomCommand = ""; ShowCommandsSheet = true; }
    [RelayCommand] private void CloseCommandsSheet() => ShowCommandsSheet = false;
    [RelayCommand] private void OpenConnectHostSheet() { HostTokenInput = ""; HostExtraInput = ""; HostLabelInput = ""; ConnectHostStatus = ""; ConnectHostKind = 1; ShowConnectHostSheet = true; }
    [RelayCommand] private void CloseConnectHostSheet() => ShowConnectHostSheet = false;
    [RelayCommand] private void DismissToast() => ShowToast = false;
    [RelayCommand] private void CancelDelete() => ShowDeleteConfirm = false;

    [RelayCommand]
    private void ConfirmRemove()
    {
        DeleteSiteFilesToo = false;
        ShowDeleteConfirm = true;
    }

    [RelayCommand]
    private async Task BrowseImportFolderAsync()
    {
        var path = await PickFolderAsync("Import Existing Site");
        if (path is not null) ImportPath = path;
    }

    [RelayCommand]
    private async Task BrowseSitesFolderAsync()
    {
        var path = await PickFolderAsync("Default sites folder");
        if (path is not null) DefaultSitesFolder = path;
    }

    [RelayCommand]
    private async Task BrowseNewSiteFolderAsync()
    {
        var path = await PickFolderAsync("Folder");
        if (path is not null) NewSiteFolder = path;
    }

    [RelayCommand]
    private async Task ImportSiteAsync()
    {
        var path = ImportPath.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            StatusLine = "Point Sherpa at a folder that already has Statamic installed.";
            return;
        }
        if (!File.Exists(Path.Combine(path, "composer.json")))
        {
            StatusLine = "That folder does not contain composer.json.";
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
        var folder = string.IsNullOrWhiteSpace(NewSiteFolder) ? DefaultSitesFolder : NewSiteFolder;
        // Always install Statamic — same happy path as Mac "create a site"
        await RunJobAsync("New Site", async ct =>
        {
            var (site, result, error) = await _svc.Install.CreateAsync(
                folder,
                NewSiteName.Trim(),
                InstallCoordinator.StartingPoint.FreshStatamic,
                NewSiteParkInHerd,
                NewSiteSecureHttps,
                AppendLog,
                ct);

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
            if (site is null) return;

            Sites.Add(site);
            PersistSites();
            SelectedSite = site;
            ShowNewSiteWizard = false;
            _svc.Notifications.Notify("Site ready", $"{site.Name} is ready to open.");
            StatusLine = $"Created {site.Name}.";
            await RefreshSitePanelsAsync();
        });
    }

    [RelayCommand]
    private void PerformDelete()
    {
        if (SelectedSite is null) return;
        var site = SelectedSite;
        var path = site.Path;
        Sites.Remove(site);
        SelectedSite = null;
        PersistSites();
        if (DeleteSiteFilesToo)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    // Move to recycle is complex on Windows without extra deps; delete with clear label
                    Directory.Delete(path, recursive: true);
                    StatusLine = $"Deleted site files at {path}.";
                }
            }
            catch (Exception ex)
            {
                StatusLine = $"Removed from Sherpa, but could not delete files: {ex.Message}";
            }
        }
        else
        {
            StatusLine = "Remove keeps the folder on disk. Delete site files moves the project folder to the Trash.";
            StatusLine = $"Removed {site.Name} from Sherpa. Files on disk were left alone.";
        }
        ShowDeleteConfirm = false;
    }

    [RelayCommand]
    private async Task OpenSiteFolderAsync()
    {
        if (SelectedSite is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SelectedSite.Path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { StatusLine = ex.Message; }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenSiteUrlAsync()
    {
        if (SelectedSite?.Url is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SelectedSite.Url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { StatusLine = ex.Message; }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task LinkHerdAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Linking to Herd", async ct =>
        {
            var (ok, msg) = await _svc.Herd.ParkAsync(SelectedSite.Path, SelectedSite.Name, AppendLog, ct);
            AppendLog(msg);
            if (!ok) ShowAdviceFrom(msg);
            else
            {
                SelectedSite.ParkedInHerd = true;
                SelectedSite.Url = _svc.Herd.UrlPreview(SelectedSite.Name, SelectedSite.Https);
                PersistSites();
                StatusLine = msg;
            }
        });
    }

    [RelayCommand]
    private async Task SecureHerdAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Secure with HTTPS (Herd)", async ct =>
        {
            var (ok, msg) = await _svc.Herd.SecureAsync(SelectedSite.Name, true, AppendLog, ct);
            AppendLog(msg);
            if (!ok) ShowAdviceFrom(msg);
            else
            {
                SelectedSite.Https = true;
                SelectedSite.Url = _svc.Herd.UrlPreview(SelectedSite.Name, true);
                PersistSites();
            }
        });
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
        await RunJobAsync("Initialize Git", async ct =>
        {
            var r = await _svc.Git.InitAsync(SelectedSite.Path, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else StatusLine = "Git repository initialized.";
            await RefreshSitePanelsAsync();
        });
    }

    [RelayCommand]
    private async Task SaveChangesAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Save changes", async ct =>
        {
            var r = await _svc.Git.SaveChangesAsync(
                SelectedSite.Path, SelectedGitPaths(), GitCommitMessage,
                GitUserName, GitUserEmail, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else StatusLine = "Save changes commits the checked files with the message below. Nothing is sent to GitHub.";
            await RefreshSitePanelsAsync();
        });
    }

    [RelayCommand]
    private async Task PullAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Pull", async ct =>
        {
            var r = await _svc.Git.PullRebaseAsync(SelectedSite.Path, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            await RefreshSitePanelsAsync();
        });
    }

    [RelayCommand]
    private async Task PushAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Push", async ct =>
        {
            var r = await _svc.Git.PushAsync(SelectedSite.Path, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else StatusLine = "Pushed to origin.";
            await RefreshSitePanelsAsync();
        });
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Sync", async ct =>
        {
            AppendLog("Sync does all three in order: commit the selection, pull --rebase, then push. Unchecked files stay uncommitted.");
            var r = await _svc.Git.SyncAsync(
                SelectedSite.Path, SelectedGitPaths(), GitCommitMessage,
                GitUserName, GitUserEmail, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else StatusLine = "Sync finished.";
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
            else { StatusLine = "Composer install finished."; _svc.Notifications.Notify("Composer", "Install finished."); }
            await RefreshSitePanelsAsync();
        });
    }

    [RelayCommand]
    private async Task ComposerUpdateAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Update Packages", async ct =>
        {
            var r = await _svc.Composer.UpdateAsync(SelectedSite.Path, null, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else { StatusLine = "Packages updated"; _svc.Notifications.Notify("Composer", "Packages updated"); }
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
            else StatusLine = "Clear Cache flushes Laravel caches.";
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
            else StatusLine = "Clear Stache rebuilds Statamic's content index from files.";
        });
    }

    [RelayCommand]
    private async Task WarmStacheAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Warming Stache", async ct =>
        {
            var r = await _svc.Commands.WarmStacheAsync(SelectedSite.Path, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
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
            else StatusLine = "Clear Glide Cache deletes generated image transforms so they regenerate on the next request.";
        });
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        if (SelectedSite is null) return;
        await RunJobAsync("Create User", async ct =>
        {
            var r = await _svc.Commands.MakeUserAsync(SelectedSite.Path, CreateUserEmail.Trim(), CreateUserPassword, CreateUserSuper, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
            else
            {
                ShowCreateUserSheet = false;
                StatusLine = "User created via please make:user.";
            }
        });
    }

    [RelayCommand]
    private async Task RunCustomCommandAsync()
    {
        if (SelectedSite is null) return;
        var raw = CustomCommand.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;
        await RunJobAsync(raw, async ct =>
        {
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ProcessResult r;
            if (parts[0].Equals("please", StringComparison.OrdinalIgnoreCase))
                r = await _svc.Commands.PleaseAsync(SelectedSite.Path, parts.Skip(1), AppendLog, ct);
            else if (parts[0].Equals("artisan", StringComparison.OrdinalIgnoreCase))
                r = await _svc.Commands.ArtisanAsync(SelectedSite.Path, parts.Skip(1), AppendLog, ct);
            else
                r = await _svc.Commands.PleaseAsync(SelectedSite.Path, parts, AppendLog, ct);
            AppendLog(r.Combined);
            if (!r.Success) ShowAdviceFrom(r.Combined);
        });
    }

    [RelayCommand]
    private async Task CopyErrorsAsync()
    {
        var text = new StringBuilder();
        foreach (var a in Advice)
            text.AppendLine($"• {a.Title}: {a.Detail}");
        if (Advice.Count > 0) text.AppendLine();
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
        p.PreferHerdForNewSites = PreferHerdForNewSites;
        p.SecureNewHerdSitesWithHttps = SecureNewHerdSites;
        p.GitUserName = GitUserName.Trim();
        p.GitUserEmail = GitUserEmail.Trim();
        _svc.Preferences.Save(p);
        RefreshRuntimeStatus();
        StatusLine = "Changed settings saved.";
    }

    [RelayCommand]
    private async Task SaveGitHubTokenAsync()
    {
        var token = GitHubTokenInput.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            GitHubStatus = "Add a GitHub token in Settings first.";
            return;
        }
        var (ok, message, _) = await _svc.GitHub.ValidateTokenAsync(token);
        if (!ok) { GitHubStatus = message; return; }
        _svc.Secrets.Set("github", token);
        UpsertHost(HostProviderKind.GitHub, "GitHub", "github", null);
        GitHubTokenInput = "";
        GitHubStatus = message + " Tokens stay in the Windows secret store. Sherpa never writes them into your sites.";
    }

    [RelayCommand]
    private void ClearGitHubToken()
    {
        _svc.Secrets.Delete("github");
        RemoveHost(HostProviderKind.GitHub);
        GitHubStatus = "Removes the account from Sherpa and deletes its token from the secret store.";
    }

    [RelayCommand]
    private void SavePackagistToken()
    {
        var token = PackagistTokenInput.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            PackagistStatus = "Add a Packagist or GitHub token in Settings. Sherpa will write it to this site's auth.json.";
            return;
        }
        _svc.Secrets.Set("packagist", token);
        PackagistTokenInput = "";
        PackagistStatus = "Packagist token saved in the Windows secret store.";
    }

    [RelayCommand]
    private async Task ConnectHostAsync()
    {
        var token = HostTokenInput.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            ConnectHostStatus = "Missing API token for this host.";
            return;
        }
        bool ok; string message; HostProviderKind kind; string key; string label;
        switch (ConnectHostKind)
        {
            case 1:
                (ok, message) = await _svc.Forge.ValidateAsync(token);
                kind = HostProviderKind.Forge; key = "forge"; label = string.IsNullOrWhiteSpace(HostLabelInput) ? "Laravel Forge" : HostLabelInput;
                break;
            case 2:
                (ok, message) = await _svc.LaravelCloud.ValidateAsync(token);
                kind = HostProviderKind.LaravelCloud; key = "laravel-cloud"; label = string.IsNullOrWhiteSpace(HostLabelInput) ? "Laravel Cloud" : HostLabelInput;
                break;
            case 3:
                (ok, message) = await _svc.Cloudflare.ValidateAsync(token, HostExtraInput.Trim());
                kind = HostProviderKind.CloudflarePages; key = "cloudflare"; label = string.IsNullOrWhiteSpace(HostLabelInput) ? "Cloudflare Pages" : HostLabelInput;
                break;
            case 4:
                (ok, message) = await _svc.Netlify.ValidateAsync(token);
                kind = HostProviderKind.Netlify; key = "netlify"; label = string.IsNullOrWhiteSpace(HostLabelInput) ? "Netlify" : HostLabelInput;
                break;
            default:
                ConnectHostStatus = "Pick a host type.";
                return;
        }
        ConnectHostStatus = message;
        if (!ok) return;
        _svc.Secrets.Set(key, token);
        UpsertHost(kind, label, key, string.IsNullOrWhiteSpace(HostExtraInput) ? null : HostExtraInput.Trim());
        HostTokenInput = "";
        ReloadHosts();
        ShowConnectHostSheet = false;
        StatusLine = message;
    }

    private void UpsertHost(HostProviderKind kind, string label, string secretKey, string? extra)
    {
        var p = _svc.Preferences.Load();
        var h = p.Hosts.FirstOrDefault(x => x.Provider == kind && x.SecretKey == secretKey);
        if (h is null)
        {
            p.Hosts.Add(new HostAccount { Provider = kind, Label = label, SecretKey = secretKey, Extra = extra });
        }
        else
        {
            h.Label = label;
            h.Extra = extra;
        }
        _svc.Preferences.Save(p);
    }

    private void RemoveHost(HostProviderKind kind)
    {
        var p = _svc.Preferences.Load();
        p.Hosts.RemoveAll(h => h.Provider == kind);
        _svc.Preferences.Save(p);
        ReloadHosts();
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
            AppendLog("Creating private GitHub repo…");
            var token = _svc.Secrets.Get("github")!;
            var repoName = HerdService.Slug(SelectedSite.Name);
            var (ok, message, url) = await _svc.GitHub.CreatePrivateRepoAsync(token, repoName, ct);
            AppendLog(message);
            if (!ok) { ShowAdviceFrom(message); return; }
            if (url is not null)
            {
                AppendLog(url);
                // try set remote + push if git exists
                var clone = url.EndsWith(".git") ? url : url.TrimEnd('/') + ".git";
                // Prefer SSH-less https with token not stored in remote permanently - use plain URL
                var remote = await _svc.Git.RemoteUrlAsync(SelectedSite.Path, ct);
                if (!remote.Success)
                    await _svc.Git.AddRemoteAsync(SelectedSite.Path, clone, ct);
                else
                    await _svc.Git.SetRemoteAsync(SelectedSite.Path, clone, ct);
            }
            AppendLog("Creates a private repo and pushes the first commit when you Sync. Token stays in the secret store.");
            StatusLine = "GitHub repo ready.";
            _svc.Notifications.Notify("GitHub", $"Private repo ready for {SelectedSite.Name}");
        });
    }

    [RelayCommand]
    private async Task DeployPrerequisiteCheckAsync()
    {
        ClearLog();
        if (SelectedSite is null) { AppendLog("Pick a site from the list, or create a new one."); return; }
        var p = _svc.Preferences.Load();
        var hasDynamic = p.Hosts.Any(h => h.Provider is HostProviderKind.Forge or HostProviderKind.LaravelCloud);
        var hasStatic = p.Hosts.Any(h => h.Provider is HostProviderKind.CloudflarePages or HostProviderKind.Netlify);
        if (!_svc.Secrets.Has("github"))
        {
            AppendLog("Add a GitHub token in Settings first.");
            ShowAdviceFrom("Add a GitHub token in Settings first.");
            return;
        }
        if (!hasDynamic && !hasStatic)
        {
            AppendLog("Connect a New Host under Hosts first.");
            AppendLog("Laravel Cloud for dynamic Statamic sites. Netlify or Cloudflare Pages for static SSG deploys.");
            AppendLog("Or Connect Forge first.");
            ShowAdviceFrom("Connect Netlify or Cloudflare Pages under Hosts first.");
            SelectedNavIndex = 1;
            return;
        }
        AppendLog("Prerequisites look good for the host accounts you connected.");
        AppendLog("Full one-click Forge / Cloud / static publish wizards use these same accounts — ship the site from Deploy when the wizard finishes running.");
        AppendLog("Your site path: " + SelectedSite.Path);
        if (hasDynamic) AppendLog("Dynamic hosting: Forge / Laravel Cloud ready.");
        if (hasStatic) AppendLog("Static hosting: Cloudflare Pages / Netlify ready.");
        StatusLine = "Hosts connected.";
        await Task.CompletedTask;
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

public partial class GitFileItem : ObservableObject
{
    [ObservableProperty] private string path = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool selected = true;
}
