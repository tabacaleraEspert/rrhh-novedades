#!/usr/bin/env bash
# DDL de inicio teórico (27-oct-2026) — correr ANTES del deploy correspondiente.
# Novedades.InicioTeorico: inicio del horario del día (primer timeSlot de Humand). Lo usa Horas
# extras para devolver la tardanza que Humand descuenta de las extras. Se rellena con el re-sync
# retroactivo de las 05:00 (30 días) o con "Sincronizar período".
# Uso:  bash infra/ddl-2026-10-27-inicio-teorico.sh
set -euo pipefail
cd "$(dirname "$0")/.."

bash infra/run-ddl.sh 'ALTER TABLE "Novedades" ADD COLUMN IF NOT EXISTS "InicioTeorico" time without time zone NULL;'
