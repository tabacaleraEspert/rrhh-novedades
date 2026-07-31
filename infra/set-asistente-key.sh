#!/usr/bin/env bash
# Carga la API key de OpenAI del asistente en Key Vault de prod.
# La lee de appsettings.secrets.local.json (gitignored) para no tipearla ni loguearla.
# Uso:  bash infra/set-asistente-key.sh
set -euo pipefail
cd "$(dirname "$0")/.."

KV_NAME="kv-rrhh-prod"
SECRETS_FILE="src/RRHHNovedades.Web/appsettings.secrets.local.json"

[ -f "$SECRETS_FILE" ] || { echo "✖ No existe $SECRETS_FILE"; exit 1; }

KEY="$(python3 -c "import json,sys;print(json.load(open('$SECRETS_FILE'))['Asistente']['ApiKey'])")"
[ -n "$KEY" ] || { echo "✖ Asistente.ApiKey vacía en $SECRETS_FILE"; exit 1; }

echo "==> Cargando Asistente--ApiKey en $KV_NAME (largo ${#KEY}, prefijo ${KEY:0:7}...)"
az keyvault secret set --vault-name "$KV_NAME" --name "Asistente--ApiKey" --value "$KEY" -o none

az keyvault secret show --vault-name "$KV_NAME" --name "Asistente--ApiKey" \
  --query "{creado:attributes.created, habilitado:attributes.enabled}" -o json

echo "✔ Secreto cargado. El asistente se habilita en el próximo arranque de la app."
