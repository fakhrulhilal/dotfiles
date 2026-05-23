# vim: set filetype=zsh:
[ -z "$DOT_HOME" ] && export DOT_HOME="$(dirname "$(dirname "$(realpath "$BASH_SOURCE")")")"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$HOME/.local/bin:$PATH"
[ -f "$HOME/.env" ] && source "$HOME/.env"
[ -f "$HOME/.bashrc" ] && source "$HOME/.bashrc"

# Added by OrbStack: command-line tools and integration
# This won't be added again if you remove it.
source ~/.orbstack/shell/init.zsh 2>/dev/null || :
