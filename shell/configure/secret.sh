: "${DOT_HOME:=$(dirname "$(dirname "$(dirname "$(printf '%s\n' "$0" | sed "s|^[^/]|$PWD/&|")")")")}"
. "$DOT_HOME/shell/functions.sh"

config_dir="$HOME/.config/fnox"
secret_git_repo="${SECRET_REPO}"
secret_dir="$HOME/.secret"
mkdir -p "$config_dir"
if [ ! -d "$secret_dir" ]; then
    if [ -z "$secret_git_repo" ]; then
        current_git_url=$(git remote get-url origin)
        new_git_url=$(printf '%s\n' "$current_git_url" | sed 's|[:/][^/][^/]*$|/secret.git|')
        if git ls-remote "$new_git_url" > /dev/null 2>&1; then
            echo "Using the same user git repo on $new_git_url as secret source"
            secret_git_repo=$new_git_url
        else
            echo "Trying git repo $new_git_url but not found, skip cloning secret repo"
        fi
    fi

    if [ -n "$secret_git_repo" ]; then
        echo "Cloning secret repo from $secret_git_repo to $secret_dir"
        git clone "$secret_git_repo" "$secret_dir"
    else
        echo "Skip configuring secret as no source found"
        exit 4
    fi
fi

if { [ -z "$SECRET_KEY" ] || [ "$SECRET_KEY" = "generate" ]; } \
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

if [ -f "$secret_dir/bootstrap.sh" ]; then
    . "$secret_dir/bootstrap.sh"
fi

unset config_dir secret_git_repo secret_dir

