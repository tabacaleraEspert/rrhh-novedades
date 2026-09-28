#!/usr/bin/env bash
# DDL de horas de refrigerio (28-sep-2026) — correr ANTES del deploy correspondiente.
# Novedades.HorasRefrigerio: horas de la categoría "Refrigerio" de categorizedHours del
# day-summary de Humand (políticas de marcaje). Se muestra como columna en Presentismo.
# Uso:  bash infra/ddl-2026-09-28-horas-refrigerio.sh
set -euo pipefail
cd "$(dirname "$0")/.."

bash infra/run-ddl.sh 'ALTER TABLE "Novedades" ADD COLUMN IF NOT EXISTS "HorasRefrigerio" double precision NOT NULL DEFAULT 0;'
