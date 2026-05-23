SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
DOT_HOME="$(dirname "$SCRIPT_DIR")"

source "$DOT_HOME/shell/functions.sh"

relink "$DOT_HOME/neo&vim/basic-rc.txt" "$HOME/.vimrc"
relink "$DOT_HOME/neo&vim/basic-rc.txt" "$HOME/.ideavimrc"

source "$DOT_HOME/shell/install/linux.sh"
source "$DOT_HOME/shell/install/dotnet.sh"
source "$DOT_HOME/shell/install/mise.sh"
source "$DOT_HOME/shell/install/font.sh"
source "$DOT_HOME/shell/install/ohmyposh.sh"

source "$DOT_HOME/shell/configure/git.sh"
source "$DOT_HOME/shell/configure/ssh.sh"
source "$DOT_HOME/shell/configure/zsh.sh"
source "$DOT_HOME/shell/configure/bash.sh"
source "$DOT_HOME/shell/configure/macos.sh"
source "$DOT_HOME/shell/configure/linux.sh"

source "$rc_file"
#source "$DOT_HOME/shell/install/mac_apps.sh"
