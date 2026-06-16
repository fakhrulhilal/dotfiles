#!/usr/bin/env bash

if [ -z "$BASH_VERSION" ]; then
    return
fi

rc_file="$HOME/.bashrc"
relink "$DOT_HOME/bash/profile.sh" "$HOME/.bash_profile"
relink "$DOT_HOME/bash/rc.sh" "$rc_file"
cat >> "$HOME/.env" <<EOF

# Added by dotfiles bootstrapper
export DOT_HOME="$DOT_HOME"
export BASH_EXT="\$DOT_HOME/bash"
EOF

source "$rc_file"
completion_dir="$HOME/.local/share/bash-completion/completions"
mkdir -p "$completion_dir"
mise completion bash > "$completion_dir/mise"
cat > "$completion_dir/bash" << 'EOF'
# bash parameter completion for the dotnet CLI

function _dotnet_bash_complete()
{
  local cur="${COMP_WORDS[COMP_CWORD]}" IFS=$'\n' # On Windows you may need to use use IFS=$'\r\n'
  local candidates

  read -d '' -ra candidates < <(dotnet complete --position "${COMP_POINT}" "${COMP_LINE}" 2>/dev/null)

  read -d '' -ra COMPREPLY < <(compgen -W "${candidates[*]:-}" -- "$cur")
}

complete -f -F _dotnet_bash_complete dotnet
EOF
