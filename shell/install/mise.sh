if ! command -v mise &>/dev/null; then
    echo "Installing mise"
    curl https://mise.run | sh
    mkdir -p "$HOME/.config/mise"
    relink "$DOT_HOME/config/mise.toml" "$HOME/.config/mise/config.toml"
    eval "$($HOME/.local/bin/mise activate $CURRENT_SHELL)"
fi

mise install
