#!/usr/bin/env bash
# Create a GitHub Release for Sherpa Windows and upload the zip.
# Usage: scripts/release.sh 0.2.11 "Optional release notes markdown"
#
# Zip contents (minimal):
#   Sherpa.exe
#   Microsoft.Web.WebView2.Core.dll   # must sit beside exe (single-file breaks it)
#   WebView2Loader.dll
set -euo pipefail

VERSION="${1:-}"
NOTES="${2:-}"
if [[ -z "$VERSION" ]]; then
  echo "Usage: $0 <version> [notes]" >&2
  echo "  e.g. $0 0.2.11 'Bug fixes'" >&2
  exit 1
fi
VERSION="${VERSION#v}"
TAG="v${VERSION}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if [[ -z "${GITHUB_PAT:-}${GITHUB_TOKEN:-}" ]]; then
  if [[ -f "${HOME}/.hermes/.env" ]]; then
    # shellcheck disable=SC1091
    set -a; source "${HOME}/.hermes/.env"; set +a
  fi
fi
TOKEN="${GITHUB_PAT:-${GITHUB_TOKEN:-}}"
if [[ -z "$TOKEN" ]]; then
  echo "GITHUB_PAT or GITHUB_TOKEN required" >&2
  exit 1
fi

export PATH="${HOME}/.dotnet:${PATH}"

echo "==> Clean publish dir"
rm -rf publish/win-x64
mkdir -p publish/win-x64

echo "==> Publish win-x64 (single-file app + external WebView2 bits)"
dotnet publish src/Sherpa/Sherpa.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64 \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true

if [[ ! -f publish/win-x64/Sherpa.exe ]]; then
  echo "ERROR: publish/win-x64/Sherpa.exe missing" >&2
  exit 1
fi

# Guarantee loader is present even if the ExcludeFromSingleFile target missed it
if [[ ! -f publish/win-x64/WebView2Loader.dll ]]; then
  LOADER="$(find "${HOME}/.nuget/packages/webview.avalonia.windows" -path '*win-x64*WebView2Loader.dll' | head -n1 || true)"
  if [[ -n "$LOADER" ]]; then
    echo "==> Copying WebView2Loader.dll from $LOADER"
    cp -f "$LOADER" publish/win-x64/WebView2Loader.dll
  fi
fi

if [[ ! -f publish/win-x64/Microsoft.Web.WebView2.Core.dll ]]; then
  CORE="$(find "${HOME}/.nuget/packages/webview.avalonia.windows" -name 'Microsoft.Web.WebView2.Core.dll' | head -n1 || true)"
  if [[ -n "$CORE" ]]; then
    echo "==> Copying Microsoft.Web.WebView2.Core.dll from $CORE"
    cp -f "$CORE" publish/win-x64/Microsoft.Web.WebView2.Core.dll
  fi
fi

echo "==> Publish folder (top):"
ls -lh publish/win-x64/Sherpa.exe \
  publish/win-x64/WebView2Loader.dll \
  publish/win-x64/Microsoft.Web.WebView2.Core.dll 2>&1 || true

echo "==> Zip minimal runtime (exe + WebView2 companions only)"
mkdir -p artifacts
rm -f artifacts/Sherpa-win-x64.zip
STAGE=$(mktemp -d)
cp -f publish/win-x64/Sherpa.exe "$STAGE/"
cp -f publish/win-x64/WebView2Loader.dll "$STAGE/" 2>/dev/null || true
cp -f publish/win-x64/Microsoft.Web.WebView2.Core.dll "$STAGE/" 2>/dev/null || true
# Fail loud if WebView2 bits missing — preview will be broken otherwise
if [[ ! -f "$STAGE/WebView2Loader.dll" || ! -f "$STAGE/Microsoft.Web.WebView2.Core.dll" ]]; then
  echo "ERROR: WebView2 companion DLLs missing from stage — refusing to ship a broken preview build" >&2
  ls -la "$STAGE" >&2 || true
  exit 1
fi
(cd "$STAGE" && zip -qr "$ROOT/artifacts/Sherpa-win-x64.zip" .)
rm -rf "$STAGE"
ZIP="$ROOT/artifacts/Sherpa-win-x64.zip"
ls -lh "$ZIP"
unzip -l "$ZIP"

if [[ -z "$NOTES" ]]; then
  NOTES="Sherpa for Windows ${VERSION}

Download **Sherpa-win-x64.zip**, unzip, run **Sherpa.exe**.

Keep the 2 small WebView2 DLL files next to Sherpa.exe (needed for the site preview)."
fi

echo "==> Create release ${TAG}"
PAYLOAD=$(VERSION="$VERSION" TAG="$TAG" NOTES="$NOTES" python3 - <<'PY'
import json, os
print(json.dumps({
    "tag_name": os.environ["TAG"],
    "target_commitish": "main",
    "name": os.environ["TAG"],
    "body": os.environ["NOTES"],
    "draft": False,
    "prerelease": False,
}))
PY
)

RESP=$(curl -sS -X POST \
  -H "Authorization: token ${TOKEN}" \
  -H "Accept: application/vnd.github+json" \
  "https://api.github.com/repos/welbinator/sherpa-windows/releases" \
  -d "$PAYLOAD")

UPLOAD=$(echo "$RESP" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('upload_url','').split('{')[0]); assert 'id' in d, d")
HTML=$(echo "$RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('html_url',''))")

echo "==> Upload zip"
ASSET=$(curl -sS -X POST \
  -H "Authorization: token ${TOKEN}" \
  -H "Content-Type: application/zip" \
  -H "Accept: application/vnd.github+json" \
  --data-binary @"$ZIP" \
  "${UPLOAD}?name=Sherpa-win-x64.zip")

echo "$ASSET" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('browser_download_url', d))"
echo "Release: $HTML"
