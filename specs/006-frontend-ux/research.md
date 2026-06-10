# Research — Frontend / UX

## Decisiones evaluadas

### 1. Vue como SPA vs Razor + islas Vue

| Opción | Pros | Contras | Decisión |
|---|---|---|---|
| SPA Vue + Vite | UX moderna, navegación sin recarga | Node + Vite obligatorio, JWT, doble deploy | ❌ |
| **Razor + islas Vue** | Encaja con MVC 5, auth cookies estándar, cero pipeline Node, SEO/back gratis | Recarga entre páginas | ✅ |

Decisión confirmada con el director del proyecto y el equipo: priorizar simplicidad operativa.

### 2. Bootstrap 5 vs shadcn-vue vs PrimeVue

| Opción | Look | Pipeline | Madurez .NET | Decisión |
|---|---|---|---|---|
| Bootstrap 5 + theme propio | "Bootstrap moderno", aceptable | Sin Node | Total | ✅ |
| shadcn-vue + Tailwind | Clavado al ref de Nexus | Vite + Tailwind | Media | ❌ |
| PrimeVue Aura | Profesional, completo | Build recomendado | Media | ❌ |

Se conserva Bootstrap 5 con la indicación de usar **shadcn/Nexus como referencia visual** para los detalles (espaciado, bordes, neutrales, sombras).

### 3. Manejo de assets

- **Vendored**: copia local en `wwwroot/lib/`. Necesario por restricción de internet en runtime hospitalario.
- **CDN**: descartado.

### 4. Iconos

- **Bootstrap Icons** sobre Feather/Lucide: por consistencia con Bootstrap base y disponibilidad como SVG sprite. Lucide era candidato por afinidad con Nexus, pero suma una dependencia más.

### 5. Tablas y grillas

- **DataTables** sobre AG Grid: AG Grid es superior pero su build comunitario es grande (~300KB). DataTables alcanza para el alcance del MVP.
- Para grillas con re-render reactivo intenso (Q2), no se usa DataTables — la isla Vue maneja su propio render.

### 6. Gráficos

- **Chart.js 4** sobre ApexCharts/ECharts: peso menor, ecosistema más conocido por el equipo, suficiente para gráficos del MVP (barras, torta, histograma).

### 7. Drag-and-drop

- Necesario en Q2 (editor de cuestionarios).
- **SortableJS** vendored + integración manual con Vue. Estable, sin dependencias, 30KB.

### 8. Validación cliente

- Razor: `jquery.validate.unobtrusive` (viene con MVC 5).
- Vue: validación reactiva manual + reglas declarativas en `enableWhen` del cuestionario.

### 9. i18n

- Razor: `Resources/.resx` con `IStringLocalizer`-like helper. Cultura por cookie.
- Vue: `vue-i18n` (vendored ~16KB) o solución casera basada en `provide/inject`. Decisión final en spec 007.

### 10. Forma de los wireframes en specs

- **ASCII** dentro de cada archivo de pantalla para revisión rápida.
- **Pencil (.pen)** para 4-6 pantallas estrella (T2 Home, Q2 Editor, R3 Renderizador, E2 Query Builder, A1 Login, A6 Auditoría).

## Referencia visual: Nexus

Proyecto interno `~/Documents/Nexus/nexus-web` (Next.js + shadcn). Se toman como referencia:
- Espaciado generoso entre cards (gap 16-24px).
- Bordes 1px en neutral-200, sin sombra por defecto.
- Headings semibold, body 14px.
- Estados de hover sutiles (fondo neutral-50).
- Iconos lucide 16-18px alineados al texto.
- Empty states con ilustración mínima + tono institucional.

ChequeAR replica el lenguaje sin clonar — Bootstrap 5 sigue siendo la base estructural.

## Branding Garrahan

- Logo: `https://www.garrahan.gov.ar/images/logo2.png` (a vendor en `Content/img/`).
- Paleta primaria extraída de su CSS: `#112855`, `#224faa`, `#2a69b8`, `#424077`.
- Zonas de color del hospital (rojo/violeta/verde/amarillo/azul/naranja): usadas como acento por especialidad en chips de filtrado.
- Tipografía: stack de sistema (heredado del sitio oficial).

## Open Questions

- ¿Gestión de usuarios (A4, A5) se delega 100% al sistema hospitalario? — Pendiente confirmación; afecta cantidad de pantallas P2.
- ¿Tema oscuro? — Out of scope MVP. Variables CSS preparadas para futura activación.
- ¿Soporte tablet en R1/R3? — Sí declarado en spec. Confirmar resolución mínima del dispositivo provisto por el hospital.
