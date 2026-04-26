dc () {
  local compose_dir compose_cmd
	local compose_file="${COMPOSE_FILE:-$DEFAULT_COMPOSE_FILE}"
	compose_dir="$(dirname "$compose_file")"
	local env_file="${ENV_FILE:-$compose_dir/.env}"
	local project_dir="${PROJECT_DIR:-$compose_dir}"
	compose_cmd=(docker compose -f "$compose_file" --project-directory "$project_dir" --env-file "$env_file")
	if [[ "$1" == "rebuild" && -n "$2" ]]
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

flush_dns() {
    sudo killall -HUP mDNSResponder
    echo 'mDNS cache flushed'
}

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

zmodload zsh/langinfo

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

url_decode() {
  local encoded="${1//+/ }"
  printf '%b' "$(echo "$encoded" | sed 's/%\([0-9A-Fa-f][0-9A-Fa-f]\)/\\x\1/g')"
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

download_file() {
  local url="$1"
  local filename="${2:-$(_resolve_filename "$url")}"  # fallback to url filename if not provided
  local output="$HOME/Downloads/$filename"
  #local -n result=${@: -1}

  #echo "⬇️  Downloading $filename..."
  curl -SL --progress-bar "$url" -o "$output"

  #result="$output"  # return the path
  echo "$output"
}

install_font() {
  local url="$1"
  local font_dir
  if [[ "$(uname)" == "Darwin" ]]; then
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

install_mac_app() {
  local url="$1"
  local checksum="$2"

  if [ -z "$url" ]; then
    echo "⚠️  Missing url"
    return 1
  fi

  local filename
  filename=$(_resolve_filename "$url")
  local filepath="$HOME/Downloads/$filename"
  # Check if already downloaded and checksum matches
  if [ -f "$filepath" ] && [ -n "$checksum" ]; then
    if _validate_checksum "$filepath" "$checksum"; then
      echo "⏭️  Already downloaded and checksum matches, skipping download"
    else
      echo "⚠️  Already downloaded but checksum mismatch, re-downloading..."
      rm -f "$filepath"
      filepath=$(download_file "$url" "$filename")
    fi
  else
    filepath=$(download_file "$url" "$filename")
  fi

  if [ ! -f "$filepath" ]; then
    echo "❌ Download failed: $url"
    return 1
  fi

  # Validate checksum if provided
  if [ -n "$checksum" ]; then
    if ! _validate_checksum "$filepath" "$checksum"; then
      echo "⏭️  Skipping $url due to checksum mismatch"
      rm -f "$filepath"
      return 1
    fi
  fi

  filename=$(basename "$filepath")
  case "$filename" in
    *.dmg) install_dmg "$filepath" ;;
    *.pkg) install_pkg "$filepath" ;;
    *.zip) install_zip "$filepath" ;;
    *)   echo "⚠️  Unknown type: $filename, skipping" ;;
  esac
}

install_dmg() {
  local app pkg
  local dmg_path="$1"
  local mount_point
  mount_point=$(hdiutil attach "$dmg_path" | grep '/Volumes/' | sed 's|.*\(/Volumes/.*\)|\1|')

  # Handle .app inside dmg
  app=$(find "$mount_point" -name "*.app" -maxdepth 1 | head -1)

  # Handle .pkg inside dmg
  pkg=$(find "$mount_point" -name "*.pkg" -maxdepth 1 | head -1)

  if [ -n "$app" ]; then
    cp -R "$app" ~/Applications/
    xattr -rd com.apple.quarantine ~/Applications/"$(basename "$app")"
    echo "✅ Installed $(basename "$app") to ~/Applications"
  elif [ -n "$pkg" ]; then
    install_pkg_file "$pkg"
  else
    echo "❌ No .app or .pkg found in $(_extract_filename "$url"), skipping"
  fi

  hdiutil detach "$mount_point"
  rm "$dmg_path"
}

install_pkg() {
  local pkg_path="$1"

  install_pkg_file "$pkg_path"
  rm "$pkg_path"
}

install_pkg_file() {
  local filename
  local pkg_path="$1"
  filename="$(_extract_filename "$pkg_path")"

  echo "📦 Installing $filename..."
  if installer -pkg "$pkg_path" -target CurrentUserHomeDirectory 2>/dev/null; then
    echo "✅ Installed $filename to user directory"
  else
    echo "❌ Failed to install $filename"
  fi
}

install_zip() {
  local zip_path="$1"
  local filename=$(_extract_filename "$zip_path")

  echo "📦 Extracting $filename..."
  unzip -q "$zip_path" -d /tmp/zip_extracted

  local app
  app=$(find /tmp/zip_extracted -name "*.app" -maxdepth 2 | head -1)

  if [ -n "$app" ]; then
    cp -R "$app" ~/Applications/
    echo "✅ Installed $(basename "$app") to ~/Applications"
  else
    echo "❌ No .app found in zip, skipping"
  fi

  rm -rf "$zip_path" /tmp/zip_extracted
}
