# Plan 0001 — Empleados bloqueados

- `Empleado.Bloqueado` (bool) + `Empleado.TieneMarcaBloqueo` (⛔ U+26D4 / 🚫 U+1F6AB).
- `ConfiguracionParte.MostrarBloqueados` (bool, default false) — persistencia del interruptor.
- `VisibilidadEmpleados` (singleton en memoria): lo lee el **filtro global de EF** en `AppDbContext`
  (`Empleado`, `NovedadDiaria`, `LicenciaManual`). Se carga en `Program.cs`, lo escribe Configuración
  y lo refresca `ParteScheduler` en cada tick. Ventaja: todas las pantallas/servicios/asistente lo
  respetan sin tocarlos, y cualquier pantalla futura también.
- Opt-out explícito (`IgnoreQueryFilters`) en lo que debe ver todo: `IngestaService` (si no, duplicaría
  empleados), `ParteService` (excluye bloqueados por su cuenta, siempre) y las correcciones retroactivas
  de `LicenciaManualService`.
- UI: tarjeta "Empleados bloqueados" en Configuración (switch + cantidad detectada); chip "Bloqueado" y
  aviso de ocultos en Empleados; Ayuda actualizada.
- DDL prod (EnsureCreated no altera): `infra/ddl-2026-09-08-empleados-bloqueados.sh` ANTES del push.
- Tests: `EmpleadosBloqueadosTests` (detección, sync marca/desmarca, filtro global, cableado DI, parte).
  Smoke: EMP-011 bloqueado en el mock, ausente, no aparece en el parte ni suma en el resumen.
