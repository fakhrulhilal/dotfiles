if [ -z "$ZSH_VERSION" ]; then
    return
fi

mkdir -p "$HOME/.local/share/zsh/completion"
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

echo "Configuring zsh"
completion_dir="$HOME/.local/share/zsh/completion"
mkdir -p "$completion_dir"
mise completion zsh > "$completion_dir/_mise"
deno completions zsh > "$completion_dir/_deno"
