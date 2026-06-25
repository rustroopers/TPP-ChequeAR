# Contract: Esquema de las vistas `VW_*`

Contrato que el mock garantiza al consumidor (`OracleViewClient` + mapeos YAML de `OracleMappings/`). Cualquier ruptura aquí es un incumplimiento del SC-004 y SC-008.

## Convenciones del contrato

- **Tipos Oracle**: indicados como aparecen en `USER_TAB_COLUMNS`. El driver `Oracle.ManagedDataAccess` los devuelve como `string`/`decimal`/`DateTime` según corresponda.
- **Nulabilidad**: `NULL` = puede venir vacío, `NOT NULL` = nunca vacío en filas devueltas por la vista.
- **Estabilidad**: las columnas listadas son **garantizadas**. Es legítimo agregar columnas nuevas en el futuro; no es legítimo renombrar/quitar sin actualizar el mapping YAML correspondiente.

---

## `VW_PACIENTE`

| Columna | Tipo Oracle | Nulabilidad |
|---|---|---|
| `ID_PACIENTE` | `VARCHAR2(16)` | NOT NULL |
| `DOCUMENTO` | `VARCHAR2(15)` | NULL |
| `TIPO_DOCUMENTO` | `VARCHAR2(8)` | NULL |
| `NOMBRE` | `VARCHAR2(80)` | NOT NULL |
| `APELLIDO` | `VARCHAR2(80)` | NOT NULL |
| `FECHA_NACIMIENTO` | `DATE` | NOT NULL |
| `SEXO` | `VARCHAR2(1)` | NOT NULL |
| `COBERTURA` | `VARCHAR2(80)` | NULL |

**Mapping FHIR esperado** (`OracleMappings/patient.yaml`): produce `Patient` con `identifier=ID_PACIENTE`, `name.given=NOMBRE`, `name.family=APELLIDO`, `birthDate=FECHA_NACIMIENTO`, `gender` mapeado desde `SEXO` (`M`→`male`, `F`→`female`, `O`→`other`).

## `VW_EPISODIO`

| Columna | Tipo Oracle | Nulabilidad |
|---|---|---|
| `ID_EPISODIO` | `VARCHAR2(16)` | NOT NULL |
| `ID_PACIENTE` | `VARCHAR2(16)` | NOT NULL |
| `TIPO` | `VARCHAR2(12)` | NOT NULL (`AMBULATORIO`\|`INTERNACION`\|`URGENCIA`) |
| `COD_ESPECIALIDAD` | `VARCHAR2(8)` | NOT NULL |
| `ESPECIALIDAD` | `VARCHAR2(120)` | NOT NULL (display denormalizado) |
| `FECHA_INICIO` | `TIMESTAMP(0)` | NOT NULL |
| `FECHA_FIN` | `TIMESTAMP(0)` | NULL |
| `MOTIVO_CONSULTA` | `VARCHAR2(400)` | NULL |

**Mapping esperado**: `Encounter` con `subject=Patient/ID_PACIENTE`, `class=TIPO`, `period.start=FECHA_INICIO`, `period.end=FECHA_FIN`, `reasonCode.text=MOTIVO_CONSULTA`.

## `VW_DIAGNOSTICO`

| Columna | Tipo Oracle | Nulabilidad |
|---|---|---|
| `ID_DIAGNOSTICO` | `NUMBER(10)` | NOT NULL |
| `ID_EPISODIO` | `VARCHAR2(16)` | NOT NULL |
| `ID_PACIENTE` | `VARCHAR2(16)` | NOT NULL |
| `COD_CIE10` | `VARCHAR2(8)` | NOT NULL |
| `DISPLAY` | `VARCHAR2(200)` | NOT NULL |
| `ES_PRINCIPAL` | `NUMBER(1)` | NOT NULL (`0`\|`1`) |
| `FECHA_DIAGNOSTICO` | `DATE` | NOT NULL |

**Mapping esperado** (`OracleMappings/condition.yaml`): `Condition` con `subject=Patient/ID_PACIENTE`, `encounter=Encounter/ID_EPISODIO`, `code.coding[system=http://hl7.org/fhir/sid/icd-10, code=COD_CIE10, display=DISPLAY]`, `onsetDateTime=FECHA_DIAGNOSTICO`.

## `VW_OBSERVACION_LAB`

| Columna | Tipo Oracle | Nulabilidad |
|---|---|---|
| `ID_OBSERVACION` | `NUMBER(10)` | NOT NULL |
| `ID_PACIENTE` | `VARCHAR2(16)` | NOT NULL |
| `ID_EPISODIO` | `VARCHAR2(16)` | NOT NULL |
| `COD_LOINC` | `VARCHAR2(20)` | NOT NULL |
| `DISPLAY` | `VARCHAR2(200)` | NOT NULL |
| `VALOR_NUMERICO` | `NUMBER(12,4)` | NULL |
| `VALOR_TEXTO` | `VARCHAR2(120)` | NULL |
| `UNIDAD` | `VARCHAR2(20)` | NULL |
| `FECHA_TOMA` | `TIMESTAMP(0)` | NOT NULL |

**Invariante**: en cada fila al menos uno de `VALOR_NUMERICO` o `VALOR_TEXTO` es NOT NULL.

**Mapping esperado** (`OracleMappings/observation.yaml`): `Observation` con `subject=Patient/ID_PACIENTE`, `encounter=Encounter/ID_EPISODIO`, `code.coding[system=http://loinc.org, code=COD_LOINC, display=DISPLAY]`, `valueQuantity` o `valueString` según corresponda, `effectiveDateTime=FECHA_TOMA`.

## `VW_SIGNOS_VITALES`

| Columna | Tipo Oracle | Nulabilidad |
|---|---|---|
| `ID_SIGNO` | `NUMBER(10)` | NOT NULL |
| `ID_EPISODIO` | `VARCHAR2(16)` | NOT NULL |
| `ID_PACIENTE` | `VARCHAR2(16)` | NOT NULL |
| `FECHA_TOMA` | `TIMESTAMP(0)` | NOT NULL |
| `PESO_KG` | `NUMBER(5,2)` | NULL |
| `TALLA_CM` | `NUMBER(5,1)` | NULL |
| `FC_LPM` | `NUMBER(3)` | NULL |
| `FR_RPM` | `NUMBER(3)` | NULL |
| `TA_SISTOLICA` | `NUMBER(3)` | NULL |
| `TA_DIASTOLICA` | `NUMBER(3)` | NULL |
| `TEMPERATURA_C` | `NUMBER(3,1)` | NULL |

## `VW_MEDICACION`

| Columna | Tipo Oracle | Nulabilidad |
|---|---|---|
| `ID_MEDICACION` | `NUMBER(10)` | NOT NULL |
| `ID_EPISODIO` | `VARCHAR2(16)` | NOT NULL |
| `ID_PACIENTE` | `VARCHAR2(16)` | NOT NULL |
| `COD_ATC` | `VARCHAR2(10)` | NOT NULL |
| `NOMBRE_COMERCIAL` | `VARCHAR2(120)` | NOT NULL |
| `DOSIS` | `VARCHAR2(60)` | NOT NULL |
| `VIA` | `VARCHAR2(20)` | NOT NULL |
| `FECHA_INICIO` | `DATE` | NOT NULL |
| `FECHA_FIN` | `DATE` | NULL |

## `VW_ALERGIA`

| Columna | Tipo Oracle | Nulabilidad |
|---|---|---|
| `ID_ALERGIA` | `NUMBER(10)` | NOT NULL |
| `ID_PACIENTE` | `VARCHAR2(16)` | NOT NULL |
| `SUSTANCIA` | `VARCHAR2(120)` | NULL (cuando `ESTADO=DESCONOCIDAS`) |
| `REACCION` | `VARCHAR2(200)` | NULL |
| `SEVERIDAD` | `VARCHAR2(12)` | NULL |
| `ESTADO` | `VARCHAR2(16)` | NOT NULL |
| `FECHA_REGISTRO` | `DATE` | NOT NULL |

## `VW_PROFESIONAL`

| Columna | Tipo Oracle | Nulabilidad |
|---|---|---|
| `ID_PROFESIONAL` | `VARCHAR2(16)` | NOT NULL |
| `MATRICULA` | `VARCHAR2(20)` | NOT NULL |
| `NOMBRE` | `VARCHAR2(80)` | NOT NULL |
| `APELLIDO` | `VARCHAR2(80)` | NOT NULL |
| `COD_ESPECIALIDAD` | `VARCHAR2(8)` | NOT NULL |
| `ESPECIALIDAD` | `VARCHAR2(120)` | NOT NULL |

## `VW_ESPECIALIDAD`

| Columna | Tipo Oracle | Nulabilidad |
|---|---|---|
| `COD_ESPECIALIDAD` | `VARCHAR2(8)` | NOT NULL |
| `NOMBRE` | `VARCHAR2(120)` | NOT NULL |

## `VW_TURNO`

| Columna | Tipo Oracle | Nulabilidad |
|---|---|---|
| `ID_TURNO` | `VARCHAR2(16)` | NOT NULL |
| `ID_PACIENTE` | `VARCHAR2(16)` | NOT NULL |
| `ID_PROFESIONAL` | `VARCHAR2(16)` | NOT NULL |
| `COD_ESPECIALIDAD` | `VARCHAR2(8)` | NOT NULL |
| `ESPECIALIDAD` | `VARCHAR2(120)` | NOT NULL |
| `FECHA_HORA` | `TIMESTAMP(0)` | NOT NULL |
| `ESTADO` | `VARCHAR2(12)` | NOT NULL |

---

## Filtros soportados por `OracleViewClient.QueryAsync`

El cliente actual acepta filtros `columna = valor` (bind variables). Las columnas filtrables están limitadas a los identificadores y códigos de cada vista:

| Vista | Columnas filtrables recomendadas |
|---|---|
| `VW_PACIENTE` | `ID_PACIENTE`, `DOCUMENTO` |
| `VW_EPISODIO` | `ID_EPISODIO`, `ID_PACIENTE`, `COD_ESPECIALIDAD` |
| `VW_DIAGNOSTICO` | `ID_EPISODIO`, `ID_PACIENTE`, `COD_CIE10` |
| `VW_OBSERVACION_LAB` | `ID_EPISODIO`, `ID_PACIENTE`, `COD_LOINC` |
| `VW_SIGNOS_VITALES` | `ID_EPISODIO`, `ID_PACIENTE` |
| `VW_MEDICACION` | `ID_EPISODIO`, `ID_PACIENTE`, `COD_ATC` |
| `VW_ALERGIA` | `ID_PACIENTE` |
| `VW_PROFESIONAL` | `ID_PROFESIONAL`, `MATRICULA`, `COD_ESPECIALIDAD` |
| `VW_ESPECIALIDAD` | `COD_ESPECIALIDAD` |
| `VW_TURNO` | `ID_TURNO`, `ID_PACIENTE`, `ID_PROFESIONAL` |

Filtros por columnas que no están en este listado pueden devolver resultados pero **no** están cubiertos por los índices y pueden degradar performance. Si el hospital real requiere otros filtros frecuentes, agregar índices en `01-schema.sql` y documentarlo aquí.
