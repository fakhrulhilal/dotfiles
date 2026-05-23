if [ "$(uname -s)" != "Darwin" ]; then
    return
fi

mkdir -p "$HOME/Library/Application Support/lazygit"
relink "$DOT_HOME/config/lazygit.yml" "$HOME/Library/Application Support/lazygit/config.yml"
mkdir -p "$HOME/Library/Application Support/com.mitchellh.ghostty"
relink "$DOT_HOME/config/ghostty.txt" "$HOME/Library/Application Support/com.mitchellh.ghostty/config.ghostty"
