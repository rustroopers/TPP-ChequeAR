# Quickstart — Frontend / UX

## Requisitos

- Visual Studio 2022 con workload "ASP.NET y desarrollo web".
- .NET Framework 4.7.1 Developer Pack.
- (Opcional) Node 20+ si se quiere recompilar el SCSS desde fuente; alternativamente se versionan los `.css` generados.

## Levantar localmente

1. Abrir `src/ChequeAR.sln`.
2. Restaurar paquetes NuGet (`nuget restore`).
3. F5 sobre `ChequeAR.Api`.
4. La aplicación abre en `https://localhost:44300/`. Login con usuario mock (ver spec 005 §Dev).

## Estructura de assets

- `wwwroot/lib/` — librerías vendored (no editar).
- `Content/theme/chequear-theme.scss` — tema. Compilar con `npm run theme` (script en `package.json` raíz, opcional).
- `Scripts/islands/` — islas Vue. Editar `.js` directamente; no requiere build en MVP.

## Convenciones de Razor

- Layouts en `Views/Shared/_Layout.cshtml`.
- Una vista por pantalla; el nombre del archivo = código de pantalla (`Q1.cshtml`, `R3.cshtml`, etc.).
- Cada vista declara su `ViewBag.PageId = "Q1"` para que el sidebar resalte el módulo activo.
- Helpers: `@Html.CqBadgeStatus(item.Status)`, `@Html.CqEmptyState(...)`, definidos en `Helpers/CqHtmlHelpers.cs`.

## Convenciones de islas Vue

- Cada isla vive en `Scripts/islands/<feature>/index.js` con `export default { name, props, setup, template }`.
- Se monta vía `data-vue-island="<feature>"` y props serializados en `data-props='{...}'` (JSON).
- Estado interno con `ref` / `reactive`. Sin Vuex/Pinia en MVP.
- Llamadas HTTP con `fetch` y headers `Accept: application/fhir+json`. Token CSRF en `X-XSRF-TOKEN` desde meta tag.

## Cambiar idioma

- Cookie `chequear_lang` = `es-AR` | `en`.
- Cambia desde el selector en navbar (`_LanguageSwitcher.cshtml`).
- Resources `.resx` en `App_GlobalResources/`. Detalle en spec 007.

## Probar accesibilidad

- `npm run a11y` ejecuta axe-core contra `http://localhost:44300/` recorriendo las 18 pantallas P1. (Script opcional; alternativa manual: extensión axe en Chrome.)

## Probar visualmente

- Set de datos seed en `App_Data/seed/` (cuestionarios de ejemplo, usuarios mock, respuestas).
- Cargar con `Update-Database` + script `Seed-Dev.ps1`.
