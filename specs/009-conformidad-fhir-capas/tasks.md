---

description: "Task list for feature 009 — Conformidad FHIR real y saneamiento de capas"
---

# Tasks: Conformidad FHIR real y saneamiento de capas

**Input**: Design documents from `/specs/009-conformidad-fhir-capas/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/fhir-api.md, quickstart.md

**Tests**: **SÍ se incluyen.** La spec los exige explícitamente — FR-021 (las pruebas no
dependen de infraestructura), FR-022 (prueba de arquitectura), SC-008 (cobertura nueva de
reglas de negocio), SC-009 (las pruebas existentes siguen pasando). No es una elección de
estilo: son criterios de aceptación.

**Organization**: Tareas agrupadas por historia de usuario para permitir implementación y
verificación independientes.

> **Revisión post-`/speckit-analyze`**: se incorporaron T024 (hueco C1: `AuditPresentation`) y
> T069 (hueco C2: pantalla de capacidades), se amplió T103 (hueco C3: documentar la frontera
> FHIR / REST propio) y se corrigió la nota de paralelismo entre US2 y US3 (hallazgo F1), que
> declaraba un paralelismo imposible sobre los mismos archivos JavaScript.

> **Revisión tras la Fase 2 (hallazgo R8)**: medir los recursos ya congelados contra el parser
> del SDK reveló que el sistema **declara `fhirVersion: 5.0.0` pero emite vocabulario R4**
> (`choice` / `open-choice`, que R5 eliminó). Se agregaron **T018b** (mapeo correcto a
> `coding` + `answerConstraint`), **T023b** (lectura permisiva de lo ya publicado, o 9
> cuestionarios dejan de renderizarse) y **T023c** (prueba de regresión), y se ajustó T056.
>
> Esas tres llevan sufijo en vez de renumerar las 88 tareas posteriores por segunda vez:
> el churn de una renumeración masiva —y romper toda referencia previa— cuesta más que la
> desprolijidad de `T018b`. Ordenan igual y señalan con claridad que se insertaron después.
> **Total: 109 tareas.**

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Puede correr en paralelo (archivos distintos, sin dependencias pendientes)
- **[Story]**: A qué historia pertenece (US1…US7)
- Cada tarea incluye la ruta exacta del archivo

## Path Conventions

Solución .NET de 5 proyectos bajo `src/`. Rutas relativas a la raíz del repositorio.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Cablear el SDK FHIR. **Es la compuerta de riesgo de toda la feature** (R1 en
`research.md`): si el SDK no carga sobre .NET Framework 4.7.1, la forma de la feature cambia.
No escribir código funcional hasta terminar esta fase.

- [X] T001 Agregar `PackageReference Include="Hl7.Fhir.R5" Version="5.13.4"` (versión pinneada, no flotante) en `src/ChequeAR.Application/ChequeAR.Application.csproj`
- [X] T002 [P] Agregar `Hl7.Fhir.Specification.R5` y `Hl7.Fhir.Specification.Data.R5` 5.13.4 en `src/ChequeAR.Infrastructure/ChequeAR.Infrastructure.csproj`
- [X] T003 [P] Agregar `Hl7.Fhir.R5` 5.13.4 en `src/ChequeAR.Api/ChequeAR.Api.csproj` (proyecto web clásico, ya usa `RestoreProjectStyle=PackageReference`)
- [X] T004 [P] Agregar `Hl7.Fhir.R5` 5.13.4 en `src/ChequeAR.Tests/ChequeAR.Tests.csproj` — NO agregar referencias a `ChequeAR.Infrastructure` ni `ChequeAR.Api` (FR-021)
- [X] T005 Restaurar paquetes, leer la versión real de assembly de los DLL en `src/ChequeAR.Api/bin/` y actualizar los `bindingRedirect` de `System.Text.Json`, `System.Text.Encodings.Web` y `System.Runtime.CompilerServices.Unsafe` en `src/ChequeAR.Api/Web.config` (líneas 129-158). No asumir la versión a partir del número de paquete
- [X] T006 Compilar con MSBuild y levantar con `/run-app`, verificando ausencia de `FileLoadException` y `MissingMethodException` (paso 1 de `quickstart.md`)

**⚠️ Checkpoint / GO-NO-GO**: si T006 no pasa tras agotar los binding redirects, aplicar el
fallback documentado en R1 — bajar a `Hl7.Fhir.R5` 5.9.x (arrastra `System.Text.Json` 8.x) — y
repetir T005-T006 antes de continuar.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Piezas compartidas por todas las historias de FHIR (US1-US5).

**⚠️ CRITICAL**: US1 a US5 no pueden empezar hasta terminar esta fase.
**US6 y US7 NO dependen de esta fase** y pueden arrancar en paralelo desde el día 1.

- [X] T007 [P] Crear `src/ChequeAR.Application/Fhir/ChequeARExtensions.cs` con las URL y helpers de lectura/escritura de las dos extensiones propias: `question-term` y `questionnaire-version-id` (ver §Extensiones en `contracts/fhir-api.md`)
- [X] T008 [P] Crear `src/ChequeAR.Application/Fhir/OperationOutcomes.cs` con fábricas `NotFound`, `Forbidden`, `Conflict(expected, actual)`, `PreconditionRequired`, `Unprocessable`, `Invalid`, `Transient`, mapeando al catálogo de `contracts/fhir-api.md` (`severity`, `code`, `diagnostics` en español)
- [X] T009 [P] Crear `src/ChequeAR.Application/Fhir/FhirBundles.cs` con `SearchSet(IEnumerable<Resource>, int total, string baseUrl)` que emita `type = "searchset"`, `total` real y `entry[].fullUrl` + `entry[].resource` (FR-003)
- [X] T010 Crear `src/ChequeAR.Api/Fhir/FhirMediaTypeFormatter.cs`: `MediaTypeFormatter` que serializa y parsea `Hl7.Fhir.Model.Resource` con `FhirJsonSerializer` / `FhirJsonParser`
- [X] T011 Registrar el formateador **primero** en la cadena en `src/ChequeAR.Api/App_Start/WebApiConfig.cs`, cubriendo `application/fhir+json` y `application/json`, y eliminar el registro actual de líneas 26-28 que sólo agrega el tipo de contenido al serializador genérico (FR-002)
- [X] T012 [P] Crear pruebas de forma de `OperationOutcome` en `src/ChequeAR.Tests/Application/OperationOutcomeTests.cs`, una por cada categoría del catálogo
- [X] T013 [P] Crear pruebas de forma de `Bundle` searchset en `src/ChequeAR.Tests/Application/FhirBundleTests.cs`, verificando `fullUrl`, `resource` y que `total` no sea el de la página
- [X] T014 Mover el registro de `IFhirValidator` de transitorio a instancia única en `src/ChequeAR.Api/App_Start/ServiceComposition.cs` (línea 97 → junto a `_jwt` / `_audit`, líneas 34-38 y 62-72). Prepara US4, donde crear un validador por petición sería inviable (R5)

**Checkpoint**: cimientos FHIR listos y cubiertos por pruebas unitarias.

> El formatter queda **cableado pero todavía sin efecto observable**: sólo actúa sobre tipos
> que derivan de `Resource`, y hasta la Fase 3 ningún endpoint devuelve uno. `/fhir/r5/metadata`
> sigue respondiendo `application/json` hasta T028. Es lo esperado, no un defecto: FR-002 se
> verifica end-to-end recién con la Fase 3.

---

## Phase 3: User Story 1 - Un sistema externo puede leer los datos clínicos (P1) 🎯 MVP

**Goal**: Todo lo que la interfaz de interoperabilidad **devuelve** es un recurso FHIR R5 real.

**Independent Test**: Consultar un cuestionario, una ficha y una búsqueda; pegar cada respuesta
en <https://validator.fhir.org/>. Cero errores. No requiere ninguna otra historia.

### Tests for User Story 1

- [X] T015 [P] [US1] Cobertura de serialización de cuestionario: `resourceType`, `item[]` anidado por grupos, `answerOption.valueCoding` y las extensiones de validación de §1.3 de `data-model.md`. **Va en `src/ChequeAR.Tests/Application/FhirQuestionnaireBuilderTests.cs`**, que ya cubre rangos, códigos, término y JSON corrupto — un archivo paralelo para la misma clase partiría la cobertura en dos lugares sin ganancia
- [X] T016 [P] [US1] Crear `src/ChequeAR.Tests/Application/FhirResponseSerializationTests.cs`: `linkId` repetidos se agrupan en **un** `item` con varias `answer`; una pregunta sin responder se serializa **sin** `answer`; el `Patient` contenido se preserva
- [X] T017 [P] [US1] Agregar caso a `FhirQuestionnaireSerializationTests.cs` verificando que el `answer[x]` de `enableWhen` se tipa según la pregunta **referenciada**, no según la que lo declara (§1.4 de `data-model.md` — invariante frágil)

### Implementation for User Story 1

- [X] T018 [US1] Reescribir `src/ChequeAR.Application/Fhir/FhirQuestionnaireBuilder.cs` para construir `Hl7.Fhir.Model.Questionnaire`, exponiendo `ToResource(Questionnaire, int versionNumber)` y conservando `Build(...)` → `string` (lo consume `QuestionnaireService.cs:158`) vía `FhirJsonSerializer`. Preservar: `useContext` de especialidad, `scale` → `integer` + itemControl `slider`, extensiones `minValue`/`maxValue`/`regex`/`mimeType`/`questionnaire-unit`, `maxLength` nativo, y el índice `linkId → Question` para tipar `enableWhen`
- [X] T018b [US1] **Corregir el mapeo de tipos de opción a vocabulario R5** en `FhirQuestionnaireBuilder.cs`: `Choice` → `coding` + `answerConstraint = optionsOnly`, `OpenChoice` → `coding` + `answerConstraint = optionsOrString`. El builder actual emite `choice` / `open-choice`, que son de **R4 y no existen en R5** — el sistema venía declarando conformidad 5.0.0 sobre vocabulario inválido. Ver §1.2 de `data-model.md`
- [X] T019 [US1] Emitir `meta.versionId` desde `ConcurrencyVersion` y `meta.profile` en `FhirQuestionnaireBuilder.cs` (FR-007)
- [X] T020 [US1] Emitir la extensión `question-term` desde `CodingJson.term` en `FhirQuestionnaireBuilder.cs` — hoy se omite a propósito; con superficie única FHIR su ausencia pierde el dato (§1.5 de `data-model.md`)
- [X] T021 [P] [US1] Reescribir `src/ChequeAR.Application/Fhir/FhirQuestionnaireResponseBuilder.cs` sobre `Hl7.Fhir.Model.QuestionnaireResponse`, conservando el agrupado por `linkId`, el `Patient` contenido y los textos tomados de la versión anclada
- [X] T022 [US1] Emitir la extensión `questionnaire-version-id` en `FhirQuestionnaireResponseBuilder.cs`
- [X] T023 [US1] Reescribir `src/ChequeAR.Application/Fhir/FhirQuestionnaireReader.cs` sobre `FhirJsonParser`, **manteniendo intacto** el contrato de salida `RenderableQuestionnaire` / `RenderableGroup` / `RenderableQuestion`. Si el contrato no cambia, la hidratación del renderizador y `QuestionnaireResponseService` no se tocan
- [X] T023b [US1] **Compatibilidad hacia atrás con las versiones ya congeladas** (FR-014, FR-023): el reader usa `ParserSettings` permisivo (`AllowUnrecognizedEnums`, `AcceptUnknownMembers`, `PermissiveParsing`) y normaliza el vocabulario legado `choice` / `open-choice` → `coding`. Medido sobre la base real: de 55 recursos congelados, **9 sólo parsean en modo permisivo** y 0 fallan del todo. Sin esto, 9 cuestionarios publicados dejan de renderizarse para los clínicos. La escritura sigue siendo estricta — permisivo al leer lo viejo, estricto al escribir lo nuevo. Ver §2.1 de `data-model.md`
- [X] T023c [P] [US1] Prueba de regresión en `src/ChequeAR.Tests/Application/FhirLegacyReadTests.cs`: un recurso congelado con `item.type = "choice"` se lee sin excepción y devuelve una pregunta de tipo `choice` en el contrato `Renderable*`. Es la red que evita romper lo ya publicado
- [X] T024 [US1] Adaptar `FlattenForHumans` de `src/ChequeAR.Api/Helpers/AuditPresentation.cs:106` al recurso reescrito: hoy parsea el JSON almacenado con `JObject` para mostrar el detalle de auditoría, y las tareas T018-T022 cambian ese JSON. Verificar `value[x]`, `contained` y la ubicación de extensiones. **Sin esta tarea el fallo sería silencioso y sólo visible en la pantalla de auditoría**
- [X] T025 [US1] Convertir las lecturas de `src/ChequeAR.Api/Controllers/Api/QuestionnaireController.cs` (`Search` línea 31, `Get` línea 39) para devolver `Bundle` vía `FhirBundles` y `Questionnaire` POCO, conservando el `ETag`
- [X] T026 [US1] Convertir las lecturas de `src/ChequeAR.Api/Controllers/Api/QuestionnaireResponseController.cs` (`Search` línea 27, `Get` línea 35)
- [X] T027 [P] [US1] Proyectar `Domain.AuditEvent` → `Hl7.Fhir.Model.AuditEvent` (`agent.who`, `entity.what`, `recorded`, `action`) en `src/ChequeAR.Api/Controllers/Api/AuditEventController.cs`
- [X] T028 [P] [US1] Emitir `CapabilityStatement` POCO en `src/ChequeAR.Api/Controllers/Api/MetadataController.cs`, leyendo de `CapabilityCatalog`
- [X] T029 [US1] Adaptar `src/ChequeAR.Api/Scripts/islands/report-builder/index.js`: `loadQuestions()` (línea 93) debe recorrer `item[]` **recursivamente** porque los grupos anidan; opciones desde `answerOption.valueCoding`; listado (línea 183) desde `data.entry[].resource`
- [X] T030 [US1] Cambiar `STATUS_LABEL` y `STATUS_ORDER` (líneas 32-33 de `report-builder/index.js`) de numéricos `{0,1,2}` a códigos FHIR `'draft' | 'active' | 'retired'`
- [X] T031 [US1] Adaptar `src/ChequeAR.Api/Views/Encounters/Search.cshtml` línea 159 para leer `bundle.entry[].resource.questionnaire` y `.authored`

**Checkpoint**: US1 funcional. Las respuestas pasan el validador oficial. Las escrituras
todavía usan el formato interno.

---

## Phase 4: User Story 2 - Los cuestionarios se crean y editan en el formato del estándar (P1)

**Goal**: La interfaz **acepta** recursos FHIR y no se pierde ningún dato en el ciclo.

**Independent Test**: Crear un cuestionario con los 11 tipos de pregunta, guardar, recargar, y
verificar que ninguna propiedad se perdió (paso 6 de `quickstart.md`).

### Tests for User Story 2

> **⚠️ Escribir estas pruebas ANTES de tocar el JavaScript.** Son la única red contra pérdida
> silenciosa de datos clínicos, que es el mayor riesgo de la feature.

- [X] T032 [P] [US2] Crear `src/ChequeAR.Tests/Application/FhirRoundTripTests.cs`: dominio → `FhirQuestionnaireBuilder` → `FhirQuestionnaireParser` → dominio, con una pregunta de cada uno de los 11 tipos de §1.2 de `data-model.md`
- [X] T033 [P] [US2] Agregar a `FhirRoundTripTests.cs` la verificación de que **no se pierden**: `term`, la distinción `AnswerOption.Code` vs `Value` vs `Display`, `scale`, `maxLength`, `pattern`, `accept`, `unit`, `min`/`max` y `enableWhen` con su tipo correcto
- [X] T034 [P] [US2] Agregar a `FhirRoundTripTests.cs` el caso de secciones anidadas con orden de preguntas preservado
- [X] T035 [P] [US2] Crear `src/ChequeAR.Tests/Application/FhirResponseRoundTripTests.cs` para el par constructor/parser de fichas, incluyendo adjuntos y el anclaje de versión

### Implementation for User Story 2

- [X] T036 [US2] Crear `src/ChequeAR.Application/Fhir/FhirQuestionnaireParser.cs`: FHIR `Questionnaire` → entidad de dominio, reconstituyendo `Groups`, `Questions`, `Options`, `ValidationJson`, `EnableWhenJson` y `CodingJson`. Es el inverso exacto de T018-T020
- [X] T037 [P] [US2] Crear `src/ChequeAR.Application/Fhir/FhirQuestionnaireResponseParser.cs`: FHIR `QuestionnaireResponse` → dominio, con los `Value*` tipados y `AttachmentId` extraído de `valueAttachment.url`
- [X] T038 [US2] Convertir `POST` y `PUT` de `src/ChequeAR.Api/Controllers/Api/QuestionnaireController.cs` (líneas 66 y 77) para recibir `Questionnaire` FHIR y pasarlo por el parser, conservando la semántica de `If-Match` / 428 / 409 de las líneas 51-64 y 85-92
- [X] T039 [P] [US2] Convertir `POST` de `src/ChequeAR.Api/Controllers/Api/QuestionnaireResponseController.cs` (línea 43) para recibir `QuestionnaireResponse` FHIR
- [X] T040 [US2] Reescribir `buildPayload()` en `src/ChequeAR.Api/Scripts/islands/questionnaire-editor/index.js` (línea 541) para emitir un `Questionnaire` FHIR: `item[]` anidado con grupos `type: "group"`, `answerOption.valueCoding`, y lo que hoy empaquetan `buildValidation()` / `buildCoding()` (líneas 497-536) como extensiones estándar + `question-term`
- [X] T041 [US2] Cambiar el `Content-Type` a `application/fhir+json` en `questionnaire-editor/index.js` línea 607
- [X] T042 [US2] Leer la versión de registro desde `data.meta.versionId` en `questionnaire-editor/index.js` líneas 645-647, y eliminar el fallback `Id`/`id` de la línea 633 (FHIR normaliza en `id` minúscula)
- [X] T043 [US2] Reescribir `toItems()` y el cuerpo de `submit()` en `src/ChequeAR.Api/Scripts/islands/questionnaire-renderer/index.js` (líneas ~300-360) para emitir `item[].answer[].value[x]` en lugar de `{ ValueBoolean, ValueInteger, ... }`, con `subject.identifier.value` y la extensión de versión anclada
  - **Nota (2026-10-16):** el alta (`POST`) manda el recurso FHIR; el guardado parcial sigue con la forma interna, ahora como `POST QuestionnaireResponse/{id}/$save-progress` (antes un `PUT` que no recibía un recurso). Por eso el CapabilityStatement ya no declara `update`.
- [X] T044 [US2] Verificar manualmente el ciclo completo del paso 6 de `quickstart.md`: 11 tipos, guardar, F5, nada perdido — **especialmente el término descriptivo**
- [X] T045 [US2] Verificar el escenario de concurrencia: dos pestañas, guardar en ambas, la segunda recibe 409 y no pisa a la primera (paso 6 de `quickstart.md`)

**Checkpoint**: US1 y US2 funcionan. Superficie única FHIR de lectura y escritura, sin pérdida.

---

## Phase 5: User Story 3 - Los errores son diagnosticables por un sistema externo (P1)

**Goal**: Toda respuesta de error de la interfaz es un `OperationOutcome`.

**Independent Test**: Provocar cada categoría de error del catálogo y verificar estructura y
código (paso 4 de `quickstart.md`).

**⚠️ Paralelismo acotado**: T046-T050 (backend) pueden ir en paralelo con US2. T051-T052 **no**:
editan `questionnaire-editor/index.js` y `questionnaire-renderer/index.js`, los mismos archivos
que T040-T043. Van después de T043.

### Tests for User Story 3

- [X] T046 [P] [US3] Extender `src/ChequeAR.Tests/Application/OperationOutcomeTests.cs` con un caso por cada regla de negocio que hoy devuelve 422: publicar sin preguntas, editar un archivado, responder un cuestionario retirado

### Implementation for User Story 3

- [X] T047 [US3] Reemplazar por `OperationOutcome` los retornos `{ error }` de `src/ChequeAR.Api/Controllers/Api/QuestionnaireController.cs` líneas 88, 104, 115 y 130, y el mensaje informativo de archivado-en-lugar-de-borrado de la línea 169
- [X] T048 [P] [US3] Reemplazar los retornos `{ error }` de `src/ChequeAR.Api/Controllers/Api/QuestionnaireResponseController.cs` líneas 54 y 68
- [X] T049 [P] [US3] Reemplazar los retornos `{ error }` de `src/ChequeAR.Api/Controllers/Api/TerminologyController.cs` líneas 53 y 59, mapeando 429/502/503 a `code = "transient"` y conservando el dato de cuánto esperar antes de reintentar
- [X] T050 [P] [US3] Devolver `OperationOutcome` con `code = "forbidden"` en `src/ChequeAR.Api/Controllers/Api/SavedReportController.cs` líneas 66 y 79
- [X] T051 [US3] Leer `body.issue[0].diagnostics` en lugar de `body.error` en `src/ChequeAR.Api/Scripts/islands/questionnaire-editor/index.js` líneas 622 (conflicto 409) y 726 (fallo de `$publish`) — **después de T042**
- [X] T052 [US3] Leer `issue[0].diagnostics` en el manejo de errores de `src/ChequeAR.Api/Scripts/islands/questionnaire-renderer/index.js` — **después de T043**
- [X] T053 [US3] Recorrer manualmente la tabla completa del paso 4 de `quickstart.md`, verificando que ninguna pantalla degrada a mensaje genérico (FR-009)

**Checkpoint**: las tres historias P1 completas. La afirmación central del documento de
arquitectura ya es verificable.

---

## Phase 6: User Story 4 - Lo publicado cumple el perfil de conformidad declarado (P2)

**Goal**: Validación real contra perfiles que existen como artefactos.

**Independent Test**: Publicar contenido que viola el perfil → rechazo con la regla incumplida;
publicar contenido correcto → pasa y valida contra el validador oficial.

### Tests for User Story 4

- [X] T054 [P] [US4] Crear `src/ChequeAR.Tests/Application/ProfileValidationTests.cs` con casos: `linkId` duplicado falla, falta de especialidad falla, cuestionario correcto pasa
- [X] T055 [P] [US4] Agregar a `ProfileValidationTests.cs` el caso de FR-014: un recurso publicado **antes** de esta feature que no cumple el perfil vigente se sigue pudiendo leer sin error

### Implementation for User Story 4

- [X] T056 [P] [US4] Escribir `src/ChequeAR.Infrastructure/Fhir/Profiles/ChequeARQuestionnaire.StructureDefinition.json` con las restricciones de §5 de `data-model.md`: `title` y `status` presentes, `subjectType = Patient`, `useContext` de especialidad obligatorio, `item.linkId` obligatorio y **único**, e `item.type` acotado al subconjunto que el renderizador sabe dibujar — usando **vocabulario R5** (`coding`, no `choice`; ver §1.2 de `data-model.md`)
- [X] T057 [P] [US4] Escribir `src/ChequeAR.Infrastructure/Fhir/Profiles/ChequeARQuestionnaireResponse.StructureDefinition.json`: `subject.identifier` obligatorio, `questionnaire` obligatorio, extensión de versión anclada obligatoria
- [X] T058 [US4] Configurar `CopyToOutputDirectory=PreserveNewest` para `Fhir/Profiles/*.json` en `src/ChequeAR.Infrastructure/ChequeAR.Infrastructure.csproj`, siguiendo el patrón de `Oracle/allowlist.yaml`
- [X] T059 [US4] Reemplazar el sustituto de `src/ChequeAR.Infrastructure/Fhir/FhirValidatorService.cs` por el `Validator` de Firely con `CachedResolver(new MultiResolver(ZipSource.CreateValidationSource(), new DirectorySource(profilesDir)))`, mapeando su `OperationOutcome` al `ValidationResult` del contrato existente para no tocar a sus dos consumidores
- [X] T060 [US4] Resolver la ruta de perfiles desde `AppDomain.CurrentDomain.BaseDirectory` con comportamiento de reserva si el directorio falta, en `src/ChequeAR.Api/App_Start/ServiceComposition.cs` (mismo patrón que líneas 91-95)
- [X] T061 [US4] Agregar calentamiento del validador al arrancar la aplicación en `src/ChequeAR.Api/App_Start/ServiceComposition.cs`, para que la primera publicación real no pague la carga de la especificación
- [X] T062 [US4] Emitir un `issue` por cada regla incumplida al fallar la validación en `$publish` y `$finalize`, en lugar de un único mensaje concatenado (`QuestionnaireService.cs:160-162`, `QuestionnaireResponseService.cs:156-161`)
- [X] T063 [US4] Verificar el paso 5 completo de `quickstart.md`, incluida la lectura de una versión publicada anterior a la feature

**Checkpoint**: el perfil que el sistema declaraba cumplir existe y se aplica de verdad.

---

## Phase 7: User Story 5 - Pacientes y encuentros como recursos del estándar (P2)

**Goal**: Cerrar la cobertura de recursos que el documento de arquitectura nombra.

**Independent Test**: Buscar un paciente y listar sus encuentros por la interfaz estándar;
validar ambas respuestas en el validador oficial.

### Tests for User Story 5

- [X] T064 [P] [US5] Crear `src/ChequeAR.Tests/Application/PatientProjectionTests.cs`: `PatientSearchHit` → `Patient` con `identifier` de historia clínica, `name` y `birthDate` cuando está disponible

### Implementation for User Story 5

- [X] T065 [US5] Mover `src/ChequeAR.Api/Controllers/Api/PatientController.cs` de `/api/patients` a `/fhir/r5/Patient`, devolviendo `Bundle` de `Patient` en la búsqueda y `Patient` en la lectura. Sin consumidores JS actuales, el movimiento es seguro
- [X] T066 [P] [US5] Crear `src/ChequeAR.Api/Controllers/Api/EncounterController.cs` en `/fhir/r5/Encounter` con `?subject=`, reusando `PatientService.GetEncountersAsync`. Reemplaza `/api/patients/{id}/encounters`
- [X] T067 [US5] Agregar `Patient` (`read`, `search-type`) y `Encounter` (**sólo `search-type`** — un encuentro derivado no tiene identidad propia estable, ver `contracts/fhir-api.md`) a `src/ChequeAR.Api/Fhir/CapabilityCatalog.cs`
- [X] T068 [US5] Declarar `Observation` como **no soportado** en `CapabilityCatalog.cs` (FR-013) — el sistema no las produce
- [X] T069 [US5] Extender `CapResource` en `src/ChequeAR.Api/Models/AdminViewModels.cs:96` con el estado de soporte, y `src/ChequeAR.Api/Views/Admin/Capability.cshtml:27` para distinguirlo. Sin esto, `Observation` se renderizaría en la pantalla F2 como un recurso soportado sin interacciones, que es engañoso
- [X] T070 [US5] Registrar `EncounterController` en `src/ChequeAR.Api/App_Start/ServiceComposition.cs` junto a los demás controllers de API
- [X] T071 [US5] Verificar el modo degradado de FR-016: detener el listener de Oracle y confirmar que la búsqueda de pacientes sigue derivando de las fichas propias, sin datos inventados
- [X] T072 [US5] Actualizar `specs/009-conformidad-fhir-capas/contracts/fhir-api.md` si alguna ruta o parámetro terminó distinto de lo contratado

**Checkpoint**: las cinco historias de FHIR completas.

---

## Phase 8: User Story 6 - La lógica de negocio no depende del motor de base de datos (P2)

**Goal**: Eliminar la dependencia de la capa de negocio sobre los componentes del motor.

**⚠️ Independiente de las Fases 2-7.** Puede arrancar en paralelo desde el día 1.

**Independent Test**: `grep -n "Oracle" src/ChequeAR.Application/ChequeAR.Application.csproj`
vacío, y las 11 pruebas de reportes siguen pasando.

### Tests for User Story 6

- [X] T073 [P] [US6] Crear `src/ChequeAR.Tests/Architecture/LayerDependencyTests.cs` que asserte sobre `typeof(QueryBuilder).Assembly.GetReferencedAssemblies()` que `ChequeAR.Application` no referencia `Oracle.ManagedDataAccess` ni `EntityFramework` (FR-022). **Debe fallar antes de T075-T081 y pasar después**

### Implementation for User Story 6

- [X] T074 [P] [US6] Crear `src/ChequeAR.Application/Reports/QueryParameter.cs`: parámetro neutro con `Name`, `Value` y un enum de tipo propio (`Int32`, `Raw16`, `Date`, `String`, `Decimal`) que cubra los casos donde hoy se necesita el tipo del driver
- [X] T075 [US6] Cambiar `BuiltQuery.Parameters` de `List<OracleParameter>` a `List<QueryParameter>` y eliminar `using Oracle.*` y `using System.Data` de `src/ChequeAR.Application/Reports/QueryBuilder.cs` (líneas 4, 8, 9 y los ~15 sitios que construyen parámetros). **El archivo se queda en Application**: decidir qué columna consultar por tipo de pregunta y cómo se traduce cada operador es lógica de negocio
- [X] T076 [US6] Convertir `GuidParameter` (línea 223 de `QueryBuilder.cs`) para emitir un `QueryParameter` de tipo `Raw16` con los bytes, en lugar de construir el tipo del driver
- [X] T077 [P] [US6] Crear `src/ChequeAR.Application/Interfaces/IReportQueryRunner.cs` con `Task<QueryRunResult> RunAsync(BuiltQuery query, bool paginate)` devolviendo filas y total
- [X] T078 [US6] Crear `src/ChequeAR.Infrastructure/Oracle/OracleReportQueryRunner.cs` con todo lo específico del driver que hoy vive en `ReportExecutor.cs:78-115`: conexión, comandos, `BindByName`, traducción `QueryParameter` → parámetro del driver, el clonado entre comandos (`CloneParameters`, línea 128) y la normalización de 16 bytes → identificador (`OracleValue.Normalize`, línea 147). **Mover con sus comentarios**: explican fallos reales y no obvios
- [X] T079 [US6] Convertir `src/ChequeAR.Application/Reports/ReportExecutor.cs` para orquestar contra `IReportQueryRunner`: resolver cuestionario, armar esquema, construir, delegar, pivotear. Conserva la regla `MaxSyncRows` (línea 17) y deja de recibir la cadena de conexión
- [X] T080 [US6] Actualizar el registro de `ReportExecutor` y agregar `IReportQueryRunner` → `OracleReportQueryRunner` en `src/ChequeAR.Api/App_Start/ServiceComposition.cs` (línea 112)
- [X] T081 [US6] Quitar `PackageReference Include="Oracle.ManagedDataAccess"` de `src/ChequeAR.Application/ChequeAR.Application.csproj` (FR-018)
- [X] T082 [US6] Ajustar las 11 pruebas de `src/ChequeAR.Tests/Application/ReportQueryTests.cs` (líneas 152-273) al nuevo tipo de parámetro. **Todas deben seguir pasando, en particular la de inyección SQL de la línea 212** (SC-009)
- [X] T083 [US6] Verificar que `LayerDependencyTests` (T073) ahora pasa
- [X] T084 [US6] Verificar el paso 9 completo de `quickstart.md`: filtros de cada clase, exportación a Excel sin `System.Byte[]` en los identificadores, estadísticas sobre la cohorte completa, y rehidratación de una consulta guardada

**Checkpoint**: la afirmación sobre aislamiento de la persistencia queda respaldada por una
prueba automática.

---

## Phase 9: User Story 7 - Las reglas de negocio son probables de forma aislada (P3)

**Goal**: Ningún controller accede a repositorios; las reglas viven donde se pueden probar.

**⚠️ Independiente de las Fases 2-7**, pero T094 toca controllers que las Fases **3, 4, 5 y 7**
también modifican (`QuestionnaireController`, `QuestionnaireResponseController`,
`AuditEventController`, `SavedReportController`, `PatientController`): coordinar o hacerlo
después para evitar conflictos.

**Independent Test**: `grep -rn "I[A-Za-z]*Repository" src/ChequeAR.Api/Controllers/` vacío, y
pruebas nuevas de autorización por propietario y de auditoría que no instancian nada web.

### Tests for User Story 7

- [X] T085 [P] [US7] Crear `src/ChequeAR.Tests/Application/SavedReportServiceTests.cs`: una persona no puede modificar ni eliminar la consulta guardada de otra (SC-008)
- [X] T086 [P] [US7] Crear `src/ChequeAR.Tests/Application/AuditQueryServiceTests.cs`: filtrado por actor y por recurso, y paginación con total real

### Implementation for User Story 7

- [X] T087 [P] [US7] Crear `src/ChequeAR.Application/Services/AuditQueryService.cs` absorbiendo el traer-5000-filas-y-filtrar-en-memoria de `AuditEventController.cs:19-24` y `Mvc/AuditController.cs:72,142`
- [X] T088 [P] [US7] Crear `src/ChequeAR.Application/Services/SavedReportService.cs` absorbiendo la autorización por propietario y el merge de campos de `SavedReportController.cs:60-77`
- [X] T089 [P] [US7] Crear `src/ChequeAR.Application/Services/UserService.cs` con el listado, conteo y alta/modificación que hoy hace `UserController` directo contra el repositorio
- [X] T090 [P] [US7] Crear `src/ChequeAR.Application/Services/HomeDashboardService.cs` absorbiendo el N+1 con cache manual de títulos de `Mvc/HomeController.cs:49-63`
- [X] T091 [P] [US7] Crear `src/ChequeAR.Application/Services/TerminologyService.cs` absorbiendo el parseo de `expansion.contains` → `{code, display}` de `TerminologyController.cs:39-47`
- [X] T092 [US7] Agregar métodos de lectura (`GetAsync`, `SearchAsync`, `CountAsync`, `GetSpecialtyCodesAsync`, `GetTitlesAsync`) a `src/ChequeAR.Application/Services/QuestionnaireService.cs`, y de lectura (`GetAsync`, `SearchAsync`) a `QuestionnaireResponseService.cs`
- [X] T093 [US7] Mover el parseo de `ValidationJson` / `EnableWhenJson` / `CodingJson` de `Mvc/QuestionnairesController.ParseValidation` (línea 234) a la capa de negocio. El armado de `QuestionView` / `QuestionGroupView` (líneas 157-230) **se queda** en presentación: es mapeo a ViewModel. Evaluar también mover ahí el parseo de `Helpers/AuditPresentation.cs` (ver T024): es la misma clase de problema
- [X] T094 [US7] Reemplazar la inyección de repositorios por servicios en los 12 controllers afectados de `src/ChequeAR.Api/Controllers/Api/` y `src/ChequeAR.Api/Controllers/Mvc/` (18 declaraciones), hasta que `grep -rn "I[A-Za-z]*Repository" src/ChequeAR.Api/Controllers/` no devuelva nada (FR-019)
- [X] T095 [US7] Registrar los cinco servicios nuevos en `src/ChequeAR.Api/App_Start/ServiceComposition.cs` (líneas 104-136) y actualizar los constructores de los controllers registrados (líneas 122-153)
- [X] T096 [US7] Verificar que `src/ChequeAR.Tests/ChequeAR.Tests.csproj` sigue referenciando **sólo** Domain y Application (FR-021)
- [X] T097 [US7] Verificar el paso 10 de `quickstart.md`: auditoría con filtros y paginación devuelve lo mismo que antes
- [X] T098 [US7] Verificar que el tablero de inicio muestra la misma información y sigue vaciándose con aviso —sin datos inventados— cuando la base no responde

**Checkpoint**: las siete historias completas.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [X] T099 Ejecutar `quickstart.md` completo de punta a punta, marcando cada casilla
- [X] T100 [P] **EJECUTADO** en <https://validator.fhir.org/> (validador v6.9.12) sobre un `Questionnaire` real de la API, autodetectado como R5. **Cero errores estructurales, de cardinalidad, de tipo o de vocabulario** — evidencia de SC-001.
      De los 4 hallazgos, 3 son timeouts del servidor de terminología público (infraestructura ajena) y 1 es nuestro: el validador no resuelve `.../question-term` porque los perfiles no están publicados en la web. Detalle completo y cómo resolverlo en R10 de `research.md`.

- [ ] T106 **PENDIENTE — el documento no vive en el repositorio.** Aplicar los ajustes de redacción al capítulo de arquitectura.
      Insumo listo en [`tesis-ajustes.md`](./tesis-ajustes.md): los textos de reemplazo ya redactados, más la evidencia concreta para la defensa y la limitación que conviene declarar antes de que la pregunten.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Fase 1 (Setup)**: sin dependencias. **Compuerta de riesgo: si falla, todo cambia**
- **Fase 2 (Foundational)**: depende de Fase 1. Bloquea US1-US5
- **Fase 3 (US1)**: depende de Fase 2
- **Fase 4 (US2)**: depende de Fase 3 — el parser es el inverso del constructor de US1
- **Fase 5 (US3)**: depende de Fase 2. **Paralela a US2 sólo en backend** (T046-T050);
  T051-T052 van después de T043 porque editan los mismos archivos JavaScript
- **Fase 6 (US4)**: depende de Fase 3 — sin recursos conformes no hay nada válido que validar
- **Fase 7 (US5)**: depende de Fase 2
- **Fase 8 (US6)**: ⚠️ **NO depende de ninguna fase FHIR**. Puede arrancar el día 1
- **Fase 9 (US7)**: ⚠️ **NO depende de ninguna fase FHIR**, pero T094 toca controllers que las
  Fases 3, 4, 5 y 7 también editan. Hacerlo después reduce conflictos
- **Fase 10 (Polish)**: depende de todas

### Desviación deliberada respecto de la plantilla

La plantilla asume que la fase Foundational bloquea **todas** las historias. Acá **US6 y US7 no
dependen de ella**: son saneamiento de capas, ortogonal a FHIR. Se documenta explícitamente
porque habilita paralelismo real, que es lo más valioso de este plan.

### Conflictos de archivo a coordinar

| Archivo | Tareas que lo tocan | Orden obligado |
|---|---|---|
| `questionnaire-editor/index.js` | T040, T041, T042 (US2) · T051 (US3) | T051 después de T042 |
| `questionnaire-renderer/index.js` | T043 (US2) · T052 (US3) | T052 después de T043 |
| Controllers de API | T025, T026, T027 (US1) · T038, T039 (US2) · T047-T050 (US3) · T065 (US5) · T094 (US7) | T094 al final |
| `ServiceComposition.cs` | T014, T060, T061, T070, T080, T095 | Secuencial |

### Within Each User Story

- Las pruebas se escriben **antes** que la implementación de esa historia
- En US2 en particular: **las pruebas de ida y vuelta (T032-T035) van antes que el JavaScript**
  (T040-T043). Es la única red contra pérdida silenciosa de datos
- Constructores antes que parsers; capa de negocio antes que controllers; controllers antes que
  islas Vue

### Parallel Opportunities

- **Desde el día 1**: Fase 1 (una persona) ∥ Fase 8 / US6 (otra persona) ∥ Fase 9 / US7
- Dentro de la Fase 2: T007, T008, T009 en paralelo; T012, T013 en paralelo
- Dentro de US1: T015-T017 en paralelo; después T021, T027, T028 en paralelo
- Dentro de US2: T032-T035 en paralelo
- Dentro de US3: T048, T049, T050 en paralelo
- Dentro de US4: T054-T055 y T056-T057 en paralelo
- Dentro de US7: T085-T086 y T087-T091 en paralelo (cinco servicios, cinco archivos distintos)
- Fase 10: T100-T103 en paralelo

---

## Parallel Example: User Story 2

```bash
# Las cuatro pruebas de ida y vuelta, juntas y ANTES de tocar el JavaScript:
Task: "T032 Round-trip de cuestionario con los 11 tipos en src/ChequeAR.Tests/Application/FhirRoundTripTests.cs"
Task: "T033 Verificar que no se pierden term, Code/Value/Display, scale, maxLength, pattern, accept, unit, min/max, enableWhen"
Task: "T034 Round-trip de secciones anidadas con orden preservado"
Task: "T035 Round-trip de fichas en src/ChequeAR.Tests/Application/FhirResponseRoundTripTests.cs"
```

## Parallel Example: User Story 7

```bash
# Los cinco servicios nuevos son cinco archivos independientes:
Task: "T087 AuditQueryService en src/ChequeAR.Application/Services/AuditQueryService.cs"
Task: "T088 SavedReportService en src/ChequeAR.Application/Services/SavedReportService.cs"
Task: "T089 UserService en src/ChequeAR.Application/Services/UserService.cs"
Task: "T090 HomeDashboardService en src/ChequeAR.Application/Services/HomeDashboardService.cs"
Task: "T091 TerminologyService en src/ChequeAR.Application/Services/TerminologyService.cs"
```

---

## Implementation Strategy

### MVP (US1 solamente)

1. Fase 1 — Setup. **Si falla, replantear antes de seguir**
2. Fase 2 — Foundational
3. Fase 3 — US1
4. **PARAR Y VALIDAR**: pegar las respuestas en el validador oficial de HL7
5. Con esto solo, la afirmación central del documento de arquitectura ya es demostrable en una
   defensa: la API **devuelve** FHIR real y validado externamente

### Entrega incremental

| Incremento | Qué habilita |
|---|---|
| Setup + Foundational + US1 | 🎯 MVP — lectura conforme, validable externamente |
| + US2 | Superficie única: también se escribe en FHIR, sin pérdida |
| + US3 | Interoperabilidad completa, errores incluidos |
| + US4 | El perfil declarado existe y se aplica |
| + US5 | Cobertura de recursos que el documento nombra |
| + US6 | La afirmación sobre aislamiento de persistencia queda respaldada |
| + US7 | La afirmación sobre testing independiente queda respaldada |

### Estrategia con dos personas

- **Persona A**: Fase 1 → 2 → 3 (US1) → 4 (US2) → 5 (US3) → 6 (US4) → 7 (US5)
- **Persona B**: Fase 8 (US6) → Fase 9 (US7) desde el día 1, sin esperar nada

US6 y US7 no tocan ningún archivo de las fases FHIR salvo `ServiceComposition.cs` y los
controllers (T094). Ver la tabla de conflictos de archivo.

---

## Notes

- `[P]` = archivos distintos, sin dependencias pendientes
- Los tres riesgos que no se pueden saltear: **T006** (compuerta del SDK), **T032-T035 antes de
  T040-T043** (pérdida silenciosa de datos), **T100** (evidencia externa)
- Al mover código con comentarios explicativos (T078 en particular), **moverlos tal cual**:
  documentan fallos reales y no obvios que ya costaron encontrar una vez
- Commit por tarea o grupo lógico; parar en cada checkpoint para validar la historia
- Evitar: tareas vagas, conflictos sobre el mismo archivo, dependencias cruzadas entre
  historias que rompan su independencia
