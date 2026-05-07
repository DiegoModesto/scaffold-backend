#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SCRIPT_DIR/../src/EntryPoints/Auth.API/dev-certs"
mkdir -p "$OUT_DIR"
openssl req -x509 -newkey rsa:2048 -days 365 -nodes \
  -keyout "$OUT_DIR/netsuite-saml.key" \
  -out "$OUT_DIR/netsuite-saml.crt" \
  -subj "/CN=auth.local/O=BaseProjectScaffold/C=BR"
openssl pkcs12 -export -out "$OUT_DIR/netsuite-saml.pfx" \
  -inkey "$OUT_DIR/netsuite-saml.key" \
  -in "$OUT_DIR/netsuite-saml.crt" \
  -password pass:dev-saml-password
echo "Dev SAML cert at $OUT_DIR/netsuite-saml.pfx (password: dev-saml-password)"
