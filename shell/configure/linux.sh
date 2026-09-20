if [ "$(uname -s)" != "Linux" ]; then
    return
fi

mkdir -p "$HOME/.config/lazygit"
relink "$DOT_HOME/config/lazygit.yml" "$HOME/.config/lazygit/config.yml"