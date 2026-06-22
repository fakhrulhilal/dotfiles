mkdir -p "$HOME/.ssh"
CONFIG="$HOME/.ssh/config"
LINE="Include \"$DOT_HOME/config/ssh.txt\""
[ ! -f "$CONFIG" ] && touch "$CONFIG"
if ! grep -Fxq "$LINE" "$CONFIG"; then
    printf "%s\n\n" "$LINE" | cat - "$CONFIG" > "$CONFIG.tmp"
    mv "$CONFIG.tmp" "$CONFIG"
fi

if [ ! -f "$HOME/.ssh/id_rsa" ]; then
    echo "🔐 Generating SSH key pair using RSA encryption"
    ssh-keygen -t rsa -b 4096 -f "$HOME/.ssh/id_rsa"
fi

if [ ! -f "$HOME/.ssh/id_ed25519" ]; then
    echo "🔐 Generating SSH key pair using ED25519 encryption"
    ssh-keygen -t ed25519 -b 4096 -f "$HOME/.ssh/id_ed25519"
fi

unset CONFIG LINE
