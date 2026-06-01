# Data Model: Gestor y Editor de Cuestionarios

Persistencia en base de datos del lado ChequeAR (los datos clínicos que persistan residen aquí; los datos hospitalarios pre-existentes se consultan vía FHIR/vista Oracle, ver módulo 003).

Motor sugerido: SQL Server (compatible con .NET 4.7.1 e IIS sobre Windows Server 2016). Se modelan tablas que reflejan el recurso FHIR `Questionnaire` pero permiten queries eficientes y versionado.

## Entidades

### `Questionnaire`

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) | Identificador FHIR lógico |
| `Title` | NVARCHAR(255) | Obligatorio |
| `Description` | NVARCHAR(MAX) | |
| `Status` | VARCHAR(16) | `draft` \| `active` \| `retired` |
| `SpecialtyCode` | VARCHAR(64) | SNOMED-CT u otro |
| `SpecialtyDisplay` | NVARCHAR(255) | |
| `CurrentVersionId` | UNIQUEIDENTIFIER (FK) | apunta al snapshot activo |
| `CreatedBy` | NVARCHAR(128) | id usuario del Hospital |
| `CreatedAt` | DATETIME2 | |
| `UpdatedAt` | DATETIME2 | |
| `IsDeleted` | BIT | soft delete |

### `QuestionnaireVersion`

Snapshot inmutable de un cuestionario publicado. Una `Questionnaire` tiene N versiones; cualquier `QuestionnaireResponse` referencia la versión exacta.

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) | |
| `QuestionnaireId` | UNIQUEIDENTIFIER (FK) | |
| `VersionNumber` | INT | incremental por cuestionario |
| `FhirJson` | NVARCHAR(MAX) | recurso FHIR serializado completo |
| `PublishedBy` | NVARCHAR(128) | |
| `PublishedAt` | DATETIME2 | |

UQ: (`QuestionnaireId`, `VersionNumber`)

### `QuestionGroup`

Grupo jerárquico (para edición rica; al publicar se serializa al JSON FHIR).

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) | |
| `QuestionnaireId` | UNIQUEIDENTIFIER (FK) | |
| `ParentGroupId` | UNIQUEIDENTIFIER (FK, null) | jerarquía |
| `LinkId` | VARCHAR(64) | FHIR `linkId` |
| `Title` | NVARCHAR(255) | |
| `OrderIndex` | INT | |

### `Question`

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) | |
| `QuestionnaireId` | UNIQUEIDENTIFIER (FK) | |
| `GroupId` | UNIQUEIDENTIFIER (FK, null) | |
| `LinkId` | VARCHAR(64) | |
| `Text` | NVARCHAR(MAX) | |
| `Type` | VARCHAR(32) | `decimal`, `integer`, `string`, `text`, `date`, `dateTime`, `boolean`, `choice`, `open-choice`, `attachment`, `scale` |
| `Required` | BIT | |
| `RepeatsAllowed` | BIT | |
| `OrderIndex` | INT | |
| `ValidationJson` | NVARCHAR(MAX) | reglas: min, max, regex, longitud, extensiones permitidas |
| `EnableWhenJson` | NVARCHAR(MAX) | lógica condicional (compatible con FHIR `enableWhen`) |
| `CodingJson` | NVARCHAR(MAX) | array de Coding (CIE-10, SNOMED, LOINC) |

### `AnswerOption`

| Campo | Tipo |
|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) |
| `QuestionId` | UNIQUEIDENTIFIER (FK) |
| `Value` | NVARCHAR(255) |
| `Display` | NVARCHAR(255) |
| `Code` | VARCHAR(64) |
| `OrderIndex` | INT |

## Relaciones

```
Questionnaire 1---N QuestionnaireVersion
Questionnaire 1---N QuestionGroup
QuestionGroup 1---N QuestionGroup (self-ref, opcional)
Questionnaire 1---N Question
QuestionGroup 1---N Question
Question 1---N AnswerOption
```

## Reglas de integridad

- No se permite `DELETE` físico de `Questionnaire` con versiones publicadas; solo soft delete.
- `Status=active` requiere `CurrentVersionId IS NOT NULL`.
- Al publicar: se serializa el árbol completo a `QuestionnaireVersion.FhirJson` (fuente de verdad para respuestas).

## Índices

- `IX_Questionnaire_Status` sobre `Status`.
- `IX_Question_LinkId` sobre `QuestionnaireId, LinkId`.
- `IX_Questionnaire_Specialty` sobre `SpecialtyCode`.
