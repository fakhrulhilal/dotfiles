#!/usr/bin/env zsh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
mkdir -p "$HOME/.local/share/zsh/completion"

relink() {
    local source="$1"
    local target="$2"
    
    if [ -f "$target" ]; then
        mv "$target" "${target}.bak"
    fi 
    ln -sf "$source" "$target"
}

relink "$ROOT_DIR/neo&vim/basic-rc.txt" "$HOME/.vimrc"
relink "$ROOT_DIR/neo&vim/basic-rc.txt" "$HOME/.ideavimrc"

DOTNET_VERSIONS=(6 8 10)
DOTNET_INSTALL_SCRIPT="/tmp/dotnet-install.sh"
if ! command -v dotnet &>/dev/null; then
    echo "Installing .NET"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$DOTNET_INSTALL_SCRIPT"
    chmod +x "$DOTNET_INSTALL_SCRIPT"
    for version in "${DOTNET_VERSIONS[@]}"; do
        "$DOTNET_INSTALL_SCRIPT" --channel "${version}.0" --runtime dotnet
        "$DOTNET_INSTALL_SCRIPT" --channel "${version}.0" --runtime aspnetcore
        echo "✅ .NET ${version} installed"
    done
    rm "$DOTNET_INSTALL_SCRIPT"
else
    echo "dotnet already installed: $(dotnet --version)"
    dotnet --list-sdks
fi

if [[ "$SHELL" == */zsh ]]; then
    relink "$ROOT_DIR/zsh/profile.txt" "$HOME/.zprofile"
    relink "$ROOT_DIR/zsh/rc.txt" "$HOME/.zshrc"
    cat >> "$HOME/.zshenv" <<EOF
    
# Added by dotfiles bootstrapper
export DOT_HOME="$ROOT_DIR"
export ZSH_EXT="\$DOT_HOME/zsh"
EOF

    if [[ ! -f "${XDG_DATA_HOME:-$HOME/.local/share}/zap/zap.zsh" ]]; then
        echo "⬇️  Installing zap-zsh..."
        zsh <(curl -s https://raw.githubusercontent.com/zap-zsh/zap/master/install.zsh) --branch release-v1
        echo "✅ zap-zsh installed"
    fi
fi

if ! command -v mise &>/dev/null; then
    echo "Installing mise"
    curl https://mise.run | sh
    mkdir -p "$HOME/.config/mise"
    relink "$ROOT_DIR/config/mise.toml" "$HOME/.config/mise/config.toml"
    mise install
fi

if [[ "$SHELL" == */zsh ]]; then
    echo "Configuring zsh"
    completion_dir="$HOME/.local/share/zsh/completion"
    mkdir -p "$completion_dir"
    mise completion zsh > "$completion_dir/_mise"
    deno completions zsh > "$completion_dir/_deno"
fi

source "$HOME/.zshrc"
install_font "https://github.com/ryanoasis/nerd-fonts/releases/download/v3.4.0/CascadiaCode.zip"
if ! command -v oh-my-posh &>/dev/null; then
    curl -s https://ohmyposh.dev/install.sh | bash -s
fi

if ! grep -q "^\[include\]" "$GITCONFIG"; then
  cat >> "$HOME/.gitconfig" <<EOF

[include]
	path = $ROOT_DIR/config/git.txt

[core]
    excludesfile = $ROOT_DIR/config/git-ignore.txt
EOF
fi

case "$(uname -s)" in
  Darwin)
    mkdir -p "$HOME/Library/Application Support/lazygit"
    relink "$DOT_HOME/config/lazygit.yml" "$HOME/Library/Application Support/lazygit/config.yml"
    ;;
  Linux)
    mkdir -p "$HOME/.config/lazygit/config.yml"
    relink "$DOT_HOME/config/lazygit.yml" "$HOME/.config/lazygit/config.yml"
    ;;
  *)
    echo "Unsupported OS"
    ;;
esac

exit 0
# Guard: macOS only
if [[ "$(uname)" != "Darwin" ]]; then
  echo "⏭️  Not macOS, skipping DMG installs"
  exit 0
fi

APP_LIST="$ROOT_DIR/mac_app.txt"
if [ ! -f "$APP_LIST" ]; then
  echo "❌ app.txt not found at $APP_LIST"
  exit 1
fi

mkdir -p ~/Applications
source "$ROOT_DIR/zsh/functions.zsh"
while IFS= read -r line || [ -n "$line" ]; do
  [[ -z "$line" || "$line" == \#* ]] && continue

  # Parse ';' separated fields
  url=$(echo "$line"      | cut -d';' -f1 | xargs)
  checksum=$(echo "$line" | cut -d';' -f2 | xargs)
  install_mac_app "$url" "$checksum"
done < "$APP_LIST"
