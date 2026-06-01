# Data Model: Seguridad y Control de Accesos

## Entidades

### `User` (cache local; fuente autoritativa es el hospital)

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | NVARCHAR(64) (PK) | id del hospital |
| `DisplayName` | NVARCHAR(255) | |
| `Email` | NVARCHAR(255) NULL | |
| `LastLoginAt` | DATETIME2 NULL | |
| `LastSyncAt` | DATETIME2 | |
| `IsActive` | BIT | flag para deshabilitar localmente |

Sin contraseñas. Sin PII sensible.

### `UserRole`

| Campo | Tipo |
|---|---|
| `UserId` | NVARCHAR(64) (FK) |
| `Role` | VARCHAR(32) | `Administrador` \| `Jefe` \| `Medico` \| `Observador` |

PK: (`UserId`, `Role`). Recomendado sincronizar en cada login.

### `UserSpecialty`

| Campo | Tipo |
|---|---|
| `UserId` | NVARCHAR(64) (FK) |
| `SpecialtyCode` | VARCHAR(64) |

PK: (`UserId`, `SpecialtyCode`).

### `Specialty` (catálogo)

| Campo | Tipo |
|---|---|
| `Code` | VARCHAR(64) (PK) |
| `Name` | NVARCHAR(128) |
| `IsActive` | BIT |

### `RefreshToken`

| Campo | Tipo |
|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) |
| `UserId` | NVARCHAR(64) (FK) |
| `TokenHash` | VARCHAR(128) | SHA-256, nunca el token en claro |
| `IssuedAt` | DATETIME2 |
| `ExpiresAt` | DATETIME2 |
| `RevokedAt` | DATETIME2 NULL |
| `UserAgent` | NVARCHAR(255) |
| `Ip` | VARCHAR(45) |

Índices: (`UserId`, `RevokedAt`), (`TokenHash`).

### `AuditEvent` (almacenamiento)

| Campo | Tipo | Notas |
|---|---|---|
| `Id` | UNIQUEIDENTIFIER (PK) | |
| `Recorded` | DATETIME2 | indexado |
| `Action` | CHAR(1) | C/R/U/D/E |
| `Outcome` | VARCHAR(8) | 0/4/8/12 (FHIR) |
| `AgentUserId` | NVARCHAR(64) | indexado |
| `AgentIp` | VARCHAR(45) |
| `EntityRef` | NVARCHAR(255) | "Questionnaire/abc" |
| `EntityType` | VARCHAR(64) |
| `Category` | VARCHAR(32) | `rest` \| `auth` \| `export` |
| `FhirJson` | NVARCHAR(MAX) | recurso FHIR completo |

Partición por mes sobre `Recorded` para favorecer la retención de 6+ años.

**Restricción**: tabla **append-only**. Triggers de SQL Server impiden UPDATE/DELETE excepto vía job de purga (no aplicable en MVP).

## Estrategia de sincronización

- En cada login: refresh de `User`, `UserRole`, `UserSpecialty` desde el hospital.
- Job nocturno: detectar usuarios desactivados en el hospital → marcar `IsActive=0` y revocar refresh tokens activos.

## Retención y particionamiento

- `AuditEvent` particionada por `Recorded` (mensual).
- Backups offline mensuales antes de cualquier purga.
- Sin purga en MVP (retención permanente por requerimiento normativo).

## Seguridad de datos

- `TokenHash` en `RefreshToken`, nunca el token en claro.
- Conexiones a SQL Server con cuenta de mínimos privilegios (no `sa`).
- Secret JWT en DPAPI o `Microsoft.Configuration.UserSecrets` (en dev) / variable de entorno protegida (en prod).
