# Avance del testing hecho por Facu

Registro del trabajo realizado en la rama `testing-hecho-por-facu` y de lo que queda
pendiente. Complementa [`testing-facu-feedback.md`](testing-facu-feedback.md) (el plan
inicial completo con feedback crudo).

> Rama: `testing-hecho-por-facu`
> Última actualización: 2026-09-06

---

## ✅ Hecho y pusheado

### Commit pendiente — Etapa 1.6 ruteo /Encounters/Search → /Pacientes

- **`App_Start/RouteConfig.cs`**: nueva ruta nombrada `Pacientes` (`Pacientes/{id}`)
  registrada ANTES de la default, mapea a `Encounters.Index`. Así
  `Url.Action("Index","Encounters")` genera `/Pacientes` y otras acciones del
  controller siguen cayendo por la default (`/Encounters/Load`, etc.).
- **`Controllers/Mvc/EncountersController.cs`**: action `Search(string q)`
  renombrada a `Index(string q)`. Los breadcrumbs internos que apuntaban a
  `Url.Action("Search")` ahora usan `Url.Action("Index")`.
- **`Views/Encounters/Search.cshtml`** → **`Views/Encounters/Index.cshtml`**
  (`git mv` preserva historia). Actualizado `Html.BeginForm("Search",...)` a
  `("Index",...)` para que el GET del buscador postee a la nueva URL.
- **`ChequeAR.Api.csproj`**: `<Content Include="...Search.cshtml">` →
  `Index.cshtml` (si no, MSBuild no lo copia y la view queda 404).
- **Callers actualizados**: `Views/Shared/_Sidebar.cshtml`,
  `Views/Encounters/Timeline.cshtml`, `Views/Home/Index.cshtml` (2 hits),
  `scripts/Seed-Reumatologia.ps1` (mensaje informativo).

### Commit `4c8fcee` — Etapa 3.8: help card del wizard responsive

### Commit `5d730e7` — Etapa 3.2 verificación código↔término

**Editor (`Scripts/islands/questionnaire-editor/index.js`):**
- **3.2 validación**: nuevo botón "Verificar" al lado del input de código SNOMED
  (aparece tanto en el panel Propiedades del paso Estructura como en el panel
  de codificación del paso Códigos). Reusa `/fhir/r5/ValueSet/$expand` con el
  código como filter (mismo endpoint que el autocomplete, ya autorizado y
  cacheado — sin cirugía en el backend) y busca match exacto en `results`.
  - Match ⇒ carga `codeDisplay` con el término oficial y muestra "Coincide:
    <term>".
  - Si el término escrito por el clínico difiere del oficial, muestra un aviso
    amarillo "El término escrito no coincide con el oficial. Revisalo o dejá
    el oficial."
  - Sin match ⇒ "El código X no existe en SNOMED CT".
  - Validación previa: si no son dígitos (6-18), avisa antes de pegarle al
    server ("Un código SNOMED CT son solo dígitos").
- Estado (`codeVerification`, `codeVerifying`) se limpia al cambiar de pregunta
  o al modificar el código, para no arrastrar feedback de otra verificación.

### Commit `b29eb31` — Etapa 3.1 wizard step-by-step

**Editor de cuestionarios (`Scripts/islands/questionnaire-editor/index.js` + `chequear-theme.css`):**
- **3.1** Reemplazado el editor "todo-en-una-pantalla" por un **wizard de 5 pasos**:
  Metadatos / Estructura / Códigos SNOMED / Vista previa / Publicación. El stepper
  vive arriba (nav horizontal, scroll en mobile, chips clickeables — no fuerza
  orden porque los power users no la necesitan). Cada paso trae una **card de
  ayuda contextual** con "Anterior" / "Siguiente".
- **Metadatos**: card dedicada para título y especialidad, con hints debajo de
  cada campo.
- **Estructura**: reusa el layout `col-lg-7` (secciones + preguntas + DnD) y
  `col-lg-5` (propiedades) sin cambios funcionales.
- **Códigos SNOMED**: **vista plana** de todas las preguntas con estado del
  código (check verde si tiene, círculo vacío si no) + contador de progreso.
  Click en una pregunta abre a la derecha SÓLO el bloque de búsqueda SNOMED y
  código — trabajo dedicado, sin scroll infinito en el árbol.
- **Vista previa**: render read-only del cuestionario con inputs deshabilitados
  y hints de codificación / lógica condicional debajo de cada pregunta.
- **Publicación**: computa `publishBlockers` (archivado, conflicto, dirty, isNew,
  issues, sin preguntas) y los muestra. Botón "Publicar ahora" habilitado sólo
  si no hay blockers, con card verde "Todo listo" cuando corresponde.

**CSS nuevo (`chequear-theme.css`):**
- `.cq-stepper` / `.cq-stepper__item` / `.cq-stepper__num` / `.cq-stepper__label`
  — Estados: base, hover (borde primary), `is-active` (fondo primary), `is-done`
  (borde verde menta). En ≤640px oculta labels y sube min-height a 44px.

### Commit `4b628e1` — Etapa 3 (parcial) + Etapa 3.8 mobile

**Editor de cuestionarios (`Scripts/islands/questionnaire-editor/index.js`):**
- **3.3** Card "TIPOS" (col-lg-2) eliminada: era decorativa, los tipos ya viven en el
  dropdown "+ Agregar pregunta". El layout se redistribuye a `col-lg-7` estructura /
  `col-lg-5` propiedades — más aire para editar y leer.
- **3.7** Reactividad del panel Propiedades: el textarea de "Opciones" combinaba
  `v-model="selected.question.optionsText"`, un `@input` que reescribía
  `selected.question.options`, y contenido interpolado dentro del `<textarea>`. En
  Vue 3 el contenido inline con v-model es ignorado y el par `options/optionsText`
  quedaba fuera de sincronía al cambiar de pregunta (los datos "se perdían" desde
  el punto de vista del usuario). Se reemplazó por una **fuente única `options`**
  editada vía un `computed` con getter/setter (`optionsText`). El campo redundante
  desapareció del modelo inicial y de `onTypeChange`.

**Mobile — sidebar drawer (`chequear-theme.css` + `_Layout.cshtml`):**
- **3.8 P0** En ≤1024px la sidebar estaba `display:none` — no había forma de
  navegar. Ahora la sidebar pasa a **drawer off-canvas** (fixed, translateX -100%),
  se abre con el mismo `#cqSidebarToggle` del navbar, tiene backdrop cliqueable
  (pseudo `body::after`) y se cierra al tocar un item.
- El JS del layout se volvió breakpoint-aware con `matchMedia("(max-width:1024px)")`:
  en desktop sigue colapsando (persistido en localStorage), en mobile abre/cierra
  el drawer (sin persistir). Al cruzar el breakpoint limpia el estado que no aplica.
- Touch targets ≥44px en `.cq-sidebar__item` y `.cq-navbar__btn` dentro del media
  mobile.

### Commit `0202a94` — Etapa 1: fixes rápidos y jerarquía de acciones

**Tokens de color** en `src/ChequeAR.Api/Content/theme/chequear-theme.css`:
- Nuevos `--cq-secondary` (verde menta) y `--cq-tertiary` (neutro fuerte).
- Nuevas clases `.btn-secondary`, `.btn-tertiary` y `.btn-outline-secondary-cq` (esta
  última con sufijo `-cq` para no pisar los "Cancelar" de modales).

**Dropdown de acciones (⋯):**
- Hover MUY visible en `.dropdown-menu .dropdown-item` (fondo violeta suave, ítems con
  radius, transición corta). Antes no se distinguía qué se estaba tocando.
- Variante `.dropdown-item.text-danger:hover` con fondo rojo suave.

**Icon buttons:**
- Nueva clase `.cq-icon-btn` / `.cq-icon-btn--danger` — box cuadrado con hover, borde y
  foco visible. Aplicada al tacho de sección y a la X de eliminar pregunta en
  `Scripts/islands/questionnaire-editor/index.js`.

**Pill "Quitar código" en el editor:**
- Reemplaza el link crudo `<a>quitar</a>` por `.cq-removable` (badge tipo chip con X en
  hover, tooltip "Quitar código").

**Users (`Views/Users/Index.cshtml`):**
- Empty state cuando `Model.Users` viene vacío ("No hay usuarios en el sistema…" +
  botón importar).

**Search (`Views/Encounters/Search.cshtml`):**
- Nuevas columnas **DNI** y **Fecha de nacimiento** (con edad entre paréntesis).
- Botón "Historial" pasa a `.btn-secondary` (verde) para distinguirlo de "Cargar ficha"
  (primary).
- Se pobló `PatientSearchHit.Document` y `.BirthDateLabel` desde
  `VW_PACIENTE.DOCUMENTO` / `FECHA_NACIMIENTO` en `PatientService.MapRow`. Nuevos
  campos también en `PatientSearchItem` (view model).

**Timeline / Historial (`Views/Encounters/Timeline.cshtml`):**
- Título en dos líneas: `Historial` (h1) + subtítulo con nombre y edad entre
  paréntesis.
- Botón `.cq-back-btn` "← Volver a pacientes" arriba del header.
- Todos los filtros (Cuestionario / Especialidad / Autor / Estado / Período) en la
  MISMA fila (col-md-2 x4 + col-md-4).
- Card empty state con `.cq-card--stretch` para llegar al borde derecho.
- Botón "Abrir" en `.btn-tertiary`.

**Lista de cuestionarios (`Views/Questionnaires/Index.cshtml`):**
- Fila entera clickeable (`.cq-row--clickable` + JS que ignora clicks sobre links,
  botones y el dropdown).
- Trigger del dropdown pasa a `.btn-tertiary` con `title="Acciones"`.

### Commit `8271d1f` — Etapa 2: sidebar colapsable + home demostrativa + tooltips guía

**Sidebar (`Views/Shared/_Sidebar.cshtml` + `_Navbar.cshtml` + `_Layout.cshtml`):**
- Botón toggle en el navbar (`#cqSidebarToggle`).
- CSS `body.cq-sidebar-collapsed` colapsa la grilla a 64px, oculta labels y grupos.
- Persistencia en `localStorage["cq.sidebar.collapsed"]` con try/catch (fallback
  silencioso en storage bloqueado). El estado se aplica antes de pintar para evitar
  flicker.
- Íconos y texto agrandados: `font-size:22px` en íconos, `15px` en texto, `gap:12px`.
- Cada item lleva tooltip a la derecha (Bootstrap 5) con label + una línea breve de
  ayuda. Útil expandido y colapsado.
- Ícono de Pacientes cambiado de `bi-person-vcard` a **`bi-person-heart`** (más acorde
  a historia clínica pediátrica — resuelve la propuesta D1 con criterio propio; se
  puede seguir debatiendo).

**Home rediseñada (`Views/Home/Index.cshtml`):**
Reemplaza los 3 "botones-que-navegan" por 3 cards demostrativas (`.cq-demo-card`):
- **Reportes** — SVG placeholder con barras + `<select>` para elegir tipo (uso /
  autores / especialidad); navega a `/Reports?tipo=…`.
- **Ficha reciente** — muestra la última `RecentEncounter` con paciente,
  cuestionario, chip de especialidad, badge de estado y timestamp; dropdown para
  saltar a las siguientes 4 recientes; botones "Buscar paciente" (tertiary) y "Abrir
  ficha" (primary).
- **Cuestionarios** — accesos rápidos a Publicados / Borradores / Archivados (filtran
  por status) + CTA "Nuevo" con tooltip.
Todos tienen empty state cuando no hay data.
La tabla `Recientes` y los KPIs (admin) siguen abajo como detalle.

**Tooltips guía:**
- Inicializados en `_Layout.cshtml` con delay 400ms show / 80ms hide.
- Se agregaron en el CTA "Crear cuestionario" y en el buscador de pacientes (explica
  qué se puede tipear: HC, DNI, apellido, nombre).

### Cubierto por rebote (etapas 4 y 5)

Muchos ítems de las etapas 4 y 5 ya quedaron resueltos por las dos primeras etapas:

- Etapa 4.1 (click en toda la fila) — hecho.
- Etapa 4.3 (hover del dropdown) — hecho vía CSS global.
- Etapa 5.1 (DNI + fecha nac.) — hecho.
- Etapa 5.2 (Historial botón secondary) — hecho.
- Etapa 5.3 (Volver del historial) — hecho.
- Etapa 5.4 (filtros en una fila) — hecho.
- Etapa 5.5 (card ancha) — hecho vía `.cq-card--stretch`.
- Etapa 5.6 (título dos líneas) — hecho.

---

## ⏸️ Pendiente

Ordenado por prioridad y por lo que requiere antes de tocar código.

### Etapa 3 — Rework editor de cuestionarios (NO ARRANCADO)

Es el bloque más grande y arriesgado. Vive en
`src/ChequeAR.Api/Scripts/islands/questionnaire-editor/index.js` (~1170 líneas de Vue
en template-string). **Recomendación: hacerlo con `/run-app` disponible en Windows y en
iteraciones cortas.** Detalle:

- **3.1 Wizard step-by-step** — ✅ hecho (ver commit pendiente arriba).
- **3.2 Autocomplete SNOMED roto** — la request no aparece en Network. Investigar
  desde la isla Vue → endpoint del Api → cliente Snowstorm en `ChequeAR.Infrastructure`.
  Silencia el error en algún try/catch. **Bug funcional P0.**
- **3.2 Validación código↔término** — ✅ hecho: botón "Verificar" que trae el
  `preferredTerm` oficial vía `$expand?filter=<code>` y avisa si el término
  escrito difiere. Se decidió NO bloquear la entrada manual (los usuarios que
  ya saben el código no pierden tiempo escribiendo el término y buscando).
- **3.3 Tipo de respuesta** — sacar gris claro que parece disabled; permitir edición
  in-place del tipo (hoy obliga a borrar la pregunta). *NOTA*: revisando el código,
  `onTypeChange` ya está implementado y el select del panel Propiedades es un
  `<select>` normal, no disabled. El "gris claro" percibido probablemente venía de
  la card "TIPOS" (ya eliminada). Verificar en runtime que el select responda.
- **3.3 Card "TIPOS"** — ✅ hecha (ver commit pendiente arriba).
- **3.4 Sección/carpeta** — si no agrupa visualmente las preguntas, eliminar el
  concepto. **Requiere definición D5.**
- **3.6 Comentarios en cuestionarios/preguntas** — feature nueva. **Requiere
  definición D4.**
- **3.7 Pérdida de datos en Propiedades** — ✅ hecho el fix más probable (textarea
  de Opciones con doble binding + interpolación inline). Si vuelve a aparecer el
  síntoma con OTROS campos, hay que reproducirlo con detalle (qué campo, qué
  acción entre selecciones) porque el resto del panel usa `v-model` limpio contra
  `selected.question.*`.
- **3.8 Mobile — sidebar drawer** — ✅ hecho (drawer off-canvas + touch targets).
  Queda pendiente el wizard step-by-step del editor (3.1), auditar el resto de
  pantallas (Timeline, Reports, Editor) a 375/390/768 y meter los ajustes que
  aparezcan.

### Etapa 4 — Lista de cuestionarios (pendientes menores)

- **4.2 Ordenamiento** — 🔵 D6. No implementado; requiere decidir el campo por
  defecto.
- **4.3 Archivar vs. borrar** — 🔵 D7. Reglas según uso previo (integridad
  referencial hacia `Encounters`).
- **4.4 Estado vs. archivado** — 🔵 D8. Revisar máquina de estados en
  `ChequeAR.Domain`.
- **4.5 Título "TRABAJO"** — 🔵 D9. Renombrar según scope real.
- **4.6 Pacientes como searcher vs. listado** — 🔵 D10. Preguntar el porqué de la
  decisión.

### Etapa 5 — Pantalla de paciente (pendiente)

- **"Ver evolución"** — no encontré el botón en `src/ChequeAR.Api/Views/` (`grep` no
  devolvió resultados). Probablemente pertenece a la feature 010 (evolución
  longitudinal, spec en `specs/010-evolucion-parametros-paciente/`) que aún no está
  cableada a la UI. Cuando aparezca: aplicar `.btn-secondary` (verde) para
  distinguirlo del primary.
- **5.7 Detalle de ficha** — 🔵 D11. Qué se puede cargar / qué se muestra. Card
  interna "Sin movimientos registrados" quedó chica: extenderla cuando definamos.

### Ruteo `/Encounters/Search` → `/Pacientes` (1.6 del plan)

✅ Hecho (ver commit pendiente arriba).

### Decisiones pendientes (tabla D1–D11 del plan)

Recordatorio: llevar a una única reunión con Carolo/Gonza. Ver
[`testing-facu-feedback.md`](testing-facu-feedback.md) sección "Cosas a definir".

| # | Tema | Estado |
|---|---|---|
| D1 | Ícono de Pacientes | Se dejó `bi-person-heart` como propuesta; validar |
| D2 | SNOMED sin match | Pendiente |
| D3 | Captura "dice el paciente…" | Pendiente |
| D4 | Comentarios en cuestionarios/preguntas | Pendiente |
| D5 | Concepto de "sección" | Pendiente |
| D6 | Ordenamiento de lista de cuestionarios | Pendiente |
| D7 | Archivar vs. borrar | Pendiente |
| D8 | Estado vs. archivado | Pendiente |
| D9 | Título "TRABAJO" | Pendiente |
| D10 | Pacientes como searcher | Pendiente |
| D11 | Propósito del detalle de ficha | Pendiente |

---

## Notas para retomar

### Archivos tocados

```
src/ChequeAR.Api/Content/theme/chequear-theme.css        (+tokens, +muchas clases nuevas)
src/ChequeAR.Api/Controllers/Mvc/EncountersController.cs (map Document/BirthDate)
src/ChequeAR.Api/Models/EncounterViewModels.cs           (+Document, +BirthDateLabel)
src/ChequeAR.Api/Scripts/islands/questionnaire-editor/index.js (icon-btn, cq-removable)
src/ChequeAR.Api/Views/Encounters/Search.cshtml          (DNI, birth, tooltip)
src/ChequeAR.Api/Views/Encounters/Timeline.cshtml        (título, volver, filtros, card)
src/ChequeAR.Api/Views/Home/Index.cshtml                 (3 cards demostrativas)
src/ChequeAR.Api/Views/Questionnaires/Index.cshtml       (fila clickeable, tertiary, tooltip)
src/ChequeAR.Api/Views/Shared/_Layout.cshtml             (script sidebar toggle + tooltips)
src/ChequeAR.Api/Views/Shared/_Navbar.cshtml             (botón toggle)
src/ChequeAR.Api/Views/Shared/_Sidebar.cshtml            (íconos grandes, tooltips)
src/ChequeAR.Api/Views/Users/Index.cshtml                (empty state)
src/ChequeAR.Application/Services/PatientService.cs      (+Document, +BirthDateLabel)
```

### Verificación

- Los tests de `PatientProjectionTests` no tocan `Document` / `BirthDateLabel`, así que
  no se rompen. No corrí el resto de la suite (proyecto .NET Framework, requiere
  MSBuild/Windows).
- No corrí la app: requiere Oracle + IIS Express + VS2022 (skill `/run-app`, entorno
  Windows). Todo lo hecho es CSS/HTML/Razor + adiciones a POCOs sin remover nada
  existente.

### Cosas a mirar primero al retomar

1. Abrir la app y ver la sidebar colapsando: confirmar que el toggle funciona y que el
   estado se persiste entre recargas.
2. Home: confirmar que las 3 cards demostrativas se renderizan bien con y sin data.
3. Search: confirmar que DNI y fecha de nacimiento aparecen cuando Oracle responde.
   Cuando cae al modo degradado (desde `SearchFromResponsesAsync`) el DNI queda vacío
   (mostramos "—") — es esperable, no hay fuente para el documento en ese modo.
4. Timeline: confirmar que los filtros no se cortan en pantallas medianas — si se
   apretan, subir a `col-lg-2` con `col-md-3` para hacer 2 filas en tablet y 1 en
   desktop.
5. Editor: mirar cómo se ven los nuevos `.cq-icon-btn` y `.cq-removable` con Vue vivo.

### Convención de commits usada

Sin `Co-Authored-By`, sin trailer de sesión (pedido explícito del usuario en esta
sesión). Título en minúsculas con prefijo `etapa N:`.
