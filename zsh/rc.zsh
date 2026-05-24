# vim: set filetype=zsh:
[ -z "$DOT_HOME" ] && export DOT_HOME="$(dirname $(dirname "$(realpath "$0")"))"
export DYLD_FALLBACK_LIBRARY_PATH="/opt/homebrew/lib:$DYLD_FALLBACK_LIBRARY_PATH"

[ -f "$ZSH_EXT/variables.zsh" ] && source "$ZSH_EXT/variables.zsh"
[ -f "$ZSH_EXT/functions.zsh" ] && source "$ZSH_EXT/functions.zsh"
[ -f "$ZSH_EXT/aliases.zsh" ] && source "$ZSH_EXT/aliases.zsh"

[ -f "${XDG_DATA_HOME:-$HOME/.local/share}/zap/zap.zsh" ] && source "${XDG_DATA_HOME:-$HOME/.local/share}/zap/zap.zsh"
plug "zsh-users/zsh-autosuggestions"
plug "zap-zsh/supercharge"
plug "wintermi/zsh-lsd"
#plug "zap-zsh/zap-prompt"
plug "zsh-users/zsh-syntax-highlighting"
plug "wintermi/zsh-brew"
plug "zsh-users/zsh-history-substring-search"
#plug "zap-zsh/web-search"
plug "wintermi/zsh-oh-my-posh"

# Load and initialise completion system
fpath=(~/.local/share/zsh/completion $fpath)
typeset -U fpath
autoload -Uz compinit
compinit -u -d "${ZDOTDIR:-$HOME}/.zcompdump"
unsetopt EXTENDED_GLOB

[ -f "$DOT_HOME/shell/rc.sh" ] && source "$DOT_HOME/shell/rc.sh"
