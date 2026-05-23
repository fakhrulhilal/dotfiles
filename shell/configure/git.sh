CONFIG="$HOME/.gitconfig"
[ ! -f "$CONFIG" ] && touch "$CONFIG"
if ! grep -q "^\[include\]" "$CONFIG"; then
  cat >> "$HOME/.gitconfig" <<EOF

[include]
	path = $DOT_HOME/config/git.txt

[core]
    excludesfile = $DOT_HOME/config/git-ignore.txt
EOF
fi

unset CONFIG
