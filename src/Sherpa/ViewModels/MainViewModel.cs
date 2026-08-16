using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sherpa.Clients;
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
    public ObservableCollection<StarterKitRow> FilteredKits { get; } = new();
    private readonly List<StarterKitRow> _allKits = new();
    private StarterKitRow? _blankKitCard;

    [ObservableProperty] private Site? selectedSite;
    [ObservableProperty] private int selectedNavIndex;
    [ObservableProperty] private int selectedDetailTab;
    [ObservableProperty] private bool isSitesNav = true;
    [ObservableProperty] private bool isHostsNav;
    [ObservableProperty] private bool isSettingsNav;
    [ObservableProperty] private string logText = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string busyLabel = "";
    [ObservableProperty] private bool isInstallingSite;
    [ObservableProperty] private string installStatus = "";
    [ObservableProperty] private double installProgress; // 0-100
    [ObservableProperty] private bool installProgressIndeterminate;
    // Current install phase band — progress crawls inside [floor, ceil) on composer noise
    private double _installPhaseFloor;
    private double _installPhaseCeil = 100;
    private int _installPhaseNoise;
    [ObservableProperty] private string sitePreviewTitle = "Site preview";
    [ObservableProperty] private string sitePreviewSubtitle = "Create or select a site to see a preview.";
    [ObservableProperty] private string sitePreviewBadge = "";
    [ObservableProperty] private string statusLine = "Sherpa for Windows · 0.2.5";
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

    // Wizard steps: 0 identity, 1 kits, 2 storage, 3 options
    [ObservableProperty] private int wizardStep;
    [ObservableProperty] private bool wizardIsIdentity = true;
    [ObservableProperty] private bool wizardIsKits;
    [ObservableProperty] private bool wizardIsStorage;
    [ObservableProperty] private bool wizardIsOptions;
    [ObservableProperty] private string wizardTitle = "New Site";
    [ObservableProperty] private bool wizardCanGoBack;
    [ObservableProperty] private bool wizardCanGoNext = true;
    [ObservableProperty] private string kitSearch = "";
    [ObservableProperty] private int kitPriceFilter; // 0 all 1 free 2 paid
    [ObservableProperty] private bool kitsLoading;
    [ObservableProperty] private string kitsStatus = "";
    [ObservableProperty] private StarterKitRow? selectedKit;
    [ObservableProperty] private bool storageFlatFiles = true;
    [ObservableProperty] private bool storageSqlite;
    [ObservableProperty] private bool storageMySql;
    [ObservableProperty] private bool showMySqlFields;
    [ObservableProperty] private string mySqlHost = "127.0.0.1";
    [ObservableProperty] private string mySqlDatabase = "";
    [ObservableProperty] private string mySqlUser = "root";
    [ObservableProperty] private string mySqlPassword = "";
    [ObservableProperty] private bool enableStatamicPro; // always forced off; UI disabled
    [ObservableProperty] private bool installSsg;
    [ObservableProperty] private bool initGit = true;
    [ObservableProperty] private bool createSuperUserOnCreate;
    [ObservableProperty] private string superUserName = "";
    [ObservableProperty] private string superUserEmail = "";
    [ObservableProperty] private string superUserPassword = "";

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
        UpdatePreviewForSite(value, IsInstallingSite && value is not null);
        if (!IsInstallingSite)
            _ = RefreshSitePanelsAsync();
    }

    partial void OnNewSiteNameChanged(string value) => RefreshNewSitePreviews();
    partial void OnNewSiteFolderChanged(string value) => RefreshNewSitePreviews();
    partial void OnNewSiteSecureHttpsChanged(bool value) => RefreshNewSitePreviews();

    partial void OnWizardStepChanged(int value)
    {
        WizardIsIdentity = value == 0;
        WizardIsKits = value == 1;
        WizardIsStorage = value == 2;
        WizardIsOptions = value == 3;
        WizardTitle = value switch
        {
            0 => "New Site",
            1 => "Starter kit",
            2 => "Content storage",
            3 => "Options",
            _ => "New Site",
        };
        WizardCanGoBack = value > 0;
        WizardCanGoNext = value < 3;
    }

    partial void OnKitSearchChanged(string value) => ApplyKitFilter();
    partial void OnKitPriceFilterChanged(int value) => ApplyKitFilter();

    partial void OnStorageFlatFilesChanged(bool value)
    {
        if (value) { StorageSqlite = false; StorageMySql = false; ShowMySqlFields = false; }
    }
    partial void OnStorageSqliteChanged(bool value)
    {
        if (value) { StorageFlatFiles = false; StorageMySql = false; ShowMySqlFields = false; }
    }
    partial void OnStorageMySqlChanged(bool value)
    {
        if (value)
        {
            StorageFlatFiles = false;
            StorageSqlite = false;
            ShowMySqlFields = true;
            if (string.IsNullOrWhiteSpace(MySqlDatabase))
                MySqlDatabase = HerdService.Slug(NewSiteName);
        }
        else ShowMySqlFields = false;
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
        WizardStep = 0;
        KitSearch = "";
        KitPriceFilter = 0;
        SelectedKit = null;
        StorageFlatFiles = true;
        StorageSqlite = false;
        StorageMySql = false;
        ShowMySqlFields = false;
        MySqlHost = "127.0.0.1";
        MySqlDatabase = "";
        MySqlUser = "root";
        MySqlPassword = "";
        EnableStatamicPro = false;
        InstallSsg = false;
        InitGit = true;
        CreateSuperUserOnCreate = false;
        SuperUserName = "";
        SuperUserEmail = "";
        SuperUserPassword = "";
        RefreshNewSitePreviews();
        EnsureBlankKitCard();
        ApplyKitFilter();
    }

    private void EnsureBlankKitCard()
    {
        _blankKitCard ??= new StarterKitRow
        {
            IsBlank = true,
            Name = "Blank site",
            Summary = "Fresh Statamic, no starter kit.",
            SellerName = "Statamic",
            PriceLabel = "Default",
            IsPaid = false,
            Package = "",
        };
    }

    private void ApplyKitFilter()
    {
        EnsureBlankKitCard();
        FilteredKits.Clear();
        // Blank always first when All or Free
        var showBlank = KitPriceFilter is 0 or 1;
        if (showBlank && (string.IsNullOrWhiteSpace(KitSearch) ||
                          "blank site".Contains(KitSearch.Trim(), StringComparison.OrdinalIgnoreCase) ||
                          "fresh".Contains(KitSearch.Trim(), StringComparison.OrdinalIgnoreCase)))
            FilteredKits.Add(_blankKitCard!);

        IEnumerable<StarterKitRow> q = _allKits;
        if (KitPriceFilter == 1) q = q.Where(k => !k.IsPaid);
        else if (KitPriceFilter == 2) q = q.Where(k => k.IsPaid);

        var s = KitSearch?.Trim() ?? "";
        if (!string.IsNullOrEmpty(s))
            q = q.Where(k =>
                k.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                k.Summary.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                k.Package.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                k.SellerName.Contains(s, StringComparison.OrdinalIgnoreCase));

        foreach (var k in q)
            FilteredKits.Add(k);

        // Keep selection if still visible
        if (SelectedKit is not null && FilteredKits.All(k => k != SelectedKit && !(k.IsBlank && SelectedKit.IsBlank)))
        {
            // try rebind blank
            if (SelectedKit.IsBlank)
                SelectedKit = FilteredKits.FirstOrDefault(x => x.IsBlank);
        }
        if (SelectedKit is null && FilteredKits.Count > 0)
            SelectedKit = FilteredKits[0];
    }

    private async Task LoadKitsIfNeededAsync()
    {
        if (_allKits.Count > 0) { ApplyKitFilter(); return; }
        KitsLoading = true;
        KitsStatus = "Loading Marketplace…";
        try
        {
            var kits = await _svc.Marketplace.GetAllStarterKitsAsync();
            _allKits.Clear();
            foreach (var k in kits)
            {
                _allKits.Add(new StarterKitRow
                {
                    IsBlank = false,
                    Name = k.Name,
                    Summary = k.Summary,
                    Package = k.Package,
                    SellerName = k.SellerName,
                    PriceLabel = k.PriceLabel,
                    IsPaid = k.IsPaid,
                    CoverUrl = k.CoverUrl,
                    MarketplaceUrl = k.Url,
                });
            }
            KitsStatus = $"{_allKits.Count} starter kits";
            ApplyKitFilter();
            if (SelectedKit is null)
                SelectedKit = FilteredKits.FirstOrDefault();
        }
        catch (Exception ex)
        {
            KitsStatus = "Couldn’t load starter kits: " + ex.Message;
            ApplyKitFilter();
        }
        finally
        {
            KitsLoading = false;
        }
    }

    private void ReloadSites()
    {
        var previousId = SelectedSite?.Id;
        Sites.Clear();
        foreach (var s in _svc.Sites.Load().OrderBy(s => s.Name))
            Sites.Add(s);

        // Prefer the previously selected site when it still exists.
        if (previousId is not null)
            SelectedSite = Sites.FirstOrDefault(s => s.Id == previousId);

        // On launch (and anytime selection is empty), open the sites overview —
        // never leave the "Create your first site" card up when sites exist.
        if (SelectedSite is null && Sites.Count > 0)
            SelectedSite = Sites[0];

        RefreshEmptyStateCopy();
    }

    private void RefreshEmptyStateCopy()
    {
        if (Sites.Count == 0)
        {
            EmptyTitle = "Create your first site";
            EmptyBody = "Create a new Statamic site, or import a folder you already have.";
        }
        else
        {
            // Should rarely show — selection is auto-restored when sites exist.
            EmptyTitle = "Select a site";
            EmptyBody = "Pick a site from the sidebar, or create a new one.";
        }
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

    private void Ui(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private void AppendLog(string line)
    {
        Ui(() =>
        {
            if (string.IsNullOrEmpty(LogText)) LogText = line;
            else LogText += "\n" + line;

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('•'))
                return;

            var shortLine = line.Length > 140 ? line.Substring(0, 137) + "…" : line;

            if (IsInstallingSite)
            {
                // Single surface: Overview install card only (no header/footer echo)
                InstallStatus = shortLine;
                AdvanceInstallProgress(line);
                return;
            }

            if (IsBusy)
            {
                BusyLabel = shortLine;
                StatusLine = shortLine;
            }
        });
    }

    /// <summary>
    /// Step-based progress. Major coordinator messages open a phase band;
    /// composer/process noise only crawls inside that band.
    /// IMPORTANT: never match bare path fragments like "Herd" — PHP/Composer live under
    /// …\Programs\Herd\… so contains("herd") was jumping the bar to ~95% on the first packages.
    /// </summary>
    private void AdvanceInstallProgress(string line)
    {
        var lower = line.Trim().ToLowerInvariant();

        // High-level steps from InstallCoordinator only (prefix checks — not path substrings)
        if (lower.StartsWith("creating statamic"))
        {
            // create-project is the long pole — most of the bar lives here
            EnterInstallPhase(5, 72);
            return;
        }
        if (lower.StartsWith("installing starter kit"))
        {
            EnterInstallPhase(72, 80);
            return;
        }
        if (lower.StartsWith("blank site"))
        {
            EnterInstallPhase(72, 78);
            return;
        }
        if (lower.StartsWith("configuring sqlite")
            || lower.StartsWith("configuring mysql")
            || lower.StartsWith("content storage"))
        {
            EnterInstallPhase(78, 84);
            return;
        }
        if (lower.StartsWith("running install:eloquent"))
        {
            EnterInstallPhase(80, 84);
            return;
        }
        if (lower.StartsWith("installing static site")
            || lower.StartsWith("please install:ssg"))
        {
            EnterInstallPhase(84, 88);
            return;
        }
        if (lower.StartsWith("creating super user"))
        {
            EnterInstallPhase(88, 91);
            return;
        }
        if (lower.StartsWith("initialize git"))
        {
            EnterInstallPhase(91, 94);
            return;
        }
        // Herd status lines we emit ourselves — NOT filesystem paths containing \Herd\
        if (lower.StartsWith("park in herd")
            || lower.StartsWith("parked in herd")
            || lower.StartsWith("parked path ")
            || lower.StartsWith("link failed")
            || lower.StartsWith("secure with https")
            || lower.StartsWith("https enabled via herd")
            || lower.StartsWith("https removed")
            || lower.StartsWith("starting herd")
            || lower.StartsWith("herd is running")
            || lower.StartsWith("herd started")
            || lower.StartsWith("herd launch")
            || lower.StartsWith("could not park in herd")
            || lower.StartsWith("could not start herd")
            || lower.StartsWith("herd was not found")
            || lower.StartsWith("herd cli was not found")
            || lower.StartsWith("timed out waiting for herd")
            || lower.StartsWith("removing https"))
        {
            EnterInstallPhase(94, 99);
            return;
        }
        if (lower.StartsWith("created project") || lower.StartsWith("site ready"))
        {
            SnapInstallProgress(100);
            _installPhaseFloor = 100;
            _installPhaseCeil = 100;
            return;
        }

        // Ignore generic composer chatter for phase changes (create-project, github URLs, package paths).
        // Only crawl inside the current band so the bar fills with real work output.
        if (_installPhaseCeil > _installPhaseFloor + 0.5 && InstallProgress < _installPhaseCeil - 0.4)
        {
            _installPhaseNoise++;
            var span = _installPhaseCeil - _installPhaseFloor;
            // create-project emits hundreds of lines — crawl slowly so it doesn't look "done"
            // ~half the band by ~80 lines, ~90% by ~220 lines
            var t = 1.0 - Math.Exp(-_installPhaseNoise / 100.0);
            var target = _installPhaseFloor + span * t;
            if (target > InstallProgress)
            {
                InstallProgress = Math.Min(_installPhaseCeil - 0.35, target);
                InstallProgressIndeterminate = false;
            }
        }
    }

    private void EnterInstallPhase(double floor, double ceil)
    {
        // Only move forward into a later phase — never snap backward to an earlier band
        // if a stray line re-matches, and never jump the floor ahead of real progress by a lot
        // when we're already past this phase.
        if (InstallProgress >= ceil)
            return;

        var enteringNew = floor > _installPhaseFloor + 0.1 || ceil > _installPhaseCeil + 0.1;
        _installPhaseFloor = floor;
        _installPhaseCeil = ceil;
        if (enteringNew)
            _installPhaseNoise = 0;

        if (InstallProgress < floor)
            InstallProgress = floor;
        InstallProgressIndeterminate = false;
    }

    private void SnapInstallProgress(double value)
    {
        InstallProgress = Math.Clamp(value, 0, 100);
        InstallProgressIndeterminate = false;
    }

    private void UpdatePreviewForSite(Site? site, bool installing)
    {
        if (site is null)
        {
            SitePreviewTitle = "Site preview";
            SitePreviewSubtitle = "Create or select a site to see a preview.";
            SitePreviewBadge = "";
            return;
        }
        SitePreviewTitle = site.Name;
        if (installing)
        {
            SitePreviewSubtitle = site.Url ?? "Installing…";
            SitePreviewBadge = "Installing";
        }
        else
        {
            SitePreviewSubtitle = site.Url ?? site.Path;
            SitePreviewBadge = site.Kind.ToString();
        }
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
        Ui(() =>
        {
            IsBusy = true;
            BusyLabel = label;
            StatusLine = label;
        });
        ClearLog();
        AppendLog(label + "…");
        try { await work(CancellationToken.None); }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
            Ui(() =>
            {
                ShowAdviceFrom(ex.Message);
                StatusLine = "Something needed your attention.";
            });
        }
        finally
        {
            Ui(() =>
            {
                IsBusy = false;
                if (!IsInstallingSite) BusyLabel = "";
            });
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
    private async Task OpenNewSiteWizard()
    {
        ResetNewSiteForm();
        ShowNewSiteWizard = true;
        await LoadKitsIfNeededAsync();
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
    private async Task WizardNextAsync()
    {
        if (WizardStep == 0)
        {
            if (string.IsNullOrWhiteSpace(NewSiteName))
            {
                StatusLine = "Site name is required.";
                return;
            }
            if (string.IsNullOrWhiteSpace(NewSiteFolder))
            {
                StatusLine = "Folder is required.";
                return;
            }
            WizardStep = 1;
            await LoadKitsIfNeededAsync();
            return;
        }
        if (WizardStep == 1)
        {
            SelectedKit ??= FilteredKits.FirstOrDefault(k => k.IsBlank) ?? FilteredKits.FirstOrDefault();
            if (SelectedKit is null)
            {
                StatusLine = "Pick Blank site or a starter kit.";
                return;
            }
            if (SelectedKit.IsPaid)
            {
                StatusLine = "Paid starter kits need a Statamic license flow we haven’t wired yet. Pick Blank site or a Free kit.";
                // still allow advancing so they can see — but Create will block. Better block here:
                return;
            }
            WizardStep = 2;
            return;
        }
        if (WizardStep == 2)
        {
            if (!StorageFlatFiles && !StorageSqlite && !StorageMySql)
                StorageFlatFiles = true;
            WizardStep = 3;
        }
    }

    [RelayCommand]
    private void SelectKitFilter(string? filter)
    {
        if (int.TryParse(filter, out var n)) KitPriceFilter = n;
    }

    [RelayCommand]
    private void WizardBack()
    {
        if (WizardStep > 0) WizardStep--;
    }

    [RelayCommand]
    private void SelectKit(StarterKitRow? kit)
    {
        if (kit is null) return;
        SelectedKit = kit;
    }

    [RelayCommand]
    private async Task CreateSiteAsync()
    {
        // Jump to options step create
        if (WizardStep != 3)
        {
            // allow create only on last step
            while (WizardStep < 3)
                await WizardNextAsync();
            if (WizardStep != 3) return;
        }

        if (CreateSuperUserOnCreate)
        {
            if (string.IsNullOrWhiteSpace(SuperUserEmail) || string.IsNullOrWhiteSpace(SuperUserPassword))
            {
                StatusLine = "Super user needs name/email/password — at least email and password.";
                return;
            }
        }

        EnableStatamicPro = false; // forced off for now

        var folder = string.IsNullOrWhiteSpace(NewSiteFolder) ? DefaultSitesFolder : NewSiteFolder;
        var kit = SelectedKit;
        var storage = StorageMySql ? InstallCoordinator.ContentStorageKind.MySql
            : StorageSqlite ? InstallCoordinator.ContentStorageKind.Sqlite
            : InstallCoordinator.ContentStorageKind.FlatFiles;

        var req = new InstallCoordinator.CreateRequest
        {
            ParentFolder = folder,
            SiteName = NewSiteName.Trim(),
            StarterKitPackage = kit is null || kit.IsBlank ? null : kit.Package,
            StarterKitIsPaid = kit?.IsPaid == true,
            Storage = storage,
            MySqlHost = MySqlHost,
            MySqlDatabase = string.IsNullOrWhiteSpace(MySqlDatabase) ? HerdService.Slug(NewSiteName) : MySqlDatabase,
            MySqlUser = MySqlUser,
            MySqlPassword = MySqlPassword,
            EnablePro = false,
            InstallSsg = InstallSsg,
            InitGit = InitGit,
            CreateSuperUser = CreateSuperUserOnCreate,
            SuperUserName = SuperUserName,
            SuperUserEmail = SuperUserEmail,
            SuperUserPassword = SuperUserPassword,
            ParkInHerd = NewSiteParkInHerd,
            SecureHttps = NewSiteSecureHttps,
        };

        // Mac behavior: dismiss wizard immediately, show Overview + live install status
        var expectedPath = _svc.Herd.WillCreatePath(folder, req.SiteName);
        var pending = new Site
        {
            Name = req.SiteName,
            Path = expectedPath,
            Url = _svc.Herd.UrlPreview(req.SiteName, req.SecureHttps),
            Https = req.SecureHttps,
            Kind = SiteKind.Statamic,
            StartingPoint = string.IsNullOrWhiteSpace(req.StarterKitPackage) ? "blank" : "kit:" + req.StarterKitPackage,
        };

        ShowNewSiteWizard = false;
        SelectedNavIndex = 0;
        SelectedDetailTab = 0;
        Sites.Insert(0, pending);
        SelectedSite = pending;
        UpdatePreviewForSite(pending, installing: true);

        await RunJobAsync("Installing " + req.SiteName, async ct =>
        {
            try
            {
                Ui(() =>
                {
                    IsInstallingSite = true;
                    InstallProgressIndeterminate = false;
                    InstallProgress = 2;
                    _installPhaseFloor = 2;
                    _installPhaseCeil = 72;
                    _installPhaseNoise = 0;
                    InstallStatus = "Starting install…";
                    // Keep header/footer quiet; don't echo install into StatusLine
                });

                var (site, result, error) = await _svc.Install.CreateAsync(req, AppendLog, ct);

                if (result is not null && !result.Success)
                {
                    AppendLog(result.Combined);
                    Ui(() => ShowAdviceFrom(result.Combined));
                }
                if (error is not null)
                {
                    AppendLog(error);
                    Ui(() =>
                    {
                        ShowAdviceFrom(error + "\n" + (result?.Combined ?? ""));
                        InstallStatus = error;
                        // Keep pending site if folder exists so user can inspect; else remove
                        if (!Directory.Exists(pending.Path))
                        {
                            Sites.Remove(pending);
                            if (SelectedSite == pending) SelectedSite = Sites.FirstOrDefault();
                        }
                        PersistSites();
                    });
                    return;
                }
                if (site is null) return;

                Ui(() =>
                {
                    // Replace pending with final site data (same list slot)
                    var idx = Sites.IndexOf(pending);
                    if (idx >= 0)
                    {
                        site.Id = pending.Id;
                        Sites[idx] = site;
                        SelectedSite = site;
                    }
                    else
                    {
                        Sites.Insert(0, site);
                        SelectedSite = site;
                    }
                    PersistSites();
                    InstallProgress = 100;
                    InstallProgressIndeterminate = false;
                    InstallStatus = "Created project";
                    UpdatePreviewForSite(site, installing: false);
                    StatusLine = $"Created {site.Name}.";
                });
                _svc.Notifications.Notify("Site ready", $"{site.Name} is ready to open.");
                await RefreshSitePanelsAsync();
                await RefreshPreviewHttpAsync(site);
            }
            finally
            {
                Ui(() =>
                {
                    IsInstallingSite = false;
                    InstallProgressIndeterminate = false;
                    if (SelectedSite is not null)
                        UpdatePreviewForSite(SelectedSite, installing: false);
                    if (InstallStatus == "Starting install…") InstallStatus = "";
                    BusyLabel = "";
                });
            }
        });
    }

    private async Task RefreshPreviewHttpAsync(Site site)
    {
        if (string.IsNullOrWhiteSpace(site.Url)) return;
        try
        {
            // Ensure Herd before probing local .test URL
            await _svc.Herd.EnsureRunningAsync(AppendLog);
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var res = await http.GetAsync(site.Url);
            Ui(() =>
            {
                SitePreviewBadge = $"{(int)res.StatusCode}";
                SitePreviewSubtitle = $"{site.Url} · HTTP {(int)res.StatusCode}";
            });
        }
        catch
        {
            Ui(() =>
            {
                SitePreviewBadge = "Local";
                SitePreviewSubtitle = site.Url + " · open when Herd is ready";
            });
        }
    }

    [RelayCommand]
    private void PerformDelete()
    {
        if (SelectedSite is null) return;
        var site = SelectedSite;
        var path = site.Path;
        Sites.Remove(site);
        // Stay on overview of another site when one remains — don't bounce to empty card
        SelectedSite = Sites.FirstOrDefault();
        RefreshEmptyStateCopy();
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
        StatusLine = "Checking Herd…";
        var (ok, msg) = await _svc.Herd.EnsureRunningAsync(AppendLog);
        if (!ok)
        {
            StatusLine = msg;
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SelectedSite.Url,
                UseShellExecute = true,
            });
            StatusLine = "Opened " + SelectedSite.Url;
        }
        catch (Exception ex) { StatusLine = ex.Message; }
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
        if (string.IsNullOrWhiteSpace(CreateUserEmail))
        {
            StatusLine = "Email is required to create a user.";
            return;
        }
        if (string.IsNullOrWhiteSpace(CreateUserPassword))
        {
            StatusLine = "Password is required (Statamic needs --password for non-interactive create).";
            return;
        }
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

public partial class StarterKitRow : ObservableObject
{
    [ObservableProperty] private bool isBlank;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private string package = "";
    [ObservableProperty] private string sellerName = "";
    [ObservableProperty] private string priceLabel = "Free";
    [ObservableProperty] private bool isPaid;
    [ObservableProperty] private string? coverUrl;
    [ObservableProperty] private string? marketplaceUrl;
}
