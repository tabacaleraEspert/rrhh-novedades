# Template de Twilio — Parte de novedades

Diseño del **Content Template** (plantilla de utilidad de WhatsApp) que el bot usa para enviar los
2 partes diarios. Se crea en **Twilio Console → Content Template Builder** y se aprueba (Meta).
Una vez aprobado, copiar el `ContentSid` (HX...) a la config `Twilio:ContentSidParte`.

---

## Recomendado: plantilla "pasa-cuerpo" (2 variables)

Los partes tienen **listas de nombres de largo variable**, lo que no encaja con variables fijas
por persona. La forma robusta es pasar el contenido ya armado en variables.

- **Categoría:** Utility (utilidad) — son mensajes proactivos transaccionales, no marketing.
- **Idioma:** Español.
- **Tipo de contenido:** `text` (sin botones, por ahora).

**Body del template:**
```
*{{1}}*
{{2}}
```

**Variables que envía la app** (ya implementado en `ParteService` → `EnviarTemplateAsync`):
- `{{1}}` = encabezado, ej. `Novedades RR. HH. — Turno Mañana, 09/06/2026`
- `{{2}}` = cuerpo, ej.:
  ```
  Presentes: 12
  Tardanzas (2): Gómez, Rosa; Pérez, Juan
  Ausentes (3): López, Carla; Ruiz, Pedro; Sosa, Mario
  Justificados (1): Díaz, Lucía
  ```

**Ejemplo de muestra para la aprobación** (sample values en el builder):
- `{{1}}`: `Novedades RR. HH. — Turno Mañana, 09/06/2026`
- `{{2}}`: `Presentes: 12\nTardanzas (1): Gómez, Rosa\nAusentes (1): Sosa, Mario\nJustificados (1): Díaz, Lucía`

---

## Alternativa: plantilla estructurada (4 variables)

Si se prefiere fijar las etiquetas en la plantilla (más legible en la aprobación, pero las listas
largas igual van como texto en una variable):

```
*Novedades RR. HH. — {{1}}*
Presentes: {{2}}
Ausentes: {{3}}
Justificados / Tardanzas: {{4}}
```
Menos flexible para el formato; si se elige esta, ajustar el armado de variables en `ParteService`.

---

## Notas
- Mientras `Twilio:ContentSidParte` esté vacío, la app envía el parte como **texto plano**
  (`EnviarMensajeAsync`) — útil para probar en el Sandbox de WhatsApp. Con el `ContentSid` cargado,
  pasa a usar el template automáticamente.
- Los partes son **outbound** a la lista de RRHH; no requieren ventana de conversación abierta porque
  son plantillas de utilidad pre-aprobadas.
- Credenciales (`AccountSid`, `AuthToken`, `WhatsAppNumber`) = las mismas de la cuenta Twilio de
  ChatbotCobros. Van en `appsettings.secrets.local.json` / App Settings de Azure, nunca en el repo.
- Ver mecánica general en `docs/TWILIO-INTEGRACION.md`.
