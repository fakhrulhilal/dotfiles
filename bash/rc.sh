# vim: set filetype=sh:
# ~/.bashrc: executed by bash(1) for non-login shells.
# see /usr/share/doc/bash/examples/startup-files (in the package bash-doc)
# for examples

# Environment goes here, not in ~/.bash_profile: terminals and multiplexers (herdr, tmux) start non-login shells,
# and those read only ~/.bashrc
[ -f "$HOME/.env" ] && source "$HOME/.env"
export DOTNETUP_ROOT="$HOME/.dotnetup"
if [ -d "$HOME/.dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
elif [ -d /usr/lib/dotnet ]; then
  export DOTNET_ROOT="/usr/lib/dotnet"
fi
# prepend in reverse so the order is DOTNETUP_ROOT, DOTNET_ROOT, its tools, ~/.local/bin; skip entries already there
for dir in "$HOME/.local/bin" ${DOTNET_ROOT:+"$DOTNET_ROOT/tools" "$DOTNET_ROOT"} "$DOTNETUP_ROOT"; do
  case ":$PATH:" in
    *":$dir:"*) ;;
    *) PATH="$dir:$PATH" ;;
  esac
done
export PATH
unset dir

# If not running interactively, don't do anything
case $- in
    *i*) ;;
      *) return;;
esac

[ -z "$DOT_HOME" ] && export DOT_HOME="$(dirname "$(dirname "$(realpath "$BASH_SOURCE")")")"

# don't put duplicate lines or lines starting with space in the history.
# See bash(1) for more options
HISTCONTROL=ignoreboth

# append to the history file, don't overwrite it
shopt -s histappend

# for setting history length see HISTSIZE and HISTFILESIZE in bash(1)
HISTSIZE=1000
HISTFILESIZE=2000

# check the window size after each command and, if necessary,
# update the values of LINES and COLUMNS.
shopt -s checkwinsize

# If set, the pattern "**" used in a pathname expansion context will
# match all files and zero or more directories and subdirectories.
#shopt -s globstar

# make less more friendly for non-text input files, see lesspipe(1)
[ -x /usr/bin/lesspipe ] && eval "$(SHELL=/bin/sh lesspipe)"

# enable programmable completion features (you don't need to enable
# this, if it's already enabled in /etc/bash.bashrc and /etc/profile
# sources /etc/bash.bashrc).
if ! shopt -oq posix; then
  if [ -f /usr/share/bash-completion/bash_completion ]; then
    . /usr/share/bash-completion/bash_completion
  elif [ -f /etc/bash_completion ]; then
    . /etc/bash_completion
  fi
fi

[ -f "$DOT_HOME/bash/variables.sh" ] && source "$DOT_HOME/bash/variables.sh"
[ -f "$DOT_HOME/bash/functions.sh" ] && source "$DOT_HOME/bash/functions.sh"
[ -f "$DOT_HOME/bash/aliases.sh" ] && source "$DOT_HOME/bash/aliases.sh"
[ -f "$HOME/.bash_aliases" ] && source "$HOME/.bash_aliases"
[ -f "$DOT_HOME/shell/rc.sh" ] && source "$DOT_HOME/shell/rc.sh"

if command -v wt >/dev/null 2>&1; then eval "$(command wt config shell init bash)"; fi
