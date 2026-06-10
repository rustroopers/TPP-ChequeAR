# Implementation Plan: Frontend / UX

**Branch**: `006-frontend-ux` | **Date**: 2026-06-10 | **Spec**: ./spec.md

## Summary

Implementar la capa de presentación de ChequeAR como una mezcla de **Razor (.cshtml)** para todas las páginas navegacionales + **islas Vue 3** para las tres pantallas con lógica reactiva intensa (Q2 editor de cuestionarios, R3 renderizador clínico, E2 query builder). Bootstrap 5 como base de componentes, custom theme SCSS para alinear el look a la referencia visual de Nexus. Branding del Hospital Garrahan.

## Technical Context

**Language/Version**: C# 7.3 (Razor) + JavaScript ES2020 (Vue islands)
**Primary Dependencies**:
- Backend: ASP.NET MVC 5, ASP.NET Web API 2, Razor 3
- Frontend: Bootstrap 5.3, Vue 3 (global build, vendored), Bootstrap Icons, Chart.js 4, DataTables 1.13
- SCSS compiler: `LibSassHost` o build externo via npm script opcional
**Storage**: n/a (UI consume APIs definidas en specs 001-005)
**Testing**: Playwright para E2E P1; axe-core para accesibilidad; xUnit + WebApi.TestHost para Controllers Razor
**Target Platform**: IIS 10 / Windows Server 2016
**Project Type**: web (MVC + Web API en mismo proyecto `ChequeAR.Api`)
**Performance Goals**: LCP ≤ 2.5s, CLS < 0.1, INP < 200ms
**Constraints**: sin internet en runtime → todos los assets vendored en `wwwroot/lib/`
**Scale/Scope**: 36 pantallas (18 P1 prioritarias), ~200 usuarios concurrentes pico

## Constitution Check

- ✅ Package by Layer respetado (Views/Controllers en `ChequeAR.Api`).
- ✅ Toda interacción con dominio pasa por servicios de `ChequeAR.Application` (no hay acceso directo a EF desde Razor).
- ✅ Auditoría (spec 005) integrada en acciones de UI relevantes.
- ✅ WCAG 2.1 AA declarado y validado en CI.
- ✅ Sin datos clínicos en client-side storage.

## Arquitectura Razor + Islas Vue

### Patrón

```
┌─────────────────────────────────────────────────────────┐
│ Razor View (_Layout + Página)                           │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │ HTML / Bootstrap estático                        │  │
│  │                                                  │  │
│  │  <div data-vue-island="questionnaire-renderer"   │  │
│  │       data-props='@Html.Raw(Model.IslandProps)'> │  │
│  │  </div>                                          │  │
│  └──────────────────────────────────────────────────┘  │
│                                                         │
│  <script src="~/Scripts/islands/bootstrap.js"></script>│
└─────────────────────────────────────────────────────────┘
```

- `bootstrap.js` recorre `[data-vue-island]`, importa el módulo correspondiente y hace `createApp(Component, props).mount(el)`.
- Cada isla es un módulo IIFE independiente compilado a partir de `.vue` (build con Vite externo) o un `.js` con `Vue.defineComponent` cuando la complejidad es baja.

### Decisión de build

Dos modos coexistentes:

1. **Sin build (MVP)**: islas escritas en JS plano con `Vue.defineComponent` y `template: \`...\``. Cargadas como `<script type="module">`. Cero pipeline Node.
2. **Con build (post-MVP, opcional)**: si la complejidad crece, agregar Vite en `frontend-build/` que genera `wwwroot/Scripts/islands/*.iife.js`. No bloquea el MVP.

Para el MVP se elige modo (1) — coherente con el equipo, sin Node en CI.

## Project Structure

```text
specs/006-frontend-ux/
├── spec.md
├── design-system.md
├── plan.md
├── research.md
├── quickstart.md
├── tasks.md
├── screens/
│   ├── 00-transversales.md
│   ├── 01-gestor.md
│   ├── 02-renderizado.md
│   ├── 03-fhir.md
│   ├── 04-analisis.md
│   └── 05-seguridad.md
└── mockups/
    └── (archivos .pen — generados con Pencil MCP)
```

### Source Code (repository root)

```text
src/ChequeAR.Api/
├── Views/
│   ├── Shared/
│   │   ├── _Layout.cshtml
│   │   ├── _Navbar.cshtml
│   │   ├── _Sidebar.cshtml
│   │   ├── _Breadcrumbs.cshtml
│   │   ├── _Toasts.cshtml
│   │   └── _LanguageSwitcher.cshtml
│   ├── Home/
│   │   └── Index.cshtml                  # T2 Dashboard
│   ├── Account/                          # A1 Login, A2 sesión expirada
│   ├── Users/                            # A4 listado, A5 detalle
│   ├── Audit/                            # A6 visor, A7 detalle
│   ├── Questionnaires/                   # Q1, Q2 (host de isla), Q5, Q6, Q7, Q9
│   ├── Encounters/                       # R1, R2, R3 (host de isla), R6, R7
│   ├── Reports/                          # E1, E2 (host de isla), E3, E4
│   ├── Admin/                            # F1, F2
│   └── Error/                            # T4
├── Controllers/
│   ├── HomeController.cs
│   ├── AccountController.cs
│   ├── UsersController.cs
│   ├── AuditController.cs
│   ├── QuestionnairesController.cs
│   ├── EncountersController.cs
│   ├── ReportsController.cs
│   ├── AdminController.cs
│   └── ErrorController.cs
├── Scripts/
│   ├── islands/
│   │   ├── bootstrap.js                  # discovery + mount
│   │   ├── questionnaire-editor/         # isla Q2
│   │   ├── questionnaire-renderer/       # isla R3
│   │   └── report-builder/               # isla E2
│   └── pages/                            # JS Razor-only (jQuery + Vanilla)
├── Content/
│   ├── theme/
│   │   ├── _variables.scss
│   │   ├── _components.scss
│   │   ├── _utilities.scss
│   │   └── chequear-theme.scss           # entry
│   └── img/
│       ├── garrahan-logo.png
│       ├── garrahan-logo-white.svg
│       └── empty-states/
└── wwwroot/lib/
    ├── bootstrap/ (5.3.x)
    ├── bootstrap-icons/
    ├── vue/ (vue.global.prod.js)
    ├── chart.js/
    └── datatables/
```

### Bundling (BundleConfig)

```csharp
bundles.Add(new StyleBundle("~/Content/css").Include(
    "~/wwwroot/lib/bootstrap/css/bootstrap.min.css",
    "~/wwwroot/lib/bootstrap-icons/bootstrap-icons.css",
    "~/Content/theme/chequear-theme.css"  // compilado de SCSS
));

bundles.Add(new ScriptBundle("~/bundles/vendor").Include(
    "~/wwwroot/lib/jquery/jquery-{version}.js",
    "~/wwwroot/lib/bootstrap/js/bootstrap.bundle.min.js",
    "~/wwwroot/lib/vue/vue.global.prod.js"
));

bundles.Add(new ScriptBundle("~/bundles/islands").Include(
    "~/Scripts/islands/bootstrap.js"
));
```

## Phasing

| Fase | Alcance | Salida |
|---|---|---|
| F0 — Foundations | Solución, BundleConfig, _Layout, theme SCSS, Navbar+Sidebar+Toasts, T4 Error, sistema i18n base (de la 007) | Shell navegable |
| F1 — Auth + Home | A1 Login, A2 expired, T2 Home (con datos mock), perfil T5 | Usuario logueado y orientado |
| F2 — Razor screens P1 | Q1, Q5, Q6, Q7, R1, R2, R6, R7, E1, E3, E4, A6, A7 | 13 pantallas Razor entregadas |
| F3 — Vue islands P1 | Q2 (editor), R3 (renderizador), E2 (query builder) | 3 islas críticas funcionando |
| F4 — P2/P3 + polish | Q3, Q4, Q8, Q9, R4, R5, F1, F2, E5, E6, A3, A4, A5 | Cobertura completa |
| F5 — A11y + i18n hardening | Auditoría axe, screen reader, traducciones es/en | Cumplimiento WCAG AA |

## Risks

| Riesgo | Mitigación |
|---|---|
| Q2 (editor) demanda complejidad mayor a la prevista en modo sin-build | Acotar Q2 a tipos `string, integer, decimal, boolean, choice, date`; tipos avanzados a F4 |
| Chart.js + DataTables suman peso en E3 | Lazy load por ruta — sólo se incluye en Views/Reports |
| Custom theme SCSS desincronizado con Bootstrap upgrades | Pin Bootstrap a 5.3.x; revisar variables al subir versión menor |
| Accesibilidad en R3 con `enableWhen` dinámico | Live region `aria-live="polite"` + foco gestionado tras toggle |

## Dependencies

- spec 005 (seguridad) — define sesión, roles, claims.
- spec 007 (i18n) — define cookie y resolver de cultura.
- specs 001/002/004 — contratos de API consumidos por islas.
