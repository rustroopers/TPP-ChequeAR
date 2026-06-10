# Feature Specification: Frontend / UX

**Feature Branch**: `006-frontend-ux`
**Created**: 2026-06-10
**Status**: Draft

## Contexto

Originalmente las specs 001–005 asumían que la UI sería provista por el Hospital Garrahan como template, y ChequeAR sólo expondría backend + un adaptador JS. Esa suposición se descarta: **la totalidad de la interfaz de usuario forma parte de la solución entregada por el equipo**.

Este módulo define la capa de presentación de ChequeAR: arquitectura, sistema de diseño, mapa de pantallas, accesibilidad, comportamiento responsive y los criterios que las pantallas deben cumplir. No define traducciones (ver spec 007).

## Personas y roles

| Rol | Pantallas críticas | Sesgo de diseño |
|---|---|---|
| Médico de planta | R1, R2, R3, R5, R6, R7 | velocidad de carga, atajos de teclado, mínima fricción |
| Referente de especialidad | Q1, Q2, Q3, Q4, Q5, Q6, Q7, Q9 | claridad sobre versionado, control de cambios |
| Investigador / Estadístico | E1, E2, E3, E4, E5 | filtros expresivos, preview confiable, exports robustos |
| Administrador | A4, A5, A6, A7, F1, F2 | trazabilidad, vista de salud del sistema |
| Auditor interno | A6, A7 | búsqueda granular, exportación de evidencia |

## User Scenarios & Testing

### User Story 1 — Médico completa una ficha clínica (Priority: P1)

Un médico de guardia abre ChequeAR, selecciona el paciente, elige el cuestionario publicado para su especialidad y carga la ficha desde una notebook hospitalaria. El render es fluido y la validación ocurre en línea.

**Why this priority**: es el caso de uso central — sin esto, no hay producto.

**Independent Test**: con un cuestionario publicado y un paciente válido, abrir desde el navegador estándar del hospital y completar la ficha sin recargas de página intermedias.

**Acceptance Scenarios**:

1. **Given** sesión iniciada y rol Médico, **When** entra al Home, **Then** ve accesos directos a "Carga de ficha" y "Mis fichas recientes".
2. **Given** una ficha con 30 ítems y 4 grupos, **When** carga la pantalla, **Then** el LCP es ≤ 2.5 s en red interna del hospital.
3. **Given** una pregunta numérica con rango, **When** el médico ingresa fuera de rango, **Then** ve el error en el mismo input sin perder el foco.

---

### User Story 2 — Referente arma un cuestionario nuevo (Priority: P1)

Una jefa de servicio entra al gestor, parte de un cuestionario existente o de cero, agrega grupos y preguntas con tipos variados, define lógica condicional, publica y comunica el cambio.

**Why this priority**: sin esta pantalla no hay manera de poblar el catálogo.

**Independent Test**: armar un cuestionario con 5 grupos / 25 preguntas mixtas, vista previa idéntica al render final, publicación exitosa con changelog visible en el historial.

**Acceptance Scenarios**:

1. **Given** un cuestionario en estado `Draft`, **When** la referente reordena preguntas con drag-and-drop, **Then** el cambio se refleja en la vista previa sin recargar.
2. **Given** una pregunta tipo `choice`, **When** define dos opciones y una regla `enableWhen`, **Then** la vista previa muestra las preguntas condicionadas sólo al cumplirse la regla.

---

### User Story 3 — Investigadora arma una cohorte y exporta (Priority: P1)

Una investigadora filtra respuestas por respuestas concretas a preguntas específicas, previsualiza la cohorte y exporta a Excel.

**Why this priority**: habilita el valor analítico secundario que justifica la inversión.

**Independent Test**: armar 3 filtros encadenados, previsualizar 100 filas, descargar XLSX y validar columnas.

**Acceptance Scenarios**:

1. **Given** un cuestionario con respuestas cargadas, **When** la investigadora agrega un filtro sobre una pregunta numérica con operador `entre`, **Then** el preview se recalcula en ≤ 1.5 s.
2. **Given** una cohorte filtrada, **When** exporta a XLSX, **Then** descarga un archivo con encabezados legibles en español y datos consistentes con la preview.

---

### User Story 4 — Auditor revisa actividad sospechosa (Priority: P1)

Un auditor abre el visor de auditoría, filtra por usuario y rango de fechas, abre un evento específico y exporta los resultados.

**Independent Test**: filtrar por un usuario y un día concreto, abrir el detalle de un evento de `update` y descargar CSV.

---

### User Story 5 — Administrador gestiona usuarios y roles (Priority: P2)

Un admin lista usuarios, asigna especialidades y roles, y desactiva un usuario que dejó la institución.

> Esta historia depende de si la gestión de usuarios queda en ChequeAR o se delega completamente al sistema hospitalario (decisión abierta — ver spec 005 §Assumptions).

---

### User Story 6 — Médico revisa fichas históricas del paciente (Priority: P2)

El médico abre el timeline del paciente, accede a una ficha anterior en modo solo lectura, compara con la actual.

---

### User Story 7 — Cualquier usuario cambia idioma (Priority: P2)

El usuario abre el selector de idioma en el navbar y cambia es ↔ en. La preferencia persiste por usuario.

> Detalle de i18n en spec 007.

---

## Mapa de pantallas

Detalle por pantalla en `screens/`. Resumen:

| Grupo | Cantidad | Prioridad |
|---|---|---|
| Transversales (`screens/00-transversales.md`) | 5 (T1–T5) | P1 mayoría |
| Autenticación & Seguridad (`screens/05-seguridad.md`) | 7 (A1–A7) | P1/P2 mixto |
| Gestor de cuestionarios (`screens/01-gestor.md`) | 9 (Q1–Q9) | P1/P2 |
| Renderizado clínico (`screens/02-renderizado.md`) | 7 (R1–R7) | P1/P2/P3 |
| FHIR / Admin (`screens/03-fhir.md`) | 2 (F1–F2) | P2/P3 |
| Análisis & Exportación (`screens/04-analisis.md`) | 6 (E1–E6) | P1/P2/P3 |

Total: **36 pantallas**, **18 P1**.

## Requirements

### Funcionales (FR)

- **FR-FE-001**: La aplicación debe servir la totalidad de la UI desde el proyecto `ChequeAR.Api` (Razor + assets estáticos), sin dependencia de un template externo.
- **FR-FE-002**: Debe existir un layout maestro (`_Layout.cshtml`) con navbar, sidebar contextual por módulo, área de contenido, breadcrumbs, slot de toasts y selector de idioma.
- **FR-FE-003**: Las pantallas marcadas `dynamic` deben implementarse como islas Vue 3 montadas sobre nodos `<div data-vue-island>` dentro de la vista Razor.
- **FR-FE-004**: Las pantallas marcadas `static` se renderizan 100% en Razor (sin Vue).
- **FR-FE-005**: La UI debe respetar el sistema de diseño definido en `design-system.md` (paleta Garrahan, tipografía, espaciado, radios, sombras, estados).
- **FR-FE-006**: La aplicación debe ser responsive desde 1280px (target principal: notebooks hospitalarias) y degradar grácilmente hasta 1024px. Soporte explícito para tablets en pantallas R1/R3.
- **FR-FE-007**: Todos los formularios deben mostrar estados: `idle`, `loading`, `success`, `error`, `empty`. Las grillas deben mostrar `empty state` con CTA.
- **FR-FE-008**: La navegación principal debe estar adaptada al rol del usuario activo (módulos visibles, atajos del Home).
- **FR-FE-009**: Las pantallas de carga clínica (R3) deben soportar guardado parcial con indicador visual de "borrador" y reanudación al volver.
- **FR-FE-010**: Errores 401/403/404/500 deben mostrar pantalla T4 con copy contextual y CTA de recuperación.

### No funcionales (NFR)

- **NFR-FE-001**: LCP ≤ 2.5 s en red interna del hospital para todas las pantallas P1.
- **NFR-FE-002**: CLS < 0.1; INP < 200 ms en las islas Vue.
- **NFR-FE-003**: Bundle Vue de cada isla ≤ 50 KB gzip. CSS theme ≤ 80 KB gzip.
- **NFR-FE-004**: La aplicación debe cumplir **WCAG 2.1 AA** en todas las pantallas P1. Contraste de color ≥ 4.5:1 para texto, ≥ 3:1 para componentes. Navegación completa por teclado. Foco visible.
- **NFR-FE-005**: Soporte de navegadores: Chrome ≥ 110, Edge ≥ 110, Firefox ≥ 110. No se soporta IE.
- **NFR-FE-006**: La UI debe ser localizable (es-AR, en) sin recompilación. Detalle en spec 007.
- **NFR-FE-007**: Ninguna PII (nombre, DNI, fecha de nacimiento del paciente) debe quedar en `localStorage`, `sessionStorage` o `console.log` en producción.

## Success Criteria

- 18 pantallas P1 implementadas, navegables y testeadas con dataset realista.
- Auditoría WCAG AA pasada con herramienta automatizada (axe-core) + revisión manual de focos y screen reader sobre R3.
- Un usuario nuevo del rol Médico completa una ficha de 20 preguntas en ≤ 3 minutos sin asistencia previa, en pruebas de usabilidad informales con 5 sujetos.

## Assumptions

- La instalación corre sobre IIS interno del hospital con HTTPS resuelto a nivel de infraestructura.
- Los assets se sirven desde el mismo origen — no se asume CDN externa.
- La librería Vue 3 se carga desde `wwwroot/lib/vue/vue.global.prod.js` (vendored, sin CDN externa).
- Los iconos provienen de **Bootstrap Icons** (`bootstrap-icons.svg` sprite local, sin red).
- El idioma activo se resuelve por cookie `chequear_lang`, con default `es-AR`.

## Out of Scope

- Aplicación móvil nativa.
- Modo offline / PWA.
- Tema oscuro (no descartado, pero no entra en MVP).
- Personalización de tema por usuario.
