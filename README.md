# Sherpa for Windows

**Jack-crafted** Windows port of [Sherpa](https://shepherd.app) — the local Statamic / Laravel site manager.

Version **0.1.0** (build 1). Early but intentional: the shell, site management, Composer/Git/please commands, secret store, and host connections are real. Full Forge / Laravel Cloud / one-click Pages deploy wizards follow the same architecture next.

## What you get

| Area | Status |
|------|--------|
| Sidebar sites + detail tabs (Overview / Git / Packages / Commands / Deploy) | ✅ |
| Import existing folders (detects Statamic vs Laravel) | ✅ |
| New site wizard (blank or `statamic/statamic` create-project) | ✅ |
| Composer install / update / require | ✅ |
| Git status, log, init | ✅ |
| `artisan` / `please` commands (cache, stache, glide) | ✅ |
| Human error advice (`ConflictTranslator`) + Copy errors | ✅ |
| GitHub + Cloudflare tokens in **Windows secret store** (DPAPI) | ✅ |
| Private GitHub repo backup | ✅ |
| Deploy prerequisite gates (plain-English) | ✅ |
| Forge / Laravel Cloud / Netlify full deploy wizards | 🚧 next |

## Download (easiest)

1. Open **[Releases](https://github.com/welbinator/sherpa-windows/releases)**
2. Download **`Sherpa-win-x64.zip`** from the latest release
3. Unzip anywhere
4. Run **`Sherpa.exe`**

Windows may show SmartScreen on unsigned builds — *More info* → *Run anyway*.

Requirements on your PC:

- Windows 10/11 x64
- [Git for Windows](https://git-scm.com/download/win)
- PHP + Composer via [Laravel Herd](https://herd.laravel.com/) (recommended) or Laragon / PATH

## Build from source

```powershell
# needs .NET 8 SDK: https://dotnet.microsoft.com/download
git clone https://github.com/welbinator/sherpa-windows.git
cd sherpa-windows
dotnet publish src/Sherpa/Sherpa.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
.\publish\win-x64\Sherpa.exe
```

## Architecture (mirrors the Mac app)

```
Models/          Site, HostAccount, ConflictAdvice
Clients/         GitHubClient, CloudflarePagesClient, PackagistClient
Services/        ComposerService, GitService, SiteCommandsService,
                 SiteStore, SecretStore, RuntimeManager, ProcessRunner,
                 InstallCoordinator, NotificationService
Support/         ConflictTranslator
ViewModels/      MainViewModel (UI only — no raw HTTP)
Views/           MainWindow
AppServices      composition root
```

Secrets never live in the repo or in `sites.json`. Tokens are DPAPI-protected under `%LocalAppData%\Sherpa\secrets\`.

## Jack mode notes

Built to match Sherpa's craft:

- Prerequisite copy: *"Add a GitHub token in Settings first."*
- Errors translated into next actions, with **Copy errors** for the raw log
- Long jobs show an activity log; finish can toast
- Host credentials separate from site list

## License

MIT — community Windows port. Sherpa/Shepherd Mac app © its authors.
