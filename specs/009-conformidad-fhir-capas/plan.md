# Implementation Plan: Conformidad FHIR real y saneamiento de capas

**Branch**: `009-conformidad-fhir-capas` | **Date**: 2026-08-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-conformidad-fhir-capas/spec.md`

## Summary

Convertir la interfaz `/fhir/r5/*` de una fachada con nombres FHIR a una API FHIR R5 real, y
corregir tres desviaciones de la arquitectura por capas. El eje técnico es **reemplazar la
construcción manual de JSON por el SDK oficial de Firely** (`Hl7.Fhir.R5` 5.13.4, la última
línea compatible con .NET Framework 4.7.1), lo que trae de arrastre: recursos tipados,
`Bundle` conformes, `OperationOutcome`, un formateador de medios que emite
`application/fhir+json` por omisión, y validación real contra perfiles que **hay que escribir
porque hoy no existen**. En paralelo, se saca ODP.NET de la capa de negocio, se elimina el
acceso directo de controllers a repositorios y se mueven las reglas de negocio que viven en la
capa web a servicios testeables.

El punto de mayor riesgo no es FHIR sino **la pérdida silenciosa de datos** al pasar la
interfaz de usuario a hablar FHIR puro (FR-005): se mitiga con extensiones explícitas y pruebas
de ida y vuelta escritas **antes** de tocar la capa de presentación.

## Technical Context

**Language/Version**: C# sobre **.NET Framework 4.7.1** (`net471`). `ChequeAR.Api` es proyecto
web clásico (no SDK-style) con `RestoreProjectStyle=PackageReference`; el resto son SDK-style.

**Primary Dependencies**: ASP.NET Web API 2 (5.2.9), ASP.NET MVC 5 (5.2.9), OWIN 4.2.2,
Entity Framework 6.5.1, `Oracle.ManagedDataAccess` 21.17.0, Newtonsoft.Json 13.0.3,
Vue 3 vía islas en `Scripts/islands/`. **Se agrega**: `Hl7.Fhir.R5` 5.13.4 (pinneada),
`Hl7.Fhir.Specification.R5` + `Hl7.Fhir.Specification.Data.R5` 5.13.4.

**Storage**: Oracle (XE en desarrollo, 12c+ en producción) vía EF6 + `Oracle.ManagedDataAccess`.
Mapeo por Fluent API en `Infrastructure/EF/Configurations` — el dominio son POCOs sin atributos
de persistencia.

**Testing**: xUnit 2.7.0 + Moq 4.20.70 en `ChequeAR.Tests`, que referencia **sólo Domain y
Application**. Esa restricción es deliberada y **no se relaja** en esta feature: es la evidencia
del aislamiento entre capas (FR-021).

**Target Platform**: IIS sobre Windows Server (infraestructura existente del hospital);
IIS Express en `https://localhost:44301` para desarrollo local.

**Project Type**: Aplicación web servidor-renderizada (Razor + islas Vue) que además expone una
API de interoperabilidad. Solución de 5 proyectos en capas.

**Performance Goals**: Sin objetivos nuevos. Restricción heredada: el tope de filas exportables
en línea del módulo de reportes se conserva. La validación FHIR agrega costo sólo en publicar y
finalizar (operaciones no interactivas de alta frecuencia), mitigado con resolución cacheada.

**Constraints**:
- **Dominante**: `Hl7.Fhir.R5` 6.x no soporta `net471`. Obliga a pinnear 5.13.4 y a subir los
  binding redirects de `System.Text.Json` (hoy `6.0.0.10`) a la versión que exige el SDK.
- Las 11 pruebas del constructor de consultas de reportes deben seguir compilando y pasando
  desde un proyecto que no ve la infraestructura (condiciona el diseño de R4).
- Las versiones de cuestionario ya publicadas son inmutables: no se migran ni se revalidan.

**Scale/Scope**: 5 proyectos, ~142 archivos `.cs`, 21 controllers (12 API + 9 MVC), 4 islas Vue,
8 clases de prueba existentes. Alcance de esta feature: ~7 archivos nuevos en la capa de
negocio, ~6 servicios nuevos, 2 perfiles de conformidad, 1 formateador de medios, 4 puntos de
llamada en JavaScript.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> ⚠️ **`.specify/memory/constitution.md` no está ratificada**: sigue siendo la plantilla con
> marcadores (`[PRINCIPLE_1_NAME]`, `[SECTION_2_NAME]`…). No hay principios contra los cuales
> evaluar formalmente. Se recomienda ejecutar `/speckit-constitution` para que esta compuerta
> tenga valor en features futuras.
>
> Para no dejar la compuerta vacía, se evalúa contra los **principios de facto** observables en
> el código y en las specs 001-008.

| Principio de facto | Evidencia en el código | ¿Esta feature lo respeta? |
|---|---|---|
| Dependencias en una sola dirección, con inversión hacia la infraestructura | Grafo acíclico `Domain ← Application ← Infrastructure ← Api`; los repositorios implementan interfaces declaradas en `Application/Interfaces` | ✅ **Lo refuerza**: US6 y US7 existen precisamente para cerrar las tres desviaciones |
| El dominio es POCO puro, sin dependencias | `ChequeAR.Domain.csproj` no declara ningún paquete más allá de las reference assemblies | ✅ No se toca el dominio |
| Las pruebas no ven infraestructura ni presentación | `ChequeAR.Tests` referencia sólo Domain y Application | ✅ **Se preserva explícitamente** (FR-021); condiciona el diseño de R4 |
| Nunca se inventan datos ante fallo de una integración | Modo degradado de `PatientService`; el tablero se vacía en lugar de rellenarse | ✅ FR-016 lo exige textualmente |
| Los datos clínicos publicados son inmutables | Versiones congeladas referenciadas por fichas | ✅ FR-014 y el supuesto de no migración |
| Interoperabilidad sobre HL7 FHIR R5 | `fhirVersion: 5.0.0` declarado | ✅ Es el objetivo de la feature |

**Resultado de la compuerta**: **PASA**. Ninguna violación que justificar; la tabla de
Complexity Tracking queda vacía a propósito.

**Re-evaluación post-diseño (Phase 1)**: **PASA**. El diseño no introduce dependencias nuevas
hacia arriba ni relaja la barrera de pruebas. La única dependencia nueva de la capa de negocio
es el SDK FHIR, que es una biblioteca de modelo de dominio clínico —no de infraestructura— y
por lo tanto pertenece a esa capa. La capa de infraestructura suma el paquete de validación, y
la de presentación el de serialización: cada una en su nivel.

## Project Structure

### Documentation (this feature)

```text
specs/009-conformidad-fhir-capas/
├── plan.md              # Este archivo
├── research.md          # Phase 0 — decisiones técnicas (R1-R7)
├── data-model.md        # Phase 1 — entidades y su mapeo al estándar
├── quickstart.md        # Phase 1 — cómo verificar la feature end-to-end
├── contracts/
│   └── fhir-api.md      # Phase 1 — contrato de la interfaz de interoperabilidad
├── checklists/
│   └── requirements.md  # Checklist de calidad de la spec
└── tasks.md             # Phase 2 — generado por /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── ChequeAR.Domain/                      # Sin cambios
│   ├── Entities/
│   └── Enums/
│
├── ChequeAR.Application/                 # + Hl7.Fhir.R5   − Oracle.ManagedDataAccess
│   ├── Fhir/
│   │   ├── FhirQuestionnaireBuilder.cs           # reescribir sobre POCOs
│   │   ├── FhirQuestionnaireResponseBuilder.cs   # reescribir sobre POCOs
│   │   ├── FhirQuestionnaireReader.cs            # reescribir; contrato de salida intacto
│   │   ├── FhirQuestionnaireParser.cs            # NUEVO — inverso del builder
│   │   ├── FhirQuestionnaireResponseParser.cs    # NUEVO
│   │   ├── ChequeARExtensions.cs                 # NUEVO — las dos extensiones propias
│   │   ├── FhirBundles.cs                        # NUEVO — searchset conforme
│   │   └── OperationOutcomes.cs                  # NUEVO — fábricas de error
│   ├── Interfaces/
│   │   └── IReportQueryRunner.cs                 # NUEVO — puerto de ejecución de reportes
│   ├── Reports/
│   │   ├── QueryParameter.cs                     # NUEVO — parámetro neutro
│   │   ├── QueryBuilder.cs                       # se queda; pierde el driver
│   │   └── ReportExecutor.cs                     # orquesta; deja de abrir conexiones
│   └── Services/
│       ├── AuditQueryService.cs                  # NUEVO
│       ├── SavedReportService.cs                 # NUEVO
│       ├── UserService.cs                        # NUEVO
│       ├── HomeDashboardService.cs               # NUEVO
│       ├── TerminologyService.cs                 # NUEVO
│       ├── QuestionnaireService.cs               # + métodos de lectura
│       └── QuestionnaireResponseService.cs       # + métodos de lectura
│
├── ChequeAR.Infrastructure/              # + Hl7.Fhir.Specification.R5 (+ .Data.R5)
│   ├── Fhir/
│   │   ├── FhirValidatorService.cs               # sustituto → validador real
│   │   └── Profiles/                             # NUEVO — copiado a la salida
│   │       ├── ChequeARQuestionnaire.StructureDefinition.json
│   │       └── ChequeARQuestionnaireResponse.StructureDefinition.json
│   └── Oracle/
│       └── OracleReportQueryRunner.cs            # NUEVO — todo lo ODP.NET de reportes
│
├── ChequeAR.Api/                         # + Hl7.Fhir.R5
│   ├── Fhir/
│   │   ├── CapabilityCatalog.cs                  # + Patient, Encounter
│   │   └── FhirMediaTypeFormatter.cs             # NUEVO — serialización del SDK
│   ├── App_Start/
│   │   ├── WebApiConfig.cs                       # formateador primero, FHIR por omisión
│   │   └── ServiceComposition.cs                 # validador → instancia única; servicios nuevos
│   ├── Controllers/Api/                          # emiten y aceptan recursos del estándar
│   │   └── EncounterController.cs                # NUEVO
│   ├── Controllers/Mvc/                          # dejan de inyectar repositorios
│   ├── Scripts/islands/                          # 4 puntos de llamada adaptados
│   ├── Views/Encounters/Search.cshtml            # lee entry[].resource
│   └── Web.config                                # binding redirects al alza
│
└── ChequeAR.Tests/                       # + Hl7.Fhir.R5 — sigue SIN ver Infrastructure ni Api
    ├── Application/
    │   ├── FhirRoundTripTests.cs                 # NUEVO — ida y vuelta sin pérdida
    │   ├── OperationOutcomeTests.cs              # NUEVO
    │   ├── SavedReportServiceTests.cs            # NUEVO
    │   └── AuditQueryServiceTests.cs             # NUEVO
    └── Architecture/
        └── LayerDependencyTests.cs               # NUEVO — falla si vuelve ODP.NET
```

**Structure Decision**: se conserva la solución de 5 proyectos en capas ya existente. **No se
crean proyectos nuevos**: cada archivo nuevo cae en la capa que le corresponde según la regla
que el proyecto ya aplica (contratos e interfaces en `Application`, adaptadores concretos en
`Infrastructure`, serialización HTTP en `Api`). La carpeta `ChequeAR.Tests/Architecture/` es la
única subdivisión nueva, y existe para alojar la prueba de FR-022.

## Phases

Las fases están ordenadas por **riesgo descendente** y por dependencia. La Fase 1 es una
compuerta: si falla, la forma de toda la feature cambia y conviene saberlo antes de invertir.

| Fase | Qué resuelve | Historias | Depende de |
|---|---|---|---|
| **1. Cablear el SDK** | Compuerta de riesgo (R1). Paquetes + binding redirects. Sin código funcional. | — | — |
| **2. Saneamiento de capas** | US6, US7 (FR-018 a FR-022). Independiente de FHIR. | US6, US7 | — |
| **3. Núcleo FHIR** | Constructores y parsers sobre POCOs, extensiones, Bundles, OperationOutcome. | US1, US2, US3 | 1 |
| **4. Validación y perfiles** | Perfiles escritos, validador real, instancia única. | US4 | 3 |
| **5. Controllers** | Emitir y aceptar recursos; formateador; Patient y Encounter. | US1, US3, US5 | 2, 3 |
| **6. Islas Vue** | 4 puntos de llamada + 2 constructores de contenido. | US1, US2, US3 | 5 |

**Por qué la Fase 2 va antes que la 5**: los controllers se reescriben en la Fase 5. Si el
saneamiento de capas fuera después, habría que reescribirlos dos veces.

**Por qué la Fase 6 va al final**: es la superficie con mayor riesgo de regresión (el editor es
la pantalla más compleja del sistema) y conviene atacarla con el backend ya verificado por
pruebas de ida y vuelta y por el validador oficial.

## Complexity Tracking

> Sin violaciones de la compuerta de Constitution Check. Tabla intencionalmente vacía.

## Riesgos

| Riesgo | Probabilidad | Impacto | Mitigación |
|---|---|---|---|
| `System.Text.Json 9` sobre `net471`: `FileLoadException` por binding redirects | Media | Bloqueante | Fase 1 aislada y verificada antes de escribir código FHIR. Fallback documentado: SDK 5.9.x con `System.Text.Json` 8.x |
| Pérdida silenciosa de datos al pasar la interfaz a FHIR puro (término de autoría, distinción código/valor de opciones) | **Alta si no se mitiga** | Alto | Extensión `question-term` explícita + pruebas de ida y vuelta **antes** de la Fase 6 |
| Regresión en reportes al reemplazar el parámetro del driver (tipos, binds, identificador binario) | Media | Alto | Las 11 pruebas existentes cubren la construcción; el paso 10 de `quickstart.md` cubre la ejecución real. El clonado y la normalización se mueven **tal cual**, con sus comentarios |
| Regresión en el editor de cuestionarios | Media | Alto | Fase 6 al final; verificación manual guiada en `quickstart.md` |
| Las definiciones oficiales suman ~10 MB al despliegue y la primera validación es lenta | Alta | Bajo | Validador como instancia única con resolución cacheada; calentamiento al arrancar |
| La compuerta de constitución no aporta valor por estar sin ratificar | Alta | Bajo | Documentado; recomendación de `/speckit-constitution` fuera del camino crítico |

## Nota para el documento de la tesis

Tres ajustes de redacción que esta feature **no** resuelve sola:

1. **Error conceptual**: `application/fhir+json` asegura compatibilidad con el ecosistema
   **FHIR / de salud**, no "con cualquier sistema del ecosistema .NET" (además del typo
   "ecosa"). La compatibilidad con .NET viene del framework elegido, no del tipo de contenido.
2. **Acotar el alcance FHIR**: la API cubre cuestionarios, fichas, eventos de auditoría,
   conjuntos de valores, pacientes y encuentros. Las observaciones se **consumen** del servidor
   del hospital; las funciones internas se exponen como REST propio.
3. **Reclamar más en la sección de arquitectura**: "Package by Layer" describe bien la
   organización por rol técnico, pero el proyecto además separa las capas en **ensamblados
   distintos** e **invierte la dependencia hacia la infraestructura**. Eso convierte el
   aislamiento en una garantía de compilación, no en una convención — y es lo que respalda
   directamente la frase sobre testing independiente.
