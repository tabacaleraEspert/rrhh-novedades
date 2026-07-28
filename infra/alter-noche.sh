#!/usr/bin/env bash
# ============================================================================
#  DDL turno noche — agrega ConfiguracionParte.HoraParteNoche en la DB de PROD.
#  (EnsureCreated no altera una DB existente; ver docs/DEPLOY-AZURE.md §Turno noche)
#
#  La DB vive en VNet privada sin acceso público, así que el ALTER se ejecuta
#  con un Container Apps Job efímero DENTRO del entorno (cae-rrhh-prod):
#  imagen postgres, connection string referenciada directo desde Key Vault vía
#  Managed Identity (el password nunca sale de Azure). El job se borra al final.
#
#  Uso:  bash infra/alter-noche.sh        (requiere az login con permisos en rg-rrhh-prod)
#  Idempotente: ADD COLUMN IF NOT EXISTS — se puede correr varias veces.
# ============================================================================
set -euo pipefail

SUB="15900c96-4a2d-493b-ab12-912d521b3113"
RG="rg-rrhh-prod"
JOB="job-rrhh-ddl-noche"
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
    secrets:
      - name: connstr
        keyVaultUrl: ${KV_SECRET}
        identity: ${MI_ID}
  template:
    containers:
      - image: postgres:16-alpine
        name: ddl
        command: ["/bin/sh", "-c"]
        # Parsea la conn string .NET a variables PG* y corre el DDL; el SELECT final
        # hace fallar el job si la columna no quedó creada.
        args:
          - |
            set -e
            get(){ echo "\$CONNSTR" | tr ";" "\n" | sed -n "s/^\$1=//p"; }
            export PGHOST="\$(get Host)" PGDATABASE="\$(get Database)" PGUSER="\$(get Username)" PGPASSWORD="\$(get Password)" PGSSLMODE=require
            psql -v ON_ERROR_STOP=1 -c "ALTER TABLE \"ConfiguracionParte\" ADD COLUMN IF NOT EXISTS \"HoraParteNoche\" character varying(5) NOT NULL DEFAULT '06:00';"
            psql -tA -c "SELECT column_name FROM information_schema.columns WHERE table_name='ConfiguracionParte' AND column_name='HoraParteNoche';" | grep -q HoraParteNoche
            echo DDL_OK
        env:
          - name: CONNSTR
            secretRef: connstr
        resources:
          cpu: 0.25
          memory: 0.5Gi
EOF

echo "==> 1/3 Creando job efímero $JOB en cae-rrhh-prod..."
az containerapp job create -g "$RG" -n "$JOB" --yaml "$YAML" -o none

echo "==> 2/3 Ejecutando..."
az containerapp job start -g "$RG" -n "$JOB" -o none

STATUS="Unknown"
for i in $(seq 1 30); do
    sleep 10
    STATUS=$(az containerapp job execution list -g "$RG" -n "$JOB" --query "[0].properties.status" -o tsv 2>/dev/null || echo Unknown)
    echo "   estado: $STATUS"
    case "$STATUS" in Succeeded|Failed) break;; esac
done

echo "==> 3/3 Limpiando (borro el job)..."
az containerapp job delete -g "$RG" -n "$JOB" --yes -o none

if [ "$STATUS" = "Succeeded" ]; then
    echo "✔ Columna HoraParteNoche creada/verificada en prod."
else
    echo "✖ El job terminó en estado '$STATUS'. Revisar logs en Log Analytics (log-rrhh-prod, job $JOB)." >&2
    exit 1
fi
