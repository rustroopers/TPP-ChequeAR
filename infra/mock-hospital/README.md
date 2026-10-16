# Mock Hospital DB

> ⚠️ **PROVISORIO — A REEMPLAZAR**
> Este módulo es **andamiaje de desarrollo**. Reproduce la cara que el Hospital
> Garrahan expondría a ChequeAR, pero todas las tablas y datos son **sintéticos
> y provisorios**. Cuando el hospital entregue el diseño real de sus tablas
> internas, este directorio entero se reemplaza siguiendo el **Checklist de
> reemplazo** del final de este archivo.

Spec completa: [`specs/008-mock-hospital-db/spec.md`](../../specs/008-mock-hospital-db/spec.md).

---

## Prerrequisitos

- Oracle XE 21c instalado como servicio de Windows, con la PDB `XEPDB1` (la misma
  base que usa ChequeAR en desarrollo).
- `sqlplus` en el PATH y acceso local como SYSDBA (`sqlplus "/ as sysdba"`).

## Cargar

```bash
cd infra/mock-hospital/init
sqlplus -S "/ as sysdba" @cargar.sql
```

`cargar.sql` corre en orden `00-users`, `01-schema`, `02-views`, `03-seed`,
`04-verify` y `99-grants`, y corta en el primer error. Tarda unos segundos.
`04-verify.sql` falla con `ORA-2000x` si el seed no cumple alguna invariante.

Usuarios que crea (contraseñas de desarrollo, las mismas que usan los tests por
omisión):

| Usuario | Contraseña | Rol |
|---|---|---|
| `HOSPITAL_MOCK` | `hospitalmockdev` | Dueño de las tablas `HOSP_*` y las vistas `VW_*` |
| `CHEQUEAR_RO` | `devdev` | Consumidor: sólo `SELECT` sobre las `VW_*` |

Las fechas del seed son relativas al día de la carga, así siempre hay turnos
futuros. Si la base quedó cargada hace meses, conviene recargarla (ver **Reset**).

## Verificar el dataset

```bash
sqlplus -S CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1 <<'SQL'
SELECT COUNT(*) AS pacientes      FROM HOSPITAL_MOCK.VW_PACIENTE;
SELECT COUNT(*) AS episodios      FROM HOSPITAL_MOCK.VW_EPISODIO;
SELECT COUNT(*) AS observaciones  FROM HOSPITAL_MOCK.VW_OBSERVACION_LAB;
SQL
```

Resultados esperados: ≥ 50, ≥ 150, ≥ 500 (SC-006).

## Conectar ChequeAR al mock

La API lee la conexión de `Oracle:ConnectionString` en
`src/ChequeAR.Api/Web.Secrets.config` (no versionado):

```xml
<add key="Oracle:ConnectionString"
     value="User Id=CHEQUEAR_RO;Password=devdev;Data Source=localhost:1521/XEPDB1;" />
```

Con el valor vacío, la API registra `MockOracleViewClient`, que sirve las mismas
vistas desde memoria con datos de demostración. Es lo que corre en Azure, donde no
hay base del hospital.

El bootstrap de DI (`ServiceComposition.cs`) carga la allowlist desde
`src/ChequeAR.Infrastructure/Oracle/allowlist.yaml` y la pasa al cliente.
Cualquier consulta a una vista fuera de la lista es rechazada antes de tocar la base.

## Auditar grants (FR-005..FR-007, CHK021)

Verificar que `CHEQUEAR_RO` sólo tiene `SELECT` sobre las `VW_*` y nada más:

```bash
sqlplus -S "/ as sysdba" <<'SQL'
ALTER SESSION SET CONTAINER = XEPDB1;
SELECT TABLE_NAME, PRIVILEGE
FROM   DBA_TAB_PRIVS
WHERE  GRANTEE = 'CHEQUEAR_RO'
ORDER  BY TABLE_NAME, PRIVILEGE;
SQL
```

Resultado esperado: exactamente 10 filas, todas con `PRIVILEGE = SELECT` y
`TABLE_NAME` ∈ `{VW_PACIENTE, VW_EPISODIO, VW_DIAGNOSTICO, VW_OBSERVACION_LAB,
VW_SIGNOS_VITALES, VW_MEDICACION, VW_ALERGIA, VW_PROFESIONAL, VW_ESPECIALIDAD,
VW_TURNO}`. Cualquier privilegio adicional o grant sobre una tabla `HOSP_*`
es una **regresión** de FR-005/007.

Verificar que las tablas `HOSP_*` son inalcanzables desde `CHEQUEAR_RO`:

```bash
sqlplus -S CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1 <<'SQL'
SELECT COUNT(*) FROM HOSPITAL_MOCK.HOSP_PACIENTE;
SQL
# Esperado: ORA-00942: table or view does not exist
```

## Reset

```bash
cd infra/mock-hospital/init
sqlplus -S "/ as sysdba" @borrar.sql
sqlplus -S "/ as sysdba" @cargar.sql
```

Como el seed es determinístico (`DBMS_RANDOM.SEED(20260625)`), los pacientes,
episodios y resultados son los mismos; sólo se corren las fechas al día de la
nueva carga (SC-002).

## Stakeholder review (US3, SC-005)

Para revisión con clínicos del Garrahan: el script `init/sample-historiales.sql`
genera 5 historiales completos (paciente + alergias + episodios + dx + signos +
laboratorio) con una semilla distinta a la del seed (`20260101`), así la muestra
no privilegia siempre los mismos pacientes.

```bash
cd infra/mock-hospital/init
sqlplus -S CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1 @sample-historiales.sql
```

Procedimiento sugerido para la review:

1. Imprimir o compartir en pantalla la salida.
2. Pedirle al clínico que evalúe **cada uno de los 5 historiales** contra esta
   rúbrica binaria (✓ / ✗):
   - **Nombre+apellido creíbles para un paciente argentino**.
   - **Edad coherente** entre `FECHA_NACIMIENTO` y los episodios.
   - **Diagnóstico CIE-10 razonable** para la edad y la especialidad atendiente.
   - **Observaciones de laboratorio dentro de rangos fisiológicos** para la edad.
   - **Signos vitales coherentes** (peso/talla vs edad, FC/FR vs banda etaria).
3. Anotar el resultado en una minuta de stakeholder review.
4. **Criterio de aceptación SC-005**: al menos 4 de los 5 historiales deben
   tener todos los ítems ✓. Si quedan <4, ajustar el seed (T028–T030) y
   re-correr la review.

Cualquier dato que el clínico marque como implausible que requiera cambio se
levanta como issue contra `infra/mock-hospital/init/03-seed.sql` o contra los
catálogos embebidos (CIE-10, LOINC).

## Tests de integración

```bash
cd tests/integration/MockHospital.Tests
dotnet test --filter "Requires=Oracle"
```

Los tests marcados `Requires=Oracle` necesitan una base con el esquema del mock y
**no corren en CI**: los runners no tienen Oracle. En CI sólo corren los que no
necesitan base (`--filter "Requires!=Oracle"`). Si la base no está, los tests
fallan con un mensaje que dice cómo cargarla.

Para apuntar a otra base sin tocar código:

| Variable de entorno | Valor por defecto |
|---|---|
| `MOCK_HOSPITAL_CONN` | `User Id=CHEQUEAR_RO;Password=devdev;Data Source=localhost:1521/XEPDB1;` |
| `MOCK_HOSPITAL_OWNER_CONN` | `User Id=HOSPITAL_MOCK;Password=hospitalmockdev;Data Source=localhost:1521/XEPDB1;` |

(SC-008)

## Comandos útiles

| Acción | Comando |
|---|---|
| Conexión como app (RO) | `sqlplus CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1` |
| Conexión como owner | `sqlplus HOSPITAL_MOCK/hospitalmockdev@//localhost:1521/XEPDB1` |
| Volver a correr sólo la verificación | `sqlplus -S "/ as sysdba" @04-verify.sql` |

## Troubleshooting

- **`ORA-01920: user name conflicts`** al cargar: el mock ya estaba. Correr
  `borrar.sql` antes de `cargar.sql`.
- **`ORA-2000x`** en `04-verify.sql`: el seed no cumplió una invariante; el mensaje
  dice cuál.
- **`ORA-12541: no listener`**: el servicio `OracleOraDB21Home1TNSListener` está
  parado (`net start OracleOraDB21Home1TNSListener` como administrador).
- **Falla `ExisteAlMenosUnTurnoFuturo`**: la carga es vieja y los turnos ya
  pasaron. Recargar.

---

## Checklist de reemplazo (FR-021, SC-007)

Cuando el Hospital Garrahan entregue el diseño real de sus tablas/vistas:

1. **Borrar `init/01-schema.sql`** — el DDL real lo provee el hospital.
2. **Borrar `init/03-seed.sql` y `init/04-verify.sql`** — los datos reales no
   se siembran desde acá; vienen del hospital. Si el equipo quiere mantener
   un dataset de testing reducido, mover el seed a `tests/integration/MockHospital.Tests/`
   bajo otro schema.
3. **Reemplazar `init/02-views.sql`** por las vistas reales que el hospital
   exponga. Mantener los nombres en `VW_*` o ajustar la allowlist en el
   siguiente paso.
4. **Actualizar `src/ChequeAR.Infrastructure/Oracle/allowlist.yaml`** con los
   nombres reales de las vistas autorizadas.
5. **Actualizar `init/99-grants.sql`** con los grants exactos del hospital
   (esto puede no necesitarse si el hospital provee directamente el usuario
   read-only con permisos pre-configurados).
6. **Crear `src/ChequeAR.Infrastructure/OracleMappings/*.yaml`** con los
   nombres reales de columnas (hoy la carpeta no existe y
   `MappingCompatibilityTests.cs` no valida nada).
7. **Re-baselinear `tests/integration/MockHospital.Tests/DeterminismTests.cs`**
   si quedaron hashes anclados.
8. **Borrar este directorio entero** y apuntar `Oracle:ConnectionString` de
   `Web.Secrets.config` (o de las App Settings de Azure) al Oracle real del hospital.
9. **Actualizar el banner** "PROVISORIO" en cualquier README que aún
   referencie el mock.

Tiempo esperado para 1–8: **< 30 min** de lectura+edición (SC-007).
