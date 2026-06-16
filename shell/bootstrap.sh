if [ -n "$ZSH_VERSION" ]; then
    emulate -L zsh
    CURRENT_SHELL="zsh"
elif [ -n "$BASH_VERSION" ]; then
    CURRENT_SHELL="bash"
else
    CURRENT_SHELL="sh"
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
DOT_HOME="$(dirname "$SCRIPT_DIR")"

source "$DOT_HOME/shell/functions.sh"

relink "$DOT_HOME/vim/basic-rc.txt" "$HOME/.vimrc"
relink "$DOT_HOME/vim/basic-rc.txt" "$HOME/.ideavimrc"

source "$DOT_HOME/shell/install/linux.sh"
source "$DOT_HOME/shell/install/dotnet.sh"
source "$DOT_HOME/shell/install/mise.sh"
source "$DOT_HOME/shell/install/font.sh"
source "$DOT_HOME/shell/install/ohmyposh.sh"

source "$DOT_HOME/shell/configure/git.sh"
source "$DOT_HOME/shell/configure/ssh.sh"
case "$CURRENT_SHELL" in
    zsh) . "$DOT_HOME/shell/configure/zsh.sh" ;;
    bash) . "$DOT_HOME/shell/configure/bash.sh" ;;
    *) echo "No support for Posix shell at the moment" ;;
esac

source "$DOT_HOME/shell/configure/secret.sh"
source "$DOT_HOME/shell/configure/macos.sh"
source "$DOT_HOME/shell/configure/linux.sh"

source "$DOT_HOME/shell/install/mac_apps.sh"
