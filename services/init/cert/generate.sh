#!/bin/sh
# Generates root CA -> intermediate (server) CA -> wildcard user certificates into $CERT_ROOT, following
# https://dev.to/fakhrulhilal/creating-ssl-certificate-with-custom-ca-402e
# Every file is created only when it is missing, so remove a file (or the whole volume) to issue it again.
set -eu

CERT_ROOT=${CERT_ROOT:-/certs}
CONFIG=${CONFIG:-/init/openssl.cnf}
BASE_DOMAIN=${BASE_DOMAIN:-dev.local}
# comma separated sub domains, each one gets its own certificate for <sub>.$BASE_DOMAIN and *.<sub>.$BASE_DOMAIN
DOMAIN=${DOMAIN:-}
# macOS rejects TLS server certificates that are valid for more than 825 days
USER_DAYS=730
CA_DAYS=3650
# hex byte, must be at least CurrentMinimumAspNetCoreCertificateVersion of the installed .NET SDK (4 for .NET 10)
export ASPNET_CERT_VERSION="${ASPNET_CERT_VERSION:-06}"
export SAN=""

if [ -z "${CERT_PASSWORD:-}" ]; then
  echo "CERT_PASSWORD (or APP_PASSWORD) is required for the PFX file" >&2
  exit 1
fi

cd "$CERT_ROOT"

if [ ! -f root-CA.crt ]; then
  echo "Creating root CA"
  rm -f ca-bundle.crt
  openssl genrsa -out root-CA.key 4096
  openssl req -config "$CONFIG" -x509 -new -nodes -key root-CA.key -sha256 -days "$CA_DAYS" -extensions ca_cert \
    -subj "/C=ID/O=Dev Lab/CN=Dev Lab Root CA" -out root-CA.crt
fi

if [ ! -f server-CA.crt ]; then
  echo "Creating intermediate CA"
  openssl genrsa -out server-CA.key 4096
  openssl req -config "$CONFIG" -new -key server-CA.key -out server-CA.csr \
    -subj "/C=ID/O=Dev Lab/OU=Development/CN=Dev Lab Server CA"
  openssl x509 -extfile "$CONFIG" -extensions ica_cert -req -CA root-CA.crt -CAkey root-CA.key -CAcreateserial \
    -sha256 -days "$CA_DAYS" -in server-CA.csr -out server-CA.crt
fi

# issue <domain>: <domain>.key/.csr/.crt, .pem (with issuer) and .pfx for <domain>, *.<domain>, localhost and 127.0.0.1
issue() {
  SAN="DNS:*.$1,DNS:$1,DNS:localhost,IP:127.0.0.1"

  if [ ! -f "$1.crt" ]; then
    echo "Creating user certificate for $SAN"
    openssl genrsa -out "$1.key" 2048
    openssl req -config "$CONFIG" -new -key "$1.key" -out "$1.csr" \
      -subj "/C=ID/ST=Jakarta/O=Dev Lab/OU=Development/CN=*.$1"
    openssl x509 -extfile "$CONFIG" -extensions web_cert -req -CA server-CA.crt -CAkey server-CA.key -CAcreateserial \
      -sha256 -days "$USER_DAYS" -in "$1.csr" -out "$1.crt"
    rm -f "$1.pem" "$1.pfx"
  fi

  # certificate followed by its issuer: servers send both, so clients only have to trust the root CA
  [ -f "$1.pem" ] || cat "$1.crt" server-CA.crt > "$1.pem"

  if [ ! -f "$1.pfx" ]; then
    openssl pkcs12 -export -name "$1 Certificate" -inkey "$1.key" -in "$1.crt" -certfile server-CA.crt \
      -passout env:CERT_PASSWORD -out "$1.pfx"
  fi

  # the user certificate has to be readable by services that do not run as root
  chmod 644 "$1.key" "$1.crt" "$1.pem" "$1.pfx"
  openssl verify -CAfile root-CA.crt -untrusted server-CA.crt "$1.crt"
}

# public roots plus our root CA, for clients that take a single trust store (SSL_CERT_FILE)
[ -f ca-bundle.crt ] || cat /etc/ssl/cert.pem root-CA.crt > ca-bundle.crt

# CA keys stay private
chmod 600 root-CA.key server-CA.key
chmod 644 root-CA.crt server-CA.crt ca-bundle.crt

issue "$BASE_DOMAIN"
for name in $(echo "$DOMAIN" | tr ',' ' '); do
  issue "$name.$BASE_DOMAIN"
done