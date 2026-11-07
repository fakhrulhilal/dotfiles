shell_name=""
if [ -n "$BASH_VERSION" ]; then
    shell_name="bash"
elif [ -n "$ZSH_VERSION" ]; then
    shell_name="zsh"
fi
export POSH_THEME="$DOT_HOME/config/ohmyposh-theme.yaml"
# mise would load the tier files as untrusted project config inside $DOT_HOME/config; they load through
# ~/.config/mise/conf.d instead
export MISE_IGNORED_CONFIG_PATHS="$DOT_HOME/config/mise/conf.d"
eval "$(mise activate $shell_name)"
eval "$(mise activate $shell_name --shims)"
eval "$(oh-my-posh init $shell_name --config $POSH_THEME)"
eval "$(fnox activate $shell_name)"
unset shell_name
