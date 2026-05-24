# Guard: macOS only
if [ -n "$SKIP_INSTALL_MAC_APPS" ]; then
    echo "⏭️  Skipping DMG installs as requested"
    exit 0
fi

if [[ "$(uname)" != "Darwin" ]]; then
  echo "⏭️  Not macOS, skipping DMG installs"
  exit 0
fi

APP_LIST="$DOT_HOME/mac_app.txt"
if [ ! -f "$APP_LIST" ]; then
  echo "❌ app.txt not found at $APP_LIST"
  exit 1
fi

mkdir -p ~/Applications
source "$DOT_HOME/zsh/functions.zsh"
while IFS= read -r line || [ -n "$line" ]; do
  [[ -z "$line" || "$line" = \#* ]] && continue

  # Parse ';' separated fields
  url=$(echo "$line"      | cut -d';' -f1 | xargs)
  checksum=$(echo "$line" | cut -d';' -f2 | xargs)
  install_mac_app "$url" "$checksum"
done < "$APP_LIST"
