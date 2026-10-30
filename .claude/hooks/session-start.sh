#!/bin/bash
# SessionStart hook for Claude Code on the web: prepares the headless Unity compile-check toolchain
# (tools/unity-compile-check: .NET 8 SDK, pinned package/engine sources, reference assemblies, test runner),
# so `bash tools/unity-compile-check/check.sh` and `python3 tools/gen-metas.py --check` work right away.
# Idempotent: a warm container re-runs setup.sh as a ~5 s no-op. Full log: $HOME/.cache/ucc-session-setup.log
# (stdout of this hook is added to Claude's context, so only a short status line is printed).
set -uo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"
log="$HOME/.cache/ucc-session-setup.log"
mkdir -p "$(dirname "$log")"

start=$(date +%s)
# fontTools for tools/check-strings.py's glyph-coverage check (same version as CI).
if ! python3 -c "import fontTools" > /dev/null 2>&1; then
  python3 -m pip install --quiet --disable-pip-version-check --user fonttools==4.66.0 >> "$log" 2>&1 \
    || python3 -m pip install --quiet --disable-pip-version-check --user --break-system-packages fonttools==4.66.0 >> "$log" 2>&1 \
    || echo "fontTools install failed (glyph check will be skipped)" >> "$log"
fi
if bash tools/unity-compile-check/setup.sh >> "$log" 2>&1; then
  # setup.sh may install the SDK into ~/.dotnet when no system dotnet exists: expose it to the session.
  if ! command -v dotnet > /dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ] && [ -n "${CLAUDE_ENV_FILE:-}" ]; then
    echo "export DOTNET_ROOT=\"$HOME/.dotnet\" PATH=\"$HOME/.dotnet:\$PATH\"" >> "$CLAUDE_ENV_FILE"
  fi
  echo "BeamRunner: compile-check toolchain ready ($(( $(date +%s) - start )) s). Verify changes with" \
       "'bash tools/unity-compile-check/check.sh', 'python3 tools/gen-metas.py --check' and" \
       "'python3 tools/check-strings.py' (see CLAUDE.md)."
else
  echo "BeamRunner: compile-check toolchain setup FAILED - see $log (last lines below);" \
       "re-run 'bash tools/unity-compile-check/setup.sh' before relying on check.sh."
  tail -n 15 "$log"
fi
exit 0
