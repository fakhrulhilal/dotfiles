mkdir -p "$HOME/.ssh"
CONFIG="$HOME/.ssh/config"
LINE="Include \"$DOT_HOME/config/ssh.txt\""
[ ! -f "$CONFIG" ] && touch "$CONFIG"
if ! grep -Fxq "$LINE" "$CONFIG"; then
    printf "%s\n\n" "$LINE" | cat - "$CONFIG" > "$CONFIG.tmp"
    mv "$CONFIG.tmp" "$CONFIG"
fi

unset CONFIG LINE
