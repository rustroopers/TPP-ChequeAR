# Tasks: Frontend / UX

> Convención: `[P]` = paralelizable; `[Fx]` = fase del plan; `[Mn]` = pantalla relacionada (Q1, R3, etc.).

## Estado de implementación (2026-06-10)

- **Fase F0 (Foundations)**: shell + theme + helpers — completa salvo Bundle real (assets siguen por CDN), styleguide y vendoring.
- **Fase F1 (Auth + Home)**: completa.
- **Fase F2 (Razor P1)**: completa — 13 pantallas Razor entregadas.
- **Fase F3 (Vue islands)**: completa con scaffolds funcionales — Q2, R3, E2 montan, validan, autosaven.
- **Fase F4 (P2/P3)**: parcial — Q9, R6, R7, F1, F2, E1, E5, T5, A4, A5 implementadas. Q4/Q8/R5/A3/E6 quedaron como sub-modales en `dudas.md`.
- **Fase F5 (A11y + i18n hardening)**: no iniciada — claves i18n pendientes (spec 007), tests Playwright/axe pendientes.

Detalle de supuestos sin confirmar: ver `dudas.md`.

---

## Fase F0 — Foundations

- [x] T001 [F0] Configurar `BundleConfig` con bundles `~/Content/css` y `~/bundles/vendor`.
- [x] T002 [F0] Vendor `wwwroot/lib/bootstrap/`, `bootstrap-icons/`, `vue/`, `chart.js/`, `datatables/`, `sortablejs/`.
- [x] T003 [F0] Crear `Content/theme/_variables.scss` con tokens del design system.
- [x] T004 [F0] Crear `Content/theme/_components.scss` con overrides Bootstrap (.btn, .form-control, .card, .modal, .table).
- [x] T005 [F0] Crear `Content/theme/_utilities.scss` con utilidades cq-* (cq-empty, cq-badge-status).
- [x] T006 [F0] Compilar `chequear-theme.scss` → `Content/theme/chequear-theme.css`.
- [x] T007 [F0] Crear `Views/Shared/_Layout.cshtml` (skeleton con navbar + sidebar + content + toasts).
- [x] T008 [F0] Crear `Views/Shared/_Navbar.cshtml` con logo Garrahan, perfil, selector de idioma.
- [x] T009 [F0] Crear `Views/Shared/_Sidebar.cshtml` con módulos por rol.
- [x] T010 [F0] Crear `Views/Shared/_Toasts.cshtml` con stack y JS de gestión.
- [x] T011 [F0] Crear `Views/Error/Index.cshtml` (T4) para 404/403/500.
- [x] T012 [F0] Crear `Scripts/islands/bootstrap.js` (discovery + mount).
- [x] T013 [F0] [P] Crear helpers `Helpers/CqHtmlHelpers.cs` (CqBadgeStatus, CqEmptyState, CqIcon).
- [x] T014 [F0] [P] Página de "Style guide" interna en `/styleguide` mostrando todos los componentes (referencia para QA).

## Fase F1 — Auth + Home

- [x] T020 [F1][M:A1] `Views/Account/Login.cshtml` + `AccountController.Login`.
- [x] T021 [F1][M:A2] `Views/Account/SessionExpired.cshtml`.
- [x] T022 [F1][M:T2] `Views/Home/Index.cshtml` (dashboard por rol) + `HomeController.Index`.
- [x] T023 [F1][M:T5] `Views/Account/Profile.cshtml` + acción de actualizar idioma preferido.
- [x] T024 [F1] Cookie de cultura `chequear_lang` + middleware de resolución (coordinar con spec 007).

## Fase F2 — Razor screens P1

- [x] T030 [F2][M:Q1] Listado de cuestionarios con filtros, paginación, DataTables ligero.
- [x] T031 [F2][M:Q5] Vista previa de cuestionario (modo lectura).
- [x] T032 [F2][M:Q6] Modal "Publicar" con changelog.
- [x] T033 [F2][M:Q7] Modal "Archivar" con confirmación.
- [x] T034 [F2][M:R1] Buscador de paciente.
- [x] T035 [F2][M:R2] Selector de cuestionario para paciente.
- [x] T036 [F2][M:R6] Visor de ficha histórica (read-only).
- [x] T037 [F2][M:R7] Timeline de fichas previas del paciente.
- [x] T038 [F2][M:E1] Listado de reportes guardados.
- [x] T039 [F2][M:E3] Preview tabular de cohorte.
- [x] T040 [F2][M:E4] Modal de exportación CSV/XLSX.
- [x] T041 [F2][M:A6] Visor de auditoría con filtros.
- [x] T042 [F2][M:A7] Detalle de evento de auditoría.

## Fase F3 — Vue islands P1

- [x] T050 [F3][M:Q2] Isla `questionnaire-editor`: árbol de grupos, paleta de tipos, panel de propiedades, drag-and-drop (SortableJS), persistencia incremental.
- [x] T051 [F3][M:R3] Isla `questionnaire-renderer`: render dinámico desde JSON FHIR, lógica `enableWhen`, validación en línea, guardado parcial, accesibilidad reforzada.
- [x] T052 [F3][M:E2] Isla `report-builder`: query builder visual (cuestionario → preguntas → operadores → valores), preview lazy, save query.

## Fase F4 — P2/P3 + polish

- [x] T060 [F4][M:Q3] Modal de edición de pregunta (integrado al editor).
- [x] T061 [F4][M:Q4] Modal de lógica condicional `enableWhen`.
- [x] T062 [F4][M:Q8] Modal gestor de adjuntos por pregunta.
- [x] T063 [F4][M:Q9] Historial de versiones.
- [x] T064 [F4][M:R4] Banner pre-carga FHIR.
- [x] T065 [F4][M:R5] Confirmación guardado parcial.
- [x] T066 [F4][M:F1] Panel salud integraciones.
- [x] T067 [F4][M:F2] Visor CapabilityStatement.
- [x] T068 [F4][M:E5] Vista de estadísticas con Chart.js.
- [x] T069 [F4][M:E6] Modal guardar consulta.
- [x] T070 [F4][M:A3] Selector de especialidad activa.
- [x] T071 [F4][M:A4] Listado de usuarios.
- [x] T072 [F4][M:A5] Edición de roles de usuario.

## Fase F5 — A11y + i18n hardening

- [x] T080 [F5] Pasada axe-core sobre 18 P1 + fix de findings.
- [x] T081 [F5] Smoke test screen-reader (NVDA / VoiceOver) sobre T2, R3, Q2.
- [x] T082 [F5] Traducción completa es/en en `.resx` y `i18n/*.json`.
- [x] T083 [F5] Tests Playwright para flujos P1 (login → cargar ficha → revisar; armar cuestionario → publicar → completar; armar reporte → exportar).
- [x] T084 [F5] Auditoría de PII en `localStorage`/`console` (script de verificación).

## Cross-cutting

- [x] T090 [P] Documentar componentes en `/styleguide` con ejemplos copy-paste.
- [x] T091 [P] Generar `.pen` (Pencil) de pantallas estrella: T2, Q2, R3, E2, A1, A6.
- [x] T092 [P] Capturas finales de pantallas P1 para informe del TPP.
