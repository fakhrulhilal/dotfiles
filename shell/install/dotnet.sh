if command -v dotnet &>/dev/null; then
    echo "dotnet already installed: $(dotnet --version)"
    dotnet --list-sdks
    return
fi

DOTNET_VERSIONS=(6 8 10)
DOTNET_INSTALL_SCRIPT="/tmp/dotnet-install.sh"
DOTNET_ROOT="$HOME/.dotnet"
echo "Installing .NET"
curl -sSL https://dot.net/v1/dotnet-install.sh -o "$DOTNET_INSTALL_SCRIPT"
chmod +x "$DOTNET_INSTALL_SCRIPT"
for version in "${DOTNET_VERSIONS[@]}"; do
    "$DOTNET_INSTALL_SCRIPT" --channel "${version}.0"
    "$DOTNET_INSTALL_SCRIPT" --channel "${version}.0" --runtime dotnet
    echo "✅ .NET ${version} installed"
done
rm "$DOTNET_INSTALL_SCRIPT"
