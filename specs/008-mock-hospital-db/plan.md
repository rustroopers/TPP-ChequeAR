# Implementation Plan: Mock de la base de datos del lado Hospital (provisorio)

**Branch**: `008-mock-hospital-db` | **Date**: 2026-06-25 | **Spec**: ./spec.md

**Input**: Feature specification from `/specs/008-mock-hospital-db/spec.md`

## Summary

Andamiaje de desarrollo: una réplica provisoria del lado hospital (Garrahan) sobre **Oracle XE 21c en Docker**, con tablas internas `HOSP_*` (schema `HOSPITAL_MOCK`) y vistas `VW_*` accesibles vía un usuario read-only (`CHEQUEAR_RO`). Se siembra de forma determinística con datos pediátricos en es-AR (≥50 pacientes, ≥150 episodios, ≥500 observaciones LOINC, más diagnósticos CIE-10, signos vitales, medicación, alergias, profesionales, especialidades y turnos). Todos los artefactos viven bajo `infra/mock-hospital/` y se levantan con `docker compose up`. Compatible 1:1 con el `IOracleViewClient` y los `OracleMappings/` ya definidos por la spec 003. El módulo es **reemplazable**: cuando el hospital entregue el diseño real, sólo cambian el DDL del mock, los YAML de mapping y la allowlist.

## Technical Context

**Language/Version**: SQL (Oracle 21c PL/SQL DDL/DML); .NET Framework 4.7.1 (C#) para los tests de integración del cliente (xUnit).

**Primary Dependencies**: `gvenzl/oracle-xe:21-slim-faststart` (imagen Docker), `Oracle.ManagedDataAccess` 21.13.0 (ya en `ChequeAR.Infrastructure`), Docker Compose v2.

**Storage**: Oracle XE 21c, schema dedicado `HOSPITAL_MOCK`. Volumen Docker `mock-hospital-data` para persistencia entre reinicios; reset = `docker compose down -v`.

**Testing**: xUnit (suite de integración nueva `tests/integration/MockHospital.Tests/`), invocando `OracleViewClient` real contra el contenedor.

**Target Platform**: Cualquier estación de desarrollo con Docker Engine ≥ 20.10 (Linux/Windows/macOS). El contenedor Oracle corre amd64; en hosts ARM (Apple Silicon) se levanta con emulación QEMU (más lento pero funcional).

**Project Type**: Infraestructura de desarrollo (scaffolding). No es un proyecto de aplicación; no se compila, no se despliega.

**Performance Goals**: Quickstart (`docker compose up` desde frío hasta `healthy`) < 10 min en máquina de desarrollo razonable (SC-001). Consultas a `VW_*` con filtros indexados < 100 ms p95 sobre el volumen sembrado.

**Constraints**:
- Read-only: el usuario `CHEQUEAR_RO` no recibe privilegios de escritura ni DDL.
- Determinismo: misma semilla → mismo dataset byte-a-byte en columnas observables (SC-002).
- Paridad de contrato: no se permiten cambios al `IOracleViewClient` ni a los YAML de mapping existentes que rompan la compatibilidad con el hospital real (SC-008).
- Datos sintéticos por construcción; ninguna fila puede derivarse de un paciente real (Ley 25.326).

**Scale/Scope**: ≥50 pacientes, ≥150 episodios, ≥500 observaciones de laboratorio, más diagnósticos/signos/medicación/alergias proporcionales. Sin requerimientos de escala horizontal — un solo contenedor por dev.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`/.specify/memory/constitution.md` está en su forma de plantilla (sin principios ratificados). En ausencia de constitución del proyecto, se aplican las restricciones documentadas en `Equipo201_Anteproyecto_ChequeAR.md` y en la spec 003:

- ✅ **Read-only sobre el lado hospital**: `CHEQUEAR_RO` recibe únicamente grants `SELECT` sobre `VW_*` (cumple FR-007, paralelo a la regla de la spec 003 sobre vistas autorizadas).
- ✅ **Datos sintéticos**: no se importan datos reales del Garrahan; el seed es generado a partir de catálogos públicos (CIE-10, LOINC) y nombres argentinos comunes. Cumple Ley 25.326.
- ✅ **No introduce dependencias nuevas en la app**: el mock vive en `infra/`, no en `src/`. La aplicación sigue usando `Oracle.ManagedDataAccess` ya declarado en NuGet.
- ✅ **Provisionalidad explícita**: la carpeta `infra/mock-hospital/README.md` incluye el "checklist de reemplazo" (FR-021).
- ✅ **Sin secretos en repo**: credenciales del usuario `CHEQUEAR_RO` se setean por variables de entorno; el `.env.example` documenta defaults inocuos (`CHEQUEAR_RO_PASSWORD=devdev`) sólo aptos para desarrollo local.

**Re-evaluación post-Fase 1**: ver al final de este documento.

## Project Structure

### Documentation (this feature)

```text
specs/008-mock-hospital-db/
├── plan.md              # Este archivo
├── research.md          # Fase 0
├── data-model.md        # Fase 1 (DDL conceptual de HOSP_* y VW_*)
├── quickstart.md        # Fase 1 (un comando para levantar el mock)
├── contracts/
│   ├── view-schema.md   # Columnas y tipos garantizados por cada VW_*
│   └── allowlist.yaml   # Allowlist propuesta para OracleViewClient
└── tasks.md             # Fase 2 (creada por /speckit-tasks)
```

### Source Code (repository root)

```text
infra/
└── mock-hospital/
    ├── docker-compose.yml
    ├── README.md                       # incluye checklist de reemplazo (FR-021)
    ├── init/
    │   ├── 01-schema.sql               # CREATE SCHEMA + tablas HOSP_*
    │   ├── 02-views.sql                # vistas VW_* + grants
    │   ├── 03-seed.sql                 # seed determinístico (~50/150/500)
    │   ├── 04-verify.sql               # asserts post-seed (SC-006)
    │   └── 99-grants.sql               # grants finales a CHEQUEAR_RO
    └── .env.example                    # variables de entorno documentadas

src/
└── ChequeAR.Infrastructure/
    └── Oracle/
        └── allowlist.yaml              # lista blanca leída por OracleViewClient

tests/
└── integration/
    └── MockHospital.Tests/             # ejercicios del OracleViewClient real
        ├── ViewContractTests.cs        # FR-005, FR-006 (allowlist + grants)
        ├── SeedInvariantsTests.cs      # SC-006 (conteos y edge cases)
        ├── DeterminismTests.cs         # SC-002 (reproducibilidad)
        └── MappingCompatibilityTests.cs# SC-004 (YAML mapping consume filas)
```

**Structure Decision**: Todo lo nuevo del mock se concentra en `infra/mock-hospital/` (un único contenedor + scripts SQL), más una `allowlist.yaml` consumida por el `OracleViewClient` ya existente, más una nueva suite de tests de integración. No se tocan controllers, services ni el DbContext de ChequeAR. Esto cumple FR-022 (reemplazo localizado) y SC-007 (checklist de reemplazo).

## Fases

| Fase | Entregable | Sprint |
|---|---|---|
| 0 | Research: imagen Oracle XE, generación determinística, validación de mapping | S2 |
| 1 | DDL de `HOSP_*`, vistas `VW_*`, allowlist YAML, quickstart | S2 |
| 2 | Seed determinístico + script de verificación + grants | S2-S3 |
| 3 | Tests de integración (`MockHospital.Tests`) | S3 |
| 4 | README con checklist de reemplazo + healthcheck del compose | S3 |

## Complexity Tracking

| Decisión | Por qué se acepta | Alternativa rechazada |
|---|---|---|
| Oracle XE en lugar de SQL Server local | Paridad 1:1 con driver/dialecto del Garrahan (decisión Q1) | SQL Server: divergencia oculta del `OracleViewClient` |
| Schema y usuario separados (`HOSPITAL_MOCK`, `CHEQUEAR_RO`) | Segunda línea de defensa para la regla "sólo `VW_*`" (decisión Q4) | Mismo schema con confianza ciega en la allowlist: una regresión en la app rompería la regla sin que el motor la detecte |
| Seed en SQL puro vía entrypoint del contenedor | Determinismo trivial (`DBMS_RANDOM.SEED`), sin runtime .NET en el camino (decisión Q3) | Seeder en consola .NET: agrega un proyecto extra para un dataset estático |
| Carpeta `infra/` separada de `src/` | El mock no es código de aplicación; mezclarlo confunde el reemplazo futuro | Bajo `src/`: hace difícil ver qué borrar cuando llegue el diseño real |

## Re-evaluación post-Fase 1

Tras producir `data-model.md`, `contracts/view-schema.md` y `contracts/allowlist.yaml`:

- ✅ Las columnas expuestas por cada `VW_*` son un superset de las requeridas por los YAML existentes de `OracleMappings/` (verificado contra `patient.yaml`, `condition.yaml`, `observation.yaml` del spec 003).
- ✅ Ninguna `VW_*` expone columnas internas de las `HOSP_*` que el hospital real difícilmente expondría (ids autoincrementales internos, flags de borrado lógico, timestamps de auditoría).
- ✅ El grant final a `CHEQUEAR_RO` se limita a `SELECT` sobre la enumeración explícita de vistas; el script `99-grants.sql` no usa `GRANT ALL` ni roles laxos.
- ⚠️ Riesgo aceptado: el contenedor Oracle XE corre en amd64; devs en Apple Silicon van a sufrir lentitud por emulación. Mitigación: documentar en README, sugerir asignar ≥4 GB de RAM a Docker Desktop. No bloquea ningún SC.

## Notas de workflow

- Script `update-agent-context.sh` no está disponible en este repo; el archivo de contexto del agente (CLAUDE.md) se actualiza manualmente al cierre del módulo si corresponde.
- Constitución del proyecto está en estado plantilla; cuando el equipo la ratifique, este plan deberá re-validarse contra los principios definitivos.
