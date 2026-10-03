encode_b64() {
    printf "%s" "$1" | base64
}

urlencode() {
    printf '%s' "$1" | awk '
    BEGIN {
        for (i = 0; i <= 255; i++) ord[sprintf("%c", i)] = i
        safe = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.~_-"
        for (i = 1; i <= length(safe); i++) is_safe[substr(safe, i, 1)] = 1
    }
    {
        out = ""
        for (i = 1; i <= length($0); i++) {
            c = substr($0, i, 1)
            if (c in is_safe)
                out = out c
            else
                out = out sprintf("%%%02X", ord[c])
        }
        print out
    }'
}

build_url() {
    # parameter name, e.g. "kafka"
    name_uc=$(printf "%s" "$1" | tr '[:lower:]' '[:upper:]')

    # resolve env vars
    eval scheme="\${${name_uc}_SCHEME:-$1}"
    eval host="\${${name_uc}_HOST}"
    eval port="\${${name_uc}_PORT}"
    eval username="\${${name_uc}_USERNAME}"
    eval password="\${${name_uc}_PASSWORD}"
    eval namespace="\${${name_uc}_NAMESPACE}"

    # base64 encode only when present
    # build auth segment
    auth=""
    if [ -n "$username" ] && [ -n "$password" ]; then
      auth="$(urlencode "$username"):$(urlencode "$password")@"
    elif [ -n "$username" ]; then
      auth="$(urlencode "$username")@"
    elif [ -n "$password" ]; then
      auth="$(urlencode ":$password")@"
    fi

    # build host:port
    hostport="$host"
    [ -n "$port" ] && hostport="${host}:${port}"

    # build namespace
    ns=""
    [ -n "$namespace" ] && ns="/${namespace}"

    # final URL
    printf "%s://%s%s%s\n" "$scheme" "$auth" "$hostport" "$ns"
}

set_export_variable() {
    var_name=$1
    var_value=$2
    shell_config=$3

    tmp_file="$shell_config.tmp"

    if [ -f "$shell_config" ]; then
        if grep -q "^export $var_name=" "$shell_config"; then
            # POSIX-safe in-place edit
            sed "s|^export $var_name=.*|export $var_name=\"$var_value\"|" \
                "$shell_config" > "$tmp_file" &&
            mv "$tmp_file" "$shell_config"
        else
            printf 'export %s="%s"\n' "$var_name" "$var_value" >> "$shell_config"
        fi
    else
        printf 'export %s="%s"\n' "$var_name" "$var_value" > "$shell_config"
    fi
}

relink() {
    local source="$1"
    local target="$2"

    if [ -f "$target" ] && [ "$(realpath "$source")" = "$(realpath "$target")" ]; then
        return
    fi

    if [ -f "$target" ]; then
        mv "$target" "${target}.bak"
    fi
    ln -sf "$source" "$target"
}

dc () {
  local compose_dir compose_cmd
	local compose_file="${COMPOSE_FILE:-$DEFAULT_COMPOSE_FILE}"
	compose_dir="$(dirname "$compose_file")"
	local env_file="${ENV_FILE:-$compose_dir/.env}"
	local project_dir="${PROJECT_DIR:-$compose_dir}"
	compose_cmd=(docker compose -f "$compose_file" --project-directory "$project_dir" --env-file "$env_file")
	if [[ "$1" = "rebuild" && -n "$2" ]]
	then
		local service_name="$2"
		local image_name="$3"
		if [ -z $image_name ]
		then
			image_name=$("${compose_cmd[@]}" config | yq ".services[\"${service_name}\"].image")
		fi
		"${compose_cmd[@]}" stop "$service_name"
		"${compose_cmd[@]}" rm "$service_name" -f
		docker rmi "$image_name" -f
		"${compose_cmd[@]}" build "$service_name" --no-cache
	else
		"${compose_cmd[@]}" "$@"
	fi
}

alias uuid='uuidgen | tr "[:upper:]" "[:lower:]"'

project_run() {
    if [[ $# -lt 1 ]]; then
        echo "Usage: project_run <project.csproj> [env_file1] [env_file2] ..."
        return 1
    fi

    local csproj_file="$1"
    shift

    # Check if .csproj file exists
    if [[ ! -f "$csproj_file" ]]; then
        echo "Error: Project file '$csproj_file' not found"
        return 1
    fi

    # Source each env file
    for env_file in "$@"; do
        if [[ -f "$env_file" ]]; then
            echo "Sourcing $env_file..."
            set -a  # automatically export all variables
            source "$env_file"
            set +a
        else
            echo "Warning: Environment file '$env_file' not found, skipping..."
        fi
    done

    # Run the dotnet project
    echo "Running dotnet project: $csproj_file"
    dotnet run --project "$csproj_file"
}

_extract_filename() {
  local url="$1"
  local clean_url="${url%%\?*}"  # strip query params
  basename "$(url_decode "$clean_url")"
}

_resolve_filename() {
  local url="$1"

  # Try Content-Disposition first
  local header disposition
  header=$(curl -sIL "$url")
  disposition=$(echo "$header" | grep -i "content-disposition" | tail -1 | sed -E 's/.*filename="?([^";&]+)"?.*/\1/' | tr -d '\r')

  if [ -n "$disposition" ]; then
    _extract_filename "$disposition"
    return
  fi

  # Try location header (follow redirects, grab last location)
  local location
  location=$(curl -sI "$url" | grep -i "^location:" | tail -1 | awk '{print $2}' | tr -d '\r')

  if [ -n "$location" ]; then
    _extract_filename "$location"
    return
  fi

  # Fallback to URL-based filename
  _extract_filename "$url"
}

_parse_checksum() {
  local checksum="$1"
  # format: md5:abc123, sha1:abc123, sha256:abc123
  local algo="${checksum%%:*}"
  local hash="${checksum##*:}"
  echo "$algo $hash"
}

_validate_checksum() {
  local file="$1"
  local checksum="$2"

  local algo hash
  read -r algo hash <<< "$(_parse_checksum "$checksum")"

  local actual
  case "$algo" in
    md5)    actual=$(md5 -q "$file") ;;
    sha1)   actual=$(shasum -a 1 "$file" | awk '{print $1}') ;;
    sha256) actual=$(shasum -a 256 "$file" | awk '{print $1}') ;;
    *)
      echo "⚠️  Unknown checksum algorithm: $algo, skipping validation"
      return 0
      ;;
  esac

  if [ "$actual" = "$hash" ]; then
    echo "✅ Checksum valid ($algo)"
    return 0
  else
    echo "❌ Checksum mismatch ($algo)"
    echo "   expected: $hash"
    echo "   actual:   $actual"
    return 1
  fi
}

download_file() {
  local url="$1"
  local filename="${2:-$(_resolve_filename "$url")}"  # fallback to url filename if not provided
  if [ -d "$HOME/Downloads" ]; then
    local output="$HOME/Downloads/$filename"
  else
    local output="/tmp/$filename"
  fi
  #local -n result=${@: -1}

  #echo "⬇️  Downloading $filename..."
  curl -SL --progress-bar "$url" -o "$output"

  #result="$output"  # return the path
  echo "$output"
}

url_decode() {
  local encoded="${1//+/ }"
  printf '%b' "$(echo "$encoded" | sed 's/%\([0-9A-Fa-f][0-9A-Fa-f]\)/\\x\1/g')"
}

install_font() {
  local url="$1"
  local font_dir
  if [[ "$(uname)" = "Darwin" ]]; then
    font_dir="$HOME/Library/Fonts"
  else
    font_dir="$HOME/.local/share/fonts"
  fi
  local font_path filename
  font_path=$(download_file "$url")
  filename=$(_extract_filename "$font_path")
  local font_name="${filename%.*}"
  mkdir -p "$font_dir"

  echo "🔠 Installing $font_name to $font_dir..."
  unzip -q -o "$font_path" -d "$font_dir"
  if command -v fc-cache &>/dev/null; then
    fc-cache -f "$font_dir"
  else
    echo "You might need to relogin for font to take effect"
  fi

  rm -rf "$font_path"
  echo "✅ $font_name installed"
}

# Quotes $1 for the shell, so it survives `eval`
_shell_quote() {
  printf "'%s'" "$(printf '%s' "$1" | sed "s/'/'\\\\''/g")"
}

# Replaces every "{name}" in $1 with $3, where $2 is the name
_meta_subst() (
  rest=$1
  out=""
  while :; do
    case "$rest" in
      *"{$2}"*) ;;
      *) break ;;
    esac

    out="$out${rest%%"{$2}"*}$3"
    rest=${rest#*"{$2}"}
  done

  printf '%s' "$out$rest"
)

# Prints the value of "name=value" lines in $1 for the name $2
_meta_get() {
  printf '%s\n' "$1" | sed -n "s/^$2=//p" | tail -1
}

# Expands $NAME and ${NAME} in a header, fails when a referenced variable is empty (the header is then skipped).
# Only plain variable names are expanded, nothing is evaluated.
_meta_header() (
  rest=$1
  out=""
  while :; do
    case "$rest" in
      *'$'*) ;;
      *) break ;;
    esac

    out="$out${rest%%\$*}"
    rest=${rest#*\$}
    case "$rest" in
      "{"*) name=${rest#\{}; name=${name%%\}*}; rest=${rest#*\}} ;;
      *) name=$(printf '%s' "$rest" | sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\).*/\1/p'); rest=${rest#"$name"} ;;
    esac
    case "$name" in
      ''|[!A-Za-z_]*|*[!A-Za-z0-9_]*) exit 1 ;;
    esac

    eval "value=\${$name-}"
    [ -n "$value" ] || exit 1
    out="$out$value"
  done

  printf '%s' "$out$rest"
)

# Turns an asset line into shell-quoted meta_asset/github_asset options, use it with `eval`.
# Line: <url> (name=value,...) [name: 'selector'] [header: 'Name: value'] # comment
#   <url>                 --url <url>
#   (name=value,...)      --<name> <value>
#   [header: '...']       --header '...'
#   [name: 'selector']    --query-<name> 'selector'
# Example:
#   args=$(meta_parse "https://example.com/feed.json (arch=macM1) [url: '.downloads.{arch}.link']")
#   eval "meta_asset $args"
meta_parse() (
  q="'"
  # one line, without the trailing "# comment" (a "#" inside a quoted selector is kept)
  spec=$(printf '%s' "$1" | tr '\n' ' ' | sed "s/[[:space:]]#[^]$q]*\$//")
  spec=${spec#"${spec%%[![:space:]]*}"}
  src=${spec%%[[:space:]]*}
  rest=${spec#"$src"}
  rest=${rest#"${rest%%[![:space:]]*}"}
  if [ -z "$src" ]; then
    echo "❌ Missing URL in: $1" >&2
    exit 1
  fi

  printf -- '--url %s' "$(_shell_quote "$src")"
  case "$rest" in
    "("*)
      opts=${rest%%")"*}
      printf '%s' "${opts#"("}" | tr ',' '\n' | tr -d ' ' | while IFS= read -r pair || [ -n "$pair" ]; do
        case "$pair" in
          ?*=*) printf ' --%s %s' "${pair%%=*}" "$(_shell_quote "${pair#*=}")" ;;
          ?*) echo "⚠️  Ignoring option without value: $pair" >&2 ;;
        esac
      done
      ;;
  esac

  printf '%s' "$rest" | awk -v q="$q" '{
    s = $0
    re = "\\[[A-Za-z_][A-Za-z0-9_]*:[ ]*" q "[^" q "]*" q "\\]"
    while (match(s, re)) {
      m = substr(s, RSTART + 1, RLENGTH - 2)
      name = substr(m, 1, index(m, ":") - 1)
      sel = substr(m, index(m, q) + 1)
      print name "\t" substr(sel, 1, length(sel) - 1)
      s = substr(s, RSTART + RLENGTH)
    }
  }' | while IFS="$(printf '\t')" read -r name selector; do
    if [ "$name" = header ]; then
      printf ' --header %s' "$(_shell_quote "$selector")"
    else
      printf ' --query-%s %s' "$name" "$(_shell_quote "$selector")"
    fi
  done
  printf '\n'
)

# Resolves a download URL from a metadata document (JSON, YAML or XML) using yq queries.
# Usage: meta_asset --url <url> [--header 'Name: value']... [--<name> <value>]... --query-<name> <selector>...
#   --url              metadata document to download
#   --header           request header; $NAME/${NAME} are expanded, and the header is skipped when one is empty
#   --<name>           variable, available to queries as {name}. os, version, arch and format default to
#                      macos, latest, arm64 and dmg, like github_asset
#   --query-<name>     evaluated in the given order with `yq`, result stored as {name}:
#                      - when the variable is given and isn't "latest", the query is skipped
#                      - when it's "latest", the biggest version among the results wins
#                      - otherwise the first non-null result wins
#   --query-url is required, its value is the output.
meta_asset() (
  nl='
'
  tab=$(printf '\t')
  src=""
  opts=""
  headers=""
  queries=""
  while [ $# -gt 0 ]; do
    if [ $# -lt 2 ]; then
      echo "❌ Missing value for $1" >&2
      exit 1
    fi

    case "$1" in
      --url) src=$2 ;;
      --header) headers="$headers$2$nl" ;;
      --query-?*) name=${1#--query-} ;;
      --?*) name=${1#--} ;;
      *) echo "❌ Unknown argument: $1" >&2; exit 1 ;;
    esac
    case "$1" in
      --url|--header) ;;
      *)
        case "$name" in
          [!A-Za-z_]*|*[!A-Za-z0-9_]*) echo "❌ Invalid name: $1" >&2; exit 1 ;;
        esac
        case "$1" in
          # stored one per line, so a multi-line query is folded (yq ignores the whitespace)
          --query-*) queries="$queries$name$tab$(printf '%s' "$2" | tr '\n\t' '  ')$nl" ;;
          *) opts="$opts$name=$2$nl" ;;
        esac
        ;;
    esac
    shift 2
  done

  if [ -z "$src" ]; then
    echo "❌ Missing --url" >&2
    exit 1
  fi

  case "$nl$queries" in
    *"${nl}url$tab"*) ;;
    *) echo "❌ Missing --query-url for $src" >&2; exit 1 ;;
  esac

  for pair in os=macos version=latest arch=arm64 format=dmg; do
    [ -n "$(_meta_get "$opts" "${pair%%=*}")" ] || opts="$opts$pair$nl"
  done

  tmp=$(mktemp)
  trap 'rm -f "$tmp"' EXIT
  set -- -fsSL -o "$tmp"
  while IFS= read -r header; do
    [ -n "$header" ] || continue
    header=$(_meta_header "$header") && set -- "$@" -H "$header"
  done <<EOF
$headers
EOF
  if ! curl "$@" "$src"; then
    echo "❌ Failed to fetch metadata: $src" >&2
    exit 1
  fi

  case "$(sed -n '/[^[:space:]]/{s/^[[:space:]]*//;p;q;}' "$tmp" | cut -c1)" in
    "{"|"[") input=json ;;
    "<") input=xml ;;
    *) input=yaml ;;
  esac

  # variables given as "latest" are resolved by their query, so they aren't substituted as is
  vars=$(printf '%s' "$opts" | grep -v '=latest$')
  while IFS="$tab" read -r name selector; do
    [ -n "$name" ] || continue

    given=$(_meta_get "$opts" "$name")
    if [ -n "$given" ] && [ "$given" != latest ]; then
      continue
    fi

    expr=$selector
    while IFS= read -r pair; do
      [ -n "$pair" ] && expr=$(_meta_subst "$expr" "${pair%%=*}" "${pair#*=}")
    done <<VARS
$vars
VARS

    values=$(yq -p "$input" -oy "$expr" "$tmp" | grep -v '^null$' | grep -v '^[[:space:]]*$')
    if [ "$given" = latest ]; then
      value=$(printf '%s\n' "$values" | sort -t. -k1,1n -k2,2n -k3,3n -k4,4n -k5,5n | tail -1)
    else
      value=$(printf '%s\n' "$values" | head -1)
    fi
    if [ -z "$value" ]; then
      echo "❌ No value for --query-$name '$expr' in $src" >&2
      exit 1
    fi

    vars=$(printf '%s\n%s=%s' "$vars" "$name" "$value")
  done <<EOF
$queries
EOF

  printf '%s\n' "$(_meta_get "$vars" url)"
)

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
. "$script_dir/functions.github.sh"
. "$script_dir/functions.tailscale.sh"
. "$script_dir/functions.macos.sh"