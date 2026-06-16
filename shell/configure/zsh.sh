#!/usr/bin/env zsh

if [ -z "$ZSH_VERSION" ]; then
    return
fi

rc_file="$HOME/.zshrc"
relink "$DOT_HOME/zsh/profile.zsh" "$HOME/.zprofile"
relink "$DOT_HOME/zsh/rc.zsh" "$rc_file"
cat >> "$HOME/.zshenv" <<EOF

# Added by dotfiles bootstrapper
export DOT_HOME="$DOT_HOME"
export ZSH_EXT="\$DOT_HOME/zsh"
EOF

if [[ ! -f "${XDG_DATA_HOME:-$HOME/.local/share}/zap/zap.zsh" ]]; then
    echo "⬇️  Installing zap-zsh..."
    zsh <(curl -s https://raw.githubusercontent.com/zap-zsh/zap/master/install.zsh) --branch release-v1
    echo "✅ zap-zsh installed"
fi

source "$rc_file"
echo "Configuring zsh"
completion_dir="$HOME/.local/share/zsh/completion"
mkdir -p "$completion_dir"
mise completion zsh > "$completion_dir/_mise"
cat > "$completion_dir/_dotnet" <<<'EOF'
# zsh parameter completion for the dotnet CLI

_dotnet_zsh_complete()
{
  local completions=("$(dotnet complete "$words")")

  # If the completion list is empty, just continue with filename selection
  if [ -z "$completions" ]
  then
    _arguments '*::arguments: _normal'
    return
  fi

  # This is not a variable assignment, don't remove spaces!
  _values = "${(ps:\n:)completions}"
}

compdef _dotnet_zsh_complete dotnet
EOF
