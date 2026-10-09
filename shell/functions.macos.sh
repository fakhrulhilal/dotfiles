if [ "$(uname)" != "Darwin" ]; then
    return
fi

set_apple_config_dictionary() {
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

flush_dns() {
    sudo killall -HUP mDNSResponder
    echo 'mDNS cache flushed'
}

install_mac_app() {
  url="$1"
  checksum="$2"

  if [ -z "$url" ]; then
    echo "⚠️  Missing url"
    return 1
  fi

  # "meta: <line>" and "github: <line>" resolve the download URL first, see meta_parse
  kind="${url%%:*}"
  case "$kind" in
    meta|github)
      args=$(${kind}_parse "${url#*:}") || return 1
      url=$(eval "${kind}_asset $args") || return 1
      echo "🔎 Resolved $url"
      ;;
  esac

  filename=$(_resolve_filename "$url")
  filepath="$HOME/Downloads/$filename"
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
  dmg_path="$1"
  mount_point=$(diskutil image attach "$dmg_path" | grep '/Volumes/' | sed 's|.*\(/Volumes/.*\)|\1|')

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

  diskutil eject "$mount_point"
  rm "$dmg_path"
}

install_pkg() {
  pkg_path="$1"

  # Keep the package on failure, so it can still be installed with sudo
  install_pkg_file "$pkg_path" || return 1
  rm "$pkg_path"
}

install_pkg_file() {
  pkg_path="$1"
  filename="$(_extract_filename "$pkg_path")"

  if [ ! -f "$pkg_path" ]; then
    echo "❌ Package not found: $pkg_path"
    return 1
  fi

  echo "📦 Installing $filename..."
  log_file=$(mktemp -t install_pkg) || return 1

  # Current user domain needs no admin right, but only works for packages that allow it
  if installer -pkg "$pkg_path" -target CurrentUserHomeDirectory >"$log_file" 2>&1; then
    echo "✅ Installed $filename to user directory"
    rm -f "$log_file"
    return 0
  fi

  # Fallback: extract the payload ourselves, but only when the whole package fits in ~/Applications
  echo "⚠️  Installer refused user-level install, checking whether $filename can be extracted instead..."
  work_dir=$(mktemp -d -t install_pkg) || { rm -f "$log_file"; return 1; }
  if ! pkgutil --expand-full "$pkg_path" "$work_dir/pkg" >>"$log_file" 2>&1; then
    echo "❌ Failed to unpack $filename"
    sed 's/^/   /' "$log_file"
    rm -rf "$work_dir" "$log_file"
    return 1
  fi

  # A flat package has PackageInfo at its root, a distribution package has one per component.
  # Every component is a dependency of the app, so a single one that needs admin right blocks the install.
  find "$work_dir/pkg" -maxdepth 2 -name PackageInfo >"$work_dir/components"
  : >"$work_dir/apps"
  : >"$work_dir/blockers"
  while IFS= read -r info; do
    _check_pkg_component "$(dirname "$info")" "$work_dir/apps" >>"$work_dir/blockers"
  done <"$work_dir/components"

  if [ -s "$work_dir/blockers" ] || [ ! -s "$work_dir/apps" ]; then
    echo "❌ Impossible to install $filename without admin right:"
    if [ -s "$work_dir/blockers" ]; then
      sed 's/^/   /' "$work_dir/blockers"
    else
      echo "   no app bundle found in the package"
    fi
    echo "   Install it with: sudo installer -pkg \"$pkg_path\" -target /"
    rm -rf "$work_dir" "$log_file"
    return 1
  fi

  # Copy everything into a staging folder first, so a failed copy leaves existing apps untouched
  mkdir -p "$HOME/Applications"
  staging=$(mktemp -d "$HOME/Applications/.install_pkg.XXXXXX") || { rm -rf "$work_dir" "$log_file"; return 1; }
  while IFS= read -r app; do
    if ! ditto "$app" "$staging/$(basename "$app")" >>"$log_file" 2>&1; then
      echo "❌ Failed to copy $(basename "$app") from $filename"
      sed 's/^/   /' "$log_file"
      rm -rf "$staging" "$work_dir" "$log_file"
      return 1
    fi
  done <"$work_dir/apps"

  for app in "$staging"/*.app; do
    app_name=$(basename "$app")
    rm -rf "${HOME:?}/Applications/$app_name"
    mv "$app" "$HOME/Applications/$app_name"
    xattr -rd com.apple.quarantine "$HOME/Applications/$app_name" 2>/dev/null
    echo "✅ Installed $app_name to ~/Applications"
  done

  rm -rf "$staging" "$work_dir" "$log_file"
}

# Check whether one expanded package component can be installed by copying it into ~/Applications.
# Prints the reason when it can't, otherwise appends its app bundles to the file in $2.
_check_pkg_component() {
  component="$1"
  apps_file="$2"
  name=$(basename "$component" .pkg)
  [ "$name" = pkg ] && name="$filename"
  location=$(sed -n 's/.*install-location="\([^"]*\)".*/\1/p' "$component/PackageInfo" | head -1)
  location="${location%/}"

  if [ -n "$(find "$component/Scripts" -mindepth 1 2>/dev/null | head -1)" ]; then
    echo "$name: has install scripts, which run as root"
    return
  fi

  [ -d "$component/Payload" ] || return

  # Prune at the first .app so helper apps nested inside a bundle are not listed on their own
  find "$component/Payload" -mindepth 1 \( -name "*.app" -type d -prune -print \) -o \( ! -type d -print \) \
    >"$component.items"
  while IFS= read -r item; do
    target="$location/${item#"$component/Payload/"}"
    case "$target" in
      /Applications/*.app) echo "$item" >>"$apps_file" ;;
      *)
        echo "$name: installs into ${target%/*}"
        return
        ;;
    esac
  done <"$component.items"
}

install_zip() {
  zip_path="$1"
  filename=$(_extract_filename "$zip_path")

  echo "📦 Extracting $filename..."
  unzip -q "$zip_path" -d /tmp/zip_extracted

  app=$(find /tmp/zip_extracted -name "*.app" -maxdepth 2 | head -1)

  if [ -n "$app" ]; then
    cp -R "$app" ~/Applications/
    echo "✅ Installed $(basename "$app") to ~/Applications"
  else
    echo "❌ No .app found in zip, skipping"
  fi

  rm -rf "$zip_path" /tmp/zip_extracted
}
