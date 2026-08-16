# Sherpa for Windows

**Jack-crafted** Windows clone of [Sherpa](https://shepherd.app) — local Statamic / Laravel site manager.

Version **0.3.0** — installable app with **GitHub auto-update** (Velopack).

## Download / install

1. Open **[Releases](https://github.com/welbinator/sherpa-windows/releases/latest)**
2. Download **`Sherpa-win-Setup.exe`**
3. Run Setup (SmartScreen → *More info* → *Run anyway* if needed)
4. Sherpa installs, opens, and adds a Start Menu shortcut

### Updates

**Settings → Check for updates** looks at GitHub Releases, downloads, then **Restart & install**.

You can also wait for the quiet check a few seconds after launch (installed builds only).

### Requirements

- Windows 10/11 x64  
- [Git for Windows](https://git-scm.com/download/win)  
- [Laravel Herd](https://herd.laravel.com/) (PHP + Composer + `.test` sites)  
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually already installed with Edge)

**Sherpa does not need to live in the Herd folder.** In Settings, set **Default sites folder** to your Herd projects folder (often `C:\Users\You\Herd`).

## Maintainers — shipping

```bash
# Local installer only (Desktop\Sherpa-Setup):
scripts/pack-velopack.sh 0.3.0

# Full ship: pack + upload Velopack assets to GitHub Releases
scripts/release.sh 0.3.0 "notes here"
```

Requires Windows-side .NET 8 SDK + `vpk` global tool (script installs/uses them under `%LocalAppData%\Microsoft\dotnet`).

Pushing `main` alone is **not** enough — users need a **GitHub Release** with Setup + Velopack feed files.

## Build from source (dev)

```powershell
git clone https://github.com/welbinator/sherpa-windows.git
cd sherpa-windows
dotnet publish src/Sherpa/Sherpa.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
.\publish\win-x64\Sherpa.exe
```

Dev/portable builds show “install Setup.exe for auto-update” in Settings.

## Architecture

Same module map as Mac Sherpa: `Models/`, `Clients/`, `Services/` (including `HerdService`, `UpdateService`, coordinators, stores), `Support/ConflictTranslator`, `AppServices`, Views bind ViewModels only.

Auto-update: **Velopack** + `GithubSource` → `welbinator/sherpa-windows` releases.

## License

See repository.
