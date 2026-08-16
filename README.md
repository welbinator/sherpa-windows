# Sherpa for Windows

**Jack-crafted** Windows clone of [Sherpa](https://shepherd.app) — local Statamic / Laravel site manager.

Version **0.2.11**. Reverse-engineered from the Mac app binary and rebuilt to match its flows (not a pixel-perfect skin).

## Download

1. Open **[Releases](https://github.com/welbinator/sherpa-windows/releases/latest)**
2. Download **`Sherpa-win-x64.zip`**
3. Unzip anywhere (not inside your Herd folder)
4. Run **`Sherpa.exe`** (leave the 2 small WebView2 `.dll` files next to it — needed for site preview)

SmartScreen may warn on unsigned builds → *More info* → *Run anyway*.

### Maintainers — shipping a version

Pushing `main` is **not** enough. Users only see versions that are **GitHub Releases**:

```bash
scripts/release.sh 0.2.11 "notes here"
```

Ships a minimal zip: `Sherpa.exe` + `WebView2Loader.dll` + `Microsoft.Web.WebView2.Core.dll` (WebView2 cannot live fully inside a single-file bundle).

### On your PC
- Windows 10/11 x64  
- [Git for Windows](https://git-scm.com/download/win)  
- [Laravel Herd](https://herd.laravel.com/) (PHP + Composer + `.test` sites)  
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (already on most PCs via Edge)  
- Node optional (frontend builds / static publish)

**Sherpa.exe does not need to live in the Herd folder.** In Settings, set **Default sites folder** to your Herd projects folder (often `C:\Users\You\Herd`).

## What matches the Mac app (0.2)

| Area | Notes |
|------|--------|
| Sidebar Sites / Hosts / Settings | Same IA |
| **New Site** wizard | Identity → Marketplace starter kits (Blank default, All/Free/Paid) → Flat/SQLite/MySQL → Options (Pro locked off, SSG, Git, super user) |
| Import Existing Site | Requires `composer.json` |
| Overview | Real WebView2 site preview, Statamic utilities, icon toolbar for Herd/HTTPS/open |
| Git | Save changes, Pull, Push, **Sync** (commit → pull --rebase --autostash → push), file checkboxes, Back up to GitHub |
| Packages | require / update / install |
| Commands | cache / stache / glide / custom please\|artisan |
| Deploy | Host prerequisites; connect Forge / Cloud / CF Pages / Netlify |
| Secrets | Windows DPAPI store (Keychain equivalent) |
| ConflictTranslator | Human advice + Copy errors |

Still catching up: Marketplace starter kit browser, full one-click Forge/Cloud/static publish wizards end-to-end, favicons/previews.

See [docs/REVERSE_ENGINEERING.md](docs/REVERSE_ENGINEERING.md).

## Build from source

```powershell
git clone https://github.com/welbinator/sherpa-windows.git
cd sherpa-windows
dotnet publish src/Sherpa/Sherpa.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
.\publish\win-x64\Sherpa.exe
```

## Architecture

Same module map as Mac Sherpa: `Models/`, `Clients/`, `Services/` (including `HerdService`, coordinators, stores), `Support/ConflictTranslator`, `AppServices`, Views bind ViewModels only.

## License

MIT — community Windows port. Sherpa/Shepherd Mac app © its authors.
