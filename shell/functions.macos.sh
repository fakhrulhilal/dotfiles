if [[ "$(uname)" != "Darwin" ]]; then
    return
fi

function set_apple_config_dictionary() {
    config_domain=$1
    config_key=$2
    dict_key=$3
    dict_field=${4:-enabled}
    dict_value=$5

    current_value="$(
      defaults read "$config_domain" "$config_key" |
      awk -v key="$dict_key" '
        $0 ~ key " =" {
            found=1
            sub("^[[:space:]]*" key " = *", "")
        }
        found {
            # Count braces
            for (i=1; i <= length($0); i++) {
                c = substr($0, i, 1)
                if (c == "{") depth++
                if (c == "}") depth--
            }
            if (found && depth == 0) {
                sub(/;[[:space:]]*$/, "")
                print
                exit
            }

            print
        }
      '
    )"
    patched="$(echo "$current_value" | sed "s/$dict_field = [^;]*;/$dict_field = $dict_value;/")"
    defaults write "$config_domain" "$config_key" -dict-add "$dict_key" "$patched"
}