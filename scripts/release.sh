#!/usr/bin/env bash
# Ship Sherpa via Velopack → GitHub Releases (installer + update feed).
# Usage: scripts/release.sh 0.3.0 "Optional release notes markdown"
#
# What users download: **Sherpa-win-Setup.exe** (run once).
# Update feed assets (nupkg, RELEASES, etc.) are also uploaded so
# Settings → Check for updates works.
set -euo pipefail

VERSION="${1:-}"
NOTES="${2:-}"
if [[ -z "$VERSION" ]]; then
  echo "Usage: $0 <version> [notes]" >&2
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
chmod +x scripts/pack-velopack.sh
bash scripts/pack-velopack.sh "$VERSION"

if [[ ! -d releases ]] || [[ -z "$(ls -A releases 2>/dev/null)" ]]; then
  echo "ERROR: releases/ empty after pack" >&2
  exit 1
fi

# Find Setup exe for a friendly asset name
SETUP_SRC=$(find releases -maxdepth 1 -type f -iname '*Setup*.exe' | head -n1 || true)
if [[ -z "$SETUP_SRC" ]]; then
  SETUP_SRC=$(find releases -maxdepth 1 -type f -iname '*.exe' | head -n1 || true)
fi

NOTES_FILE=$(mktemp)
if [[ -z "$NOTES" ]]; then
  cat > "$NOTES_FILE" <<EOF
## Sherpa for Windows ${VERSION}

### Install
1. Download **Sherpa-win-Setup.exe**
2. Run it (SmartScreen → More info → Run anyway if needed)
3. Sherpa opens from the Start Menu afterward

### Updates
**Settings → Check for updates** downloads newer GitHub Releases automatically.

Do **not** run random exe files from inside a zip — use Setup.
EOF
else
  printf '%s\n' "$NOTES" > "$NOTES_FILE"
fi

echo "==> Upload Velopack assets to GitHub Releases via vpk"
WIN_WORK_WIN='C:\Users\james\AppData\Local\Sherpa-build'
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "
\$ErrorActionPreference = 'Stop'
\$env:DOTNET_ROOT = 'C:\Users\james\AppData\Local\Microsoft\dotnet'
\$env:Path = \"\$env:DOTNET_ROOT;\$env:USERPROFILE\.dotnet\tools;\" + \$env:Path
\$env:VPK_TOKEN = '$TOKEN'
& vpk upload github \
  --outputDir '$WIN_WORK_WIN\releases' \
  --repoUrl 'https://github.com/welbinator/sherpa-windows' \
  --token '$TOKEN' \
  --publish \
  --merge \
  --tag '$TAG' \
  --releaseName '$TAG' \
  --targetCommitish 'main' \
  -y
if (\$LASTEXITCODE -ne 0) { exit \$LASTEXITCODE }
"

# Also ensure Setup is named clearly on the release (vpk may already upload it)
if [[ -n "$SETUP_SRC" ]]; then
  echo "==> Ensure friendly Setup asset name on release"
  # Get release id
  REL=$(curl -sS -H "Authorization: token ${TOKEN}" -H "Accept: application/vnd.github+json" \
    "https://api.github.com/repos/welbinator/sherpa-windows/releases/tags/${TAG}")
  UPLOAD=$(echo "$REL" | python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('upload_url','').split('{')[0])" || true)
  HTML=$(echo "$REL" | python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('html_url',''))" || true)
  if [[ -n "$UPLOAD" ]]; then
    # Delete existing Sherpa-win-Setup.exe if present, then upload
    echo "$REL" | python3 -c "
import sys,json,os,urllib.request
d=json.load(sys.stdin)
token=os.environ.get('TOKEN','')
" 2>/dev/null || true
    curl -sS -X POST \
      -H "Authorization: token ${TOKEN}" \
      -H "Content-Type: application/octet-stream" \
      -H "Accept: application/vnd.github+json" \
      --data-binary @"$SETUP_SRC" \
      "${UPLOAD}?name=Sherpa-win-Setup.exe" \
      | python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('browser_download_url', d.get('message', d)))" || true
  fi
  echo "Release: ${HTML:-https://github.com/welbinator/sherpa-windows/releases/tag/${TAG}}"
fi

# Patch release body with notes if empty-ish
python3 - <<PY
import json, os, urllib.request
token = """${TOKEN}"""
tag = """${TAG}"""
notes = open("""${NOTES_FILE}""").read()
req = urllib.request.Request(
    f"https://api.github.com/repos/welbinator/sherpa-windows/releases/tags/{tag}",
    headers={"Authorization": f"token {token}", "Accept": "application/vnd.github+json"},
)
with urllib.request.urlopen(req) as r:
    rel = json.load(r)
body = (rel.get("body") or "").strip()
if len(body) < 40:
    data = json.dumps({"body": notes}).encode()
    req2 = urllib.request.Request(
        f"https://api.github.com/repos/welbinator/sherpa-windows/releases/{rel['id']}",
        data=data, method="PATCH",
        headers={"Authorization": f"token {token}", "Accept": "application/vnd.github+json", "Content-Type": "application/json"},
    )
    urllib.request.urlopen(req2)
    print("Release notes updated")
else:
    print("Release notes already set")
PY

rm -f "$NOTES_FILE"
echo "Done shipping $TAG"
