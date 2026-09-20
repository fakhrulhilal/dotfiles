: "${DOT_HOME:=$(dirname "$(dirname "$(dirname "$(printf '%s\n' "$0" | sed "s|^[^/]|$PWD/&|")")")")}"
. "$DOT_HOME/shell/functions.sh"

config_dir="$HOME/.config/fnox"
secret_git_repo="${SECRET_REPO}"
secret_dir="$HOME/.secret"
mkdir -p "$config_dir"

if command -v fnox 2>&1 >/dev/null; then
    fnox deactivate 2>&1 >/dev/null
fi

if [ ! -d "$secret_dir" ]; then
    if [ -z "$secret_git_repo" ]; then
        current_git_url=$(git -C $DOT_HOME remote get-url origin)
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

if [ -z "$SECRET_KEY" ]; then
    if [ ! -f "$config_dir/age.txt" ] || [ "$SECRET_KEY" = "generate" ]; then
        echo "🔐 Generating age secret key to $config_dir/age.txt"
        public_key=$(age-keygen -o "$config_dir/age.txt" 2>&1 | sed 's/^[^:]*:[[:space:]]*//')
    elif [ -f "$config_dir/age.txt" ]; then
        echo "🔐 Getting age public & secret key from $config_dir/age.txt"
        public_key=$(grep 'public key' "$config_dir/age.txt" | sed 's/^[^:]*:[[:space:]]*//')
    fi
    SECRET_KEY=$(grep "AGE-SECRET-KEY" "$config_dir/age.txt")
    if [ ! -f "$secret_dir/fnox.toml" ]; then
        echo "🗂️ Defining fnox global config to $secret_dir/fnox.toml with public key $public_key"
        cat > "$secret_dir/fnox.toml" <<EOF
default_provider = "age"

[providers.age]
type = "age"
recipients = ["$public_key"]
EOF
    elif ! grep -Fq "$public_key" "$secret_dir/fnox.toml" ; then
        echo "🗂️ Storing public key ($public_key) to fnox global config in $secret_dir/fnox.toml"
        file="$secret_dir/fnox.toml"
        tmp="$file.tmp.$$"
        trap 'rm -f "$tmp"' EXIT HUP INT TERM
        cp -p "$file" "$tmp" # inherit mode/owner, then truncate on redirect

        echo "Age secret key has been generated and added to $secret_dir/fnox.toml."
        echo "You need to commit and push, then regenerate secrets to make it work."
        echo "Run: `fnox reencrypt --provider age` from other machines."
        echo "see onboarding guide: https://fnox.jdx.dev/guide/real-world-example.html#_6-onboard-a-teammate"
        echo "Press [enter] to continue"
        comment="added by script at $(date '+%Y-%m-%d %H:%M')"
        if awk -v r="$public_key" -v c="$comment" '
          /^[[:space:]]*\[/ {
            section = $0
            sub(/^[[:space:]]+/, "", section)
            sub(/[[:space:]]*(#.*)?$/, "", section)
          }
          !done && section == "[providers.age]" &&
          /^[[:space:]]*recipients[[:space:]]*=[[:space:]]*\[[[:space:]]*$/ {
            print
            if ((getline nextline) > 0) {
              indent = nextline
              sub(/[^[:space:]].*$/, "", indent)
              if (indent == "") indent = "    "
            } else {
              indent = "    "
              nextline = ""
            }
            entry = indent "\"" r "\","
            if (c != "") entry = entry " # " c
            print entry
            if (nextline != "") print nextline
            done = 1
            next
          }
          { print }
          END { exit done ? 0 : 1 }
        ' "$file" >"$tmp"; then
          mv "$tmp" "$file"
          trap - EXIT HUP INT TERM
        else
          printf '%s: no multi-line recipients array under [providers.age]\n' "$file" >&2
          exit 1
        fi
        unset file tmp comment
    fi
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

