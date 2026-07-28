#!/usr/bin/env bash
# ============================================================================
#  DDL manual contra la DB de PROD (pg-rrhh-prod / rrhhnovedades).
#  (EnsureCreated no altera tablas existentes; ver docs/DEPLOY-AZURE.md)
#
#  La DB vive en VNet privada sin acceso público: el SQL se ejecuta con un
#  Container Apps Job efímero DENTRO del entorno (cae-rrhh-prod), imagen
#  postgres, connection string referenciada directo desde Key Vault vía
#  Managed Identity (el password nunca sale de Azure). El job se borra al final.
#
#  Uso:  bash infra/run-ddl.sh 'ALTER TABLE "Tabla" ADD COLUMN IF NOT EXISTS ...;'
#        (requiere az login con permisos en rg-rrhh-prod; usar SQL idempotente)
# ============================================================================
set -euo pipefail

SQL="${1:?Uso: run-ddl.sh '<SQL>'}"
SUB="15900c96-4a2d-493b-ab12-912d521b3113"
RG="rg-rrhh-prod"
JOB="job-rrhh-ddl"
MI_ID="/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-rrhh-prod"
ENV_ID="/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.App/managedEnvironments/cae-rrhh-prod"
KV_SECRET="https://kv-rrhh-prod.vault.azure.net/secrets/ConnectionStrings--Default"

az account set --subscription "$SUB"

# Spec por YAML: el --command del CLI no acepta args que empiezan con "-" (el "-c" de sh
# se lo come argparse); con YAML command y args van separados y no hay ambigüedad.
YAML=$(mktemp /tmp/job-rrhh-ddl.XXXXXX.yaml)
trap 'rm -f "$YAML"' EXIT
cat > "$YAML" <<EOF
location: brazilsouth
identity:
  type: UserAssigned
  userAssignedIdentities:
    ${MI_ID}: {}
properties:
  environmentId: ${ENV_ID}
  configuration:
    triggerType: Manual
    replicaTimeout: 300
    replicaRetryLimit: 0
    manualTriggerConfig:
      parallelism: 1
      replicaCompletionCount: 1
  template:
    containers:
      - image: postgres:16-alpine
        name: ddl
        command: ["/bin/sh", "-c"]
        # Parsea la conn string .NET a variables PG* y ejecuta el SQL; ON_ERROR_STOP
        # hace fallar el job (estado Failed) si el SQL falla.
        args:
          - |
            set -e
            get(){ echo "\$CONNSTR" | tr ";" "\n" | sed -n "s/^\$1=//p"; }
            export PGHOST="\$(get Host)" PGDATABASE="\$(get Database)" PGUSER="\$(get Username)" PGPASSWORD="\$(get Password)" PGSSLMODE=require
            psql -v ON_ERROR_STOP=1 -c "\$DDL_SQL"
            echo DDL_OK
        env:
          - name: DDL_SQL
            value: |-
$(printf '%s\n' "$SQL" | sed 's/^/              /')
        resources:
          cpu: 0.25
          memory: 0.5Gi
EOF

echo "==> 1/4 Creando job efímero $JOB en cae-rrhh-prod..."
# Si quedó un job de una corrida anterior, borrarlo y ESPERAR a que el delete termine
# (el delete de ARM es async; crear encima da Conflict "pending delete").
az containerapp job delete -g "$RG" -n "$JOB" --yes -o none 2>/dev/null || true
for i in $(seq 1 24); do
    az containerapp job show -g "$RG" -n "$JOB" -o none 2>/dev/null || break
    echo "   esperando que termine el delete anterior..."
    sleep 5
done
az containerapp job create -g "$RG" -n "$JOB" --yaml "$YAML" -o none

# El secreto de Key Vault se agrega DESPUÉS del create: si va en el create, ARM intenta
# resolverlo antes de terminar de asociar la Managed Identity y falla (IdentityDoesNotExist).
echo "==> 2/4 Vinculando secreto de Key Vault (vía Managed Identity)..."
az containerapp job secret set -g "$RG" -n "$JOB" \
  --secrets "connstr=keyvaultref:${KV_SECRET},identityref:${MI_ID}" -o none
az containerapp job update -g "$RG" -n "$JOB" --set-env-vars "CONNSTR=secretref:connstr" -o none

echo "==> 3/4 Ejecutando: $SQL"
az containerapp job start -g "$RG" -n "$JOB" -o none

STATUS="Unknown"
for i in $(seq 1 30); do
    sleep 10
    STATUS=$(az containerapp job execution list -g "$RG" -n "$JOB" --query "[0].properties.status" -o tsv 2>/dev/null || echo Unknown)
    echo "   estado: $STATUS"
    case "$STATUS" in Succeeded|Failed) break;; esac
done

echo "==> 4/4 Limpiando (borro el job)..."
az containerapp job delete -g "$RG" -n "$JOB" --yes -o none

if [ "$STATUS" = "Succeeded" ]; then
    echo "✔ DDL aplicado en prod."
else
    echo "✖ El job terminó en estado '$STATUS'. Revisar logs en Log Analytics (log-rrhh-prod, job $JOB)." >&2
    exit 1
fi
