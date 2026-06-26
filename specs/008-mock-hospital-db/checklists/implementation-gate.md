# Implementation Gate Checklist: Mock de la base de datos del lado Hospital

**Purpose**: Gate de calidad de requerimientos a correr **antes** de `/speckit-implement`. Valida que los requerimientos del feature están completos, claros, consistentes, medibles y cubren los escenarios necesarios. Esto NO valida implementación — eso lo hacen los tests T019–T026b.

**Created**: 2026-06-25
**Feature**: [spec.md](../spec.md) — focos: (1) seed determinístico, (2) contrato de acceso, (3) provisionalidad/reemplazo, (4) operación local.

## Seed determinístico — completeness & measurability

- [ ] CHK001 ¿La semilla del RNG está fijada con un valor concreto y reproducible en los requerimientos? [Clarity, Spec §FR-009 / research.md R2]
- [ ] CHK002 ¿Está documentado cuál es el "ancla temporal" contra la que se calculan fechas relativas, en lugar de `SYSDATE`? [Clarity, research.md R2]
- [ ] CHK003 ¿Los conteos mínimos (≥50 pacientes, ≥150 episodios, ≥500 observaciones) están escritos como umbrales medibles y no como expresiones cualitativas? [Measurability, Spec §FR-010..FR-012]
- [ ] CHK004 ¿La spec define qué se entiende por "rango fisiológicamente plausible para la edad pediátrica" de modo que pueda decidirse objetivamente si una observación lo cumple? [Clarity, Spec §FR-012]
- [ ] CHK005 ¿Está especificada la distribución (no sólo el total) de episodios entre pacientes — sesgada vs. uniforme? [Completeness, Spec §FR-011 / research.md R4]
- [ ] CHK006 ¿El formato del identificador de paciente `PAC-NNNNNN` está fijado con longitud, prefijo y padding precisos en los requerimientos? [Clarity, Spec §FR-015]
- [ ] CHK007 ¿Los edge cases obligatorios (paciente sin episodios, paciente crónico con ≥10, observación con valor textual, turno futuro, episodio sin dx secundario) están enumerados con criterios cuantitativos y no como "algunos"? [Completeness, Spec §FR-023]
- [ ] CHK008 ¿La definición de "es-AR plausible" para nombres/apellidos está acotada con criterios objetivos (origen, exclusión de famosos/reales)? [Clarity, Spec §FR-014]
- [ ] CHK009 ¿La reproducibilidad byte-a-byte (SC-002) está expresada como criterio testeable y no como aspiración? [Measurability, Spec §SC-002]
- [ ] CHK010 ¿Se especifica qué columnas son "observables" para la verificación de reproducibilidad (vs. columnas internas que sí pueden cambiar)? [Clarity, Spec §SC-002]
- [ ] CHK011 ¿Existe un requisito explícito de que **ningún** valor del seed dependa de `SYS_GUID()`, `SYSTIMESTAMP`, secuencias auto-iniciadas u otra fuente no determinística? [Gap, research.md R2]

## Contrato de acceso restringido — coverage & consistency

- [ ] CHK012 ¿La spec define explícitamente que el contrato de acceso es read-only a nivel de motor (grants) **además** del filtro de allowlist a nivel aplicación? [Completeness, Spec §FR-005..FR-007]
- [ ] CHK013 ¿La enumeración de vistas permitidas en allowlist y la enumeración de vistas en `data-model.md` son la misma lista, sin discrepancias? [Consistency, contracts/allowlist.yaml ↔ data-model.md ↔ contracts/view-schema.md]
- [ ] CHK014 ¿La spec define el comportamiento esperado cuando se consulta una vista válida con filtros que no matchean nada (lista vacía, no error)? [Edge Case, Spec § Edge Cases]
- [ ] CHK015 ¿La spec define el comportamiento esperado cuando se consulta una tabla `HOSP_*` directamente — qué error, en qué capa? [Clarity, Spec §FR-005 / Edge Cases]
- [ ] CHK016 ¿La spec define qué columnas internas (p. ej. `ID_INTERNO`, timestamps de auditoría, soft-delete) **no** se exponen, y no sólo qué columnas **sí** se exponen? [Completeness, contracts/view-schema.md]
- [ ] CHK017 ¿Para cada YAML existente en `OracleMappings/` se garantiza por contrato que la `VW_*` correspondiente expone un superset de las columnas que el YAML lee? [Coverage, Spec §FR-008]
- [ ] CHK018 ¿La spec enumera explícitamente qué columnas son filtrables por `OracleViewClient.QueryAsync` para cada vista, distinguiéndolas de columnas sólo de salida? [Clarity, contracts/view-schema.md]
- [ ] CHK019 ¿La spec define qué pasa si una nueva vista se agrega al schema pero no a la allowlist — debe ser inalcanzable, no consultable accidentalmente? [Gap, Spec §FR-006]
- [ ] CHK020 ¿SC-008 ("tests de integración del módulo 003 pasan al 100%") está expresado como criterio binario verificable y referencia explícitamente la suite a correr? [Measurability, Spec §SC-008 / tasks T026b]
- [ ] CHK021 ¿Los usuarios Oracle (`HOSPITAL_MOCK`, `CHEQUEAR_RO`) y sus privilegios mínimos están definidos en los requerimientos, no sólo en el plan? [Completeness, Spec §FR-005..FR-007 vs research.md R7]

## Compatibilidad con `OracleMappings/` existentes

- [ ] CHK022 ¿La spec define que el contrato de columnas/tipos/nulabilidad de cada `VW_*` es la fuente de verdad y que cualquier cambio rompe el contrato salvo "agregar columnas"? [Clarity, contracts/view-schema.md]
- [ ] CHK023 ¿Está documentado el mapeo conceptual entre cada `VW_*` y el recurso FHIR que produce (Patient, Encounter, Condition, Observation)? [Completeness, contracts/view-schema.md]
- [ ] CHK024 ¿La invariante de `VW_OBSERVACION_LAB` ("VALOR_NUMERICO XOR VALOR_TEXTO no ambos NULL") está expresada en los requerimientos del contrato, no sólo como check de DB? [Consistency, contracts/view-schema.md ↔ data-model.md]
- [ ] CHK025 ¿La spec define qué sistema de codificación (CIE-10 vs. otra, LOINC vs. otra) usa cada vista, para que el mapping FHIR sepa qué `system` URI emitir? [Clarity, Spec §FR-003 / contracts/view-schema.md]

## Provisionalidad y reemplazo — completeness

- [ ] CHK026 ¿La spec marca con un mecanismo destacado (banner/título) la naturaleza provisoria del mock para que no se confunda con código de producción? [Clarity, Spec §FR-020]
- [ ] CHK027 ¿El checklist de reemplazo enumera explícitamente cada archivo a modificar (no genéricamente "los mapeos")? [Completeness, Spec §FR-021 / data-model.md sección "Reemplazo"]
- [ ] CHK028 ¿La spec define el criterio cuantitativo de "reemplazo barato" — el SC-007 establece "≤30 min" y eso es medible? [Measurability, Spec §SC-007]
- [ ] CHK029 ¿La spec define qué pasa si el hospital entrega un diseño que rompe el patrón `VW_*`/allowlist (p. ej. usa stored procedures en vez de vistas)? [Gap, Spec § Assumptions]
- [ ] CHK030 ¿La spec define qué pasa con los datos sembrados durante el reemplazo — se descartan completos, no se migran? [Clarity, Spec §FR-020..FR-022]
- [ ] CHK031 ¿Todos los artefactos del mock (compose, SQL, README) viven en una única carpeta del repo y eso está expresado como requerimiento, no como sugerencia de plan? [Consistency, Spec §FR-022 vs plan.md]
- [ ] CHK032 ¿La allowlist (`src/ChequeAR.Infrastructure/Oracle/allowlist.yaml`) está identificada como punto de cambio fuera de `infra/mock-hospital/`, y eso no contradice FR-022? [Conflict, Spec §FR-022 vs plan.md vs tasks.md T022]

## Operación local — measurability & edge cases

- [ ] CHK033 ¿La spec define "máquina de desarrollo razonable" con un baseline cuantitativo (RAM, CPU, OS) para que SC-001 (≤10 min) sea testeable? [Ambiguity, Spec §FR-019, §SC-001]
- [ ] CHK034 ¿El criterio de `healthy` del contenedor está expresado como invariante observable (conteo ≥50), no como "cuando termine el seed"? [Measurability, Spec §FR-016 / research.md R6]
- [ ] CHK035 ¿La spec define qué pasa cuando el seed falla parcialmente — el contenedor queda `unhealthy`, no en estado mixto? [Edge Case, Spec § Edge Cases / data-model.md verify]
- [ ] CHK036 ¿La spec define el comportamiento esperado en re-seed sobre base ya cargada (idempotencia o limpieza previa)? [Edge Case, Spec § Edge Cases / FR-017]
- [ ] CHK037 ¿La spec define qué pasa en plataformas ARM (Apple Silicon) — degradación aceptada vs. bloqueo? [Coverage, plan.md Complexity / research.md R8]
- [ ] CHK038 ¿El comando del quickstart está expresado como "un único comando" verificable, no como secuencia humana? [Clarity, Spec §FR-016]
- [ ] CHK039 ¿Está especificado qué puertos, variables de entorno y credenciales son requeridos para el quickstart, sin asumir valores implícitos? [Completeness, quickstart.md ↔ Spec §FR-016]
- [ ] CHK040 ¿La spec define que credenciales por defecto del compose (`devdev`) son sólo para dev local y no deben portarse a otro entorno? [Gap / Assumption, plan.md Constitution Check]

## Trazabilidad

- [ ] CHK041 ¿Cada FR de la spec está enlazada a al menos un task de `tasks.md`? (verificación cruzada del coverage map del analyze) [Traceability]
- [ ] CHK042 ¿Cada SC de la spec está enlazada a al menos un task de `tasks.md` y a un test concreto de `tests/integration/MockHospital.Tests/`? [Traceability, Spec §SC-001..SC-008]
- [ ] CHK043 ¿Las decisiones de `Clarifications/Session 2026-06-25` (motor, id format, seed, schema isolation, orquestación) están reflejadas íntegramente en spec, plan y tasks sin contradicciones? [Consistency, Spec § Clarifications]
- [ ] CHK044 ¿Existe un esquema estable de IDs (FR-, SC-, CHK-, T-) que permita referenciar requerimientos en commits y PRs futuros? [Traceability]

## Notas

- Marcar items completos como `[x]`.
- Cada item es **una unit test del requerimiento**, no del código.
- Si un item falla, la solución es **editar `spec.md` / `plan.md` / contracts** — no escribir código defensivo.
- Este checklist debe estar 100% verde antes de invocar `/speckit-implement`.
- Items marcados `[Gap]` o `[Ambiguity]` que no se puedan resolver acá deben volver a `/speckit-clarify`.
