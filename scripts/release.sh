#!/usr/bin/env bash
# Create a GitHub Release for Sherpa Windows and upload the zip.
# Usage: scripts/release.sh 0.2.9 "Optional release notes markdown"
set -euo pipefail

VERSION="${1:-}"
NOTES="${2:-}"
if [[ -z "$VERSION" ]]; then
  echo "Usage: $0 <version> [notes]" >&2
  echo "  e.g. $0 0.2.9 'Bug fixes'" >&2
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

echo "==> Publish win-x64 (multi-file so WebView2Loader.dll ships beside exe)"
dotnet publish src/Sherpa/Sherpa.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64

# Belt-and-suspenders: ensure WebView2Loader.dll is next to Sherpa.exe
if [[ ! -f publish/win-x64/WebView2Loader.dll ]]; then
  LOADER="$(find "${HOME}/.nuget/packages/webview.avalonia.windows" -path '*win-x64*WebView2Loader.dll' | head -n1 || true)"
  if [[ -n "$LOADER" ]]; then
    echo "==> Copying WebView2Loader.dll from $LOADER"
    cp -f "$LOADER" publish/win-x64/WebView2Loader.dll
  else
    echo "WARN: WebView2Loader.dll missing from publish output" >&2
  fi
fi

echo "==> Zip entire publish folder (not just the exe)"
mkdir -p artifacts
rm -f artifacts/Sherpa-win-x64.zip
(cd publish/win-x64 && zip -qr ../../artifacts/Sherpa-win-x64.zip .)
ZIP="$ROOT/artifacts/Sherpa-win-x64.zip"
ls -lh "$ZIP"
echo "Contents sample:"
unzip -l "$ZIP" | head -40

if [[ -z "$NOTES" ]]; then
  NOTES="Sherpa for Windows ${VERSION}

Download **Sherpa-win-x64.zip**, unzip to a folder (not inside Herd), run **Sherpa.exe**.

Keep the whole unzipped folder together — WebView preview needs the companion DLLs next to the exe."
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
