if [ "$(uname -s)" != "Linux" ] || [ ! -f /etc/os-release ]; then
   return
fi

. /etc/os-release
case "$ID" in
    ubuntu|pop)
        sudo apt update
        sudo apt install -y ca-certificates curl
        sudo install -m 0755 -d /etc/apt/keyrings
        if [ ! -f /etc/apt/keyrings/docker.asc ]; then
            sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
            sudo chmod a+r /etc/apt/keyrings/docker.asc
        fi
        
        if [ ! -f /etc/apt/sources.list.d/docker.sources ]; then
            sudo tee /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF
        fi
        sudo apt update
        # libicu carries its version in the package name (libicu74 on 24.04, libicu78 on 26.04), so pick the newest
        icu=$(apt-cache search --names-only '^libicu[0-9]+$' | cut -d' ' -f1 | sort -V | tail -n 1)
        sudo apt install -y \
            build-essential clang \
            libc6 libgcc-s1 libstdc++6 zlib1g zlib1g-dev libgssapi-krb5-2 \
            "$icu" libssl3t64 tzdata \
            unzip zstd \
            docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
        unset icu
        sudo usermod -aG docker "$USER"
        if [ "$ID" = "pop" ]; then
            sudo apt install -y zst
        fi
        ;;
    debian)
        sudo apt-get update
        icu=$(apt-cache search --names-only '^libicu[0-9]+$' | cut -d' ' -f1 | sort -V | tail -n 1)
        sudo apt-get install -y \
            build-essential clang zlib1g-dev \
            ca-certificates libssl3t64 \
            libc6 libgcc-s1 zlib1g libstdc++6 libgssapi-krb5-2 \
            "$icu" tzdata
        unset icu
        ;;
    fedora)
        sudo dnf install -y \
            clang zlib-devel \
            glibc libgcc libstdc++ krb5-libs zlib \
            ca-certificates openssl-libs \
            libicu tzdata
        ;;
    rocky|centos|rhel)
        sudo dnf install -y \
            clang zlib-devel \
            glibc libgcc libstdc++ zlib krb5-libs \
            ca-certificates openssl-libs \
            libicu tzdata
        ;;
    *)
        echo "Unsupported linux distro: $ID"
        ;;
esac