shell_name=""
if [ -n "$BASH_VERSION" ]; then
    shell_name="bash"
elif [ -n "$ZSH_VERSION" ]; then
    shell_name="zsh"
fi
export POSH_THEME="$DOT_HOME/config/ohmyposh-theme.yaml"
eval "$(mise activate $shell_name --shims)"
eval "$(oh-my-posh init $shell_name --config $POSH_THEME)"
eval "$(fnox activate $shell_name)"
unset shell_name
