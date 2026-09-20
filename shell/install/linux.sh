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
        sudo apt install -y \
            build-essential \
            libc6 libstdc++6 zlib1g libgssapi-krb5-2 \
            unzip zstd \
            docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
        sudo usermod -aG docker $USER
        newgrp docker
        if [ "$ID" = "pop" ]; then
            sudo apt install -y \
                libgcc-s1 clang zlib1g-dev \
                libicu74 tzdata \
                libssl3t64 \
                zst
        fi
        if [ "$ID" = "ubuntu" ]; then
            sudo apt install -y \
                libgcc1 \
                libicu76 \
                libssl3
        fi
        ;;
    debian)
        sudo apt-get install -y \
            ca-certificates libssl3t64 \
            libc6 libgcc-s1 zlib1g libstdc++6 libgssapi-krb5-2 \
            libicu76 tzdata
        ;;
    fedora)
        sudo dnf install -y \
            glibc libgcc libstdc++ krb5-libs zlib \
            ca-certificates openssl-libs \
            libicu tzdata
        ;;
    rocky|centos|rhel)
        sudo dnf install \
            glibc libgcc libstdc++ zlib krb5-libs \
            ca-certificates openssl-libs \
            libicu tzdata
        ;;
    *)
        echo "Unsupported linux distro: $ID"
        ;;
esac