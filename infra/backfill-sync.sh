#!/bin/bash
# Backfill de novedades en PROD: re-sincroniza 01-jun → 31-ago desde Humand vía /api/ops/sync-rango
# (corrige licencias retroactivas, francos y fichadas perdidas en Ausentismo/Nocturnidad/Presentismo).
# Uso: bash infra/backfill-sync.sh   (requiere az login con acceso a kv-rrhh-prod)
set -euo pipefail

BASE="https://ca-rrhh-prod.wittydune-1044833e.brazilsouth.azurecontainerapps.io"
JAR=$(mktemp)
trap 'rm -f "$JAR"' EXIT

echo "Leyendo PIN maestro del Key Vault..."
PIN=$(az keyvault secret show --vault-name kv-rrhh-prod --name Auth--MasterPin --query value -o tsv)

echo "Login en prod..."
code=$(curl -s -o /dev/null -w '%{http_code}' -c "$JAR" -X POST "$BASE/api/auth/login" \
  --data-urlencode "email=desarrollador1@tabacaleraespert.com" --data-urlencode "pin=$PIN")
unset PIN
# El login redirige (302) al dashboard si salió bien; /login?error=... si falló.
[ "$code" = "302" ] || { echo "Login falló (HTTP $code)"; exit 1; }

# Rangos de ≤31 días (límite del endpoint)
for rango in "2026-06-01 2026-07-01" "2026-07-02 2026-08-01" "2026-08-02 2026-08-31"; do
  set -- $rango
  echo "Sincronizando $1 → $2 (puede tardar varios minutos)..."
  resp=$(curl -s -b "$JAR" -X POST "$BASE/api/ops/sync-rango?desde=$1&hasta=$2" --max-time 1200)
  echo "$resp" | python3 -c "import json,sys; d=json.load(sys.stdin); print(f\"  OK: {d.get('dias','?')} días, {sum((d.get('novedadesPorDia') or {}).values())} novedades\")" \
    || { echo "  Respuesta inesperada: $resp"; exit 1; }
done

echo "Verificación rápida (ausentismo 03→06 ago, caso Dias):"
curl -s -b "$JAR" "$BASE/api/ops/ausentismo?desde=2026-08-03&hasta=2026-08-06" | python3 -c "
import json,sys
d=json.load(sys.stdin)
for e in d.get('detalle', d.get('Detalle', [])):
    nom = e.get('empleado') or e.get('Empleado') or ''
    if 'DIAS' in str(nom).upper():
        print(' ', json.dumps(e, ensure_ascii=False))
"
echo "LISTO. Revisá Ausentismo (Dias 3-6 ago justificadas, Fernández 8-ago franco) y Nocturnidad (Nuñez 30-31 jul)."
