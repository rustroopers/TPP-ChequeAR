# ChequeAR Constitution

## Core Principles

### I. Capas en un solo sentido
`Domain ← Application ← Infrastructure ← Api`. Domain son POCOs sin paquetes. Application
declara las interfaces (`Application/Interfaces`) y no conoce EF, Oracle, System.Web ni Owin.
Infrastructure las implementa. `LayerDependencyTests` lo verifica sobre ensamblados y csproj:
una feature que lo rompa no se mergea.

### II. Tests sin infraestructura
`ChequeAR.Tests` referencia sólo Domain y Application (xUnit + Moq) y mantiene el umbral de
cobertura configurado en su csproj. Lo que necesita Oracle va a `tests/integration/` con
`[Trait("Requires", "Oracle")]` y se corre a mano; el CI corre el resto. Cada cambio de
comportamiento trae su test.

### III. Oracle como única base
Oracle XE 21c en desarrollo y Oracle Autonomous Database en producción, vía EF6 con
migraciones Code First. Nada de tipos ni sintaxis de SQL Server. El SQL crudo va
parametrizado y con el esquema `"CHEQUEAR"` calificado. Una migración no puede perder datos sin
un paso explícito que lo justifique.

### IV. FHIR R5 conforme
Todo lo que cuelga de `/fhir/r5` es HL7 FHIR R5 real: `application/fhir+json`, Bundles válidos,
errores como `OperationOutcome` y concurrencia con `ETag`/`If-Match`. Lo que no tiene recurso
natural vive fuera de `/fhir/r5` (`/api/...`). Una extensión propia nueva se justifica sólo si
no hay campo nativo, y se documenta en `src/README.md`.

### V. Sólo es-AR
La interfaz, los mensajes y la documentación están en español rioplatense; la interfaz, en trato de usted. Fechas y horas se
muestran en hora argentina. No hay selector de idioma (spec 007 descartada).

## Restricciones

- .NET Framework 4.7.1, Web API 2 + MVC 5, islas Vue sin bundler, deploy en Azure App Service
  (Windows) y Oracle Autonomous Always Free.
- No hay API del hospital: login local y datos de pacientes desde las vistas `VW_*`
  (allowlist en `Infrastructure/Oracle/allowlist.yaml`).
- Permisos por `[RequiresPermissionApi]` en cada endpoint y filtro por especialidad en los
  servicios. Escrituras y exportaciones dejan `AuditEvent`.
- Secretos fuera del repo (`Web.Secrets.config`, `Web.ConnectionStrings.config`, App Settings).

## Flujo de trabajo

- Ramas desde `dev`, PR a `dev` con el CI en verde (build con `-warnaserror` y tests). `main`
  recibe releases desde `dev` y despliega; las migraciones las aplica el deploy después de un
  export Data Pump.
- Cada feature tiene su carpeta en `specs/` con `spec.md`, `plan.md` y `tasks.md`, y las marcas
  de `tasks.md` reflejan lo que está en el código.

## Governance

Esta constitución tiene precedencia sobre los planes de cada feature. Cambiarla requiere un PR
propio que explique el motivo y actualice las specs afectadas.

**Version**: 1.0.0 | **Ratified**: 2026-10-16 | **Last Amended**: 2026-10-16
