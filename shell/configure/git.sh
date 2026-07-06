CONFIG="$HOME/.gitconfig"
LINE="path = $DOT_HOME/config/git.txt"
[ ! -f "$CONFIG" ] && touch "$CONFIG"
if ! grep -Fq "$LINE" "$CONFIG"; then
    cat > "$CONFIG.tmp" << EOF
[include]
	path = $DOT_HOME/config/git.txt

[core]
    excludesfile = $DOT_HOME/config/git-ignore.txt

EOF
    cat "$CONFIG" >> "$CONFIG.tmp"
    mv "$CONFIG.tmp" "$CONFIG"
fi

unset CONFIG LINE
