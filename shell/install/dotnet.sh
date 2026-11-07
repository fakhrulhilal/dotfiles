export DOTNET_ROOT="$HOME/.dotnet"
export DOTNETUP_ROOT="$HOME/.dotnetup"
export PATH="$DOTNETUP_ROOT:$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

if ! command -v dotnetup >/dev/null 2>&1; then
  echo "Installing dotnetup"
  curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash
fi

dotnetup env set none --dotnetup-on-path false
if [ -n "$DOT_MINIMAL" ]; then
  dotnetup sdk install 10.0 --install-path "$DOTNET_ROOT" && echo "✅ .NET SDK 10 installed"
else
  dotnetup sdk install 6.0 8.0 10.0 --install-path "$DOTNET_ROOT" && echo "✅ .NET SDK 6, 8 and 10 installed"
fi
