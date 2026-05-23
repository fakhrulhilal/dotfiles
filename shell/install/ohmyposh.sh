if command -v oh-my-posh &>/dev/null; then
    return
fi

curl -s https://ohmyposh.dev/install.sh | bash -s -- -d ~/.local/bin
