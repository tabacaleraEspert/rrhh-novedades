#!/usr/bin/env bash
# ─── Smoke test E2E — RRHHNovedades ─────────────────────────────────────────
# Levanta la app con datos MOCK (sin tocar Humand ni Twilio ni la DB de dev),
# ejercita el pipeline completo (sync → clasificación → parte) y valida el
# resultado. Un comando, ~1 minuto:
#
#   bash tools/smoke-test.sh            # compila y corre
#   bash tools/smoke-test.sh --no-build # usa el último build
#
set -u
cd "$(dirname "$0")/.."

PORT=5098
BASE="http://localhost:$PORT"
COOKIES=$(mktemp)
APP_PID=""
FALLOS=0

rojo()  { printf '\033[31m%s\033[0m\n' "$*"; }
verde() { printf '\033[32m%s\033[0m\n' "$*"; }

check() { # check <descripcion> <texto_donde_buscar> <esperado>
    if echo "$2" | grep -qF "$3"; then verde "  OK   $1"; else rojo "  FAIL $1 — esperaba: '$3'"; FALLOS=$((FALLOS+1)); fi
}

cleanup() {
    [ -n "$APP_PID" ] && kill "$APP_PID" 2>/dev/null
    # En Windows, kill al bash no siempre baja al hijo dotnet/exe: rematar por nombre.
    powershell -Command "Get-Process -Name 'RRHHNovedades.Web' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue" 2>/dev/null
    rm -f "$COOKIES"
}
trap cleanup EXIT

echo "── Smoke test RRHHNovedades ──────────────────────────────"

if [ "${1:-}" != "--no-build" ]; then
    echo "[1/5] Compilando..."
    dotnet build src/RRHHNovedades.Web/RRHHNovedades.Web.csproj -v q 2>&1 | tail -2 || { rojo "Build FAILED"; exit 1; }
else
    echo "[1/5] (sin build)"
fi

echo "[2/5] Levantando app (mock Humand, Twilio off, DB Smoke, scheduler neutralizado)..."
# DB efímera (Postgres, igual que la app): se recrea en cada corrida para que el resultado sea
# siempre el mismo. Se intenta psql local y, si no hay, el contenedor Docker que publica el 5432.
DROP_SQL='DROP DATABASE IF EXISTS "RRHHNovedades_Smoke" WITH (FORCE);'
if command -v psql >/dev/null 2>&1; then
    PGPASSWORD=postgres psql -h localhost -p 5432 -U postgres -c "$DROP_SQL" >/dev/null 2>&1
else
    PG_CONT=$(docker ps --filter "publish=5432" --format '{{.Names}}' 2>/dev/null | head -1)
    [ -n "${PG_CONT:-}" ] && docker exec "$PG_CONT" psql -U postgres -c "$DROP_SQL" >/dev/null 2>&1
fi
SSO_SECRET="secreto-sso-smoke-0123456789abcdef-0123456789" # solo para el smoke (32+ chars)
ASPNETCORE_ENVIRONMENT=Development \
Humand__UseMock=true \
Twilio__AccountSid= Twilio__AuthToken= Twilio__ContentSidParte= \
Sso__SharedSecret="$SSO_SECRET" \
ConnectionStrings__Default="Host=localhost;Port=5432;Database=RRHHNovedades_Smoke;Username=postgres;Password=postgres" \
Asistencia__HoraParteManana=23:59 Asistencia__HoraParteTarde=23:59 Asistencia__HoraParteNoche=23:59 \
Asistencia__AutoSyncHoras__0=23:59 \
dotnet run --project src/RRHHNovedades.Web --no-build --no-launch-profile --urls "$BASE" >/dev/null 2>&1 &
APP_PID=$!

for i in $(seq 1 45); do
    code=$(curl -s -o /dev/null -w "%{http_code}" "$BASE/health" 2>/dev/null)
    [ "$code" = "200" ] && break
    sleep 2
done
[ "${code:-}" = "200" ] || { rojo "La app no levantó (health=$code)"; exit 1; }
verde "  OK   app arriba ($BASE)"

echo "[3/5] Login + sincronización mock..."
login=$(curl -s -c "$COOKIES" -o /dev/null -w "%{http_code}" \
    -d "email=desarrollador1@tabacaleraespert.com&pin=0000" "$BASE/api/auth/login")
[ "$login" = "302" ] && verde "  OK   login" || { rojo "  FAIL login (HTTP $login)"; FALLOS=$((FALLOS+1)); }

sync=$(curl -s -b "$COOKIES" -X POST "$BASE/api/ops/sync")
check "sync de 10 empleados mock" "$sync" '"empleados":10'
check "10 novedades del día"      "$sync" '"novedades":10'

echo "[4/5] Validando clasificación y parte (turno mañana)..."
resumen=$(curl -s -b "$COOKIES" "$BASE/api/ops/resumen")
check "resumen: total 10"              "$resumen" '"total":10'
check "resumen: 3 presentes"           "$resumen" '"Presente":3'
check "resumen: 1 ausente injust."     "$resumen" '"AusenteInjustificado":1'
check "resumen: 2 justificados"        "$resumen" '"AusenteJustificado":2'
check "resumen: 1 franco"              "$resumen" '"FrancoNoLaborable":1'

parte=$(curl -s -b "$COOKIES" "$BASE/api/ops/parte/preview?turno=Manana")
check "parte: encabezado"              "$parte" "Novedades RR. HH."
check "parte: tardanza con nombre"     "$parte" "Tardanzas: 1 (Gómez, Rosa)"
check "parte: ausente con nombre"      "$parte" "Ausentes: 1 (Sosa, Mario)"
check "parte: justificados (2)"        "$parte" "Justificados: 2 (Díaz, Lucía; Ruiz, Pedro)"
check "parte: pie fijo del template"   "$parte" "Reporte automático de asistencia"

parteT=$(curl -s -b "$COOKIES" "$BASE/api/ops/parte/preview?turno=Tarde")
check "parte tarde: tardanza de López" "$parteT" "Tardanzas: 1 (López, Carla)"

parteN=$(curl -s -b "$COOKIES" "$BASE/api/ops/parte/preview?turno=Noche")
check "parte noche: encabezado"        "$parteN" "Turno Noche"
check "parte noche: 1 presente"        "$parteN" "Presentes: 1"
check "parte noche: tardanza de Acosta" "$parteN" "Tardanzas: 1 (Acosta, Bruno)"

echo "[4b/5] SSO Command Center (ticket JWT de un solo uso)..."
b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
mk_ticket() { # mk_ticket <dni> — emite un ticket como lo haría el Command Center
    local iat exp h p s
    iat=$(date +%s); exp=$((iat + 60))
    h=$(printf '{"alg":"HS256","typ":"JWT"}' | b64url)
    p=$(printf '{"dni":"%s","aud":"rrhh-novedades","iat":%d,"exp":%d,"jti":"smoke-%s%s"}' "$1" "$iat" "$exp" "$RANDOM" "$RANDOM" | b64url)
    s=$(printf '%s.%s' "$h" "$p" | openssl dgst -sha256 -hmac "$SSO_SECRET" -binary | b64url)
    printf '%s.%s.%s' "$h" "$p" "$s"
}
curl -s -b "$COOKIES" -X POST "$BASE/api/ops/usuarios" -H "Content-Type: application/json" \
    -d '{"email":"sso-smoke@tabacaleraespert.com","nombre":"SSO Smoke","rol":"RRHH","pin":"1234","dni":"30111222"}' >/dev/null
landing=$(curl -s "$BASE/sso")
check "landing /sso" "$landing" "Validando acceso"
TICKET=$(mk_ticket "30111222")
ssoc=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/api/auth/sso" -H "Content-Type: application/json" -d "{\"ticket\":\"$TICKET\"}")
[ "$ssoc" = "200" ] && verde "  OK   ticket válido → 200" || { rojo "  FAIL ticket válido (HTTP $ssoc)"; FALLOS=$((FALLOS+1)); }
# Replay del MISMO ticket: 401 exacto (un 401 sin body era re-ejecutado por StatusCodePages y salía 400).
replayc=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/api/auth/sso" -H "Content-Type: application/json" -d "{\"ticket\":\"$TICKET\"}")
[ "$replayc" = "401" ] && verde "  OK   replay del ticket → 401" || { rojo "  FAIL replay (HTTP $replayc, esperaba 401)"; FALLOS=$((FALLOS+1)); }

echo "[5/5] Páginas clave responden..."
for ruta in "/" "/ayuda" "/empleados" "/mensajes" "/nocturnidad"; do
    pc=$(curl -s -b "$COOKIES" -o /dev/null -w "%{http_code}" "$BASE$ruta")
    [ "$pc" = "200" ] && verde "  OK   $ruta" || { rojo "  FAIL $ruta (HTTP $pc)"; FALLOS=$((FALLOS+1)); }
done

echo "──────────────────────────────────────────────────────────"
if [ "$FALLOS" -eq 0 ]; then
    verde "SMOKE TEST: TODO OK ✔"
    exit 0
else
    rojo "SMOKE TEST: $FALLOS fallo(s) ✖"
    exit 1
fi
