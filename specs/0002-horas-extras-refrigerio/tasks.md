# Tasks 0002 — Horas extras y refrigerio

- [x] Relevar políticas reales en Humand (API, 26/09 → 20/10): 4 categorías; `EXTRA_HOURS` no se usa.
- [x] Modelo + `JornadaHumand` + parser de categorías + ingesta.
- [x] `HorasExtrasService` + tests.
- [x] Página `/horas-extras` + menú + flag + appsettings.
- [x] Mock de Humand con extras/refrigerio para dev y smoke.
- [x] Tool del asistente + datos.md.
- [x] Ayuda.razor + UltimaActualizacion.
- [x] DDL script + DEPLOY-AZURE.md + ENDPOINTS-RELEVANTES.md + CLAUDE.md.
- [x] `dotnet test` en verde + smoke E2E.
- [ ] Davor: DDL en prod (`bash infra/ddl-2026-10-20-horas-extras.sh`), push, "Sincronizar período" en la página.

## Revisión (20-oct-2026)
Revisor independiente: sin bloqueantes, 4 menores corregidos (texto domingos/feriados, "—" en días
con extras, mock alineado a lun–vie/sábado, comentario de herramientas). Suite 223/223, smoke OK.
Verificado contra Humand real (base local nueva, sync 26/09 → 20/10): la página reproduce exactamente
los totales de la API (HE50 2.938,54 · HE100 234,25 · refrigerio 1.398,67).
