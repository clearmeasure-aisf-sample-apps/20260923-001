#!/usr/bin/env bash
# SessionStart hook: cloud sessions only (CLAUDE_CODE_REMOTE=true).
# Ensures the .NET SDK pinned by global.json and PowerShell (dotnet global tool) exist under
# ~/.dotnet, and puts them on PATH for the session. Idempotent; a warm container returns in
# well under a second because nothing is downloaded when the tools already exist.
set -u

[ "${CLAUDE_CODE_REMOTE:-}" = "true" ] || exit 0

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dotnet_root="$HOME/.dotnet"
tools="$dotnet_root/tools"
export DOTNET_ROOT="$dotnet_root" PATH="$dotnet_root:$tools:$PATH"

# SDK version pinned by global.json (empty -> channel 10.0).
version=""
if [ -f "$root/global.json" ]; then
  version="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$root/global.json" | head -1)"
fi

has_sdk() {
  [ -x "$dotnet_root/dotnet" ] || return 1
  if [ -n "$version" ]; then
    [ -d "$dotnet_root/sdk/$version" ]
  else
    ls "$dotnet_root"/sdk/10.* >/dev/null 2>&1
  fi
}

if ! has_sdk; then
  installer="$(mktemp)"
  if curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"; then
    if [ -n "$version" ]; then
      bash "$installer" --version "$version" --install-dir "$dotnet_root" >&2 \
        || bash "$installer" --channel 10.0 --install-dir "$dotnet_root" >&2
    else
      bash "$installer" --channel 10.0 --install-dir "$dotnet_root" >&2
    fi
  else
    echo "session-start: could not download dotnet-install.sh" >&2
  fi
  rm -f "$installer"
fi

if [ ! -x "$tools/pwsh" ] && [ -x "$dotnet_root/dotnet" ]; then
  "$dotnet_root/dotnet" tool install --global PowerShell >&2 \
    || echo "session-start: PowerShell install failed" >&2
fi

# Persist for the rest of the session.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$dotnet_root\""
    echo "export PATH=\"$dotnet_root:$tools:\$PATH\""
  } >> "$CLAUDE_ENV_FILE"
fi
exit 0
