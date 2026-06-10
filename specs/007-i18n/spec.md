# Feature Specification: Internacionalización (i18n)

**Feature Branch**: `007-i18n`
**Created**: 2026-06-10
**Status**: Draft

## Contexto

ChequeAR debe operar en **español rioplatense (es-AR, default)** y **inglés (en)**. El inglés se incluye principalmente para revisores externos, jurados académicos y eventual interoperabilidad regional. Toda la UI definida en spec 006 debe poder cambiar de idioma sin recompilar.

Hay tres tipos de textos a traducir:
1. **Estáticos de UI** — labels, títulos, mensajes (Razor + Vue).
2. **Validaciones y errores** — server-side (Web API) + client-side.
3. **Dinámicos del dominio** — nombres de cuestionarios, grupos, preguntas, opciones. Estos se cargan desde la base y deben tener traducciones opcionales por idioma.

## User Scenarios & Testing

### User Story 1 — Cambio de idioma global (Priority: P1)

Cualquier usuario abre el selector de idioma en navbar, elige "English", la página recarga y todo lo estático aparece en inglés. La preferencia persiste.

**Independent Test**: setear cookie `chequear_lang=en`, recargar Home, ver labels en inglés.

**Acceptance Scenarios**:

1. **Given** un usuario nuevo, **When** ingresa por primera vez, **Then** la cultura activa es `es-AR` (default).
2. **Given** un usuario que cambió a inglés, **When** vuelve días después, **Then** la cultura es `en` (persistida por cookie 30 días).
3. **Given** una pantalla con isla Vue, **When** cambia idioma, **Then** la isla muestra sus textos en el idioma seleccionado.

---

### User Story 2 — Cuestionario con textos en dos idiomas (Priority: P2)

Una referente carga un cuestionario y traduce sus preguntas y opciones al inglés. Un usuario con idioma `en` ve la versión inglesa; uno con `es-AR` ve la española.

**Independent Test**: cargar `Questionnaire` con `text.es-AR` y `text.en` por pregunta. Cambiar cookie y verificar.

**Acceptance Scenarios**:

1. **Given** una pregunta con sólo traducción al español, **When** un usuario `en` la abre, **Then** se muestra el texto en español con badge `(es)` indicando fallback.
2. **Given** una pregunta sin texto en ningún idioma activo, **Then** se muestra el código FHIR como fallback de último recurso.

---

### User Story 3 — Formato regional de fechas y números (Priority: P1)

En `es-AR`: fechas `dd/mm/yyyy`, números con `.` como separador de miles y `,` decimal. En `en`: `mm/dd/yyyy`, `,` miles y `.` decimal.

---

### User Story 4 — Errores de validación traducidos (Priority: P1)

Cuando el servidor rechaza un campo, el error vuelve con clave i18n. El cliente la traduce con su recurso.

## Requirements

### Funcionales

- **FR-I18N-001**: Idiomas soportados: `es-AR` (default), `en`. Diseño abierto a sumar otros sin cambios estructurales.
- **FR-I18N-002**: Cookie `chequear_lang` (HttpOnly: false; SameSite: Lax; expires 30d) determina la cultura activa.
- **FR-I18N-003**: En login, la cultura se resuelve por `Accept-Language` del navegador si no hay cookie.
- **FR-I18N-004**: Razor consume textos desde `App_GlobalResources/<Resource>.<culture>.resx`.
- **FR-I18N-005**: Vue islands consumen textos desde `Scripts/i18n/<culture>.json` cargados al boot de la isla.
- **FR-I18N-006**: Las claves siguen formato namespaced `modulo.pantalla.elemento` (ej.: `q1.filters.specialty.placeholder`).
- **FR-I18N-007**: Las fechas y números se formatean con `CultureInfo` en server-side e `Intl` en cliente, ambos derivados de la cultura activa.
- **FR-I18N-008**: Errores de validación vuelven con `{ code: "rules.range.outOfBounds", params: { min, max } }`. El cliente resuelve el mensaje.
- **FR-I18N-009**: Textos dinámicos de `Questionnaire` se modelan como `LocalizedText { defaultText, translations: [{ lang, text }] }`. Detalle en `data-model.md`.
- **FR-I18N-010**: Cuando una traducción falta, fallback en cascada: idioma activo → `es-AR` → texto default del dominio → clave bruta.

### No funcionales

- **NFR-I18N-001**: El cambio de idioma no debe agregar > 50 KB al peso de página inicial.
- **NFR-I18N-002**: Sin requests adicionales para textos estáticos — todo embebido en bundle.
- **NFR-I18N-003**: Cobertura de claves al cierre del MVP: 100% de las 18 pantallas P1.
- **NFR-I18N-004**: El proceso de traducción debe ser revisable por un no-desarrollador (los `.resx` y `.json` deben ser legibles y comentables).

## Success Criteria

- Una pasada manual recorriendo las 18 pantallas P1 en ambos idiomas sin claves sin traducir visibles.
- Los formatos de fecha/número son consistentes con la cultura activa en todas las pantallas.
- Un test E2E con Playwright cambia idioma y valida 3 labels distintos en cada modo.

## Assumptions

- El equipo entrega ambas traducciones; no se contrata traductor externo.
- No se requiere soporte de bidi (rtl) en MVP.
- No se requiere traducción de contenidos clínicos cargados por usuarios (las respuestas quedan en el idioma en que fueron tipeadas).

## Out of Scope

- Detección automática de idioma del paciente.
- Traducción automática (ej.: API externa).
- Pluralización compleja con ICU MessageFormat (se usa un formato simple `{count} item / {count} items`).
- Soporte de calendarios no gregorianos.
