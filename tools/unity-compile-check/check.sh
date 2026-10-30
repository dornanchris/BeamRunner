#!/usr/bin/env bash
# unity-compile-check: compile-check the Unity project's C# and run the pure-C# unit tests.
#
#   bash tools/unity-compile-check/check.sh [--project <UnityProjectRoot>] [--stages core,tests,player,editor-runtime,editor,playmode]
#                                           [--define SYMBOL]... [--no-tests] [--strict-unity6] [--no-unity6-audit] [-v]
#
# Exit code: 0 = all stages passed, 1 = at least one stage failed, 2+ = usage/toolchain problem.
# Run tools/unity-compile-check/setup.sh once first.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -z "${DOTNET:-}" ]; then
  if command -v dotnet >/dev/null 2>&1; then DOTNET="$(command -v dotnet)"
  elif [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"
  fi
fi
export DOTNET="${DOTNET:-}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
exec python3 "$HERE/ucc.py" check "$@"
