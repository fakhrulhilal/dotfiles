# vim: set filetype=zsh:
[ -z "$DOT_HOME" ] && export DOT_HOME="$(dirname $(dirname "$(realpath "$0")"))"
if [ -d "$HOME/.dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
elif [ -d /usr/lib/dotnet ]; then
  export DOTNET_ROOT="/usr/lib/dotnet"
fi
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$HOME/.bun/bin:$PATH"

# Added by OrbStack: command-line tools and integration
# This won't be added again if you remove it.
source ~/.orbstack/shell/init.zsh 2>/dev/null || :
