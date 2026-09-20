if [ "$(uname -s)" != "Linux" ] || [ ! -f /etc/os-release ]; then
   return
fi

. /etc/os-release
case "$ID" in
    pop)
        sudo apt install -y \
            build-essential \
            libc6 libgcc-s1 libstdc++6 clang zlib1g zlib1g-dev libgssapi-krb5-2 \
            libicu74 tzdata \
            libssl3t64 \
            unzip zstd zst
        ;;
    ubuntu)
        sudo apt install -y \
            build-essential \
            libc6 libgcc1 libstdc++6 zlib1g libgssapi-krb5-2 \
            libicu76 \
            libssl3 \
            unzip zstd
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