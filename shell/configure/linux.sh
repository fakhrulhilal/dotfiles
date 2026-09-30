if [ "$(uname -s)" != "Linux" ]; then
    return
fi

mkdir -p "$HOME/.config/lazygit"
relink "$DOT_HOME/config/lazygit.yml" "$HOME/.config/lazygit/config.yml"

mkdir -p "$HOME/.config/zed"
relink "$DOT_HOME/config/zed/settings.json" "$HOME/.config/zed/settings.json"
relink "$DOT_HOME/config/zed/keymap.json" "$HOME/.config/zed/keymap.json"