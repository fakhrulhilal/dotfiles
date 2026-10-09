# vim: set filetype=bash:
[ -z "$DOT_HOME" ] && export DOT_HOME="$(dirname "$(dirname "$(realpath "$BASH_SOURCE")")")"
if [ -d "$HOME/.dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
elif [ -d /usr/lib/dotnet ]; then
  export DOTNET_ROOT="/usr/lib/dotnet"
fi
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$HOME/.local/bin:$PATH"
[ -f "$HOME/.env" ] && source "$HOME/.env"
[ -f "$HOME/.bashrc" ] && source "$HOME/.bashrc"

# Added by OrbStack: command-line tools and integration
# This won't be added again if you remove it.
source ~/.orbstack/shell/init.bash 2>/dev/null || :
