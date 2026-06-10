# Dudas y supuestos abiertos — Frontend / UX

Este archivo recoge las decisiones que tomé sin tu confirmación durante la implementación de pantallas. Cuando lo revisemos, marcamos cada una como ✅ confirmada, ✏️ corregir o ❌ revertir.

## Branding y colores

- [ ] **Paleta lila**: usé `#6f5fa5` como primario (un lila más saturado que el `#9283be` del sitio para asegurar contraste WCAG AA sobre blanco). El `#9283be` original queda como `--cq-primary-400` para fondos suaves. **¿Te gusta el tono más saturado o preferís el lila tal cual del sitio aunque pierda contraste?**
- [ ] **Verde menta `#73C4A4`** lo dejé como acento secundario para CTAs no-primarios y éxito visual. ¿Va?
- [ ] **Logo Garrahan**: linkeado al PNG live del sitio (`https://www.garrahan.gov.ar/images/logo2.png`). En producción debería vendorearse a `Content/img/`. ¿Tenés una versión SVG o blanca para el navbar?
- [ ] **Tagline ChequeAR**: usé "Fichas Clínicas Interoperables" — placeholder. Confirmá copy oficial.

## Estructura y navegación

- [ ] **Sidebar items**: los agrupé en "Trabajo" (Inicio, Pacientes, Cuestionarios, Reportes) y "Administración" (Auditoría, Usuarios, Integraciones). El usuario admin ve todo; los demás ven sólo el grupo Trabajo. **¿Confirmás esa segmentación o querés que los items de admin estén ocultos completamente para no-admin (no sólo deshabilitados)?**
- [ ] **Visibilidad por rol**: para el MVP, todos los users ven el menú completo en mock. La lógica real de RBAC viene del módulo 005.
- [ ] **Breadcrumbs**: implementados, pero algunos flujos profundos (ej.: edición de cuestionario v3 → editor de pregunta) tienen 4-5 niveles. ¿Aceptás colapsar (`… > Editor > Pregunta`) cuando hay > 3 niveles?

## Gestión de usuarios (A4, A5)

- [ ] **¿Existe en ChequeAR o se delega 100% al hospital?** Mientras tanto las implementé como pantallas funcionales tipo "vista local de usuarios del hospital" (read-mostly + edición de roles por especialidad sólo dentro de ChequeAR). Si la gestión es 100% delegada, borramos las pantallas.

## Pantallas R1/R2/R3 (carga clínica)

- [ ] **Buscador de paciente (R1)**: por DNI / HC / apellido. Asumí campo libre único + parsing por contenido (numérico → DNI/HC, alfanumérico → apellido). Alternativa: pestañas o radios. ¿Cuál preferís?
- [ ] **Modal R2** vs página: lo dejé como modal sobre R1 cuando ya elegiste paciente, para no sumar una página intermedia. Si querés ruta dedicada `/encounters/new/:patientId`, lo cambio.
- [ ] **Autosave R3**: cada 2 s con debounce. ¿OK?
- [ ] **Índice lateral en R3**: lo activé siempre. En cuestionarios cortos (< 10 preguntas) podría ocultarse. ¿Regla?

## Editor Q2

- [ ] **Drag-and-drop**: usé SortableJS asumiendo que se va a vendorear. Stub: por ahora los handles existen pero la lógica de reordenamiento real espera SortableJS cargado.
- [ ] **Tipos soportados en MVP del editor**: `string, integer, decimal, boolean, choice, date`. Adjuntos y `display` quedan stubeados (placeholder de "próximamente"). Confirmá.
- [ ] **Autosave del editor**: debounce 2 s, igual que R3. ¿Querés un botón Guardar explícito además del autosave?

## E2 / E3 / E4

- [ ] **Query Builder (E2)**: operadores por tipo de pregunta. Mapeo asumido:
  - numérico: `= ≠ < ≤ > ≥ entre vacío no_vacío`
  - texto: `= ≠ contiene empieza_con vacío`
  - booleano: `es`
  - fecha: `= ≠ < ≤ > ≥ entre últimos_N_días vacío`
  - choice: `es es_alguno_de no_es vacío`
- [ ] **Preview cohorte (E3)**: paginación server-side, 50 por página. ¿Va?
- [ ] **Exportación (E4)**: > 10k filas dispara modo async. Endpoint pendiente en backend.

## A6/A7 Auditoría

- [ ] **Filtro por defecto**: últimas 24 hs. ¿Mantenemos o ampliamos a 7 días?
- [ ] **Export de auditoría**: comparte el endpoint de reportes (CSV/XLSX) o tiene endpoint propio? Asumí endpoint propio `GET /api/audit/export?...`.

## i18n

- [ ] **Claves en MVP**: dejé los textos hardcoded en español por ahora; las pantallas funcionan y son legibles. La pasada de i18n llenando `.resx` queda como tarea separada (spec 007 F2/F3).
- [ ] **Switch de idioma**: funcional, setea cookie y recarga. Pero los textos no rotan hasta que esté el resource provider.

## CSS / responsive

- [ ] **Breakpoint mobile (< 1024px)**: oculto el sidebar y queda sólo el navbar. ¿Acepta como degradación o querés sidebar overlay tipo hamburguesa?
- [ ] **Tablet (R1, R3)**: la spec menciona soporte explícito. Lo dejé responsivo pero no probé con touch.

## Backend wiring (junio 2026)

- [ ] **DI container custom** vs SimpleInjector/Autofac: usé un `ServiceComposition` propio (sin NuGet nuevo) que implementa los dos `IDependencyResolver` (MVC y Web API). Funciona pero no maneja scope por request fino. Si el equipo prefiere SimpleInjector, el `ServiceComposition` está aislado y se reemplaza sin tocar controllers.
- [ ] **`DbContext` transient**: cada repo crea un context nuevo por resolución. Para un MVP funciona; para producción conviene scope-por-request (con SimpleInjector trivial).
- [ ] **`Audit detalle`** (A7) hoy hace un `SearchAsync(_count=5000)` + filtro client-side por id porque `IAuditRepository` no tiene `GetAsync(id)`. Agregar el método al interfaz cuando se trabaje en hardening del módulo 005.
- [ ] **`PatientService.SearchAsync`** asume que la vista Oracle `VW_PACIENTE_DATOS_BASICOS` acepta un filtro `QUERY` libre que parsea DNI/HC/apellido internamente. Si la vista no es así, lo refinamos.
- [ ] **JWT en islas Vue**: las islas hoy usan mock data internamente. Para que llamen a `/fhir/r5/Questionnaire` etc. con `Authorization: Bearer`, hay que embeber el token en un `<meta name="cq-token">` del layout y leerlo desde `bootstrap.js`. Diseño definido; implementación pendiente.
- [ ] **Modo demo (`ViewBag.DataMode`)**: los MVC controllers caen a `MockSeed` si la DB no responde. Útil para que el equipo pueda levantar el proyecto sin migración. En prod el flag NO debería entrar; agregar un assert en `Application_Start` que verifique connectividad y bloquee el arranque si no hay DB.

## Cosas que no implementé y son spec

- [ ] **Style guide en `/styleguide`** (T014 de tasks): no lo creé. Vale la pena si vas a iterar el theme.
- [ ] **Asset vendoring** (Bootstrap a `wwwroot/lib/`): sigue por CDN.
- [ ] **Pencil mockups** (`.pen`): no los generé.
- [ ] **Tests Playwright y axe**: no escritos en este pase.

## Convenciones de código

- [ ] **Mock data en controllers**: dejé seed estático dentro de los controllers MVC para que las pantallas se vean con contenido. En cuanto los servicios de Application estén conectados, se reemplaza.
- [ ] **Sin DI todavía**: los controllers no usan inyección — instancian todo. Cuando se configure el container (ver README.md de `src/`) se ajustan los constructors.
