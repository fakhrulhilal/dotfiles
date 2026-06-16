: "${DOT_HOME:=$(dirname "$(dirname "$(dirname "$(printf '%s\n' "$0" | sed "s|^[^/]|$PWD/&|")")")")}"
. "$DOT_HOME/shell/functions.sh"

config_dir="$HOME/.config/fnox"
secret_git_repo="${SECRET_REPO:-git@github.com:fakhrulhilal/secret.git}"
secret_dir="$HOME/.secret"
mkdir -p "$config_dir"
[ -d "$secret_dir" ] || git clone "$secret_git_repo" "$secret_dir"
if { [ -z "$SECRET_KEY" ] || [ "$SECRET_KEY" = "generate" ] } \
    && [ ! -f "$secret_dir/fnox.toml" ]; then
    echo "🔐 Generating age secret key to $config_dir/age.txt"
    [ -f "$config_dir/age.txt" ] && rm -f "$config_dir/age.txt"
    public_key=$(age-keygen -o "$config_dir/age.txt" 2>&1 | sed 's/^[^:]*:[[:space:]]*//')
    SECRET_KEY=$(grep "AGE-SECRET-KEY" "$config_dir/age.txt")
    echo "🗂️ Defining fnox global config to $secret_dir/fnox.toml with public key $public_key"
    cat > "$secret_dir/fnox.toml" <<EOF
default_provider = "age"

[providers]
age = { type = "age", recipients = ["$public_key"] }
EOF
elif [ -n "$SECRET_KEY" ] \
    && [ -f "$config_dir/age.txt" ] \
    && ! grep -Fxq "$SECRET_KEY" "$config_dir/age.txt" ; then
    echo "🔐 Storing age secret key to $config_dir/age.txt"
    echo "$SECRET_KEY" > "$config_dir/age.txt"
fi

relink "$secret_dir/fnox.toml" "$config_dir/config.toml"
relink "$config_dir/age.txt" "$HOME/.config/mise/age.txt"
set_export_variable "FNOX_AGE_KEY" "$SECRET_KEY" "$HOME/.zshenv"
set_export_variable "FNOX_AGE_KEY" "$SECRET_KEY" "$HOME/.env"
unset config_dir secret_git_repo secret_dir