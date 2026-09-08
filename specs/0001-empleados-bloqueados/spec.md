# Spec 0001 — Empleados bloqueados (⛔ en Humand)

**Fecha:** 08-sep-2026 · **Pedido por:** Davor · **Estado:** implementado

## Qué
RRHH marca en Humand a ciertos empleados agregando el símbolo ⛔ al nombre (ej. "FELICE ⛔, CRISTIAN ARIEL").
Esos empleados:
1. **No deben salir nunca en el parte de WhatsApp** (ni presentes, ni tardanzas, ni ausentes, ni justificados).
2. En el **resto del sistema** (dashboard, Presentismo, Nocturnidad, Ausentismo, Tardanzas, Empleados,
   asistente IA) se **muestran u ocultan según una configuración** editable por RRHH. Ocultos por defecto.

## Por qué
El parte del 03/09/2026 (turno tarde) listó como ausente a un empleado bloqueado. Es ruido para los
destinatarios y RRHH ya lo administra en Humand con la marca ⛔; el sistema debe respetarla.

## Fuera de alcance
- Bloquear/desbloquear desde esta app (se sigue haciendo en Humand).
- Dejar de sincronizar a los bloqueados (se siguen ingestando para poder mostrarlos si se pide).

## Criterios de aceptación
- Sync detecta ⛔ (o 🚫) en nombre o apellido → `Bloqueado`; al quitar la marca, deja de estarlo.
- El parte excluye bloqueados con la configuración en cualquiera de sus dos valores.
- Con "ocultar", ninguna pantalla ni el asistente los cuenta ni los lista; con "mostrar", aparecen
  (en Empleados con etiqueta "Bloqueado").
- Manual de uso (Ayuda) actualizado. Tests + smoke.
