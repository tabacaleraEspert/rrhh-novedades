#!/usr/bin/env bash
# ============================================================================
#  RRHHNovedades — provisión de infra prod en Azure (estándar Espert).
#  Idempotente: se puede correr varias veces. Crea/actualiza todo el stack.
#
#  Requisitos: az CLI logueado (az login) con permisos para crear recursos,
#  AcrPush en el ACR compartido y rol para asignar roles (Owner/UAA) en el RG.
#
#  Antes de correr: completá las variables de CONFIG de abajo.
# ============================================================================
set -euo pipefail

# --- CONFIG (completar) -----------------------------------------------------
SUBSCRIPTION="<SUB_ID>"
LOCATION="brazilsouth"
PROJECT="rrhh"
ENVIRONMENT="prod"

# Recursos COMPARTIDOS (rg-espert-shared)
ACR_NAME="<ACR_NAME>"                  # ej. acrespertshared (sin .azurecr.io)
LOG_ANALYTICS_ID="/subscriptions/${SUBSCRIPTION}/resourceGroups/rg-espert-shared/providers/Microsoft.OperationalInsights/workspaces/log-shared"

# Secretos de la app (se cargan en Key Vault, NO quedan en la config del Container App)
PG_ADMIN_USER="rrhhadmin"
PG_ADMIN_PASSWORD="$(openssl rand -base64 24)"   # password fuerte autogenerado
HUMAND_API_KEY="<HUMAND_API_KEY>"      # rotar antes de prod
TWILIO_ACCOUNT_SID="<TWILIO_ACCOUNT_SID>"
TWILIO_AUTH_TOKEN="<TWILIO_AUTH_TOKEN>" # rotar antes de prod
# ---------------------------------------------------------------------------

RG="rg-${PROJECT}-${ENVIRONMENT}"
MI_NAME="id-${PROJECT}-${ENVIRONMENT}"
IMAGE="rrhh-novedades"
TAG="$(git rev-parse --short HEAD 2>/dev/null || echo manual)"
ACR_LOGIN_SERVER="${ACR_NAME}.azurecr.io"
FULL_IMAGE="${ACR_LOGIN_SERVER}/${IMAGE}:${TAG}"

az account set --subscription "$SUBSCRIPTION"

echo "==> 1/6 Resource group $RG"
az group create -n "$RG" -l "$LOCATION" -o none

echo "==> 2/6 Managed Identity $MI_NAME"
az identity create -g "$RG" -n "$MI_NAME" -l "$LOCATION" -o none
MI_PRINCIPAL_ID="$(az identity show -g "$RG" -n "$MI_NAME" --query principalId -o tsv)"

echo "==> 3/6 AcrPull para la MI en el ACR compartido"
ACR_ID="$(az acr show -n "$ACR_NAME" --query id -o tsv)"
az role assignment create --assignee-object-id "$MI_PRINCIPAL_ID" --assignee-principal-type ServicePrincipal \
  --role AcrPull --scope "$ACR_ID" -o none 2>/dev/null || echo "   (rol ya existía)"

echo "==> 4/6 Build & push de la imagen ($FULL_IMAGE)"
az acr build --registry "$ACR_NAME" --image "${IMAGE}:${TAG}" --image "${IMAGE}:latest" --file Dockerfile .

echo "==> 5/6 Deploy del Bicep"
OUTPUTS="$(az deployment group create \
  --resource-group "$RG" \
  --template-file infra/main.bicep \
  --parameters \
      location="$LOCATION" project="$PROJECT" env="$ENVIRONMENT" \
      logAnalyticsWorkspaceId="$LOG_ANALYTICS_ID" \
      acrLoginServer="$ACR_LOGIN_SERVER" \
      containerImage="$FULL_IMAGE" \
      pgAdminUser="$PG_ADMIN_USER" \
      pgAdminPassword="$PG_ADMIN_PASSWORD" \
  --query properties.outputs -o json)"

KV_NAME="$(echo "$OUTPUTS" | jq -r .keyVaultName.value)"
PG_FQDN="$(echo "$OUTPUTS" | jq -r .pgFqdn.value)"
PG_DB="$(echo "$OUTPUTS" | jq -r .pgDatabase.value)"
APP_FQDN="$(echo "$OUTPUTS" | jq -r .containerAppFqdn.value)"

echo "==> 6/6 Secretos en Key Vault $KV_NAME"
# El connection string apunta al FQDN privado del Postgres (resuelve por la VNet). SSL obligatorio.
CONN="Host=${PG_FQDN};Database=${PG_DB};Username=${PG_ADMIN_USER};Password=${PG_ADMIN_PASSWORD};SSL Mode=Require;Trust Server Certificate=true"
az keyvault secret set --vault-name "$KV_NAME" --name "ConnectionStrings--Default" --value "$CONN" -o none
az keyvault secret set --vault-name "$KV_NAME" --name "Humand--ApiKey"            --value "$HUMAND_API_KEY" -o none
az keyvault secret set --vault-name "$KV_NAME" --name "Twilio--AccountSid"        --value "$TWILIO_ACCOUNT_SID" -o none
az keyvault secret set --vault-name "$KV_NAME" --name "Twilio--AuthToken"         --value "$TWILIO_AUTH_TOKEN" -o none

# Reiniciar la app para que tome los secretos recién cargados.
az containerapp revision restart -g "$RG" -n "ca-${PROJECT}-${ENVIRONMENT}" \
  --revision "$(az containerapp show -g "$RG" -n "ca-${PROJECT}-${ENVIRONMENT}" --query properties.latestRevisionName -o tsv)" -o none || true

echo ""
echo "✅ Listo. App: https://${APP_FQDN}"
echo "   Postgres admin password (guardalo en un lugar seguro / ya está en KV el connection string):"
echo "   $PG_ADMIN_PASSWORD"
