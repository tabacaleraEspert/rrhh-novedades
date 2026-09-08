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

# La Managed Identity se asigna en un paso aparte vía ARM (PATCH): con la extensión containerapp
# 1.2.0b4 (sep-2026) tanto el bloque `identity` del YAML como `job identity assign` fallan con
# IdentityDoesNotExist. El PATCH directo funciona.
echo "==> 1b/4 Asignando Managed Identity id-rrhh-prod al job (ARM PATCH)..."
JOB_ID="/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.App/jobs/$JOB"
az rest --method patch --url "https://management.azure.com${JOB_ID}?api-version=2024-03-01" \
  --body "{\"identity\":{\"type\":\"UserAssigned\",\"userAssignedIdentities\":{\"$MI_ID\":{}}}}" -o none
sleep 5

# Secreto de Key Vault + env CONNSTR: también por ARM PATCH (merge-patch). `job secret set` de la
# extensión 1.2.0b4 relee el job y reenvía la identidad con clientId/principalId, y ARM lo rechaza
# (InvalidIdentityValues). El PATCH reemplaza el array `containers`, por eso va el contenedor completo.
echo "==> 2/4 Vinculando secreto de Key Vault (vía Managed Identity, ARM PATCH)..."
PATCH=$(mktemp /tmp/job-rrhh-ddl.XXXXXX.json)
trap 'rm -f "$YAML" "$PATCH"' EXIT
DDL_SQL="$SQL" MI_ID="$MI_ID" KV_SECRET="$KV_SECRET" python3 - "$PATCH" <<'PY'
import json, os, sys
script = """set -e
get(){ echo "$CONNSTR" | tr ";" "\n" | sed -n "s/^$1=//p"; }
export PGHOST="$(get Host)" PGDATABASE="$(get Database)" PGUSER="$(get Username)" PGPASSWORD="$(get Password)" PGSSLMODE=require
psql -v ON_ERROR_STOP=1 -c "$DDL_SQL"
echo DDL_OK
"""
body = {"properties": {
  "configuration": {"secrets": [{"name": "connstr", "keyVaultUrl": os.environ["KV_SECRET"], "identity": os.environ["MI_ID"]}]},
  "template": {"containers": [{
    "image": "postgres:16-alpine", "name": "ddl",
    "command": ["/bin/sh", "-c"], "args": [script],
    "env": [{"name": "DDL_SQL", "value": os.environ["DDL_SQL"]}, {"name": "CONNSTR", "secretRef": "connstr"}],
    "resources": {"cpu": 0.25, "memory": "0.5Gi"}}]}}}
open(sys.argv[1], "w").write(json.dumps(body))
PY
az rest --method patch --url "https://management.azure.com${JOB_ID}?api-version=2024-03-01" --body @"$PATCH" -o none
sleep 5

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
