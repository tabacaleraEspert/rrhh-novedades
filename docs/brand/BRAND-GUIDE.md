# ESPERT — Guía Completa de Imagen de Marca y Estética

## Identidad Visual

| Elemento | Descripción |
|----------|-------------|
| **Empresa** | Tabacalera Espert |
| **Producto** | Tablero de Gestión Comercial |
| **Tipografía** | Inter (Google Fonts), system-ui, sans-serif |
| **Dark mode** | Sí — toggle manual con clase `dark` en `<html>`, persistido en `localStorage("theme")` |
| **Librería de gráficos** | @tremor/react (sobre Recharts) |
| **Body base** | `bg-gray-50 text-gray-900 antialiased dark:bg-gray-900 dark:text-gray-100` |

---

## 1. Paleta de Colores

### Colores Institucionales

| Token | Hex | Uso |
|-------|-----|-----|
| `espert-gold` | `#A48242` | Acento principal, bordes focus, indicadores activos |
| `espert-gold-light` | `#C4A866` | Hover, estados activos dark mode |
| `espert-gold-dark` | `#7A6132` | Texto activo sidebar, hover botones gold |
| `espert-black` | `#000000` | Fondo panel branding login |
| `espert-gray-dark` | `#53565A` | Texto secundario |
| `espert-gray` | `#97999B` | Texto terciario, subtítulos |
| `espert-gray-light` | `#F4F4F5` | Fondos claros |

### Escala Amber (override Tailwind → oro institucional PANTONE 8334 C)

```
50:  #FAF6ED    100: #F2E9D0    200: #E5D3A1
300: #D4B96A    400: #C4A866    500: #A48242
600: #8A6D37    700: #7A6132    800: #5C4A25
900: #3D3118    950: #1F190C
```

### Escala Stone (override Tailwind → gris institucional #53565A)

```
50:  #F3F4F4    100: #E0E1E2    200: #C1C3C5
300: #97999B    400: #6E7175    500: #53565A
600: #44474A    700: #36383B    800: #28292B
900: #1A1B1C    950: #0D0D0E
```

### Dark Mode — Variables CSS

| Variable | Light | Dark |
|----------|-------|------|
| `--espert-gold` | `#A48242` | `#C4A866` |
| `--espert-gold-light` | `#C4A866` | `#D4BE8A` |
| `--espert-gray-dark` | `#53565A` | `#d1d5db` |
| `--espert-gray` | `#97999B` | `#9ca3af` |

### Tremor Theme Tokens (CSS Variables)

| Variable | Light | Dark |
|----------|-------|------|
| `--tremor-brand` | `#3b82f6` | `#3b82f6` |
| `--tremor-brand-emphasis` | `#1d4ed8` | `#60a5fa` |
| `--tremor-content` | `#6b7280` | `#9ca3af` |
| `--tremor-content-emphasis` | `#374151` | `#d1d5db` |
| `--tremor-content-strong` | `#111827` | `#f9fafb` |
| `--tremor-background` | `#ffffff` | `#111827` |
| `--tremor-border` | `#e5e7eb` | `#374151` |

---

## 2. Logotipos

| Archivo | Uso |
|---------|-----|
| `isologotipo-byn.png` / `.svg` | Header, sidebar (modo claro) |
| `isologotipo-negativo.png` / `.svg` | Header, sidebar (modo oscuro), login branding |
| `isotipo-byn.png` | Favicon, sidebar colapsado (modo claro) |
| `isotipo-negativo.png` | Sidebar colapsado (modo oscuro) |
| `isologotipo-byn.jpg` | OG image, compartir |

**Tamaños estándar:**
- Header/sidebar expandido: `106×32`
- Login hero: `213×64`
- Sidebar colapsado (isotipo): `28×28`
- Favicon: `32×32`

---

## 3. Tipografía

| Rol | Clases |
|-----|--------|
| Título de página (header) | `text-base font-semibold text-gray-900 sm:text-lg dark:text-white` |
| Título de sección (admin) | `text-2xl font-bold text-gray-900 dark:text-white` |
| KPI grande (metric) | `text-3xl font-bold` / Tremor `<Metric>` |
| Título card | Tremor `<Title>` / `text-tremor-title` |
| Subtítulo card | Tremor `<Text>` / `text-tremor-default` |
| Body | `text-sm text-gray-600 dark:text-gray-400` |
| Label | `text-xs font-medium text-gray-500 dark:text-gray-400` / `text-tremor-label` |
| Texto diminuto | `text-xs text-gray-400` |
| Contador caracteres | `text-right text-xs text-gray-400` |

---

## 4. Layout

### Shell del Dashboard

```
Container:     flex min-h-screen bg-gray-50 dark:bg-gray-900
Content area:  min-w-0 flex-1 transition-all duration-300
Main padding:  p-4 sm:p-6
Spacing entre secciones: space-y-6
```

### Header Bar (sticky)

```
sticky top-0 z-30 flex h-16 items-center justify-between border-b border-gray-200 bg-white px-4 sm:px-6 dark:border-gray-700 dark:bg-gray-800
```

### Grids Responsivos

| Patrón | Uso |
|--------|-----|
| `grid grid-cols-2 gap-4 lg:grid-cols-4` | KPIs (4 columnas) |
| `grid grid-cols-1 gap-4 xl:grid-cols-2 sm:gap-6` | Dos columnas |
| `grid grid-cols-1 gap-6 lg:grid-cols-3` | Tres columnas |

---

## 5. Sidebar

```
Container:  fixed inset-y-0 left-0 z-30 flex flex-col border-r border-gray-200 bg-white transition-all duration-300 dark:border-gray-700 dark:bg-gray-800
Expandida:  w-60
Colapsada:  w-16 max-sm:-translate-x-full

Header:     flex h-16 items-center border-b border-gray-200 dark:border-gray-700
Nav:        mt-4 flex-1 space-y-1 px-2
Footer:     border-t border-gray-200 p-3 dark:border-gray-700
```

### Nav Items

```
Activo:   flex items-center gap-3 rounded-md px-3 py-2.5 text-sm font-medium bg-espert-gold/10 text-espert-gold-dark dark:bg-espert-gold/20 dark:text-espert-gold-light
Inactivo: flex items-center gap-3 rounded-md px-3 py-2.5 text-sm font-medium text-espert-gray-dark hover:bg-gray-100 hover:text-gray-900 dark:text-gray-400 dark:hover:bg-gray-700 dark:hover:text-white
```

### Mobile Backdrop

```
fixed inset-0 z-20 bg-black/50 backdrop-blur-sm
```

### Avatar Colapsado

```
h-8 w-8 rounded-full bg-espert-gold/20 flex items-center justify-center
Text: text-xs font-bold text-espert-gold-dark
```

---

## 6. Botones

### Primario (Gold)

```
rounded-lg bg-espert-gold px-4 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-espert-gold-dark focus:outline-none focus:ring-2 focus:ring-espert-gold focus:ring-offset-2 disabled:opacity-50 transition-colors
```

### Primario (Blue) — contexto Admin

```
rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700
```

### Primario (Green) — Admin crear

```
rounded-md bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50
```

### Secundario / Cancel

```
rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50 dark:border-gray-600 dark:text-gray-400 dark:hover:bg-gray-700
```

### Icon Button

```
rounded-lg p-2 text-gray-500 hover:bg-gray-100 dark:text-gray-400 dark:hover:bg-gray-700 transition-colors
```

### FAB (Floating Action Button)

```
fixed bottom-6 right-6 z-40 flex h-12 w-12 items-center justify-center rounded-full bg-espert-gold text-white shadow-lg transition-transform hover:scale-110 hover:bg-espert-gold-dark focus:outline-none focus:ring-2 focus:ring-espert-gold/50
```

### Destructivo (texto)

```
text-sm font-medium text-red-600 hover:text-red-800
```

### Activar (texto)

```
text-sm font-medium text-green-600 hover:text-green-800
```

### Logout

```
mt-2 text-xs text-red-500 hover:text-red-700
```

### Disabled (global)

```
disabled:opacity-50
```

---

## 7. Cards

### Card Estándar

```
rounded-lg bg-white p-6 shadow-sm ring-1 ring-gray-100 dark:bg-gray-800 dark:ring-gray-700
```

### Card Elevada (modales, login)

```
rounded-xl bg-white p-6 shadow-2xl dark:bg-gray-800
```

### Card con Decoración (KPI — Tremor)

```
<Card decoration="top" decorationColor="amber|green|blue|stone|red|violet|emerald|rose">
```

### Lógica de Color KPI

| decorationColor | Semántica |
|-----------------|-----------|
| `amber` | Métrica gold / estándar |
| `green` | Positivo, logrado |
| `blue` | Informativo |
| `stone` | Neutro / secundario |
| `red` | Crítico / peligro |
| `violet` | Especial |
| `emerald` | Alta efectividad |
| `rose` | Baja efectividad |

---

## 8. Inputs y Formularios

### Text Input (estilo principal)

```
mt-1 block w-full rounded-lg border border-gray-300 px-3 py-2.5 text-sm text-gray-900 bg-white shadow-sm focus:border-espert-gold focus:outline-none focus:ring-1 focus:ring-espert-gold
```

### Text Input (estilo admin)

```
mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500
```

### Textarea

```
w-full rounded-lg border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-900 placeholder-gray-400 focus:border-espert-gold focus:outline-none focus:ring-1 focus:ring-espert-gold dark:border-gray-600 dark:bg-gray-700 dark:text-white dark:placeholder-gray-500
```

### Select Nativo

```
rounded-md border border-gray-300 bg-white px-2 py-1.5 text-xs font-medium text-gray-700 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-300 focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500
```

### Label

```
block text-sm font-medium text-gray-700 dark:text-gray-300
```

### Búsqueda en Dropdown

```
w-full rounded border border-gray-300 bg-white px-2 py-1 text-xs text-gray-700 placeholder-gray-400 focus:border-amber-500 focus:outline-none dark:border-gray-600 dark:bg-gray-700 dark:text-gray-200
```

---

## 9. Filtros

### Category Filter (Tab-style)

```
Container: flex items-center gap-1 rounded-lg bg-gray-100 p-1 dark:bg-gray-700
Activo:    rounded-md px-3 py-1.5 text-xs font-medium bg-white text-espert-gold-dark shadow-sm dark:bg-gray-600 dark:text-espert-gold-light
Inactivo:  rounded-md px-3 py-1.5 text-xs font-medium text-gray-600 hover:text-gray-900 dark:text-gray-400 dark:hover:text-white
```

### Filter Dropdown Button

```
Default:  flex items-center gap-1 rounded-md border border-gray-300 bg-white px-2 py-1.5 text-xs font-medium text-gray-700 hover:border-amber-500 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-300
Activo:   flex items-center gap-1 rounded-md border border-amber-500 bg-amber-50 px-2 py-1.5 text-xs font-medium text-amber-700 dark:bg-amber-900/20 dark:text-amber-300
```

### Dropdown Panel

```
absolute right-0 top-full z-50 mt-1 w-48 rounded-lg border border-gray-200 bg-white py-1 shadow-lg dark:border-gray-700 dark:bg-gray-800
```

### Dropdown Item

```
Normal:     w-full px-3 py-1.5 text-left text-xs text-gray-600 hover:bg-gray-50 dark:text-gray-300 dark:hover:bg-gray-700
Seleccionado: bg-amber-50 font-medium text-amber-700 dark:bg-amber-900/20 dark:text-amber-300
```

### Checkbox en Dropdown

```
Checked:   inline-block h-3 w-3 rounded border border-amber-500 bg-amber-500
Unchecked: inline-block h-3 w-3 rounded border border-gray-300 dark:border-gray-500
```

### Chip de Línea (toggle)

```
Activo:   rounded-full px-2 py-0.5 text-xs font-medium bg-espert-gold text-white
Inactivo: rounded-full px-2 py-0.5 text-xs font-medium bg-gray-100 text-gray-600 hover:bg-gray-200 dark:bg-gray-700 dark:text-gray-400 dark:hover:bg-gray-600
```

### Day/Preset Toggle

```
Activo:   rounded px-2 py-1 text-xs font-medium bg-amber-500 text-white
Inactivo: rounded px-2 py-1 text-xs font-medium bg-gray-100 text-gray-600 hover:bg-amber-100 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-amber-900/30
```

### Feedback Type Selector

```
Activo:   rounded-lg border border-espert-gold bg-espert-gold/10 px-3 py-1.5 text-sm font-medium text-espert-gold-dark dark:bg-espert-gold/20 dark:text-espert-gold-light
Inactivo: rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-600 hover:border-gray-300 dark:border-gray-600 dark:text-gray-400 dark:hover:border-gray-500
```

---

## 10. Badges y Pills

### Filtro Activo (pill)

```
inline-flex items-center gap-1 rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-900/40 dark:text-amber-200
```

### Container de Filtros Activos

```
mb-4 flex flex-wrap items-center gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 dark:border-amber-900/50 dark:bg-amber-950/20
```

### "Limpiar todo"

```
ml-auto text-xs font-medium text-red-600 hover:text-red-800 dark:text-red-400 dark:hover:text-red-300
```

### Badge Inline (contexto)

```
Blue: inline-flex items-center gap-1 rounded-full bg-blue-50 px-2 py-0.5 text-xs font-medium text-blue-800 dark:bg-blue-900/30 dark:text-blue-300
Gold: inline-flex rounded-full bg-espert-gold/10 px-2 py-0.5 text-xs font-medium text-espert-gold-dark
Gray: rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-700 dark:bg-gray-700 dark:text-gray-300
```

### Badge de Rol

```
Admin: inline-flex rounded-full px-2 text-xs font-semibold leading-5 bg-purple-100 text-purple-800
Otro:  inline-flex rounded-full px-2 text-xs font-semibold leading-5 bg-blue-100 text-blue-800
```

### Badge de Status

```
Activo:   inline-flex rounded-full px-2 text-xs font-semibold leading-5 bg-green-100 text-green-800
Inactivo: inline-flex rounded-full px-2 text-xs font-semibold leading-5 bg-red-100 text-red-800
```

### Badge de Ranking

```
Top 3: inline-flex h-6 w-6 items-center justify-center rounded-full text-xs font-bold bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300
Otro:  inline-flex h-6 w-6 items-center justify-center rounded-full text-xs font-bold bg-gray-100 text-gray-600 dark:bg-gray-700 dark:text-gray-400
```

### Tremor Badges

```
<Badge color="emerald|amber|rose|red|orange|blue" size="xs|lg">
<BadgeDelta deltaType="increase|decrease|unchanged" size="xs">
```

---

## 11. Tablas

### Tabla Nativa (Admin)

```
Container: overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm dark:border-gray-700 dark:bg-gray-800
Table:     min-w-full divide-y divide-gray-200
Thead:     bg-gray-50 dark:bg-gray-700
Th:        px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500
Tbody:     divide-y divide-gray-200
Td:        whitespace-nowrap px-6 py-4 text-sm text-gray-900 dark:text-white
```

### Fila Interactiva (hover)

```
cursor-pointer transition-colors hover:bg-amber-50 dark:hover:bg-amber-950/20
```

### Filas de Riesgo (Alertas)

```
Crítico: bg-red-50 dark:bg-red-950/60
Alto:    bg-orange-50 dark:bg-orange-950/40
```

### Heatmap

```
Tabla:        w-max border-separate border-spacing-1 text-[10px]
Header sticky: sticky top-0 z-[2] bg-white px-1 py-1.5 text-center font-semibold text-gray-600 dark:bg-gray-800 dark:text-gray-400
Col sticky:    sticky left-0 z-[1] max-w-[140px] truncate bg-white px-2 py-0.5 text-xs text-gray-700 dark:bg-gray-800 dark:text-gray-300
Container:     isolate relative mt-3 w-full overflow-auto max-h-[500px]
```

Escala de intensidad (heatmap):

| Intensidad | Light | Dark |
|------------|-------|------|
| 0% | `bg-gray-100` | `dark:bg-gray-700` |
| <15% | `bg-amber-100` | `dark:bg-amber-900/30` |
| <30% | `bg-amber-200` | `dark:bg-amber-800/40` |
| <50% | `bg-amber-300` | `dark:bg-amber-700/50` |
| <70% | `bg-amber-400` | `dark:bg-amber-600/60` |
| ≥70% | `bg-amber-500` | `dark:bg-amber-500` |

---

## 12. Gráficos

### Bar Chart

```
Colores: amber(#f59e0b), stone(#a8a29e), blue(#3b82f6), emerald(#10b981), rose(#f43f5e), violet(#8b5cf6), cyan(#06b6d4), orange(#f97316)
Bar radius: [4, 4, 0, 0]
Max bar size: 50
Animación: 800ms
Tooltip: rounded-[0.5rem], font-size 12px, fondo según tema
Grid: strokeDasharray="3 3"
Axis ticks: fontSize 11
Labels: fontSize 10px, fontWeight 500
```

### Donut Chart

```
Paleta (GOLD_PALETTE): #A48242, #0891B2, #7C3AED, #059669, #E11D48, #53565A, #D97706
innerRadius: 45%, outerRadius: 70%
Label text: text-xs fill-gray-700 dark:fill-gray-300
```

### Line Chart (Tremor)

```
className="mt-4 h-64"
colors={["stone", "amber"]}
curveType="monotone"
```

### Tabs de Gráfico (Tremor)

```
<TabGroup>
  <TabList variant="solid" color="amber">
  <TabPanels>
Scroll responsive: overflow-x-auto -mx-4 px-4 sm:mx-0 sm:px-0
```

---

## 13. Gauge (Semicircular SVG)

```
Container: flex flex-col items-center
Segmentos de color:
  Rojo (0-50):     #ef4444
  Amarillo (50-65): #fbbf24
  Ámbar (65-75):   #f59e0b
  Lima (75-90):    #84cc16
  Verde (90-100):  #22c55e
Track fondo: stroke="#e5e7eb" dark:stroke-gray-700
Aguja:       stroke="#1f2937" dark:stroke-gray-200
Valor:       fill-gray-900 dark:fill-white (bold, grande)
Label:       mt-1 text-sm font-medium text-gray-500 dark:text-gray-400
```

---

## 14. Barras de Progreso

### Barra Principal (Objetivos)

```
Track:  h-7 w-full rounded bg-gray-100 dark:bg-gray-700
Fill:   flex h-7 items-center rounded px-3 text-sm font-bold
  ≥100%: bg-green-600 text-white
  <100%: bg-amber-500 text-white
  Objetivo (siempre full): bg-amber-400 text-gray-900
```

### Mini Barra por Vendedor

```
h-5 rounded bg-amber-400 (objetivo)
h-5 rounded bg-amber-600 (real)
Text: px-1 text-xs font-bold text-gray-900 | text-white
```

### Tremor ProgressBar

```
<ProgressBar value={pct} color="amber|green" />
```

---

## 15. Toasts / Notificaciones

### Container

```
fixed bottom-4 right-4 z-50 flex flex-col gap-2 pointer-events-none
```

### Item Base

```
pointer-events-auto flex items-center gap-3 rounded-lg border px-4 py-3 shadow-lg animate-slide-in
```

### Variantes

| Tipo | Fondo & Borde |
|------|---------------|
| success | `bg-emerald-50 text-emerald-800 border-emerald-200 dark:bg-emerald-950/80 dark:text-emerald-200 dark:border-emerald-800` |
| error | `bg-red-50 text-red-800 border-red-200 dark:bg-red-950/80 dark:text-red-200 dark:border-red-800` |
| warning | `bg-amber-50 text-amber-800 border-amber-200 dark:bg-amber-950/80 dark:text-amber-200 dark:border-amber-800` |
| info | `bg-blue-50 text-blue-800 border-blue-200 dark:bg-blue-950/80 dark:text-blue-200 dark:border-blue-800` |

### Icono Circular

```
flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-bold
success: bg-emerald-100 text-emerald-600 dark:bg-emerald-900 dark:text-emerald-300
error:   bg-red-100 text-red-600 dark:bg-red-900 dark:text-red-300
warning: bg-amber-100 text-amber-600 dark:bg-amber-900 dark:text-amber-300
info:    bg-blue-100 text-blue-600 dark:bg-blue-900 dark:text-blue-300
```

### Animación

```css
@keyframes slide-in {
  from { opacity: 0; transform: translateX(100%); }
  to { opacity: 1; transform: translateX(0); }
}
.animate-slide-in { animation: slide-in 0.25s ease-out; }
```

---

## 16. Modales / Diálogos

```
Backdrop:  fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm
Panel:     mx-4 w-full max-w-md rounded-xl bg-white p-6 shadow-2xl dark:bg-gray-800
Título:    text-lg font-semibold text-gray-900 dark:text-white
Cerrar:    rounded p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200
```

### Estado Éxito (en modal)

```
Container:  flex flex-col items-center gap-3 py-8
Icono:      flex h-14 w-14 items-center justify-center rounded-full bg-emerald-100 dark:bg-emerald-900/40
SVG:        h-7 w-7 text-emerald-600 dark:text-emerald-400
Título:     text-lg font-semibold text-gray-900 dark:text-white
Subtítulo:  text-sm text-gray-500 dark:text-gray-400
```

---

## 17. Tooltips

```
Trigger: relative ml-1 inline-flex cursor-help
Icono:   h-3.5 w-3.5 text-gray-400 dark:text-gray-500
Burbuja: absolute bottom-full left-1/2 z-50 mb-2 -translate-x-1/2 whitespace-nowrap rounded bg-gray-900 px-2.5 py-1.5 text-xs text-white shadow-lg dark:bg-gray-100 dark:text-gray-900
Flecha:  absolute left-1/2 top-full -translate-x-1/2 border-4 border-transparent border-t-gray-900 dark:border-t-gray-100
```

---

## 18. Estados de Carga (Skeleton)

```
Pulse: animate-pulse rounded bg-gray-200 dark:bg-gray-700
```

### KPI Skeleton

```
grid grid-cols-2 gap-4 lg:grid-cols-4
Card → 3 pulses: h-3 w-20, h-8 w-28, h-2 w-32
```

### Chart Skeleton

```
Card → pulses + flex items-end gap-2 (alturas variables)
```

### Table Skeleton

```
Card → 5 filas con flex gap-4 → (h-4 flex-[2], h-4 flex-1, h-4 w-16)
```

---

## 19. Estados de Error

### Error en Card (con borde lateral)

```
Card className="border-l-4 border-l-red-500"
Icono: mt-0.5 flex h-6 w-6 items-center justify-center rounded-full bg-red-100 text-sm font-bold text-red-700 dark:bg-red-900/30 dark:text-red-400
Título: font-semibold text-gray-900 dark:text-white
Mensaje: mt-1 text-sm text-gray-600 dark:text-gray-400
```

### Error en Login

```
rounded-lg bg-red-50 p-3
Text: text-sm text-red-700
```

### Error Inline

```
text-sm text-red-600 dark:text-red-400
```

---

## 20. Página Error / 404

### Full-page

```
Container: flex min-h-screen items-center justify-center bg-gray-50 dark:bg-gray-900 px-4
Error:     text-5xl font-bold text-red-500
404:       text-6xl font-bold text-espert-gold
Heading:   mt-4 text-2xl font-bold text-gray-900 dark:text-white
Message:   mt-2 text-gray-600 dark:text-gray-400
CTA:       mt-6 inline-block rounded-md bg-espert-gold px-6 py-2.5 text-sm font-medium text-white hover:bg-espert-gold-dark transition-colors
```

### Dashboard-level Error

```
Container: flex flex-col items-center justify-center py-20
Heading:   text-4xl (error) / text-xl (subtítulo)
```

---

## 21. Estado Vacío

```
flex items-center justify-center py-8
Text: text-gray-400 (Tremor Text)
```

---

## 22. Colores Condicionales (Lógica de negocio)

### Valores Positivo/Negativo

```
Positivo: text-green-600
Negativo: text-red-600
```

### Cumplimiento

```
≥100%: bg-green-600 text-white
<100%: bg-amber-500 text-white
```

### Efectividad (Badge dinámico)

```
≥70%: color="emerald"
≥50%: color="amber"
<50%: color="rose"
```

### Riesgo por Días Sin Compra

```
>45 días: text-red-600 font-bold
>30 días: text-amber-600 font-bold
Default:  text-gray-700 dark:text-gray-300
```

---

## 23. Transiciones y Animaciones

| Elemento | Clases |
|----------|--------|
| Sidebar | `transition-all duration-300` |
| Botones/links | `transition-colors` |
| FAB | `transition-transform hover:scale-110` |
| Toast slide-in | `animation: slide-in 0.25s ease-out` |

---

## 24. Iconos

- Estilo: SVG inline, stroke-based (outline)
- Tamaño nav: `h-5 w-5`
- Tamaño pequeño (tooltip, close): `h-3.5 w-3.5`
- Tamaño close: `h-5 w-5`
- strokeWidth: `1.5` (nav) / `2` (acciones)

---

## 25. Login — Composición

```
┌─────────────────────┬─────────────────────┐
│                     │                     │
│   PANEL BRANDING    │   PANEL FORM        │
│   bg-espert-black   │   bg-gray-50        │
│                     │                     │
│   isologotipo-neg   │   card rounded-xl   │
│   ─── gold line ─── │   shadow-sm ring-1  │
│   "Gestión Comerc." │   inputs + submit   │
│                     │                     │
└─────────────────────┴─────────────────────┘
  (hidden en mobile)     (full-width mobile)
```

- Panel izquierdo: `hidden lg:flex lg:w-1/2 bg-espert-black` + acentos circulares `bg-espert-gold/5 rounded-full`
- Divider decorativo: `h-0.5 w-16 bg-espert-gold mx-auto`
- Panel derecho: `flex flex-1 items-center justify-center bg-gray-50`
- Form card: `w-full max-w-sm rounded-xl bg-white p-6 sm:p-8 shadow-sm ring-1 ring-gray-100`
- Mobile: solo panel derecho con logo centrado + divider gold

---

## 26. Stack Tecnológico

| Capa | Tecnología |
|------|-----------|
| Framework | Next.js 16 (App Router) |
| Estilos | Tailwind CSS 3.4 + custom config |
| Gráficos | @tremor/react (KPIs, barras, donuts, áreas, tabs) |
| Charts custom | Recharts (bar, donut con labels) |
| Iconos | SVG inline custom (24×24, stroke) |
| Fuente | Inter via `next/font/google` |
| Dark mode | `darkMode: "class"` en Tailwind, toggle manual |

---

## 27. Cómo Replicar en Otro Proyecto

1. Copiar la carpeta `public/brand/` completa (logos + esta guía)
2. Instalar Tailwind + configurar la paleta `espert` y los overrides de `amber`/`stone` en `tailwind.config.ts`
3. Agregar las CSS variables `--espert-*` y `--tremor-*` en `globals.css` (light y dark)
4. Usar `Inter` como font principal via next/font o CDN
5. Aplicar los patrones documentados arriba
6. **Focus siempre en dorado**: `focus:border-espert-gold focus:ring-espert-gold`
7. **Hover en sidebar/filtros**: dorado suave `bg-espert-gold/10`
8. **Cards**: `rounded-lg` para standard, `rounded-xl` para elevadas
9. **Tablas**: bordes con `divide-y`, hover `amber-50`
10. **Toasts**: posición bottom-right, slide-in desde derecha
