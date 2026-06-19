#!/usr/bin/env bash
# ============================================================================
#  RRHHNovedades — provisión de infra prod en Azure (estándar Espert).
#  Idempotente: se puede correr varias veces. Crea/actualiza todo el stack.
#
#  Requisitos: az CLI logueado (az login) con permisos para crear recursos y
#  asignar roles en la suscripción.
#
#  ACR y Log Analytics son propios del proyecto (igual que gastos/facturación;
#  el "shared" del estándar todavía no existe — es deuda futura de consolidación).
#
#  Antes de correr: completá las variables de CONFIG de abajo.
# ============================================================================
set -euo pipefail

# --- CONFIG (completar) -----------------------------------------------------
SUBSCRIPTION="15900c96-4a2d-493b-ab12-912d521b3113"
LOCATION="brazilsouth"
PROJECT="rrhh"
ENVIRONMENT="prod"
ACR_NAME="acrrrhhprod"                 # ACR del proyecto (se crea acá)

# Secretos de la app (se cargan en Key Vault, NO quedan en la config del Container App)
PG_ADMIN_USER="rrhhadmin"
PG_ADMIN_PASSWORD="$(openssl rand -base64 24)"   # password fuerte autogenerado
HUMAND_API_KEY="<HUMAND_API_KEY>"      # rotar antes de prod
TWILIO_ACCOUNT_SID="<TWILIO_ACCOUNT_SID>"
TWILIO_AUTH_TOKEN="<TWILIO_AUTH_TOKEN>" # rotar antes de prod
# ---------------------------------------------------------------------------

RG="rg-${PROJECT}-${ENVIRONMENT}"
IMAGE="rrhh-novedades"
TAG="$(git rev-parse --short HEAD 2>/dev/null || echo manual)"
FULL_IMAGE="${ACR_NAME}.azurecr.io/${IMAGE}:${TAG}"

az account set --subscription "$SUBSCRIPTION"

echo "==> 1/5 Resource group $RG"
az group create -n "$RG" -l "$LOCATION" -o none

echo "==> 2/5 ACR $ACR_NAME"
az acr create -n "$ACR_NAME" -g "$RG" --sku Basic --admin-enabled false -o none

echo "==> 3/5 Build & push de la imagen ($FULL_IMAGE)"
az acr build --registry "$ACR_NAME" --image "${IMAGE}:${TAG}" --image "${IMAGE}:latest" --file Dockerfile .

echo "==> 4/5 Deploy del Bicep (VNet, Postgres privado, KeyVault+MI, CAE, Container App)"
OUTPUTS="$(az deployment group create \
  --resource-group "$RG" \
  --name "${PROJECT}-${ENVIRONMENT}-deploy" \
  --template-file infra/main.bicep \
  --parameters \
      location="$LOCATION" project="$PROJECT" env="$ENVIRONMENT" \
      acrName="$ACR_NAME" \
      containerImage="$FULL_IMAGE" \
      pgAdminUser="$PG_ADMIN_USER" \
      pgAdminPassword="$PG_ADMIN_PASSWORD" \
  --query properties.outputs -o json)"

KV_NAME="$(echo "$OUTPUTS" | jq -r .keyVaultName.value)"
PG_FQDN="$(echo "$OUTPUTS" | jq -r .pgFqdn.value)"
PG_DB="$(echo "$OUTPUTS" | jq -r .pgDatabase.value)"
CA_NAME="$(echo "$OUTPUTS" | jq -r .containerAppName.value)"
APP_FQDN="$(echo "$OUTPUTS" | jq -r .containerAppFqdn.value)"

echo "==> 5/5 Secretos en Key Vault $KV_NAME"
# Darse permiso de datos sobre el KV (RBAC) por si el deployer no lo tiene.
MY_ID="$(az ad signed-in-user show --query id -o tsv)"
KV_ID="$(az keyvault show -n "$KV_NAME" --query id -o tsv)"
az role assignment create --assignee-object-id "$MY_ID" --assignee-principal-type User \
  --role "Key Vault Secrets Officer" --scope "$KV_ID" -o none 2>/dev/null || true
sleep 30  # propagación RBAC

# El connection string apunta al FQDN del Postgres (resuelve a IP privada por la VNet). SSL obligatorio.
CONN="Host=${PG_FQDN};Database=${PG_DB};Username=${PG_ADMIN_USER};Password=${PG_ADMIN_PASSWORD};SSL Mode=Require;Trust Server Certificate=true"
az keyvault secret set --vault-name "$KV_NAME" --name "ConnectionStrings--Default" --value "$CONN" -o none
az keyvault secret set --vault-name "$KV_NAME" --name "Humand--ApiKey"            --value "$HUMAND_API_KEY" -o none
az keyvault secret set --vault-name "$KV_NAME" --name "Twilio--AccountSid"        --value "$TWILIO_ACCOUNT_SID" -o none
az keyvault secret set --vault-name "$KV_NAME" --name "Twilio--AuthToken"         --value "$TWILIO_AUTH_TOKEN" -o none

# Reiniciar para que tome los secretos recién cargados (la 1ra revisión arrancó sin ellos).
REV="$(az containerapp show -g "$RG" -n "$CA_NAME" --query properties.latestRevisionName -o tsv)"
az containerapp revision restart -g "$RG" -n "$CA_NAME" --revision "$REV" -o none || true

echo ""
echo "✅ Listo. App: https://${APP_FQDN}"
echo "   Postgres admin password (también en el connection string del KV):"
echo "   $PG_ADMIN_PASSWORD"
