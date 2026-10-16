# Feature Specification: Mock de la base de datos del lado Hospital (provisorio)

**Feature Branch**: `008-mock-hospital-db`

**Created**: 2026-06-25

**Status**: Implementada; sobre Oracle local, sin Docker

## Clarifications

### Session 2026-06-25

- Q: ¿Qué motor de base de datos se usa para el mock? → A: Oracle XE 21c en Docker (imagen `gvenzl/oracle-xe`), para preservar paridad 1:1 con el driver y dialecto que el Hospital Garrahan usa en producción.
- Q: ¿Qué formato tiene el identificador hospitalario de paciente? → A: `PAC-NNNNNN` (prefijo `PAC-` + 6 dígitos con padding de ceros, p. ej. `PAC-000042`).
- Q: ¿Cómo se carga el seed? → A: Scripts SQL versionados (`01-schema.sql`, `02-views.sql`, `03-seed.sql`) montados como volumen y ejecutados por el entrypoint del contenedor Oracle XE al primer arranque; no se introduce un proyecto seeder en .NET.
- Q: ¿Cómo se aísla el esquema del lado hospital del esquema de ChequeAR? → A: Schema/usuario Oracle dedicado `HOSPITAL_MOCK` separado del schema operativo de ChequeAR; las tablas `HOSP_*` viven en ese schema y sólo se otorgan grants `SELECT` sobre las `VW_*` al usuario que usa `OracleViewClient`. Esto espeja la arquitectura real del hospital y refuerza la imposibilidad de alcanzar las tablas internas.
- Q: ¿Cómo se orquesta el levantamiento del mock? → A: Un único `docker-compose.yml` en `infra/mock-hospital/` con el servicio Oracle XE, los volúmenes de scripts SQL y un healthcheck que se vuelve `healthy` recién cuando el seed terminó. La receta de FR-016 queda como `docker compose up` desde esa carpeta.

**Input**: User description: "Crear un mock de la base de datos del lado hospital (Garrahan) para que ChequeAR pueda desarrollarse e interactuar end-to-end sin acceso al sistema real. Modelar tablas y vistas (HOSP_* + VW_*) que representen lo mínimo que un hospital de alta complejidad almacena: Paciente, Episodio/Internación, Diagnóstico (CIE-10), Observación/Laboratorio (LOINC), Signos vitales, Medicación, Alergias, Profesional, Especialidad, Turno. Incluir seed determinístico con ~50 pacientes pediátricos (0-17 años, datos en es-AR), ~150 episodios, ~500 observaciones. Las vistas VW_* son las únicas expuestas al OracleViewClient (read-only, allowlist). Esta base y sus tablas son PROVISORIAS — se reemplazarán cuando el hospital envíe el diseño real de sus tablas internas. Debe ser compatible con el contrato de IOracleViewClient ya existente en specs/003-interoperabilidad-fhir y con los YAML de OracleMappings/."

> **Nota de scope**: Esta feature es una pieza de **andamiaje de desarrollo**, no una funcionalidad de usuario final. Los "usuarios" referidos en este spec son **desarrolladores, QAs y stakeholders demo** del equipo ChequeAR. La base modelada es **provisoria y a reemplazar** apenas el Hospital Garrahan entregue el diseño autoritativo de sus tablas/vistas internas.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Levantar un dataset hospitalario reproducible para desarrollo local (Priority: P1)

Cualquier integrante del equipo (dev o QA) puede levantar localmente un dataset completo que simule la información que el Hospital Garrahan expondría a ChequeAR. Una vez levantado, el resto de los módulos (cuestionarios, motor de renderizado, interoperabilidad, reportes) pueden consumir datos realistas de pacientes pediátricos sin depender de conectividad ni credenciales del hospital real.

**Why this priority**: Sin este dataset, los módulos 001–005 no pueden ejercitarse end-to-end. Es prerrequisito para cualquier demo, test de integración o validación de mapping FHIR. Hoy el equipo carece de un sustituto del lado hospital y eso bloquea el progreso de las specs que dependen de datos clínicos previos (precarga, exportación, lookups por paciente).

**Independent Test**: Un dev clona el repo en una máquina limpia, ejecuta una receta documentada y, en menos de 10 minutos, obtiene una base operativa con datos consultables que cumple un conjunto de invariantes verificables (p. ej. "existen al menos 50 pacientes con edad entre 0 y 17 años").

**Acceptance Scenarios**:

1. **Given** un entorno de desarrollo recién inicializado sin datos previos, **When** el dev sigue el quickstart del módulo, **Then** la base queda lista con el dataset semillado completo y la receta finaliza sin errores.
2. **Given** la base semillada, **When** un dev la borra y vuelve a sembrar usando la misma semilla, **Then** los identificadores, conteos y valores resultantes son idénticos a la corrida previa (reproducibilidad determinística).
3. **Given** la base semillada, **When** se cuentan filas por entidad, **Then** los conteos satisfacen los rangos comprometidos (≥50 pacientes, ≥150 episodios, ≥500 observaciones).

---

### User Story 2 - Consumir el mock desde ChequeAR respetando el contrato de acceso restringido (Priority: P1)

ChequeAR consulta el lado hospital exclusivamente a través de un cliente que aplica una lista blanca de vistas autorizadas. El mock debe respetar ese contrato: las tablas internas (`HOSP_*`) no son alcanzables directamente; sólo las vistas (`VW_*`) registradas en la allowlist responden. Esto permite que el resto del sistema desarrollado contra el mock sirva 1:1 cuando se enchufe al hospital real.

**Why this priority**: Si el mock permite saltar la allowlist, el código que se escriba encima se construirá con supuestos inválidos que romperán al migrar al hospital real. La paridad de contrato es lo que justifica usar un mock y no datos hardcodeados.

**Independent Test**: Ejecutar una consulta dirigida a una tabla `HOSP_*` directa devuelve un error de "vista no autorizada"; la misma consulta dirigida a la vista `VW_*` equivalente devuelve filas. Las pruebas de integración del módulo 003 que ya existen pasan sin modificarse contra el mock.

**Acceptance Scenarios**:

1. **Given** la base mock en línea, **When** se consulta una vista presente en la allowlist (p. ej. `VW_PACIENTE`) con filtros por columna válida, **Then** se reciben filas consistentes con el seed.
2. **Given** la base mock en línea, **When** se intenta consultar una tabla interna `HOSP_*` o una vista no listada, **Then** el cliente rechaza la consulta antes de tocar la base.
3. **Given** los YAML de mapping ya existentes, **When** se invoca el mapping Oracle→FHIR sobre filas del mock, **Then** los recursos FHIR generados validan estructuralmente sin cambios al YAML.

---

### User Story 3 - Demostrar flujos clínicos completos con datos realistas para stakeholder reviews (Priority: P2)

En las stakeholder reviews semanales con el Hospital Garrahan, el equipo necesita mostrar flujos completos: abrir el historial de un paciente pediátrico, ver sus diagnósticos previos, observaciones de laboratorio, signos vitales, alergias y medicación; cargar una ficha de cuestionario; y exportar un reporte. Los datos sembrados deben ser plausibles para que los profesionales clínicos los reconozcan como realistas y no como "datos de juguete".

**Why this priority**: Stakeholder reviews dependen de que los datos sean creíbles; nombres genéricos o rangos imposibles erosionan la confianza en el sistema. Es P2 porque el módulo puede entregar valor técnico (P1) aun con datos menos pulidos, pero el valor de demostración requiere este nivel de cuidado.

**Independent Test**: Un stakeholder clínico revisa 5 historiales aleatorios del mock y confirma que: nombres y apellidos son creíbles para Argentina, edades son pediátricas, los diagnósticos CIE-10 son frecuentes en pediatría, las observaciones de laboratorio caen en rangos fisiológicamente posibles para la edad y los signos vitales son consistentes.

**Acceptance Scenarios**:

1. **Given** el dataset semillado, **When** se inspecciona el historial de cualquier paciente, **Then** todos los datos son internamente coherentes (fecha de nacimiento consistente con la edad del episodio, diagnósticos coherentes con la especialidad atendida, observaciones dentro de rangos plausibles para la edad).
2. **Given** un episodio cualquiera, **When** se navega a sus diagnósticos, observaciones, signos vitales y medicaciones, **Then** todos referencian al mismo paciente y caen dentro del período del episodio.
3. **Given** una ficha cargada que deriva una observación, **When** la observación derivada se inserta del lado ChequeAR, **Then** convive con las observaciones del hospital sin colisión de identificadores ni ambigüedad de fuente.

---

### User Story 4 - Reemplazar el mock por el diseño real del hospital sin reescritura masiva (Priority: P2)

Cuando el Hospital Garrahan entregue el diseño real de sus tablas/vistas, el equipo necesita poder reemplazar el mock con cambios localizados, sin tener que reescribir los módulos que lo consumen. La spec, el mapping YAML y los puntos de integración deben dejar un "checklist de reemplazo" explícito.

**Why this priority**: Es la razón de ser del mock; si reemplazarlo cuesta tanto como rehacer el sistema, el mock perdió su propósito.

**Independent Test**: Un dev recibe un diseño hipotético del hospital y, siguiendo el checklist de reemplazo, identifica en menos de 30 minutos exactamente qué archivos de mapping, qué entradas de allowlist y qué fixtures de test deben actualizarse, sin tocar los servicios de aplicación ni los controladores.

**Acceptance Scenarios**:

1. **Given** el mock vigente, **When** se consulta la documentación de la feature, **Then** existe un listado explícito de "qué reemplazar cuando llegue el diseño real" con archivos, vistas y mapeos enumerados.
2. **Given** una vista del hospital real con columnas renombradas, **When** se actualiza únicamente el mapping YAML y la entrada de allowlist, **Then** el resto del sistema sigue compilando y los tests de integración relevantes siguen pasando.

---

### Edge Cases

- **Datos faltantes plausibles**: el mock debe incluir pacientes con alergias desconocidas, episodios sin diagnóstico secundario y observaciones con unidades alternativas, para no ocultar bugs que sólo aparecen con datos incompletos reales.
- **Identificadores con formato hospitalario**: los IDs de paciente deben seguir un formato que se parezca al de un sistema hospitalario (no GUIDs); si el equipo asume GUIDs y el hospital usa códigos cortos, se introducen bugs al integrar.
- **Pacientes sin ningún episodio** (recién admitidos administrativamente): el mock debe incluir al menos uno para detectar manejos defectuosos de "historial vacío".
- **Pacientes con muchos episodios** (pacientes crónicos): el mock debe incluir al menos uno con ≥10 episodios para detectar paginación o performance pobres.
- **Observaciones con valor textual además de numérico** (p. ej. "no detectable"): el mock las incluye porque LOINC admite ambos.
- **Fechas en el futuro inmediato**: turnos programados deben existir; el sistema no debe asumir que toda fecha está en el pasado.
- **Vistas vacías legítimas**: una consulta legítima a una vista válida con filtros que no matchean nada debe devolver lista vacía, no error.
- **Consulta a vista no autorizada**: debe ser rechazada antes de tocar la base, con un error claro de autorización (no un error de SQL).
- **Re-seed sobre base ya cargada**: la receta debe dejar la base en estado conocido, sea limpiando primero o detectando idempotencia; nunca dejarla en estado mixto.

## Requirements *(mandatory)*

### Functional Requirements

**Modelado de entidades hospitalarias**

- **FR-001**: El mock DEBE modelar las siguientes entidades del lado hospital: Paciente, Episodio (ambulatorio/internación/urgencia), Diagnóstico, Observación de laboratorio, Signos vitales, Medicación, Alergia, Profesional, Especialidad y Turno.
- **FR-002**: Cada entidad DEBE incluir, como mínimo, los atributos que un hospital de alta complejidad registra para esa entidad en su práctica diaria (identificador, vínculos a paciente/episodio cuando corresponda, fecha relevante, codificación clínica estándar cuando aplique).
- **FR-003**: Los diagnósticos DEBEN estar codificados con CIE-10 y las observaciones de laboratorio con LOINC, ambos con su `display` legible.
- **FR-004**: Las entidades DEBEN relacionarse de forma referencialmente íntegra: episodios pertenecen a un paciente, diagnósticos a un episodio, etc.

**Contrato de acceso (paridad con producción)**

- **FR-005**: El mock DEBE exponer al consumidor externo (ChequeAR) **únicamente** vistas con prefijo `VW_`; las tablas internas `HOSP_*` no DEBEN ser alcanzables a través del cliente de acceso. Las `HOSP_*` viven en el schema `HOSPITAL_MOCK` y el usuario que utiliza `OracleViewClient` (p. ej. `CHEQUEAR_RO`) sólo recibe grants `SELECT` sobre las `VW_*`, nunca sobre las tablas.
- **FR-006**: El conjunto de vistas alcanzables DEBE estar declarado como una lista blanca (allowlist) en configuración versionada del lado ChequeAR; cualquier vista fuera de la lista DEBE ser rechazada antes de ejecutar la consulta. La allowlist es la primera línea de defensa; los grants Oracle son la segunda.
- **FR-007**: El acceso a las vistas DEBE ser estrictamente de sólo lectura; el usuario `CHEQUEAR_RO` no DEBE tener privilegios de `INSERT`, `UPDATE`, `DELETE`, ni `DDL` sobre el schema `HOSPITAL_MOCK`.
- **FR-008**: Las filas devueltas por las vistas DEBEN ser consumibles por los mapeos Oracle→FHIR existentes sin requerir modificaciones al contrato de columnas asumido por dichos mapeos.

**Seed determinístico y volúmenes**

- **FR-009**: El proceso de carga inicial DEBE ser determinístico: la misma semilla produce los mismos identificadores, fechas y valores en ejecuciones repetidas.
- **FR-010**: El dataset semillado DEBE contener al menos 50 pacientes con edad entre 0 y 17 años al momento del seeding.
- **FR-011**: El dataset semillado DEBE contener al menos 150 episodios, distribuidos entre los pacientes de forma desigual (algunos pacientes con un único episodio, otros con muchos).
- **FR-012**: El dataset semillado DEBE contener al menos 500 observaciones de laboratorio, vinculadas a episodios existentes y con valores dentro de rangos fisiológicamente plausibles para la edad pediátrica.
- **FR-013**: El dataset DEBE incluir además: diagnósticos pediátricos frecuentes, signos vitales por episodio, medicaciones con código y posología, alergias declaradas (incluyendo casos "desconocidas"), profesionales asignados a especialidades, especialidades pediátricas representativas y turnos programados (algunos en pasado, algunos en futuro inmediato).
- **FR-014**: Los datos textuales (nombres, apellidos, descripciones) DEBEN estar en español rioplatense (es-AR) y ser plausibles para población argentina.
- **FR-015**: Los identificadores de paciente DEBEN seguir el formato `PAC-NNNNNN` (prefijo `PAC-` + 6 dígitos con padding de ceros, p. ej. `PAC-000042`), ser estables entre corridas y permitir hasta 999.999 pacientes distintos.

**Reproducibilidad y operación**

- **FR-016**: El módulo DEBE proveer una receta documentada de un único comando (`docker compose up` desde `infra/mock-hospital/`) para levantar el dataset desde cero en una máquina de desarrollo. El servicio sólo debe reportarse como `healthy` cuando los scripts de seed terminaron de ejecutarse.
- **FR-017**: La receta DEBE incluir un mecanismo de reinicio (`docker compose down -v && docker compose up`) que limpie el volumen y vuelva a sembrar sin intervención manual.
- **FR-018**: El módulo DEBE proveer una verificación post-seed que confirme los conteos mínimos y la integridad referencial; un fallo de verificación DEBE marcar la base como no apta.
- **FR-019**: Levantar y semillar la base desde cero DEBE completarse en menos de 10 minutos en una máquina de desarrollo razonable.

**Provisionalidad y reemplazo**

- **FR-020**: La documentación del módulo DEBE indicar explícitamente que el diseño es **provisorio** y será reemplazado por el diseño real entregado por el Hospital Garrahan.
- **FR-021**: La documentación DEBE incluir un "checklist de reemplazo" enumerando los archivos, mapeos y entradas de configuración que cambiarán cuando llegue el diseño real.
- **FR-022**: Los artefactos del mock DEBEN concentrarse en una única carpeta (`infra/mock-hospital/`) que contenga el `docker-compose.yml`, los scripts SQL (`01-schema.sql`, `02-views.sql`, `03-seed.sql`), los grants y el README con el checklist de reemplazo, de modo que un futuro reemplazo no requiera buscar referencias dispersas a lo largo del repo.

**Edge cases requeridos en el seed**

- **FR-023**: El seed DEBE incluir al menos 1 paciente sin episodios, 1 paciente con ≥10 episodios, 1 episodio sin diagnóstico secundario, 1 observación con valor textual y 1 turno programado en el futuro.

### Key Entities *(include if feature involves data)*

- **Paciente**: persona pediátrica atendida en el hospital; atributos clave incluyen identificador hospitalario, documento, nombre, apellido, fecha de nacimiento, sexo y cobertura. Es la entidad raíz para casi todo el historial clínico.
- **Episodio**: contacto del paciente con el hospital (ambulatorio, internación o urgencia); atributos clave incluyen fechas de inicio/fin, tipo, servicio o especialidad atendiente, y profesional responsable. Agrupa diagnósticos, observaciones, signos vitales y medicación de ese contacto.
- **Diagnóstico**: condición clínica registrada para un episodio, codificada en CIE-10; distingue principal vs. secundario.
- **Observación de laboratorio**: medición codificada en LOINC asociada a un episodio; puede tener valor numérico con unidad o valor textual.
- **Signos vitales**: mediciones puntuales por episodio (peso, talla, frecuencia cardíaca, frecuencia respiratoria, tensión arterial, temperatura).
- **Medicación**: prescripción asociada a un episodio (medicamento, dosis, vía, frecuencia, duración).
- **Alergia**: alergia conocida del paciente (sustancia, reacción, severidad); admite el estado "desconocidas".
- **Profesional**: profesional de la salud con identificador, nombre y especialidad principal.
- **Especialidad**: especialidad médica representada en el hospital (pediatría general, neonatología, cardiología pediátrica, etc.).
- **Turno**: cita programada paciente–profesional–especialidad con fecha y estado (programado, atendido, ausente, cancelado).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Cualquier integrante del equipo puede levantar el dataset completo en una máquina limpia en menos de **10 minutos** siguiendo una única receta documentada.
- **SC-002**: Dos ejecuciones consecutivas del seed con la misma semilla producen datasets **idénticos** byte a byte en las columnas observables vía las vistas autorizadas (reproducibilidad 100%).
- **SC-003**: El **100%** de las vistas autorizadas devuelve resultados ante consultas de prueba representativas; **0%** de los intentos de consulta a tablas internas o vistas no autorizadas tiene éxito.
- **SC-004**: El **100%** de los recursos FHIR generados por el mapping Oracle→FHIR existente, aplicado sobre filas del mock, valida estructuralmente sin requerir cambios al mapping ni a las clases consumidoras.
- **SC-005**: Stakeholders clínicos revisando una muestra aleatoria de 5 historiales califican el realismo de los datos como aceptable para demo en **≥4 de los 5 casos**.
- **SC-006**: El dataset cumple los conteos mínimos comprometidos: **≥50** pacientes pediátricos, **≥150** episodios, **≥500** observaciones de laboratorio, y todos los edge cases enumerados en FR-023.
- **SC-007**: El "checklist de reemplazo" permite a un dev del equipo identificar los puntos exactos de cambio para integrar un diseño real hipotético en **menos de 30 minutos** de lectura.
- **SC-008**: Los tests de integración existentes del módulo de interoperabilidad (003) que dependen del cliente de acceso al lado hospital pasan al **100%** contra el mock, sin modificaciones a su código de producción.

## Assumptions

- El motor de base de datos del mock es **Oracle XE 21c en Docker** (imagen `gvenzl/oracle-xe`), confirmado en clarificación. Esto preserva paridad 1:1 con el driver `Oracle.ManagedDataAccess` y con el dialecto SQL que el hospital usa en producción. Si el hospital corrigiera esto en futuras conversaciones, el módulo migra a lo que indiquen.
- La población simulada es **estrictamente pediátrica** (0–17 años) porque ese es el alcance inicial de ChequeAR (especialidades pediátricas del Garrahan).
- Los datos sembrados son **sintéticos**, no anonimizados de pacientes reales; cumplen con la Ley 25.326 por construcción (no hay datos personales reales).
- Se asume que el equipo del hospital eventualmente entregará un diseño autoritativo; hasta entonces, **todo** lo modelado aquí es reemplazable.
- Se asume que las vistas reales del hospital también seguirán un patrón de "prefijo + nombre" con allowlist; si llegaran a usar otro mecanismo de autorización, la spec del módulo 003 se actualiza y este mock se ajusta.
- Se asume que los códigos clínicos estándar (CIE-10, LOINC) son los que el hospital usa o usará; si surge otra codificación interna, se mapea contra ella en una iteración posterior.
- Las vistas autorizadas exponen sólo las columnas que ChequeAR necesita; el resto de columnas internas de las `HOSP_*` permanece oculta.
- El alcance excluye: telemetría hospitalaria en tiempo real, imágenes médicas, facturación, y cualquier dato administrativo no clínico que no sea necesario para los flujos descritos en las specs 001–005.

## Dependencias

- **Spec 003 (Interoperabilidad y almacenamiento)**: define el contrato de `IOracleViewClient`, la estrategia de allowlist y la ubicación de los mapeos Oracle→FHIR (`OracleMappings/`). Este mock debe satisfacer ese contrato sin proponer cambios.
- **Spec 001 (Gestor de cuestionarios)** y **Spec 002 (Motor de renderizado)**: consumen datos del paciente vía precarga; el mock debe proveer pacientes consultables por las claves que esos módulos usan.
- **Spec 004 (Análisis y exportación)**: ejecuta consultas que cruzan respuestas de cuestionarios con datos del paciente; el mock debe contener volúmenes suficientes para que los reportes no devuelvan cohortes vacías triviales.
