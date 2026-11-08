# vim: set filetype=bash:
# Login shells only. The environment lives in ~/.bashrc, which non-login shells read too.
[ -f "$HOME/.bashrc" ] && source "$HOME/.bashrc"

# Added by OrbStack: command-line tools and integration
# This won't be added again if you remove it.
source ~/.orbstack/shell/init.bash 2>/dev/null || :
