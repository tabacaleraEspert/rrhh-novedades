# Deploy a Azure — RRHHNovedades (estándar Espert)

Guía del despliegue **on-standard** (ver `espert-azure-standards/STANDARDS.md`). Reemplaza a la
vieja `DEPLOY.md` (App Service + Azure SQL), que quedó **deprecada**.

## Arquitectura (Tier M)

```
Internet ──► Container App (ca-rrhh-prod, ingress HTTPS + session affinity)
                 │  .NET 10 Blazor Server + bot (scheduler in-process)
                 ├─► PostgreSQL Flexible (pg-rrhh-prod) — VNet privada, sin acceso público
                 ├─► Key Vault (kv-rrhh-prod) ◄── Managed Identity (id-rrhh-prod)
                 ├─► ACR compartido (imagen)          [rg-espert-shared]
                 └─► Log Analytics compartido (logs)  [rg-espert-shared]
```

Decisiones clave frente al estándar:
- **Compute:** Container App (Docker), no App Service. → `Dockerfile` multi-stage, runtime .NET 10 embebido.
- **DB:** PostgreSQL Flexible (no Azure SQL). El código usa Npgsql; esquema vía `EnsureCreated`.
- **Región:** Brazil South.
- **Secretos:** Key Vault + Managed Identity. La app los lee con `DefaultAzureCredential` (env `KeyVault__Uri`). Cero secretos en la config del Container App.
- **Red:** la DB no tiene acceso público; vive en una subred delegada, accesible solo desde la VNet del Container Apps Environment.
- **1 sola réplica (`min=max=1`):** el scheduler de partes corre dentro del proceso; con más de una réplica se mandarían WhatsApps duplicados. Además Blazor Server necesita afinidad de sesión.

## Naming

| Recurso | Nombre |
|---|---|
| Resource Group | `rg-rrhh-prod` |
| Container App / Env | `ca-rrhh-prod` / `cae-rrhh-prod` |
| PostgreSQL | `pg-rrhh-prod` (db `rrhhnovedades`) |
| Key Vault | `kv-rrhh-prod` |
| Managed Identity | `id-rrhh-prod` |
| VNet | `vnet-rrhh-prod` |
| ACR / Log Analytics | **compartidos** (`rg-espert-shared`) |

## Archivos

- `Dockerfile` + `.dockerignore` — imagen de la app.
- `infra/main.bicep` (+ `main.parameters.json`) — toda la infra.
- `infra/deploy.sh` — provisión idempotente (RG, MI, AcrPull, build, deploy, secretos).
- `.github/workflows/deploy.yml` — CI/CD: build en ACR → update del Container App → smoke test.

## Provisión inicial (una vez)

1. **Requisitos:** `az login` con permisos en la suscripción; AcrPush en el ACR compartido; rol para asignar roles en el RG nuevo. Tener a mano: nombre del ACR compartido, resource id del Log Analytics compartido, API key de Humand (rotada), Account SID + Auth Token de Twilio (rotado).
2. Editar las variables de **CONFIG** al inicio de `infra/deploy.sh`.
3. Correr:
   ```bash
   ./infra/deploy.sh
   ```
   El script: crea el RG y la Managed Identity, le da AcrPull, buildea y pushea la imagen, despliega el Bicep y carga los secretos en Key Vault. Al final imprime la URL.
4. (Recomendado) Antes del paso real, revisar el plan:
   ```bash
   az deployment group what-if -g rg-rrhh-prod --template-file infra/main.bicep --parameters @infra/main.parameters.json
   ```

## Deploys siguientes (automáticos)

En cada **push a `main`**, `deploy.yml` compila+testea, buildea la imagen en el ACR (tag `{sha}`+`latest`), actualiza `ca-rrhh-prod` y corre el smoke test (`/health` + `/ready`). Purga imágenes viejas del ACR (deja 10).

Configurar en GitHub → Settings → Secrets and variables → Actions (**Variables**):
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `ACR_NAME`, `RESOURCE_GROUP` (=`rg-rrhh-prod`), `CONTAINER_APP` (=`ca-rrhh-prod`). La identidad OIDC necesita AcrPush en el ACR y Contributor en `rg-rrhh-prod`.

## Health checks

- `GET /health` — **liveness** (no toca la DB). Si falla, el orquestador reinicia el contenedor.
- `GET /ready` — **readiness** (chequea la DB). Si falla, deja de recibir tráfico.

## Primer arranque

Al levantar, la app crea las tablas (`EnsureCreated`) y siembra los 2 usuarios (`espert`). Después:
1. **Cambiar ambas contraseñas seed** de inmediato.
2. Cargar destinatarios reales del parte en **Configuración**.
3. Probar **Sincronizar hoy** (~190 empleados desde Humand) y un **envío manual** de parte.
4. El scheduler envía solo a las **07:00** y **14:00** (hora Argentina).

## Checklist Go-Live

Correr `espert-azure-standards/GO-LIVE-CHECKLIST.md`. Estado de esta app:

- ✅ Naming estándar · RG dedicado · ACR/Log compartidos
- ✅ Secretos en Key Vault + Managed Identity · rotados respecto de dev
- ✅ `httpsOnly` (ingress sin `allowInsecure`) · auth server-side (cookie)
- ✅ DB: backups 7d · SSL require · **sin** `0.0.0.0`/AllowAzureServices (VNet privada)
- ✅ Health checks liveness/readiness · logs a Log Analytics
- ✅ Deploy por workflow + smoke test · purge ACR
- ⚠️ **HA de Postgres desactivada** (SKU Burstable no la soporta). Riesgo aceptado para el piloto; subir a tier superior si pasa a crítico.
- ⚠️ **Sin `staging`** (decisión: solo prod al inicio). Agregar revisión de Container Apps cuando empiece a cambiar seguido.
- ⬜ Usuario de DB read-only para la app (hoy usa el admin). Mejora pendiente.
- ⬜ Alertas básicas (5xx, DB down, app caída) en Log Analytics.

## Pendientes de código previos a prod (recomendados)

- **Scheduler persistente:** hoy el "ya envié el parte de hoy" vive en memoria; en cada reinicio (todo deploy reinicia el contenedor) re-manda el parte. Debe chequear la tabla `EnviosParte`. **Importante con Container Apps** porque cada revisión reinicia.
- **Confirmación al enviar** parte manual (evita disparos accidentales).
