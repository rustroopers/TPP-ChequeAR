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

- Docker Engine ≥ 20.10 y Docker Compose v2 (`docker compose version`).
- Puerto local `1521` libre.
- ≥ 4 GB de RAM asignados a Docker (≥ 6 GB en Apple Silicon).

## Quickstart

```bash
cd infra/mock-hospital
cp .env.example .env          # opcional; los defaults sirven para dev local
docker compose up -d --wait
```

`--wait` bloquea hasta que el contenedor reporte `healthy`. El healthcheck sólo
pasa cuando `HOSP_PACIENTE` tiene ≥ 50 filas (es decir, el seed terminó).

Tiempo esperado: **< 3 min en amd64**, **< 8 min en Apple Silicon** (todo dentro
del SC-001 ≤ 10 min).

## Verificar el dataset

```bash
docker compose exec mock-hospital sqlplus -S CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1 <<'SQL'
SELECT COUNT(*) AS pacientes      FROM HOSPITAL_MOCK.VW_PACIENTE;
SELECT COUNT(*) AS episodios      FROM HOSPITAL_MOCK.VW_EPISODIO;
SELECT COUNT(*) AS observaciones  FROM HOSPITAL_MOCK.VW_OBSERVACION_LAB;
SQL
```

Resultados esperados: ≥ 50, ≥ 150, ≥ 500 (SC-006).

## Conectar ChequeAR al mock

`src/ChequeAR.Api/Web.config` ya viene apuntando al mock con la connection string:

```
User Id=CHEQUEAR_RO;Password=devdev;Data Source=localhost:1521/XEPDB1;
```

El bootstrap de DI (`ServiceComposition.cs`) carga la allowlist desde
`src/ChequeAR.Infrastructure/Oracle/allowlist.yaml` y la pasa al `OracleViewClient`.
Cualquier consulta a una vista fuera de la lista es rechazada antes de tocar la base.

## Auditar grants (FR-005..FR-007, CHK021)

Verificar que `CHEQUEAR_RO` sólo tiene `SELECT` sobre las `VW_*` y nada más:

```bash
docker compose exec mock-hospital sqlplus -S system/oraclesys@//localhost:1521/XEPDB1 <<'SQL'
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
docker compose exec mock-hospital sqlplus -S CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1 <<'SQL'
SELECT COUNT(*) FROM HOSPITAL_MOCK.HOSP_PACIENTE;
SQL
# Esperado: ORA-00942: table or view does not exist
```

## Reset

```bash
docker compose down -v   # borra el volumen → próximo `up` re-sembra
docker compose up -d --wait
```

Como el seed es determinístico (`DBMS_RANDOM.SEED(20260625)`), el dataset
resultante es idéntico al anterior (SC-002).

## Stakeholder review (US3, SC-005)

Para revisión con clínicos del Garrahan: el script `init/sample-historiales.sql`
genera 5 historiales completos (paciente + alergias + episodios + dx + signos +
laboratorio) con una semilla distinta a la del seed (`20260101`), así la muestra
no privilegia siempre los mismos pacientes.

```bash
docker compose exec mock-hospital sqlplus -S CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1 \
  @/container-entrypoint-initdb.d/sample-historiales.sql
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
dotnet test
```

La suite asume que el contenedor está corriendo y `healthy`. Si no, los tests
fallan con un mensaje claro indicando cómo levantarlo.

### Wiring de la suite del módulo 003

Cuando el módulo 003 cree su suite `tests/integration/Oracle.Tests/` (todavía
no existe en el repo), debe respetar la siguiente convención para apuntarse
al mock sin cambios de código:

| Variable de entorno | Valor por defecto |
|---|---|
| `MOCK_HOSPITAL_CONN` | `User Id=CHEQUEAR_RO;Password=devdev;Data Source=localhost:1521/XEPDB1;` |
| `MOCK_HOSPITAL_OWNER_CONN` | `User Id=HOSPITAL_MOCK;Password=hospitalmockdev;Data Source=localhost:1521/XEPDB1;` |

Cualquier test que necesite acceso al lado hospital lee esa variable. Esto
cierra SC-008.

## Comandos útiles

| Acción | Comando |
|---|---|
| Logs del seed | `docker compose logs -f mock-hospital` |
| Conexión como app (RO) | `docker compose exec mock-hospital sqlplus CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1` |
| Conexión como owner | `docker compose exec mock-hospital sqlplus HOSPITAL_MOCK/hospitalmockdev@//localhost:1521/XEPDB1` |
| Parar sin perder datos | `docker compose stop` |
| Ver salud | `docker compose ps` |

## Troubleshooting

- **`unhealthy` después de 10 min**: revisar `docker compose logs mock-hospital`.
  Si aparece `ORA-2000x`, el seed falló alguna invariante de `04-verify.sql`.
  Hacer `down -v` y volver a levantar.
- **Apple Silicon muy lento**: confirmar que el compose declara
  `platform: linux/amd64`. Subir la memoria asignada a Docker Desktop a ≥ 6 GB.
- **Puerto 1521 ocupado**: editar `ports:` en `docker-compose.yml` y reflejarlo
  en el `Web.config` de la API.
- **Seed no se ejecutó**: si arrancaste el contenedor antes y los archivos de
  init cambiaron, los scripts no se re-ejecutan (es comportamiento de la
  imagen `gvenzl/oracle-xe`). Hacer `down -v` para borrar el volumen y forzar
  re-inicialización.

## Smoke manual cronometrado (SC-001)

Para evidenciar el SC-001 ("< 10 min de setup en máquina razonable") en un
commit/PR concreto:

1. En una máquina limpia (o tras `git clean -fdx` + `docker system prune -a`),
   clonar el repo desde cero.
2. Cronometrar:
   ```bash
   cd infra/mock-hospital
   cp .env.example .env
   time docker compose up -d --wait
   ```
3. Anotar el tiempo `real` reportado por `time`. Debe ser `<10m0.000s`.
4. Registrar el resultado en el cuerpo del commit/PR como:
   ```
   smoke SC-001: <tiempo> en <CPU, RAM, OS, plataforma docker>
   ```
   Ejemplo: `smoke SC-001: 2m44s en MBP M2 16GB, Docker Desktop 4.30 (Apple Silicon, QEMU)`.

Si el tiempo supera 10 min, abrir un issue con `[SC-001]` y revisar
healthcheck (`docker compose logs`), tamaño del seed o RAM asignada a Docker
antes de mergear.

## Apple Silicon (M1/M2/M3)

Oracle XE no tiene imagen ARM64 oficial. Docker Desktop usa emulación
QEMU/Rosetta automáticamente porque el compose declara `platform: linux/amd64`.
El primer arranque pasa de ~30 s (en amd64) a ~2 min. Funcional, pero asignar
≥ 6 GB de RAM a Docker para evitar OOM durante el seed.

---

## Checklist de reemplazo (FR-021, SC-007)

Cuando el Hospital Garrahan entregue el diseño real de sus tablas/vistas:

1. **Borrar `init/01-schema.sql`** — el DDL real lo provee el hospital.
2. **Borrar `init/03-seed.sql` y `init/04-verify.sql`** — los datos reales no
   se siembran desde acá; vienen del hospital. Si el equipo quiere mantener
   un dataset de testing reducido, mover el seed a `tests/integration/Oracle.Tests/`
   bajo otro schema.
3. **Reemplazar `init/02-views.sql`** por las vistas reales que el hospital
   exponga. Mantener los nombres en `VW_*` o ajustar la allowlist en el
   siguiente paso.
4. **Actualizar `src/ChequeAR.Infrastructure/Oracle/allowlist.yaml`** con los
   nombres reales de las vistas autorizadas.
5. **Actualizar `init/99-grants.sql`** con los grants exactos del hospital
   (esto puede no necesitarse si el hospital provee directamente el usuario
   read-only con permisos pre-configurados).
6. **Ajustar `src/ChequeAR.Infrastructure/OracleMappings/*.yaml`** con los
   nombres reales de columnas. Cuando los YAML existan, los tests
   `MappingCompatibilityTests.cs` empezarán a validar automáticamente.
7. **Re-baselinear `tests/integration/MockHospital.Tests/DeterminismTests.cs`**
   si quedaron hashes anclados.
8. **Borrar este directorio entero** y apuntar la connection string de
   `Web.config` al servidor Oracle real del hospital.
9. **Actualizar el banner** "PROVISORIO" en cualquier README que aún
   referencie el mock.

Tiempo esperado para 1–8: **< 30 min** de lectura+edición (SC-007).
