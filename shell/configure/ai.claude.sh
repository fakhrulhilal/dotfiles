#!/bin/sh
# Claude Code plugins for CLI tools. They are a nice to have, so this always exits 0 and never fails its caller.
#   ai.claude.sh <plugin> <marketplace source>  remember the plugin, then install what is missing
#   ai.claude.sh                                install what is missing (bootstrap, claude's postinstall)
# A tool's mise postinstall calls the first form. Never give the tool depends = "claude": it must install without it.
# Claude Code may come from mise or anywhere else; a later bootstrap installs plugins remembered before it existed.
state_dir="${XDG_STATE_HOME:-$HOME/.local/state}/dotfiles"
wanted="$state_dir/claude-plugins.txt"

if [ $# -eq 2 ]; then
  mkdir -p "$state_dir"
  grep -qxF "$1 $2" "$wanted" 2>/dev/null || echo "$1 $2" >> "$wanted"
elif [ $# -ne 0 ]; then
  echo "Warning: usage: ai.claude.sh [<plugin> <marketplace source>]" >&2
  exit 0
fi
[ -f "$wanted" ] || exit 0

for claude in "$(command -v claude 2>/dev/null)" "$(mise which claude 2>/dev/null)" ""; do
  [ -n "$claude" ] && "$claude" --version >/dev/null 2>&1 </dev/null && break
done
[ -n "$claude" ] || exit 0

installed="$("$claude" plugin list --json 2>/dev/null </dev/null)"
while read -r plugin marketplace; do
  [ -n "$plugin" ] || continue
  case "$installed" in *"\"$plugin@"*) continue ;; esac
  echo "Installing Claude Code plugin: $plugin"
  "$claude" plugin install "$plugin" --marketplace "$marketplace" --scope user </dev/null \
    || echo "Warning: failed to install Claude Code plugin $plugin" >&2
done < "$wanted"
exit 0