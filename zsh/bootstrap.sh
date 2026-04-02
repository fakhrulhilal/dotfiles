#!/usr/bin/env bash

DOTNET_VERSIONS=(6 8 10)
DOTNET_INSTALL_SCRIPT="/tmp/dotnet-install.sh"


if ! command -v dotnet &>/dev/null; then
    echo "dotnet is not found, installing"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$DOTNET_INSTALL_SCRIPT"
    chmod +x "$DOTNET_INSTALL_SCRIPT"
    for version in "${DOTNET_VERSIONS[@]}"; do
        "$DOTNET_INSTALL_SCRIPT" --channel "${version}.0"
        echo "✅ .NET ${version} installed"
    done
    rm "$DOTNET_INSTALL_SCRIPT"
else
    echo "dotnet already installed: $(dotnet --version)"
    dotnet --list-sdks
fi

if [[ "$SHELL" == */zsh ]]; then
    echo "⬇️  Installing zap-zsh..."
    zsh <(curl -s https://raw.githubusercontent.com/zap-zsh/zap/master/install.zsh) --branch release-v1
    echo "✅ zap-zsh installed"
fi

curl https://mise.run | sh
