#!/usr/bin/env bash
# Renders the BeamRunner procedural audio bank with the exact Unity Core/Audio sources, writes 16-bit WAVs
# (full mixes x2, every stem x2, every SFX, loops x4) and prints the level/timing table.
#   bash tools/audio-preview/run.sh [outDir] [sampleRate]      (add --tests to also run the NUnit audio tests)
#   python3 tools/audio-preview/calibrate.py <saved table>     (re-balances music stem gains toward LUFS targets)
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ "${1:-}" == "--tests" ]]; then
  shift
  dotnet test "$here/Tests/Tests.csproj" -c Release
fi
dotnet build "$here/Preview/Preview.csproj" -c Release -v quiet -nologo >/dev/null
dotnet "$here/Preview/bin/Release/net8.0/Preview.dll" "$@"
