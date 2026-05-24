#!/usr/bin/env bash

if [ -z "$BASH_VERSION" ]; then
    return
fi

rc_file="$HOME/.bashrc"
relink "$DOT_HOME/bash/profile.sh" "$HOME/.bash_profile"
relink "$DOT_HOME/bash/rc.sh" "$rc_file"
cat >> "$HOME/.env" <<EOF

# Added by dotfiles bootstrapper
export DOT_HOME="$DOT_HOME"
export BASH_EXT="\$DOT_HOME/bash"
EOF

source "$rc_file"
