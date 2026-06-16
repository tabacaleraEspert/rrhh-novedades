# Guía de despliegue a producción — Novedades RRHH

Guía paso a paso para poner el bot a correr en Azure. Una vez desplegado, el sistema
envía los 2 partes diarios solo (07:00 y 14:00, hora Argentina) sin que haya una máquina
encendida.

> **A quién va dirigida:** la persona de Sistemas/IT que hace el deploy. Asume acceso al
> portal de Azure y a la cuenta de Twilio de ESPERT.

---

## 0. Qué se despliega

Una sola aplicación web (Blazor Server, .NET 10) que hace todo: el dashboard, el panel del
bot y el envío automático de los partes (corre como tarea de fondo dentro de la misma app).
No hay servicios separados ni colas: **un App Service + una base de datos SQL** alcanza.

---

## 1. Requisitos previos

- [ ] Suscripción de Azure con permisos para crear recursos.
- [ ] Acceso a la **Twilio Console** de ESPERT (la misma cuenta de ChatbotCobros).
- [ ] La **API Key de producción de Humand** (pedirla / rotarla — ver paso 6).
- [ ] El repositorio clonado o acceso a GitHub `tabacaleraEspert/rrhh-novedades`.
- [ ] .NET 10 SDK instalado localmente si se publica desde la máquina.

> ⚠️ **.NET 10 es preview.** El runtime puede no estar disponible por defecto en App Service.
> Para evitar problemas, publicar como **self-contained** (incluye el runtime en el paquete) —
> ver paso 4. Así no dependés de que Azure tenga la versión exacta instalada.

---

## 2. Crear los recursos en Azure

1. **Resource Group** (ej. `rg-rrhh-novedades`).
2. **Azure SQL Database**
   - Crear un *SQL Server* lógico + una *Database*.
   - Tier **Basic** o **S0** alcanza de sobra para el volumen (≈200 empleados, 1 fila por día).
   - Anotar el nombre del server, la base, el usuario admin y su contraseña.
   - En **Networking** del SQL Server: habilitar *"Allow Azure services and resources to access
     this server"* para que el App Service pueda conectarse.
3. **App Service**
   - Plan: Windows, **.NET** (cualquier tier B1 o superior; el plan Free puede dormir la app y
     perderse un disparo del scheduler — usar al menos **B1 "Always On"**).
   - Después de crearlo: **Configuration → General settings → Always On = On**
     (clave: si la app se "duerme", el scheduler no dispara los partes).

---

## 3. Variables de entorno (App Settings)

En el App Service: **Configuration → Application settings**. Agregar estas variables.
Azure mapea el doble guion bajo `__` a la jerarquía de `appsettings.json`.

| Nombre | Valor | ¿Secreto? |
|---|---|---|
| `ConnectionStrings__Default` | cadena de conexión de la Azure SQL (ver abajo) | 🔒 sí |
| `Humand__ApiKey` | API Key de producción de Humand | 🔒 sí |
| `Humand__UseMock` | `false` | no |
| `Twilio__AccountSid` | el Account SID (Twilio Console → Account Info) | 🔒 sí |
| `Twilio__AuthToken` | el Auth Token (rotar antes — ver paso 6) | 🔒 sí |

**Eso es todo lo obligatorio.** El número de WhatsApp emisor (`Twilio:WhatsAppNumber`) y el
template aprobado (`Twilio:ContentSidParte`) **ya vienen en `appsettings.json`** y no hace falta
repetirlos, salvo que quieras sobrescribirlos.

**Cadena de conexión de Azure SQL** (reemplazar `<...>`):

```
Server=tcp:<servidor>.database.windows.net,1433;Initial Catalog=<base>;Persist Security Info=False;User ID=<usuario>;Password=<contraseña>;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

> 💡 Mejor todavía: guardar los 4 valores secretos en **Azure Key Vault** y referenciarlos
> desde App Settings con `@Microsoft.KeyVault(...)`. Opcional pero recomendado.

---

## 4. Publicar la aplicación

**Opción A — desde Visual Studio (más simple):**
1. Click derecho en el proyecto `RRHHNovedades.Web` → **Publish**.
2. Target: **Azure → Azure App Service (Windows)** → elegir el App Service creado.
3. En el perfil de publicación, **Deployment Mode: Self-Contained**, Target Runtime: `win-x64`.
4. **Publish**.

**Opción B — desde la línea de comandos:**
```bash
cd src/RRHHNovedades.Web
dotnet publish -c Release --self-contained -r win-x64 -o ./publish
```
Luego subir el contenido de `./publish` al App Service (zip deploy, FTP, o
`az webapp deploy --type zip`).

> No hace falta tocar nada para los estilos: al publicar, el CSS/JS de MudBlazor y
> `blazor.web.js` quedan empaquetados en el output. (El problema de "sin estilos" sólo aparece
> con `dotnet run` suelto en modo Production, no en el output publicado.)

---

## 5. Primera corrida (verificación)

1. Abrir la URL del App Service (`https://<app>.azurewebsites.net`).
2. Al arrancar, la app crea las tablas automáticamente (`EnsureCreated`) y siembra el usuario
   admin.
3. **Login:**
   - Usuario: `desarrollador1@tabacaleraespert.com`
   - Contraseña: `espert`  ← **cambiarla de inmediato** (ver paso 6).
4. Ir a **Configuración → Destinatarios del parte** y agregar los teléfonos reales del equipo
   de RRHH (formato `+549` + área + número). El destinatario de ejemplo que viene cargado está
   **inactivo**; dejarlo así o borrarlo.
5. **Probar la ingesta:** en la página **Bot de novedades**, tocar **Sincronizar hoy**.
   Debería traer ~200 empleados desde Humand. Si falla, revisar `Humand__ApiKey` y `UseMock=false`.
6. **Probar un envío real:** en la misma página, **Enviar parte ahora → Mañana**. Confirmar que
   llega el WhatsApp a los destinatarios cargados.
7. Si todo anduvo, el scheduler enviará los partes solo a las **07:00** y **14:00** (Argentina)
   todos los días.

---

## 6. Seguridad post-deploy (importante)

- [ ] **Cambiar la contraseña** del usuario `desarrollador1@tabacaleraespert.com` (viene como
  `espert`). *(Hoy se cambia regenerando el hash en la base; si se necesita seguido, pedir una
  pantalla de cambio de contraseña.)*
- [ ] **Rotar la API Key de Humand** (`POST /api-keys/rotate` en la consola de Humand) y cargar
  la nueva en `Humand__ApiKey`. La key vieja anduvo por el chat / docs, así que conviene rotarla.
- [ ] **Rotar el Auth Token de Twilio** en la Twilio Console y actualizar `Twilio__AuthToken`.
- [ ] **Eliminar `docs/humand/datos_api.md`** del repo (tiene la key vieja en texto plano) o
  moverlo fuera del control de versiones.
- [ ] Verificar que `appsettings.secrets.local.json` **nunca** se subió (está en `.gitignore`).

---

## 7. Migraciones EF (cuando el esquema se estabilice)

Hoy la app usa `EnsureCreatedAsync` (crea las tablas al arrancar, sin versionado). Funciona para
producción inicial, pero **no permite evolucionar el esquema sin recrear la base**. Cuando el
modelo deje de cambiar:

1. En `Program.cs`, reemplazar `await db.Database.EnsureCreatedAsync();` por
   `await db.Database.MigrateAsync();`.
2. Crear la migración inicial:
   ```bash
   cd src/RRHHNovedades.Web
   dotnet ef migrations add Initial
   dotnet ef database update
   ```
3. Republicar.

---

## 8. Operación diaria y monitoreo

- **Partes automáticos:** 07:00 (mañana) y 14:00 (tarde), hora Argentina. Horarios configurables
  vía `Asistencia__HoraParteManana` / `Asistencia__HoraParteTarde` en App Settings.
- **Auto-sync:** la app sincroniza con Humand a las 10:30 y 16:30 además de antes de cada parte.
- **Logs:** App Service → **Log stream** para ver en vivo. Cada envío queda registrado en la
  tabla `EnviosParte` y se ve en el dashboard.
- **Disparo manual:** página **Bot de novedades** (re-sincronizar o reenviar un parte a mano).

---

## Referencias

- `docs/PENDIENTES.md` — estado del proyecto y decisiones de negocio pendientes con Yanina.
- `docs/TWILIO-INTEGRACION.md` — detalle técnico de Twilio/WhatsApp.
- `docs/humand/ENDPOINTS-RELEVANTES.md` — API de Humand.
- `CLAUDE.md` — referencia general del proyecto.
