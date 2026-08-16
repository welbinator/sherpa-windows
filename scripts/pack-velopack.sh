#!/usr/bin/env bash
# Build a Velopack Windows installer locally (no GitHub upload).
# Usage: scripts/pack-velopack.sh [version]
# Example: scripts/pack-velopack.sh 0.3.0
#
# Output:
#   releases/  — Setup.exe, nupkg, RELEASES, portable zip (Velopack assets)
#   Desktop\Sherpa-Setup\  — copy of Setup.exe for James to try
set -euo pipefail

VERSION="${1:-0.3.0}"
VERSION="${VERSION#v}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

export PATH="${HOME}/.dotnet:${PATH}"
export DOTNET_ROOT_WIN='C:\Users\james\AppData\Local\Microsoft\dotnet'

WIN_ROOT="/mnt/c/Users/james"
# Project path as Windows sees it (repo is under WSL home — need a Windows-visible path)
# Copy publish output to a Windows path for vpk.
WIN_WORK="${WIN_ROOT}/AppData/Local/Sherpa-build"
WIN_WORK_WIN='C:\Users\james\AppData\Local\Sherpa-build'

echo "==> Publish multi-file win-x64 (WebView2-safe)"
rm -rf publish/win-x64
dotnet publish src/Sherpa/Sherpa.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64 \
  -p:PublishSingleFile=false \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -p:Version="$VERSION" \
  -p:AssemblyVersion="${VERSION}.0" \
  -p:FileVersion="${VERSION}.0"

rm -f publish/win-x64/*.pdb

if [[ ! -f publish/win-x64/Sherpa.exe ]]; then
  echo "ERROR: Sherpa.exe missing after publish" >&2
  exit 1
fi
if [[ ! -f publish/win-x64/WebView2Loader.dll || ! -f publish/win-x64/Microsoft.Web.WebView2.Core.dll ]]; then
  echo "ERROR: WebView2 companions missing from publish output" >&2
  ls publish/win-x64 | head >&2
  exit 1
fi

echo "==> Stage publish folder on Windows filesystem for vpk"
rm -rf "$WIN_WORK"
mkdir -p "$WIN_WORK/publish" "$WIN_WORK/releases"
cp -a publish/win-x64/. "$WIN_WORK/publish/"
# Icon
if [[ -f src/Sherpa/Assets/sherpa.ico ]]; then
  cp -f src/Sherpa/Assets/sherpa.ico "$WIN_WORK/sherpa.ico"
fi

echo "==> vpk pack (Windows CLI)"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "
\$ErrorActionPreference = 'Stop'
\$env:DOTNET_ROOT = '$DOTNET_ROOT_WIN'
\$env:Path = \"\$env:DOTNET_ROOT;\$env:USERPROFILE\.dotnet\tools;\" + \$env:Path
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
  dotnet tool install -g vpk --version 1.2.0
}
\$packDir = '$WIN_WORK_WIN\publish'
\$outDir  = '$WIN_WORK_WIN\releases'
\$icon    = '$WIN_WORK_WIN\sherpa.ico'
\$args = @(
  'pack',
  '-u', 'Sherpa',
  '-v', '$VERSION',
  '-p', \$packDir,
  '-o', \$outDir,
  '-e', 'Sherpa.exe',
  '--packTitle', 'Sherpa',
  '--packAuthors', 'welbinator',
  '-y'
)
if (Test-Path \$icon) { \$args += @('-i', \$icon) }
Write-Output ('vpk ' + (\$args -join ' '))
& vpk @args
if (\$LASTEXITCODE -ne 0) { exit \$LASTEXITCODE }
Get-ChildItem \$outDir | Format-Table Name, Length
"

echo "==> Copy releases back to repo"
rm -rf releases
mkdir -p releases
cp -a "$WIN_WORK/releases/." releases/

echo "==> Desktop installer folder"
DEST="/mnt/c/Users/james/Desktop/Sherpa-Setup"
rm -rf "$DEST"
mkdir -p "$DEST"
# Prefer the Setup exe Velopack produced
SETUP=$(find releases -maxdepth 1 -type f \( -iname '*Setup*.exe' -o -iname 'Sherpa-win-Setup.exe' -o -iname '*.exe' \) | head -n1)
if [[ -z "$SETUP" ]]; then
  echo "WARN: no Setup exe found — listing releases:" >&2
  ls -la releases >&2
else
  cp -f "$SETUP" "$DEST/$(basename "$SETUP")"
  # Also drop a short readme
  cat > "$DEST/README.txt" <<EOF
Sherpa for Windows — installer
==============================

1. Double-click the Setup .exe
2. Sherpa installs and opens (Start Menu shortcut is created)
3. Later: Settings → Check for updates

Your sites and settings live in %LocalAppData%\\Sherpa\\
EOF
  powershell.exe -NoProfile -Command "Get-ChildItem 'C:\Users\james\Desktop\Sherpa-Setup' | Unblock-File" || true
fi

echo ""
echo "Done. Version $VERSION"
echo "  Repo releases/: $(ls releases | tr '\n' ' ')"
echo "  Desktop: $DEST"
ls -lh "$DEST" 2>/dev/null || true
