# Data Model: Motor de Renderizado Dinámico

> Los tipos de este documento son de SQL Server. La base es Oracle: los tipos reales están en las migraciones de `src/ChequeAR.Infrastructure/Migrations`.

## Entidades persistidas

### `QuestionnaireResponse`

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) | |
| `QuestionnaireId` | UNIQUEIDENTIFIER (FK) | apunta a `Questionnaire` |
| `QuestionnaireVersionId` | UNIQUEIDENTIFIER (FK) | versión exacta usada |
| `PatientHospitalId` | NVARCHAR(64) | ID interno del paciente |
| `EncounterId` | NVARCHAR(64) | opcional |
| `Status` | VARCHAR(16) | `in-progress` \| `completed` \| `amended` \| `entered-in-error` |
| `AuthoredAt` | DATETIME2 | |
| `AuthorUserId` | NVARCHAR(128) | usuario autenticado |
| `FhirJson` | NVARCHAR(MAX) | snapshot del recurso FHIR completo |
| `LastSavedAt` | DATETIME2 | |
| `IsAutosave` | BIT | |

Índices:
- `IX_QR_Patient` (`PatientHospitalId`, `Status`)
- `IX_QR_Questionnaire` (`QuestionnaireId`, `Status`)
- `IX_QR_AuthoredAt` (`AuthoredAt`)

### `QuestionnaireResponseItem`

Tabla auxiliar para queries analíticas (el módulo 004 las consume). Se rellena al finalizar (`completed`).

| Campo | Tipo |
|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) |
| `ResponseId` | UNIQUEIDENTIFIER (FK) |
| `LinkId` | VARCHAR(64) |
| `QuestionType` | VARCHAR(32) |
| `ValueString` | NVARCHAR(MAX) |
| `ValueDecimal` | DECIMAL(18,4) NULL |
| `ValueInteger` | INT NULL |
| `ValueDate` | DATE NULL |
| `ValueBoolean` | BIT NULL |
| `ValueCode` | VARCHAR(128) NULL |
| `AttachmentId` | UNIQUEIDENTIFIER NULL |
| `WasPrefilled` | BIT | |

Índices: por (`ResponseId`, `LinkId`), por (`LinkId`, `ValueCode`) para reportes.

### `Attachment`

| Campo | Tipo |
|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) |
| `OriginalFileName` | NVARCHAR(255) |
| `ContentType` | VARCHAR(64) |
| `SizeBytes` | BIGINT |
| `StoragePath` | NVARCHAR(512) | path en file share del hospital |
| `UploadedBy` | NVARCHAR(128) |
| `UploadedAt` | DATETIME2 |
| `Hash` | VARCHAR(64) | SHA-256 para detección de duplicados |

## Relaciones

```
Questionnaire 1---N QuestionnaireResponse
QuestionnaireVersion 1---N QuestionnaireResponse
QuestionnaireResponse 1---N QuestionnaireResponseItem
QuestionnaireResponseItem 0..1 ---0..1 Attachment
```

## Reglas de integridad

- `Status=completed` requiere todos los `required=true` respondidos.
- Una vez `completed`, las modificaciones generan `status=amended` y un nuevo snapshot (no se sobreescribe).
- `QuestionnaireVersionId` es obligatorio y nunca cambia.

## Retención

- `in-progress` huérfanos > 30 días → job de limpieza opcional (configurable).
- Datos finalizados se conservan indefinidamente (cumplimiento Ley 26.529 — Derechos del Paciente).
