#!/usr/bin/env bash
# DDL de empleados bloqueados (08-sep-2026) — correr ANTES del deploy correspondiente.
# Empleados.Bloqueado: ⛔ en el nombre en Humand (nunca salen en el parte de WhatsApp).
# ConfiguracionParte.MostrarBloqueados: interruptor de Configuración (mostrar/ocultar en la app).
# Uso:  bash infra/ddl-2026-09-08-empleados-bloqueados.sh
set -euo pipefail
cd "$(dirname "$0")/.."

bash infra/run-ddl.sh 'ALTER TABLE "Empleados" ADD COLUMN IF NOT EXISTS "Bloqueado" boolean NOT NULL DEFAULT FALSE;
ALTER TABLE "ConfiguracionParte" ADD COLUMN IF NOT EXISTS "MostrarBloqueados" boolean NOT NULL DEFAULT FALSE;'
