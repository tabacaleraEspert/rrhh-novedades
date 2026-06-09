# Twilio WhatsApp — Integración ChatbotCobros

Referencia técnica para conectarse a la misma cuenta Twilio desde otro proyecto.

---

## Credenciales necesarias

Todas viven en `appsettings.json` (o como secretos/variables de entorno en prod):

```json
"Twilio": {
  "AccountSid":           "<ver Azure App Service / Key Vault>",
  "AuthToken":            "<ver Azure App Service / Key Vault>",
  "WhatsAppNumber":       "whatsapp:+14155238886",
  "ContentSidPreguntaOtro": "HX740023f2b5cfd875191515917540ca1e"
}
```

| Variable | Descripción |
|---|---|
| `AccountSid` | SID de la cuenta Twilio (empieza con `AC...`) |
| `AuthToken` | Token de autenticación (nunca exponerlo en el código) |
| `WhatsAppNumber` | Número de WhatsApp Twilio. Formato obligatorio: `whatsapp:+<número>`. En sandbox es `+14155238886` |
| `ContentSidPreguntaOtro` | SID del Content Template de botones "¿Cargar otro?" (`HX...`). Sólo necesario si enviás mensajes con botones interactivos |

---

## Enviar un mensaje de texto

```csharp
// Inicializar cliente (una sola vez al arrancar la app)
TwilioClient.Init(accountSid, authToken);

// Enviar mensaje
var message = await MessageResource.CreateAsync(
    body: "Hola, tu pago fue recibido.",
    from: new PhoneNumber("whatsapp:+14155238886"),
    to:   new PhoneNumber("whatsapp:+549XXXXXXXXXX")  // número del destinatario
);

// message.Sid → ID del mensaje enviado
```

**Formato del número destino:** siempre `whatsapp:+<código_país><número>` sin espacios ni guiones.
Argentina: `whatsapp:+549XXXXXXXXXX` (el 9 va después del 54 para móviles).

---

## Enviar un mensaje con botones (Content Template)

Se usa cuando querés botones de respuesta rápida en WhatsApp (requiere un template aprobado por Meta).

```csharp
var message = await MessageResource.CreateAsync(
    from:       new PhoneNumber("whatsapp:+14155238886"),
    to:         new PhoneNumber("whatsapp:+549XXXXXXXXXX"),
    contentSid: "HX740023f2b5cfd875191515917540ca1e"   // SID del template
    // No se pasa `body` cuando se usa contentSid
);
```

Para crear o editar templates: [Twilio Console → Content Template Builder](https://console.twilio.com/us1/develop/sms/content-template-builder).

Cuando el usuario toca un botón, Twilio envía un webhook con `Body` = texto del botón (igual que si lo hubiera escrito a mano).

---

## Recibir mensajes (Webhook)

Twilio hace un `POST` a la URL configurada en la consola cada vez que llega un mensaje.

**URL de este proyecto (prod):**
```
https://espert-chat-comprobantes.azurewebsites.net/api/webhook/whatsapp
```

**Payload form-urlencoded relevante:**

| Campo | Contenido |
|---|---|
| `From` | Número del remitente: `whatsapp:+549XXXXXXXXXX` |
| `Body` | Texto del mensaje (o texto del botón presionado) |
| `NumMedia` | Cantidad de archivos adjuntos (0 si es solo texto) |
| `MediaUrl0` | URL del primer archivo adjunto (imagen/PDF). Requiere auth básica para descargar |
| `MediaContentType0` | MIME type del adjunto (`image/jpeg`, `application/pdf`, etc.) |

**Respuesta esperada:** `<Response></Response>` con `Content-Type: application/xml`.
Si Twilio no recibe respuesta en ~15 segundos, reintenta. Por eso el bot encola el procesamiento y responde al instante.

### Validar que el request viene de Twilio

```csharp
var validator = new RequestValidator(authToken);
bool esValido = validator.Validate(url, formParameters, xTwilioSignature);
```

- `url`: URL pública completa del webhook (ej. `https://espert-chat-comprobantes.azurewebsites.net/api/webhook/whatsapp`).
- `formParameters`: diccionario con todos los campos del form recibido.
- `xTwilioSignature`: valor del header `X-Twilio-Signature`.

Ignorar en desarrollo local (ngrok cambia la URL y la firma no coincide).

---

## Descargar adjuntos (imágenes/PDF)

Las `MediaUrl` de Twilio requieren autenticación básica para descargar:

```csharp
var client = new HttpClient();
var credentials = Convert.ToBase64String(
    Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Basic", credentials);

var response = await client.GetAsync(mediaUrl);
var stream = await response.Content.ReadAsStreamAsync();
```

---

## NuGet

```xml
<PackageReference Include="Twilio" Version="7.14.9" />
```

---

## Configurar el webhook en la Consola Twilio

1. Ir a **Messaging → Senders → WhatsApp Senders** (o Sandbox si es de desarrollo).
2. Seleccionar el número.
3. En **"A message comes in"** → Webhook → POST → pegar la URL del endpoint.
4. Guardar.

En **sandbox** también hay que configurar la URL de sandbox en **Messaging → Try it out → Send a WhatsApp message**.

---

## Notas de diseño de este proyecto

- El webhook responde vacío (`<Response/>`) **al instante** y encola el procesamiento en background (`IWhatsAppQueue` + `WhatsAppQueueWorker`) para no superar el timeout de Twilio de ~15s cuando el OCR + GPT tarda más.
- Los mensajes de un mismo número se procesan **en orden** (cola particionada por número de teléfono).
- La firma de Twilio se valida **solo en producción** (`!IsDevelopment()`). En dev se saltea para poder usar ngrok.
