# Research: Mock de la base de datos del lado Hospital

## R1 — Imagen Oracle XE para desarrollo local

**Decisión**: `gvenzl/oracle-xe:21-slim-faststart` (Oracle XE 21c, variante slim con arranque rápido).

**Rationale**:
- Mantenida activamente; es la imagen comunitaria recomendada por la propia documentación de Oracle XE.
- Soporta `/container-entrypoint-initdb.d/*.sql` y `*.sh` ejecutados en orden alfabético al primer arranque — encaja exacto con la decisión Q3 (seed por scripts SQL).
- Variante `faststart` reduce el arranque inicial de ~6 min a ~30 s al precargar el datafile.
- Compatible con el driver `Oracle.ManagedDataAccess` ya en uso (probado por la comunidad y por la propia spec 003).
- Permite definir `APP_USER` / `APP_USER_PASSWORD` por env vars, lo que da el usuario `CHEQUEAR_RO` desde el primer arranque sin script extra.

**Alternativas consideradas**:
- `container-registry.oracle.com/database/express:21.3.0-xe`: oficial pero requiere SSO de Oracle y licencia aceptada manualmente; rompe el quickstart de un solo comando.
- Oracle XE 18c: estable pero anterior al 21c; usar 21c reduce el delta con la versión que típicamente corren los hospitales hoy.

**Conexión esperada**:
```
Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))
            (CONNECT_DATA=(SERVICE_NAME=XEPDB1)));User Id=CHEQUEAR_RO;Password=devdev;
```

## R2 — Estrategia de determinismo del seed

**Decisión**: Inicializar el RNG de Oracle con `DBMS_RANDOM.SEED(20260625)` al inicio de `03-seed.sql`, luego derivar **todos** los valores no-fijos a partir de `DBMS_RANDOM.VALUE` / `DBMS_RANDOM.STRING`. Los identificadores primarios (`PAC-NNNNNN`) se generan con un contador determinístico, no con `SYS_GUID()`.

**Rationale**:
- `DBMS_RANDOM.SEED` con valor entero es totalmente reproducible en Oracle 21c.
- Evita cualquier dependencia de `SYSTIMESTAMP`, `SYS_GUID()` o secuencias auto-iniciadas en momentos arbitrarios.
- Las fechas relativas (p. ej. "episodio hace X días") se calculan contra una fecha de anclaje fija (`DATE '2026-06-25'`), no contra `SYSDATE`. Eso permite ejecutar el seed mañana y obtener el mismo resultado.
- Para verificar reproducibilidad (SC-002), basta correr `04-verify.sql` que produce hashes (`STANDARD_HASH(...)`) por tabla y compararlos.

**Alternativas consideradas**:
- Seed generado por una herramienta externa (Faker en Python, Bogus en .NET): rompe la decisión Q3 (todo en SQL) y agrega dependencias al quickstart.
- Inserts hardcoded uno por uno (sin RNG): explota cuando se quieren ~500 observaciones; el archivo SQL sería inmantenible.

## R3 — Catálogos clínicos para el seed

**Decisión**: Cargar tablas-catálogo pequeñas dentro del propio `03-seed.sql` con subsets curados:
- **CIE-10 pediátricos**: ~25 códigos frecuentes (J06.9 infección vía resp. sup., A09 diarrea/gastroenteritis, J45.9 asma, K21.9 ERGE, R50.9 fiebre, etc.).
- **LOINC**: ~15 códigos de laboratorio comunes (hemoglobina 718-7, hematocrito 4544-3, glucosa 2345-7, sodio 2951-2, potasio 2823-3, creatinina 2160-0, etc.).
- **Medicamentos**: ~20 con descripción + presentación típica pediátrica.
- **Alergenos**: ~10 (penicilina, aines, frutos secos, lácteos, polen, etc.).

**Rationale**:
- Suficiente diversidad para que stakeholders clínicos reconozcan los datos como creíbles (SC-005) sin explotar el SQL.
- Los códigos son públicos y se pueden copiar a un cambio futuro sin licencia.
- Los `display` están en español rioplatense (es-AR), no en inglés (FR-014).

**Alternativas consideradas**:
- Importar catálogos completos (CIE-10 tiene ~14k códigos, LOINC ~95k): innecesario para el mock; agranda la imagen sin valor.
- Generar `display` aleatorios: rompe SC-005 (los stakeholders detectarían sandbox a la vista).

## R4 — Distribución de episodios y observaciones por paciente

**Decisión**: Distribución sesgada (Zipf truncada):
- 1 paciente sin episodios (FR-023).
- 1 paciente con 10–15 episodios (paciente crónico, FR-023).
- ~30% de los pacientes con 1 episodio único.
- ~50% con 2–4 episodios.
- ~20% con 5–8 episodios.

Cada episodio produce 2–6 observaciones de laboratorio, 1 set de signos vitales y 0–3 medicaciones. Esto da ≥150 episodios y ≥500 observaciones cumpliendo FR-011, FR-012 sin variaciones bruscas entre corridas.

**Rationale**:
- Distribución realista (los hospitales atienden mayoría de casos puntuales y minoría de crónicos).
- Permite ejercitar paginación en módulos consumidores (SC-008 indirecto).
- El RNG determinístico garantiza que la asignación paciente→cantidad de episodios es estable.

**Alternativas consideradas**:
- Uniforme (3 episodios por paciente): irreal y oculta bugs de paginación.
- Generación libre por Faker: rompe Q3.

## R5 — Verificación de compatibilidad con `OracleMappings/` (spec 003)

**Decisión**: Antes de fijar los nombres de columnas de las `VW_*`, leer los YAML existentes (`patient.yaml`, `condition.yaml`, `observation.yaml`) y derivar las columnas mínimas que cada vista debe exponer. Documentar el delta en `contracts/view-schema.md`.

**Rationale**:
- Garantiza SC-004 (mapping consume el mock sin tocar el YAML) por construcción, no por suerte.
- Si el spec 003 cambiara, el delta se vuelve a calcular; el mock no necesita "saber" sobre FHIR.

**Alternativas consideradas**:
- Asumir nombres y arreglar al primer test fallido: convierte el desarrollo en un loop reactivo; ya hay info suficiente para hacerlo bien la primera vez.

## R6 — Healthcheck del compose

**Decisión**: Healthcheck propio que ejecuta `SELECT COUNT(*) FROM HOSPITAL_MOCK.HOSP_PACIENTE` y exige `≥ 50`. Marca el servicio como `healthy` sólo cuando el seed terminó.

**Rationale**:
- El healthcheck nativo del contenedor `gvenzl/oracle-xe` se vuelve `healthy` apenas el listener responde, lo que ocurre **antes** de que el entrypoint corra los scripts. Eso engaña a los tests CI.
- Agregar un check de conteo desplaza la marca `healthy` al fin real del seeding y hace que `docker compose up --wait` sea suficiente para el quickstart.

**Alternativas consideradas**:
- Polling desde el lado tests: empuja la complejidad al consumidor y rompe el "un solo comando".

## R7 — Aislamiento de schema y grants mínimos

**Decisión**: Dos usuarios Oracle:
- `HOSPITAL_MOCK` (owner, password no documentada, sólo usada por los scripts de init).
- `CHEQUEAR_RO` (consumido por `OracleViewClient`). Recibe **únicamente** `GRANT SELECT ON HOSPITAL_MOCK.VW_X TO CHEQUEAR_RO` por cada vista listada en la allowlist. Cero grants sobre `HOSP_*`.

**Rationale**:
- Hace cumplir FR-005 a nivel motor: aun si la app intenta `SELECT * FROM HOSPITAL_MOCK.HOSP_PACIENTE`, Oracle responde `ORA-00942: table or view does not exist`.
- Tests de integración pueden verificar el grant con `DBA_TAB_PRIVS` (test reproducible).

**Alternativas consideradas**:
- Un solo usuario con permisos amplios: pierde la segunda línea de defensa decidida en Q4.

## R8 — Compatibilidad amd64 / Apple Silicon

**Decisión**: El compose declara `platform: linux/amd64`. En hosts ARM, Docker Desktop usa Rosetta/QEMU; el arranque sube de ~30 s a ~2 min pero sigue dentro del SC-001 (10 min).

**Rationale**:
- Oracle XE no tiene imagen oficial para ARM64.
- La pérdida de tiempo (en arranque, no en queries) es aceptable porque el seed corre una vez por sesión de dev.

**Alternativas consideradas**:
- Compilar Oracle desde fuente para ARM: no se permite por licencia y es absurdo para un mock.
- Mover a un motor distinto en ARM: rompe la decisión Q1.

## Resoluciones de NEEDS CLARIFICATION

Ninguna pendiente. Las cinco clarifications del spec (Q1–Q5) cubren los puntos que originalmente quedaban abiertos. R1–R8 anclan los detalles que un planificador necesita para que tasks.md no quede vago.
