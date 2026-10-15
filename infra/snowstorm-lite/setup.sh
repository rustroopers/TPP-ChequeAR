#!/usr/bin/env bash
# Se corre UNA vez en la VM, desde esta carpeta. Genera .env (password de admin + key) y el
# certificado autofirmado (10 años), y levanta el stack. Imprime lo que va a Azure.
set -euo pipefail
cd "$(dirname "$0")"
umask 077
if [ ! -f .env ]; then
  printf 'ADMIN_PASSWORD=%s\nTERM_KEY=%s\nLITE_HEAP=2g\n' "$(openssl rand -hex 24)" "$(openssl rand -hex 32)" > .env
fi
if [ ! -f certs/cert.pem ]; then
  mkdir -p certs
  IP=$(curl -s https://ifconfig.me)
  openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=chequear-snowstorm" \
    -addext "subjectAltName=IP:${IP}" -keyout certs/key.pem -out certs/cert.pem 2>/dev/null
  chmod 644 certs/cert.pem   # nginx corre como root para leer la key, el cert es público
fi
docker compose create
# La imagen corre como uid 1000 y el volumen nuevo nace de root: sin esto Snowstorm Lite no
# puede crear lucene-index/data y entra en loop de reinicio.
docker run --rm -v "$(basename "$(pwd)")_lite-index:/idx" alpine chown -R 1000:1000 /idx
docker compose up -d
echo
echo "Terminology:CertSha256 = $(openssl x509 -in certs/cert.pem -noout -fingerprint -sha256 | cut -d= -f2)"
echo "Terminology:ApiKey     = (ver TERM_KEY en $(pwd)/.env)"
