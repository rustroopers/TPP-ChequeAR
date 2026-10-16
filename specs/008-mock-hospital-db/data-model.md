# Data Model: Mock Hospital DB

> Modelo del **lado hospital** (NO el de ChequeAR). Para el modelo de ChequeAR, ver `specs/001-gestor-cuestionarios/data-model.md` y `specs/003-interoperabilidad-fhir/data-model.md`.
>
> **Schema**: `HOSPITAL_MOCK` — owner de las tablas y vistas.
> **Usuario consumidor**: `CHEQUEAR_RO` — sólo `SELECT` sobre `VW_*`.

## Convenciones

- Identificadores Oracle en `UPPER_SNAKE_CASE`, ≤ 30 chars.
- Tipos:
  - Identificadores hospitalarios: `VARCHAR2(16)` (ej. `PAC-000042` ocupa 10 chars).
  - Códigos clínicos (CIE-10, LOINC): `VARCHAR2(20)`.
  - Texto libre corto: `VARCHAR2(120)`; descripciones: `VARCHAR2(400)`.
  - Fechas con hora: `TIMESTAMP(0)`. Fechas puras: `DATE`.
  - Numéricos: `NUMBER(p,s)` con `p,s` por dominio (peso `NUMBER(5,2)` kg, etc.).
- Soft delete no se modela (un hospital real puede hacerlo, pero acá no agrega valor y complica el seed).
- Todas las FK con `ON DELETE NO ACTION` (la imagen oficial Oracle no soporta DDL parcial; mantenemos rígido).

---

## Tablas internas (`HOSP_*`) — NO expuestas al consumidor

### `HOSP_PACIENTE`

| Columna | Tipo | Nulo | Notas |
|---|---|---|---|
| `ID_PACIENTE` | `VARCHAR2(16)` | NO | PK. Formato `PAC-NNNNNN` (FR-015). |
| `DOCUMENTO` | `VARCHAR2(15)` | SÍ | DNI; puede faltar (recién nacidos). |
| `TIPO_DOCUMENTO` | `VARCHAR2(8)` | SÍ | `DNI`, `PAS`, `CI`. |
| `NOMBRE` | `VARCHAR2(80)` | NO | |
| `APELLIDO` | `VARCHAR2(80)` | NO | |
| `FECHA_NACIMIENTO` | `DATE` | NO | Garantiza edad 0–17 al anclaje. |
| `SEXO` | `VARCHAR2(1)` | NO | `M`, `F`, `O`. |
| `COBERTURA` | `VARCHAR2(80)` | SÍ | Obra social o "Particular". |
| `FECHA_ALTA_ADM` | `DATE` | NO | Alta administrativa en el hospital. |
| `ID_INTERNO` | `NUMBER(10)` | NO | Surrogate interno; NO se expone. |

Índices: PK por `ID_PACIENTE`; UQ por `ID_INTERNO`; IDX por `DOCUMENTO`.

### `HOSP_ESPECIALIDAD`

| Columna | Tipo | Notas |
|---|---|---|
| `COD_ESPECIALIDAD` | `VARCHAR2(8)` PK | `PED`, `NEO`, `CARP`, `NEUM`, `GAST`, `INFE`, `NEFRO`, `ENDO`. |
| `NOMBRE` | `VARCHAR2(120)` | "Pediatría General", "Neonatología", etc. |

### `HOSP_PROFESIONAL`

| Columna | Tipo | Notas |
|---|---|---|
| `ID_PROFESIONAL` | `VARCHAR2(16)` PK | `PROF-NNNNNN`. |
| `MATRICULA` | `VARCHAR2(20)` | UQ. Formato `MN-NNNNN`. |
| `NOMBRE` | `VARCHAR2(80)` | |
| `APELLIDO` | `VARCHAR2(80)` | |
| `COD_ESPECIALIDAD` | `VARCHAR2(8)` FK→`HOSP_ESPECIALIDAD` | |

### `HOSP_EPISODIO`

| Columna | Tipo | Notas |
|---|---|---|
| `ID_EPISODIO` | `VARCHAR2(16)` PK | `EPI-NNNNNN`. |
| `ID_PACIENTE` | `VARCHAR2(16)` FK | |
| `TIPO` | `VARCHAR2(12)` | `AMBULATORIO`, `INTERNACION`, `URGENCIA`. |
| `COD_ESPECIALIDAD` | `VARCHAR2(8)` FK | |
| `ID_PROFESIONAL_RESP` | `VARCHAR2(16)` FK | Profesional responsable. |
| `FECHA_INICIO` | `TIMESTAMP(0)` | |
| `FECHA_FIN` | `TIMESTAMP(0)` NULL | NULL ⇒ episodio abierto. |
| `MOTIVO_CONSULTA` | `VARCHAR2(400)` | Texto libre corto. |

Índices: IDX (`ID_PACIENTE`, `FECHA_INICIO DESC`).

### `HOSP_DIAGNOSTICO`

| Columna | Tipo | Notas |
|---|---|---|
| `ID_DIAGNOSTICO` | `NUMBER(10)` PK | Surrogate. |
| `ID_EPISODIO` | `VARCHAR2(16)` FK | |
| `COD_CIE10` | `VARCHAR2(8)` | Ej. `J06.9`. |
| `DISPLAY` | `VARCHAR2(200)` | "Infección aguda de vías respiratorias superiores". |
| `ES_PRINCIPAL` | `NUMBER(1)` | `1`/`0`. Un episodio tiene 0 o 1 principal. |
| `FECHA_DIAGNOSTICO` | `DATE` | |

Índices: IDX (`ID_EPISODIO`); IDX (`COD_CIE10`).

### `HOSP_OBSERVACION_LAB`

| Columna | Tipo | Notas |
|---|---|---|
| `ID_OBSERVACION` | `NUMBER(10)` PK | Surrogate. |
| `ID_EPISODIO` | `VARCHAR2(16)` FK | |
| `ID_PACIENTE` | `VARCHAR2(16)` FK | Redundante para acelerar queries por paciente. |
| `COD_LOINC` | `VARCHAR2(20)` | Ej. `718-7`. |
| `DISPLAY` | `VARCHAR2(200)` | "Hemoglobina en sangre". |
| `VALOR_NUMERICO` | `NUMBER(12,4)` NULL | Mutuamente excluyente con `VALOR_TEXTO`. |
| `VALOR_TEXTO` | `VARCHAR2(120)` NULL | Para LOINC con resultado cualitativo. |
| `UNIDAD` | `VARCHAR2(20)` NULL | Ej. `g/dL`. |
| `FECHA_TOMA` | `TIMESTAMP(0)` | |

Check constraint: `(VALOR_NUMERICO IS NOT NULL) OR (VALOR_TEXTO IS NOT NULL)`.
Índices: IDX (`ID_PACIENTE`, `COD_LOINC`, `FECHA_TOMA DESC`).

### `HOSP_SIGNOS_VITALES`

| Columna | Tipo | Notas |
|---|---|---|
| `ID_SIGNO` | `NUMBER(10)` PK | |
| `ID_EPISODIO` | `VARCHAR2(16)` FK | |
| `FECHA_TOMA` | `TIMESTAMP(0)` | |
| `PESO_KG` | `NUMBER(5,2)` NULL | |
| `TALLA_CM` | `NUMBER(5,1)` NULL | |
| `FC_LPM` | `NUMBER(3)` NULL | |
| `FR_RPM` | `NUMBER(3)` NULL | |
| `TA_SISTOLICA` | `NUMBER(3)` NULL | mmHg |
| `TA_DIASTOLICA` | `NUMBER(3)` NULL | mmHg |
| `TEMPERATURA_C` | `NUMBER(3,1)` NULL | |

### `HOSP_MEDICACION`

| Columna | Tipo | Notas |
|---|---|---|
| `ID_MEDICACION` | `NUMBER(10)` PK | |
| `ID_EPISODIO` | `VARCHAR2(16)` FK | |
| `COD_ATC` | `VARCHAR2(10)` | |
| `NOMBRE_COMERCIAL` | `VARCHAR2(120)` | |
| `DOSIS` | `VARCHAR2(60)` | Texto libre ("10 mg/kg cada 8 hs"). |
| `VIA` | `VARCHAR2(20)` | `ORAL`, `IV`, `IM`, `TOPICA`, `INH`. |
| `FECHA_INICIO` | `DATE` | |
| `FECHA_FIN` | `DATE` NULL | NULL si continúa. |

### `HOSP_ALERGIA`

| Columna | Tipo | Notas |
|---|---|---|
| `ID_ALERGIA` | `NUMBER(10)` PK | |
| `ID_PACIENTE` | `VARCHAR2(16)` FK | |
| `SUSTANCIA` | `VARCHAR2(120)` NULL | NULL + `ESTADO='DESCONOCIDAS'`. |
| `REACCION` | `VARCHAR2(200)` NULL | |
| `SEVERIDAD` | `VARCHAR2(12)` NULL | `LEVE`, `MODERADA`, `SEVERA`. |
| `ESTADO` | `VARCHAR2(16)` | `CONFIRMADA`, `SOSPECHADA`, `DESCONOCIDAS`, `DESCARTADA`. |
| `FECHA_REGISTRO` | `DATE` | |

### `HOSP_TURNO`

| Columna | Tipo | Notas |
|---|---|---|
| `ID_TURNO` | `VARCHAR2(16)` PK | `TRN-NNNNNN`. |
| `ID_PACIENTE` | `VARCHAR2(16)` FK | |
| `ID_PROFESIONAL` | `VARCHAR2(16)` FK | |
| `COD_ESPECIALIDAD` | `VARCHAR2(8)` FK | |
| `FECHA_HORA` | `TIMESTAMP(0)` | |
| `ESTADO` | `VARCHAR2(12)` | `PROGRAMADO`, `ATENDIDO`, `AUSENTE`, `CANCELADO`. |

---

## Vistas expuestas (`VW_*`) — ÚNICO acceso desde `OracleViewClient`

Cada vista expone **sólo** las columnas que un sistema externo necesitaría ver. Columnas internas como `ID_INTERNO` no aparecen.

| Vista | Filas base | Columnas expuestas |
|---|---|---|
| `VW_PACIENTE` | `HOSP_PACIENTE` | `ID_PACIENTE`, `DOCUMENTO`, `TIPO_DOCUMENTO`, `NOMBRE`, `APELLIDO`, `FECHA_NACIMIENTO`, `SEXO`, `COBERTURA` |
| `VW_EPISODIO` | `HOSP_EPISODIO` ⋈ `HOSP_ESPECIALIDAD` | `ID_EPISODIO`, `ID_PACIENTE`, `TIPO`, `COD_ESPECIALIDAD`, `ESPECIALIDAD`, `FECHA_INICIO`, `FECHA_FIN`, `MOTIVO_CONSULTA` |
| `VW_DIAGNOSTICO` | `HOSP_DIAGNOSTICO` ⋈ `HOSP_EPISODIO` | `ID_DIAGNOSTICO`, `ID_EPISODIO`, `ID_PACIENTE`, `COD_CIE10`, `DISPLAY`, `ES_PRINCIPAL`, `FECHA_DIAGNOSTICO` |
| `VW_OBSERVACION_LAB` | `HOSP_OBSERVACION_LAB` | `ID_OBSERVACION`, `ID_PACIENTE`, `ID_EPISODIO`, `COD_LOINC`, `DISPLAY`, `VALOR_NUMERICO`, `VALOR_TEXTO`, `UNIDAD`, `FECHA_TOMA` |
| `VW_SIGNOS_VITALES` | `HOSP_SIGNOS_VITALES` ⋈ `HOSP_EPISODIO` | `ID_SIGNO`, `ID_EPISODIO`, `ID_PACIENTE`, `FECHA_TOMA`, `PESO_KG`, `TALLA_CM`, `FC_LPM`, `FR_RPM`, `TA_SISTOLICA`, `TA_DIASTOLICA`, `TEMPERATURA_C` |
| `VW_MEDICACION` | `HOSP_MEDICACION` ⋈ `HOSP_EPISODIO` | `ID_MEDICACION`, `ID_EPISODIO`, `ID_PACIENTE`, `COD_ATC`, `NOMBRE_COMERCIAL`, `DOSIS`, `VIA`, `FECHA_INICIO`, `FECHA_FIN` |
| `VW_ALERGIA` | `HOSP_ALERGIA` | `ID_ALERGIA`, `ID_PACIENTE`, `SUSTANCIA`, `REACCION`, `SEVERIDAD`, `ESTADO`, `FECHA_REGISTRO` |
| `VW_PROFESIONAL` | `HOSP_PROFESIONAL` ⋈ `HOSP_ESPECIALIDAD` | `ID_PROFESIONAL`, `MATRICULA`, `NOMBRE`, `APELLIDO`, `COD_ESPECIALIDAD`, `ESPECIALIDAD` |
| `VW_ESPECIALIDAD` | `HOSP_ESPECIALIDAD` | `COD_ESPECIALIDAD`, `NOMBRE` |
| `VW_TURNO` | `HOSP_TURNO` ⋈ `HOSP_ESPECIALIDAD` | `ID_TURNO`, `ID_PACIENTE`, `ID_PROFESIONAL`, `COD_ESPECIALIDAD`, `ESPECIALIDAD`, `FECHA_HORA`, `ESTADO` |

Las vistas con `⋈` denormalizan el `display` legible de la especialidad para no obligar a un join en el cliente.

## Relaciones (ER condensado)

```
HOSP_PACIENTE 1 --- N HOSP_EPISODIO
HOSP_EPISODIO 1 --- N HOSP_DIAGNOSTICO
HOSP_EPISODIO 1 --- N HOSP_OBSERVACION_LAB
HOSP_EPISODIO 1 --- N HOSP_SIGNOS_VITALES
HOSP_EPISODIO 1 --- N HOSP_MEDICACION
HOSP_PACIENTE 1 --- N HOSP_ALERGIA
HOSP_PACIENTE 1 --- N HOSP_TURNO
HOSP_PROFESIONAL N --- 1 HOSP_ESPECIALIDAD
HOSP_PROFESIONAL 1 --- N HOSP_EPISODIO  (como responsable)
HOSP_PROFESIONAL 1 --- N HOSP_TURNO
```

## Reglas de integridad y seed

- Edad del paciente al anclaje `DATE '2026-06-25'`: entre 0 y 17 años (FR-010).
- Cada `HOSP_EPISODIO.FECHA_INICIO ≤ FECHA_FIN` cuando `FECHA_FIN IS NOT NULL`.
- Cada `HOSP_EPISODIO.FECHA_INICIO ≥ HOSP_PACIENTE.FECHA_NACIMIENTO`.
- `HOSP_DIAGNOSTICO.FECHA_DIAGNOSTICO` cae dentro del rango del episodio.
- `HOSP_OBSERVACION_LAB.FECHA_TOMA` cae dentro del rango del episodio.
- Cada `HOSP_OBSERVACION_LAB` cumple el check (numérico o texto, no ambos NULL).
- Edge cases obligatorios (FR-023):
  - ≥ 1 paciente sin ningún episodio.
  - ≥ 1 paciente con ≥ 10 episodios.
  - ≥ 1 episodio sin diagnóstico secundario (sólo principal).
  - ≥ 1 observación con `VALOR_TEXTO` no NULL.
  - ≥ 1 turno con `FECHA_HORA > DATE '2026-06-25'`.

## Verificación post-seed (`04-verify.sql`)

Ejecuta una serie de asserts; cualquier fallo emite `RAISE_APPLICATION_ERROR(-20001, ...)` y el contenedor queda no-`healthy`:

```sql
DECLARE
  v_cnt NUMBER;
BEGIN
  SELECT COUNT(*) INTO v_cnt FROM HOSPITAL_MOCK.HOSP_PACIENTE;
  IF v_cnt < 50 THEN RAISE_APPLICATION_ERROR(-20001, 'Pacientes < 50: ' || v_cnt); END IF;

  SELECT COUNT(*) INTO v_cnt FROM HOSPITAL_MOCK.HOSP_EPISODIO;
  IF v_cnt < 150 THEN RAISE_APPLICATION_ERROR(-20002, 'Episodios < 150: ' || v_cnt); END IF;

  SELECT COUNT(*) INTO v_cnt FROM HOSPITAL_MOCK.HOSP_OBSERVACION_LAB;
  IF v_cnt < 500 THEN RAISE_APPLICATION_ERROR(-20003, 'Observaciones < 500: ' || v_cnt); END IF;

  -- edge cases (FR-023) ...
END;
/
```

## Reemplazo cuando llegue el diseño real (referencia rápida)

| Cambio probable del hospital | Archivos a tocar |
|---|---|
| Renombrar tablas/columnas | `infra/mock-hospital/init/01-schema.sql`, `02-views.sql`, `allowlist.yaml`, `PatientService`, `MockHospitalFhirClient` y `MockOracleViewClient` |
| Nuevas vistas autorizadas | `infra/mock-hospital/init/99-grants.sql`, `src/ChequeAR.Infrastructure/Oracle/allowlist.yaml` |
| Nuevas columnas que ChequeAR consume | `PatientService` / `MockHospitalFhirClient` + datos en memoria de `MockOracleViewClient` |
| Cambio de codificación (de CIE-10 a otra) | catálogos en `03-seed.sql` + `MockHospitalFhirClient` |

El borrado del mock ya no afecta a la aplicación: basta con `git rm -r infra/mock-hospital/` y apuntar `Oracle:ConnectionString` al Oracle real.
