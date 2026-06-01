# Research: Capa de Interoperabilidad y Almacenamiento

## Decisiones

### D1. SDK FHIR para .NET 4.7.1

**Decisión**: Firely .NET SDK (`Hl7.Fhir.R5`).

**Justificación**: Soporta .NET Standard 2.0 (compatible con .NET Framework 4.7.1); validador y serializadores oficiales; mayor adopción que alternativas. HAPI-FHIR es Java y se usará sólo como validator-as-a-service en CI/dev.

### D2. Storage de recursos FHIR: relacional + JSON o documental

**Decisión**: SQL Server con tablas dedicadas y `FhirJson NVARCHAR(MAX)` como fuente de verdad.

**Justificación**: El hospital corre SQL Server. Documental (Mongo/Cosmos) no está en su stack soportado. La columna JSON da fidelidad FHIR; columnas extraídas habilitan los `search params` con buen rendimiento.

**Alternativas descartadas**:
- *HAPI FHIR Server (Java)*: rompe el stack .NET acordado y complica mantenimiento por el equipo del hospital.
- *Mongo*: incompatible con la política de infraestructura.

### D3. Acceso a Oracle desde .NET 4.7.1

**Decisión**: `Oracle.ManagedDataAccess` (driver managed-only).

**Justificación**: No requiere instalar cliente Oracle nativo; deploy más simple en IIS. Limitado a vistas read-only en el alcance acordado con el hospital.

### D4. Resiliencia del cliente del hospital

**Decisión**: Polly con:
- Timeout 5 s por request.
- Retry (3) con backoff exponencial + jitter (excepto en 4xx).
- Circuit-breaker (5 fallos → break 30 s).
- Fallback a cache TTL 60 s si está disponible.

**Justificación**: Mitiga directamente el riesgo del anteproyecto "Interoperabilidad fallida" (criticidad 10). Polly es estándar en .NET.

### D5. Validación FHIR

**Decisión**: HAPI-FHIR Validator como servicio Docker para CI + Firely Validator embebido en runtime (más liviano).

**Justificación**: Doble red de seguridad. El de runtime no debe caer la app por warnings, sólo por errors.

### D6. Mapeo Oracle → FHIR declarativo (YAML)

**Decisión**: Cada vista del hospital se mapea con un YAML versionado en repo. Cargados al iniciar la app y reloadables vía endpoint admin.

**Justificación**: Mitiga riesgo "Cambios en los requerimientos" (criticidad 12 del anteproyecto). Cambios de columna no requieren recompilación.

### D7. Identidad y referencias

**Decisión**: El identificador autoritativo del paciente es el `ID interno hospitalario`. ChequeAR no genera ni almacena un id propio para `Patient`; sólo cachea.

**Justificación**: Evita drift y problemas de matching entre fuentes.

### D8. Caché

**Decisión**: Tabla `HospitalFhirCache` en SQL Server con TTL 60 s + job de limpieza.

**Justificación**: Sin Redis disponible en infra. Persistente entre recycles de IIS. Performance suficiente para el scope.

## Referencias

- HL7 FHIR R5 Specification — https://hl7.org/fhir/R5/
- Firely .NET SDK — https://github.com/FirelyTeam/firely-net-sdk
- HAPI FHIR Validator — https://hapifhir.io/hapi-fhir/docs/validation/
- Polly — https://github.com/App-vNext/Polly
- Oracle.ManagedDataAccess — https://docs.oracle.com/en/database/oracle/oracle-data-access-components/

## Riesgos abiertos

| Riesgo | Mitigación |
|---|---|
| Cambios en la API FHIR del hospital | Versionar el `HospitalFhirClient` y mockear en tests de integración |
| Schema Oracle cambia sin aviso | Health-check periódico que verifica columnas esperadas |
| Carga sobre Oracle | Pool de 10 conexiones; no consultar en endpoints high-throughput sin caché |
