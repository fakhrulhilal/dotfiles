if ! command -v mise &>/dev/null; then
    echo "Installing mise"
    curl https://mise.run | sh
    eval "$($HOME/.local/bin/mise activate $CURRENT_SHELL)"
fi

# config.toml is the minimal setup. Each conf.d/<tier>.toml adds a tier on top of it:
#   DOT_MINIMAL=1             no tiers (small servers)
#   DOT_MISE_TIERS="shell ai" only these tiers, plus lang
#   neither                   every tier
# lang is a hard dependency outside minimal: the other tiers build cargo:, go:, npm: and pipx: tools with it.
mise_dir="$HOME/.config/mise"
mkdir -p "$mise_dir/conf.d"
relink "$DOT_HOME/config/mise/config.toml" "$mise_dir/config.toml"

if [ -n "$DOT_MINIMAL" ]; then
    mise_tiers=""
elif [ -n "$DOT_MISE_TIERS" ]; then
    mise_tiers="$(echo lang $DOT_MISE_TIERS | tr ' ' '\n' | sed '/^$/d' | sort -u)"
else
    mise_tiers="$(ls "$DOT_HOME/config/mise/conf.d" | sed -n 's/\.toml$//p')"
fi
# one space-separated line, padded so " $tier " matches whole names only
mise_tiers=" $(echo $mise_tiers | tr '\n' ' ') "

# drop tiers linked by an earlier run that are no longer selected
ls "$mise_dir/conf.d" | sed -n 's/\.toml$//p' | while read -r tier; do
    link="$mise_dir/conf.d/$tier.toml"
    [ -L "$link" ] || continue
    [ "$(readlink "$link")" = "$DOT_HOME/config/mise/conf.d/$tier.toml" ] || continue
    case "$mise_tiers" in *" $tier "*) ;; *) rm "$link" ;; esac
done

echo "$mise_tiers" | tr ' ' '\n' | while read -r tier; do
    [ -n "$tier" ] || continue
    if [ -f "$DOT_HOME/config/mise/conf.d/$tier.toml" ]; then
        relink "$DOT_HOME/config/mise/conf.d/$tier.toml" "$mise_dir/conf.d/$tier.toml"
    else
        echo "Unknown mise tier: $tier"
    fi
done
unset mise_dir mise_tiers

mise install
