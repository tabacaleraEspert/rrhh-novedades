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
# DB efímera: se recrea en cada corrida para que el resultado sea siempre el mismo.
sqlcmd -S "(localdb)\\mssqllocaldb" -Q "IF DB_ID('RRHHNovedades_Smoke') IS NOT NULL BEGIN ALTER DATABASE [RRHHNovedades_Smoke] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [RRHHNovedades_Smoke]; END" >/dev/null 2>&1
ASPNETCORE_ENVIRONMENT=Development \
Humand__UseMock=true \
Twilio__AccountSid= Twilio__AuthToken= Twilio__ContentSidParte= \
ConnectionStrings__Default="Server=(localdb)\\mssqllocaldb;Database=RRHHNovedades_Smoke;Trusted_Connection=True;MultipleActiveResultSets=true" \
Asistencia__HoraParteManana=23:59 Asistencia__HoraParteTarde=23:59 \
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
    -d "email=desarrollador1@tabacaleraespert.com&password=espert" "$BASE/api/auth/login")
[ "$login" = "302" ] && verde "  OK   login" || { rojo "  FAIL login (HTTP $login)"; FALLOS=$((FALLOS+1)); }

sync=$(curl -s -b "$COOKIES" -X POST "$BASE/api/ops/sync")
check "sync de 8 empleados mock" "$sync" '"empleados":8'
check "8 novedades del día"      "$sync" '"novedades":8'

echo "[4/5] Validando clasificación y parte (turno mañana)..."
resumen=$(curl -s -b "$COOKIES" "$BASE/api/ops/resumen")
check "resumen: total 8"               "$resumen" '"total":8'
check "resumen: 2 presentes"           "$resumen" '"Presente":2'
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

echo "[5/5] Páginas clave responden..."
for ruta in "/" "/ayuda" "/empleados" "/mensajes"; do
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
