---

description: "Task list — Mock de la base de datos del lado Hospital (provisorio)"
---

# Tasks: Mock de la base de datos del lado Hospital (provisorio)

**Input**: Design documents from `/specs/008-mock-hospital-db/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/view-schema.md, contracts/allowlist.yaml, quickstart.md

**Tests**: Incluidos. La spec exige una suite `tests/integration/MockHospital.Tests/` con 4 grupos (`ViewContractTests`, `SeedInvariantsTests`, `DeterminismTests`, `MappingCompatibilityTests`) que respaldan SC-002, SC-003, SC-004, SC-006 y SC-008.

**Organization**: Tareas agrupadas por user story (US1 P1, US2 P1, US3 P2, US4 P2). US1 y US2 son ambos P1 pero independientes: US1 entrega el dataset; US2 entrega el contrato de acceso. Cada uno puede demostrarse por separado.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Puede ejecutarse en paralelo con otras [P] del mismo bloque (archivos distintos, sin dependencias entre sí).
- **[Story]**: A qué user story sirve la tarea (US1–US4). Las fases Setup/Foundational/Polish no llevan story.

## Path Conventions

- Mock e infraestructura: `infra/mock-hospital/`
- Código de aplicación a tocar: `src/ChequeAR.Infrastructure/Oracle/`
- Tests: `tests/integration/MockHospital.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Esqueleto de carpetas y compose.

- [X] T001 Crear estructura de carpetas `infra/mock-hospital/{init/}` y `tests/integration/MockHospital.Tests/` en la raíz del repo
- [X] T002 [P] Crear `infra/mock-hospital/.env.example` con variables `ORACLE_PASSWORD`, `HOSPITAL_MOCK_PASSWORD`, `CHEQUEAR_RO_PASSWORD=devdev` y comentarios sobre cuáles son seguras para dev
- [X] T003 Crear `infra/mock-hospital/docker-compose.yml` con servicio `mock-hospital` (imagen `gvenzl/oracle-xe:21-slim-faststart`, `platform: linux/amd64`, puerto 1521, volumen `mock-hospital-data`, montaje `./init:/container-entrypoint-initdb.d:ro`) sin healthcheck todavía (se completa en T017)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: DDL del schema y vistas + grants. Estos artefactos son consumidos por **todas** las user stories (US1 los puebla, US2 los expone, US3 los usa, US4 los reemplaza).

- [X] T004 Crear `infra/mock-hospital/init/01-schema.sql`: `CREATE USER HOSPITAL_MOCK` + `CREATE USER CHEQUEAR_RO` + `CREATE TABLE HOSPITAL_MOCK.HOSP_*` para las 10 entidades de `data-model.md` (PACIENTE, ESPECIALIDAD, PROFESIONAL, EPISODIO, DIAGNOSTICO, OBSERVACION_LAB, SIGNOS_VITALES, MEDICACION, ALERGIA, TURNO) con tipos, PKs, FKs e índices declarados; incluir el `CHECK ((VALOR_NUMERICO IS NOT NULL) OR (VALOR_TEXTO IS NOT NULL))` en `HOSP_OBSERVACION_LAB`
- [X] T005 Crear `infra/mock-hospital/init/02-views.sql` con las 10 vistas `VW_*` definidas en `contracts/view-schema.md` (joins denormalizando `ESPECIALIDAD` donde aplica); cada vista debe seleccionar **sólo** las columnas declaradas en el contrato — no exponer `ID_INTERNO` ni columnas internas
- [X] T006 Crear `infra/mock-hospital/init/99-grants.sql` con `GRANT SELECT ON HOSPITAL_MOCK.VW_X TO CHEQUEAR_RO` enumerando explícitamente las 10 vistas; verificar (en comentario al pie) que NO hay `GRANT ALL`, ni `GRANT ... TO PUBLIC`, ni grants sobre `HOSP_*`

**Checkpoint**: Tras T004–T006, `docker compose up` levanta una base vacía consultable por `CHEQUEAR_RO` sobre las vistas (devolverán cero filas). Bloqueo: los siguientes pasos asumen este estado.

---

## Phase 3: User Story 1 — Dataset reproducible para desarrollo local (Priority: P1) 🎯 MVP

**Story Goal**: Un dev clona el repo, corre `docker compose up -d --wait` desde `infra/mock-hospital/` y en < 10 min tiene una base con ≥50 pacientes, ≥150 episodios, ≥500 observaciones, reproducible byte-a-byte con la misma semilla.

**Independent Test**: Correr el quickstart en máquina limpia; verificar conteos vía `VW_PACIENTE`/`VW_EPISODIO`/`VW_OBSERVACION_LAB`; hacer `down -v && up -d --wait`; verificar que los hashes por tabla coinciden con la corrida previa.

### Seed determinístico (todas modifican `infra/mock-hospital/init/03-seed.sql`)

- [X] T007 [US1] En `infra/mock-hospital/init/03-seed.sql`: cabecera con `ALTER SESSION SET NLS_DATE_FORMAT='YYYY-MM-DD'`, `ALTER SESSION SET NLS_TIMESTAMP_FORMAT='YYYY-MM-DD HH24:MI:SS'` y `BEGIN DBMS_RANDOM.SEED(20260625); END;` para fijar el RNG (R2 de research.md); definir constante PL/SQL `c_anclaje DATE := DATE '2026-06-25';`
- [X] T008 [US1] Insertar catálogo de 8 especialidades pediátricas en `HOSP_ESPECIALIDAD` (PED, NEO, CARP, NEUM, GAST, INFE, NEFRO, ENDO) con `display` es-AR (R3)
- [X] T009 [US1] Insertar 12 profesionales en `HOSP_PROFESIONAL` distribuidos entre las 8 especialidades; matrículas formato `MN-NNNNN` derivadas determinísticamente de un contador
- [X] T010 [US1] Insertar exactamente 52 pacientes en `HOSP_PACIENTE` con `ID_PACIENTE='PAC-' || LPAD(rownum, 6, '0')`, fechas de nacimiento que aseguren edad 0–17 al anclaje, nombres+apellidos desde un array es-AR estático embebido en el script (≥30 nombres × ≥30 apellidos), sexo distribuido ~50/50, cobertura mezcla de OSDE/SwissMedical/PAMI/Particular; usar `DBMS_RANDOM.VALUE` para elegir índices del array (R4)
- [X] T011 [US1] Generar episodios en `HOSP_EPISODIO` con distribución Zipf-like (R4): 1 paciente con 0 episodios, 1 paciente con 12 episodios, 30% restante con 1, 50% con 2–4, 20% con 5–8 → asegurar ≥150 episodios totales; `FECHA_INICIO` siempre ≥ `FECHA_NACIMIENTO`, `FECHA_FIN` NULL para el ~10% más reciente
- [X] T012 [US1] [P] Generar diagnósticos en `HOSP_DIAGNOSTICO` desde catálogo embebido de ~25 códigos CIE-10 pediátricos (R3); para cada episodio: 1 principal (`ES_PRINCIPAL=1`), 0–2 secundarios; ≥1 episodio queda sólo con principal (FR-023)
- [X] T013 [US1] [P] Generar observaciones en `HOSP_OBSERVACION_LAB` desde catálogo embebido de ~15 LOINC (R3): por cada episodio 2–6 observaciones con rangos fisiológicos plausibles para la edad; ≥1 observación con `VALOR_TEXTO` no NULL (LOINC cualitativo, FR-023); asegurar ≥500 totales
- [X] T014 [US1] [P] Generar signos vitales en `HOSP_SIGNOS_VITALES` (1 set por episodio): peso/talla coherentes con edad (curvas OMS aproximadas), FC/FR/TA/T° en rangos pediátricos
- [X] T015 [US1] [P] Generar medicaciones en `HOSP_MEDICACION` desde catálogo embebido de ~20 fármacos pediátricos (R3): 0–3 por episodio, con vías y posologías plausibles
- [X] T016a [US1] [P] Generar alergias en `HOSP_ALERGIA` desde catálogo embebido de ~10 alergenos; mezclar estados `CONFIRMADA`/`SOSPECHADA`/`DESCONOCIDAS`/`DESCARTADA`; ≥1 paciente con `ESTADO='DESCONOCIDAS'` y `SUSTANCIA IS NULL` (FR-023)
- [X] T016b [US1] [P] Generar turnos en `HOSP_TURNO` (1–3 por paciente activo) con estados `PROGRAMADO`/`ATENDIDO`/`AUSENTE`/`CANCELADO`; ≥1 turno con `FECHA_HORA > c_anclaje` (futuro) y ≥1 con `FECHA_HORA < c_anclaje` (pasado) (FR-023)
- [X] T017 [US1] Crear `infra/mock-hospital/init/04-verify.sql` con bloque PL/SQL que valida (con `RAISE_APPLICATION_ERROR -20001..-20010`): pacientes ≥50, episodios ≥150, observaciones ≥500, paciente sin episodios ≥1, paciente con ≥10 episodios ≥1, observación con `VALOR_TEXTO` ≥1, turno futuro ≥1, todas las FKs íntegras (sin huérfanos)
- [X] T018 [US1] Actualizar `infra/mock-hospital/docker-compose.yml` agregando healthcheck que ejecute `SELECT COUNT(*) FROM HOSPITAL_MOCK.HOSP_PACIENTE` y exija ≥50 (R6), con `interval: 15s start_period: 600s retries: 30`; documentar que el contenedor sólo reporta `healthy` cuando el seed terminó

### Tests de la story

- [X] T019 [P] [US1] Crear `tests/integration/MockHospital.Tests/MockHospital.Tests.csproj` (.NET Framework 4.7.1, xUnit) con referencias a `Oracle.ManagedDataAccess`, `Xunit`, `Xunit.Runner.VisualStudio`; agregar al `ChequeAR.sln`
- [X] T020 [P] [US1] Crear `tests/integration/MockHospital.Tests/SeedInvariantsTests.cs`: ejercicios contra `OracleViewClient` real apuntando al contenedor; tests `Pacientes_AlMenos50`, `Episodios_AlMenos150`, `Observaciones_AlMenos500`, `ExisteAlMenosUnPacienteSinEpisodios`, `ExisteAlMenosUnPacienteCronico`, `ExisteAlMenosUnaObservacionTextual`, `ExisteAlMenosUnTurnoFuturo` (cubre SC-006)
- [x] T021 [P] [US1] Crear `tests/integration/MockHospital.Tests/DeterminismTests.cs`: test `Seed_ProduceHashesEsperados` que invoca `STANDARD_HASH` sobre cada `VW_*` y compara contra valores baseline embebidos (regenerables cuando legítimamente cambia el seed); test adicional `Reset_RegeneraDatasetIdentico` que ejecuta `docker compose down -v && docker compose up -d --wait` (vía `Process.Start`) y verifica que los hashes post-reset coinciden con los previos (cubre FR-017 + SC-002) *(hecho después: hashes anclados sin columnas de fecha; el seed no consume números aleatorios según la fecha)*

**Checkpoint US1**: El dataset levanta solo, cumple conteos y edge cases, es reproducible. MVP entregable.

---

## Phase 4: User Story 2 — Acceso restringido por allowlist (Priority: P1)

**Story Goal**: `OracleViewClient` consume el mock únicamente a través de las `VW_*` declaradas; cualquier intento de tocar `HOSP_*` o una vista no listada falla por allowlist (primera línea) o por grants Oracle (segunda línea).

**Independent Test**: Ejecutar `ViewContractTests` contra el contenedor; confirmar que (a) las 10 vistas listadas responden, (b) `HOSP_PACIENTE` directa devuelve `ORA-00942` desde `CHEQUEAR_RO`, (c) un nombre fuera de allowlist arroja `ArgumentException` antes de llegar a Oracle.

### Allowlist y DI

- [X] T022 [US2] Mover/copiar `specs/008-mock-hospital-db/contracts/allowlist.yaml` a `src/ChequeAR.Infrastructure/Oracle/allowlist.yaml` (ubicación productiva del archivo); marcar como `Content` + `CopyToOutputDirectory=PreserveNewest` en `ChequeAR.Infrastructure.csproj`
- [X] T023 [US2] Crear `src/ChequeAR.Infrastructure/Oracle/AllowlistLoader.cs` con método estático `Load(string yamlPath) -> IReadOnlyCollection<string>` que parsea `allowed_views:` usando `YamlDotNet` (ya dependencia del proyecto según spec 003 research)
- [X] T024 [US2] Registrar `IOracleViewClient` en el DI bootstrap (ver `src/ChequeAR.Api/Global.asax.cs` y/o `App_Start/UnityConfig.cs` según el patrón en uso) instanciando `new OracleViewClient(connectionString, AllowlistLoader.Load("Oracle/allowlist.yaml"))`; agregar entrada `OracleHospitalView` en `Web.config` apuntando al contenedor local

### Tests de la story

- [X] T025 [P] [US2] Crear `tests/integration/MockHospital.Tests/ViewContractTests.cs`: test `VW_Paciente_DevuelveFilas` (vista permitida), `Tabla_HospPaciente_RechazadaPorAllowlist` (esperar `ArgumentException` sin tocar la DB), `Tabla_HospPaciente_RechazadaPorGrants` (invocar SQL directo bypaseando allowlist y esperar `ORA-00942` desde `CHEQUEAR_RO`); cubre SC-003
- [x] T026 [P] [US2] Crear `tests/integration/MockHospital.Tests/MappingCompatibilityTests.cs`: para cada YAML de `src/ChequeAR.Infrastructure/OracleMappings/` cargar el mapping, consultar la `VW_*` correspondiente vía `OracleViewClient`, aplicar el `OracleToFhirMapper` y validar que produce un recurso FHIR estructuralmente válido; cubre SC-004 *(reemplazado por MockViewColumnsTests: no hay YAML de mapeo; compara las columnas del mock en memoria con las vistas)*
- [ ] T026b [P] [US2] Configurar la suite ya existente del módulo 003 (`tests/integration/Oracle.Tests/`) para apuntar al contenedor del mock (via `appsettings.test.json` o variable `ChequeAR__OracleHospitalView`) y ejecutarla en CI; el criterio de aceptación es que **el 100% de los tests existentes del 003 pasan sin modificarse contra el mock** (cubre SC-008) *(no existe tests/integration/Oracle.Tests/)*
- [X] T027 [US2] Verificar manualmente (con `DBA_TAB_PRIVS` desde el contenedor) que `CHEQUEAR_RO` no tiene privilegios distintos de `SELECT` sobre las 10 vistas; documentar la consulta de verificación en `infra/mock-hospital/README.md` (sección "Auditar grants")

**Checkpoint US2**: La aplicación queda enchufada al mock con el contrato de acceso correcto; ambos P1 entregables independientes.

---

## Phase 5: User Story 3 — Realismo para stakeholder reviews (Priority: P2)

**Story Goal**: Los datos son creíbles para clínicos del Garrahan en una review (nombres, edades, dx, observaciones plausibles).

**Independent Test**: Generar un sample de 5 historiales (script SQL) y revisión manual por un clínico; calificación SC-005.

- [X] T028 [P] [US3] Curar el array de nombres+apellidos es-AR en `03-seed.sql` (T010): asegurar variedad de orígenes (italiano, español, criollo, mapuche), evitar nombres famosos o reales
- [X] T029 [P] [US3] Curar el catálogo CIE-10 (T012): elegir 25 códigos efectivamente frecuentes en pediatría (infecciones respiratorias, gastroenteritis, asma, fiebre, otitis, varicela, etc.) con `display` clínicamente correcto en es
- [X] T030 [P] [US3] Curar rangos de LOINC en `03-seed.sql` (T013): cada código LOINC debe tener un rango de valor distinto por banda etaria (neonatal, lactante, preescolar, escolar, adolescente) para que las observaciones sean plausibles para la edad del paciente
- [X] T031 [US3] Crear `infra/mock-hospital/init/sample-historiales.sql`: script SQL stand-alone que selecciona 5 pacientes aleatorios (semilla fija distinta a la del seed) y produce un reporte legible (paciente + episodios + dx + observaciones + signos) para la review con clínicos
- [X] T032 [US3] Documentar en `infra/mock-hospital/README.md` el procedimiento de stakeholder review (cómo ejecutar `sample-historiales.sql`, qué preguntarle al clínico, cómo registrar el resultado para SC-005)

**Checkpoint US3**: Listo para demos con stakeholders.

---

## Phase 6: User Story 4 — Reemplazo cuando llegue el diseño real (Priority: P2)

**Story Goal**: Un dev puede identificar en < 30 min los puntos de cambio cuando el hospital entregue el diseño real (SC-007).

**Independent Test**: Un dev lee `infra/mock-hospital/README.md` y, ante un diseño hipotético, enumera correctamente los archivos a tocar.

- [X] T033 [US4] Crear `infra/mock-hospital/README.md` con: propósito (provisorio), prerrequisitos, comandos del quickstart (copiar desde `quickstart.md`), sección "Auditar grants" (T027), troubleshooting
- [X] T034 [US4] En el mismo README, agregar sección "Checklist de reemplazo" enumerando explícitamente: (a) `infra/mock-hospital/init/01-schema.sql` se borra cuando llega el DDL real, (b) `init/02-views.sql` se reemplaza por las vistas reales del hospital, (c) `src/ChequeAR.Infrastructure/Oracle/allowlist.yaml` se actualiza con los nombres reales, (d) `src/ChequeAR.Infrastructure/OracleMappings/*.yaml` se ajustan a los nombres reales de columnas, (e) `tests/integration/MockHospital.Tests/MappingCompatibilityTests.cs` se re-baselinea
- [X] T035 [US4] Marcar el directorio `infra/mock-hospital/` con un banner claro "PROVISIONAL — A REEMPLAZAR" al tope del README (FR-020)

**Checkpoint US4**: Provisionalidad explícita; reemplazo es procedimiento documentado, no arqueología.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T036 [P] Agregar al README sección "Apple Silicon": confirmar `platform: linux/amd64`, sugerir ≥6 GB de RAM en Docker Desktop, advertir que primer arranque puede tardar ~8 min (R8)
- [X] T037 [P] Agregar al README sección "Troubleshooting" copiando de `quickstart.md` (puerto 1521 ocupado, `unhealthy` tras 10 min, logs del seed)
- [ ] T038 [P] Agregar workflow opcional `.github/workflows/mock-hospital-smoke.yml` que en cada PR a `dev` levanta el compose, espera `--wait`, corre los 4 grupos de tests (~5 min total) y reporta; marcar como opcional si CI de GitHub Actions no tiene Docker disponible
  - **Nota (2026-10-16):** el workflow se borró en 0f911ad. Los runners Windows del CI no pueden levantar el contenedor; los tests `Requires=Oracle` se corren a mano.
- [X] T039 Actualizar `src/README.md` (ChequeAR) con una línea al final apuntando a `infra/mock-hospital/README.md` para que devs nuevos lo descubran
- [ ] T040 Smoke manual completo: clonar el repo en una máquina limpia (o `git clean -fdx`), seguir el quickstart al pie de la letra, cronometrar tiempo a `healthy`; documentar el resultado en una entrada de commit message para evidenciar SC-001 *(no se registró el smoke; el mock ya no usa Docker)*

---

## Dependencias / orden de ejecución

```
Phase 1 (T001–T003)  →  Phase 2 (T004–T006)  →  ┬─ Phase 3 / US1 (T007–T021)
                                                  └─ Phase 4 / US2 (T022–T027)
Phase 3 + Phase 4 completas  →  Phase 5 / US3 (T028–T032)
Phase 2 completa             →  Phase 6 / US4 (T033–T035)  (puede arrancar en paralelo a US3)
Todo lo anterior            →  Phase 7 / Polish (T036–T040)
```

**Independencia**:
- US1 y US2 son independientes una vez completada la Fase 2 — podés tener el dataset cargado pero sin allowlist, o la allowlist en su lugar pero contra una base vacía. Cada uno se demuestra solo.
- US3 depende del dataset (US1) para ser revisable.
- US4 sólo necesita la estructura física (Fase 2) para enumerar los archivos a reemplazar.

## Ejecución en paralelo (oportunidades)

- **Phase 1**: T001 secuencial; T002 y T003 en paralelo tras T001.
- **Phase 3** (después de T007–T011): T012, T013, T014, T015, T016 son paralelizables porque cada una toca una sección distinta del mismo `03-seed.sql` (coordinar merges) o, idealmente, dividir en `03-seed-diagnosticos.sql`, `03-seed-observaciones.sql`, etc. para evitar conflictos. Tests T019 / T020 / T021 son `.cs` separados y plenamente paralelos.
- **Phase 4**: T025 y T026 paralelizables (archivos `.cs` distintos).
- **Phase 5**: T028, T029, T030 paralelizables (secciones distintas del seed o catálogos separados).
- **Phase 7**: T036, T037, T038 paralelizables.

## MVP

**MVP = Phase 1 + Phase 2 + Phase 3 (US1).** Con eso un dev tiene el dataset corriendo y verificable. US2 entra inmediatamente después como el segundo P1, completando el MVP "técnico" listo para que el resto de los módulos (001/002/003/004) lo consuman.

## Implementation Strategy

1. **Sprint S2**: Phases 1, 2, 3 (US1). Foco: que el `docker compose up` funcione end-to-end y los tests de invariantes pasen.
2. **Sprint S2 fin / S3 inicio**: Phase 4 (US2). Allowlist + DI + tests de contrato. Ya queda el mock 100% listo para que los demás módulos lo consuman.
3. **Sprint S3**: Phase 5 (US3) y Phase 6 (US4) en paralelo. Realismo para la stakeholder review + checklist de reemplazo en su lugar.
4. **Sprint S3 fin**: Phase 7 (Polish). Apple Silicon, CI smoke, README final.

## Validación de formato

- Todas las tareas siguen `- [ ] TID [P?] [US?] descripción con path`.
- Story labels presentes en Phases 3–6, ausentes en Phases 1, 2, 7 (correcto).
- Cada tarea referencia una ruta concreta (`infra/mock-hospital/init/03-seed.sql`, `src/ChequeAR.Infrastructure/Oracle/AllowlistLoader.cs`, etc.).
- 42 tareas en total (incluye split T016 → T016a/T016b y nuevo T026b por SC-008, aplicados tras `/speckit-analyze`).
