# Data Model: Capa de Interoperabilidad y Almacenamiento

## Persistencia interna

### Estrategia general

- **Tablas dedicadas por recurso** con columnas extraídas para búsqueda + columna `FhirJson NVARCHAR(MAX)` con el recurso íntegro.
- La `FhirJson` es la fuente de verdad; las columnas extraídas son índices de búsqueda mantenidos por triggers o por la capa Infrastructure.

### `FhirResource_Patient` (referencia, no contenido completo)

| Campo | Tipo | Notas |
|---|---|---|
| `HospitalId` | NVARCHAR(64) (PK) | id interno |
| `LastSyncedAt` | DATETIME2 | cache |
| `FhirJson` | NVARCHAR(MAX) | snapshot último |
| `Source` | VARCHAR(16) | `hospital-fhir` \| `oracle-view` |

Tabla mínima: ChequeAR NO es la fuente autoritativa del paciente. Sólo cachea.

### `FhirResource_Observation`

| Campo | Tipo |
|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) |
| `PatientHospitalId` | NVARCHAR(64) |
| `Code` | VARCHAR(64) | LOINC u otro |
| `EffectiveDate` | DATETIME2 |
| `ValueDecimal` | DECIMAL(18,4) NULL |
| `ValueString` | NVARCHAR(255) NULL |
| `Unit` | VARCHAR(32) NULL |
| `Source` | VARCHAR(16) | `derived-from-qr` \| `hospital-fhir-cache` |
| `FhirJson` | NVARCHAR(MAX) |

Índices: (`PatientHospitalId`, `Code`, `EffectiveDate DESC`).

### `FhirResource_AuditEvent`

Ver módulo 005 (delegada allí). Esta capa expone los endpoints REST.

### `OracleViewMapping` (config en archivos, no en DB)

YAML versionado bajo `src/ChequeAR.Infrastructure/OracleMappings/`. Recargado al iniciar la app.

### `HospitalFhirCache`

Tabla simple de cache (alternativa a Redis):

| Campo | Tipo |
|---|---|
| `CacheKey` | VARCHAR(256) (PK) |
| `Payload` | NVARCHAR(MAX) |
| `ExpiresAt` | DATETIME2 |

Job de limpieza cada 5 minutos.

## Conexión a Oracle

- Driver: `Oracle.ManagedDataAccess` (compatible .NET Framework 4.7.1, no requiere cliente Oracle nativo).
- Credenciales: cuenta de service principal del hospital, **solo lectura**, sobre las vistas autorizadas.
- Pool de conexiones limitado (10 max) para no saturar el servidor del hospital.

## Mapeo Oracle → FHIR

Pipeline:

1. Consulta SQL parametrizada (filtros por `ID_PACIENTE`).
2. Por cada fila, aplica el mapping YAML → produce un recurso FHIR.
3. Valida con HAPI Validator.
4. Devuelve `Bundle` (searchset).

### Ejemplo (concreto)

Fila Oracle:
```
ID_PACIENTE=PAC-001, DIAGNOSTICO_CIE10=K74.6, FECHA_DIAGNOSTICO=2024-03-15
```

Recurso producido:
```json
{
  "resourceType": "Condition",
  "subject": {"reference": "Patient/PAC-001"},
  "code": {"coding": [{"system": "http://hl7.org/fhir/sid/icd-10", "code": "K74.6"}]},
  "onsetDateTime": "2024-03-15"
}
```

## Reglas

- Recursos cacheados nunca se exponen como autoritativos: incluyen `meta.source = "oracle-view"` o `"hospital-fhir-cache"`.
- Recursos derivados de QR (`Observation` calculada al finalizar una ficha) llevan `meta.source = "chequear-derived"` y `derivedFrom` apunta al `QuestionnaireResponse`.
