#!/usr/bin/env bash
# unity-compile-check: one-time / idempotent setup (safe to re-run; a fresh cloud container needs ~1-3 min).
#
#   bash tools/unity-compile-check/setup.sh [--force]
#
# 1. makes sure a .NET 8 SDK is available (existing `dotnet`, else ~/.dotnet via dotnet-install.sh, else apt)
# 2. downloads the pinned NuGet packages and clones the pinned package / engine-assembly / UnityCsReference sources
# 3. builds the Roslyn helper (UccTool), the UnityCsReference 6000.3.25f1 API index, the package assemblies (per
#    player platform: Android + iOS, plus editor flavours) and the stubs
# 4. restores the NUnit test-runner project
# Everything goes to $UCC_CACHE (default ~/.cache/unity-compile-check); git clones go under $UCC_SRC_ROOT (default:
# /home/user when that is a writable directory, else $UCC_CACHE/src): needle-mirror/<pkg>,
# unity-technologies/unitycsreference, lavagang/unity-runtime-libraries, unity-technologies/graphics - or wherever
# $UCC_NEEDLE_DIR, $UCC_CSREF_DIR, $UCC_URL_DIR, $UCC_GRAPHICS_DIR point. Fresh clones are shallow, blob-less and
# sparse; when the cache already holds everything built from them (e.g. a restored CI cache) missing clones are
# skipped. Nothing is written into the repository.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOTNET_CHANNEL="8.0"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

log() { printf '[setup.sh] %s\n' "$*"; }

have_dotnet8() {
  local d="$1"
  [ -x "$d" ] || return 1
  "$d" --list-sdks 2>/dev/null | grep -Eq '^(8|9|10)\.'
}

find_dotnet() {
  for d in "${DOTNET:-}" "$(command -v dotnet 2>/dev/null || true)" "$HOME/.dotnet/dotnet"; do
    if [ -n "$d" ] && have_dotnet8 "$d"; then echo "$d"; return 0; fi
  done
  return 1
}

install_dotnet() {
  local script
  script="$(mktemp)"
  # dotnet-install.sh: official location first, GitHub mirror of the script second.
  if curl -fsSL --retry 3 -o "$script" https://dot.net/v1/dotnet-install.sh 2>/dev/null \
     || curl -fsSL --retry 3 -o "$script" https://raw.githubusercontent.com/dotnet/install-scripts/main/src/dotnet-install.sh; then
    log "installing .NET SDK $DOTNET_CHANNEL into ~/.dotnet (dotnet-install.sh)"
    if bash "$script" --channel "$DOTNET_CHANNEL" --install-dir "$HOME/.dotnet" --no-path; then
      rm -f "$script"; return 0
    fi
    log "dotnet-install.sh failed (the SDK download host may be blocked by the network policy)"
  fi
  rm -f "$script"
  # Fallback: Ubuntu ships dotnet-sdk-8.0 in its own archive (works where builds.dotnet.microsoft.com is blocked).
  if command -v apt-get >/dev/null 2>&1; then
    local SUDO=""
    [ "$(id -u)" = 0 ] || SUDO="sudo"
    log "falling back to: apt-get install dotnet-sdk-8.0"
    $SUDO apt-get update -qq || true
    DEBIAN_FRONTEND=noninteractive $SUDO apt-get install -y -qq --no-install-recommends dotnet-sdk-8.0 && return 0
  fi
  return 1
}

for tool in curl git python3; do
  command -v "$tool" >/dev/null 2>&1 || { echo "setup.sh: '$tool' is required but not installed" >&2; exit 1; }
done

if ! DOTNET_BIN="$(find_dotnet)"; then
  install_dotnet || { echo "setup.sh: could not install a .NET 8 SDK" >&2; exit 1; }
  DOTNET_BIN="$(find_dotnet)" || { echo "setup.sh: .NET SDK installed but not found" >&2; exit 1; }
fi
export DOTNET="$DOTNET_BIN"
log "using $DOTNET ($("$DOTNET" --version))"

exec python3 "$HERE/ucc.py" setup "$@"
