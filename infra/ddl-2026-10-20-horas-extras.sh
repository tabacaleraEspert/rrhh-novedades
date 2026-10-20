#!/usr/bin/env bash
# DDL de horas extra (20-oct-2026) — correr ANTES del deploy correspondiente.
# Novedades.HorasExtras50 / HorasExtras100: horas de las categorías "Horas extras al 50" y
# "Horas extras al 100" de categorizedHours del day-summary de Humand (políticas de marcaje).
# Sección "Horas extras y refrigerio" (spec 0002). Después del deploy, "Sincronizar período"
# en la página para rellenar el mes en curso.
# Uso:  bash infra/ddl-2026-10-20-horas-extras.sh
set -euo pipefail
cd "$(dirname "$0")/.."

bash infra/run-ddl.sh 'ALTER TABLE "Novedades" ADD COLUMN IF NOT EXISTS "HorasExtras50" double precision NOT NULL DEFAULT 0; ALTER TABLE "Novedades" ADD COLUMN IF NOT EXISTS "HorasExtras100" double precision NOT NULL DEFAULT 0;'
