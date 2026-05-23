source "$DOT_HOME/shell/functions.sh"

zmodload zsh/langinfo

# Guard: macOS only
if [[ "$(uname)" != "Darwin" ]]; then
  echo "⏭️  Not macOS, skipping DMG installs"
  exit 0
fi

flush_dns() {
    sudo killall -HUP mDNSResponder
    echo 'mDNS cache flushed'
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
