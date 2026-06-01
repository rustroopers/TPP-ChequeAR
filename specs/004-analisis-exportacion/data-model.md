# Data Model: Módulo de Análisis y Exportación

## Lecturas

El módulo lee principalmente de `QuestionnaireResponseItem` (ver módulo 002) que está indexada por (`ResponseId`, `LinkId`) y por (`LinkId`, `ValueCode`).

## Entidades nuevas

### `SavedReport`

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) | |
| `Name` | NVARCHAR(255) | |
| `Scope` | VARCHAR(16) | `private` \| `specialty` |
| `SpecialtyCode` | VARCHAR(64) NULL | requerido si `scope=specialty` |
| `QuestionnaireId` | UNIQUEIDENTIFIER (FK) | |
| `QueryJson` | NVARCHAR(MAX) | filtros + selección |
| `CreatedBy` | NVARCHAR(128) | |
| `CreatedAt` | DATETIME2 | |
| `UpdatedAt` | DATETIME2 | |

Índices: (`CreatedBy`), (`SpecialtyCode`, `Scope`).

### `ReportExecution`

| Campo | Tipo |
|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) |
| `SavedReportId` | UNIQUEIDENTIFIER NULL (FK) |
| `QueryJson` | NVARCHAR(MAX) |
| `ExecutedBy` | NVARCHAR(128) |
| `ExecutedAt` | DATETIME2 |
| `RowCount` | INT |
| `DurationMs` | INT |
| `ArtifactId` | UNIQUEIDENTIFIER NULL |
| `Status` | VARCHAR(16) |

### `ExportArtifact`

| Campo | Tipo |
|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) |
| `Format` | VARCHAR(8) | `xlsx` \| `csv` |
| `StoragePath` | NVARCHAR(512) |
| `SizeBytes` | BIGINT |
| `Sha256` | VARCHAR(64) |
| `CreatedBy` | NVARCHAR(128) |
| `CreatedAt` | DATETIME2 |
| `ExpiresAt` | DATETIME2 |

Job de limpieza diaria elimina `ExpiresAt < now`.

### `ExportJob` (cola de trabajo)

| Campo | Tipo |
|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) |
| `QueryJson` | NVARCHAR(MAX) |
| `Format` | VARCHAR(8) |
| `RequestedBy` | NVARCHAR(128) |
| `Status` | VARCHAR(16) | `pending` \| `running` \| `completed` \| `failed` |
| `Progress` | DECIMAL(5,2) |
| `ArtifactId` | UNIQUEIDENTIFIER NULL |
| `Error` | NVARCHAR(MAX) NULL |
| `CreatedAt` | DATETIME2 |
| `CompletedAt` | DATETIME2 NULL |

Worker en segundo plano (servicio Windows o `Hangfire` self-host) procesa pendientes.

## Vistas analíticas

Vista materializada (refresh diario o on-demand) para reportes pesados:

```sql
CREATE VIEW vw_FlatResponses AS
SELECT
  qr.Id              AS ResponseId,
  qr.PatientHospitalId,
  qr.AuthoredAt,
  qr.QuestionnaireId,
  qrv.VersionNumber  AS QuestionnaireVersion,
  qri.LinkId,
  qri.ValueDecimal,
  qri.ValueString,
  qri.ValueDate,
  qri.ValueBoolean,
  qri.ValueCode
FROM QuestionnaireResponse qr
JOIN QuestionnaireVersion qrv ON qrv.Id = qr.QuestionnaireVersionId
JOIN QuestionnaireResponseItem qri ON qri.ResponseId = qr.Id
WHERE qr.Status = 'completed';
```

Para consultas con N filtros, se construye SQL dinámico parametrizado con `EXISTS` o pivot por `LinkId`.

## Reglas de integridad

- `ExportArtifact` huérfano (sin `ReportExecution` ni `ExportJob` activo y > 7 días) → eliminable.
- Compartir un `SavedReport` requiere `SpecialtyCode` no nulo.
- No se permite ejecutar consultas sobre `QuestionnaireResponse` en `in-progress`.
