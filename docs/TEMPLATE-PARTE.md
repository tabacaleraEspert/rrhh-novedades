# Template de Twilio — Parte de novedades

Diseño del **Content Template** (plantilla de utilidad de WhatsApp) que el bot usa para enviar los
2 partes diarios. Se crea en **Twilio Console → Content Template Builder**, se aprueba (Meta) y se copia
el `ContentSid` (HX...) a la config `Twilio:ContentSidParte`.

**Formato elegido: compacto con indicadores de color** (legible desde el celular).

---

## ⚠️ Por qué las listas van por variables y los saltos de línea NO

WhatsApp **rechaza** valores de variable que contengan saltos de línea, tabs o más de 4 espacios
seguidos. Por eso el cuerpo (etiquetas, emojis y saltos de línea) va **fijo en la plantilla**, y por
variables van solo valores de **una sola línea**: el encabezado, los números y las listas de nombres
(separadas por `; `). Una variable nunca puede ir vacía → cuando no hay nombres se envía `—`.

---

## Cuerpo del template (copiar tal cual en el builder)

```
*Novedades RR. HH. — {{1}}*

🟢 Presentes: {{2}}
🟡 Tardanzas ({{3}}): {{4}}
🔴 Ausentes ({{5}}): {{6}}
🔵 Justificados ({{7}}): {{8}}
```

- **Categoría:** Utility (utilidad) — mensajes proactivos transaccionales.
- **Idioma:** Español.
- **Tipo de contenido (Twilio):** `text` (sin botones, por ahora).

## Variables (las arma `ParteService.ArmarParteAsync`)

| Var | Contenido | Ejemplo |
|-----|-----------|---------|
| `{{1}}` | Turno · fecha | `Turno Mañana · 09/06/2026` |
| `{{2}}` | Presentes (número) | `12` |
| `{{3}}` | Tardanzas (cantidad) | `2` |
| `{{4}}` | Tardanzas (apellido, nombre) | `Gómez, Rosa; Pérez, Juan` |
| `{{5}}` | Ausentes (cantidad) | `3` |
| `{{6}}` | Ausentes (apellido, nombre) | `López, Carla; Ruiz, Pedro; Sosa, Mario` |
| `{{7}}` | Justificados (cantidad) | `1` |
| `{{8}}` | Justificados (apellido, nombre) | `Díaz, Lucía` |

## Cómo se ve enviado

```
*Novedades RR. HH. — Turno Mañana · 09/06/2026*

🟢 Presentes: 12
🟡 Tardanzas (2): Gómez, Rosa; Pérez, Juan
🔴 Ausentes (3): López, Carla; Ruiz, Pedro; Sosa, Mario
🔵 Justificados (1): Díaz, Lucía
```

## Valores de muestra para la aprobación (sample values en el builder)

- `{{1}}`: `Turno Mañana · 09/06/2026`
- `{{2}}`: `12`  ·  `{{3}}`: `2`  ·  `{{4}}`: `Gómez, Rosa; Pérez, Juan`
- `{{5}}`: `3`  ·  `{{6}}`: `López, Carla; Ruiz, Pedro; Sosa, Mario`
- `{{7}}`: `1`  ·  `{{8}}`: `Díaz, Lucía`

---

## Pasos en la consola de Twilio

1. **Twilio Console → Messaging → Content Template Builder → Create new.**
2. Nombre: `rrhh_novedades_parte` (o similar). Idioma: `es`. Categoría: **Utility**.
3. Tipo: **Text**. Pegar el cuerpo de arriba (con los `{{1}}`…`{{8}}`).
4. Cargar los **sample values** de arriba (Meta los pide para aprobar).
5. Guardar y **enviar a aprobación de WhatsApp**.
6. Cuando quede **Approved**, copiar el **Content SID** (`HX…`) a la config `Twilio:ContentSidParte`
   (en `appsettings.secrets.local.json` / App Settings de Azure).

---

## Notas
- Mientras `Twilio:ContentSidParte` esté vacío, la app envía el parte como **texto plano**
  (`EnviarMensajeAsync`) — útil para probar en el Sandbox de WhatsApp. Con el `ContentSid` cargado,
  pasa a usar el template automáticamente.
- Los partes son **outbound** a la lista de RRHH; al ser plantilla de utilidad pre-aprobada no
  requieren ventana de conversación abierta.
- Credenciales (`AccountSid`, `AuthToken`, `WhatsAppNumber`) = las mismas de la cuenta Twilio de
  ChatbotCobros. Van en secrets / App Settings de Azure, nunca en el repo.
- Si más adelante se quieren botones (ej. "Ver detalle"), se cambia el tipo de contenido a
  `twilio/quick-reply` o `card` y se vuelve a aprobar.
- Ref general: `docs/TWILIO-INTEGRACION.md`.
