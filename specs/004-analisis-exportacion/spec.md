# Feature Specification: Módulo de Análisis y Exportación

**Feature Branch**: `004-analisis-exportacion`
**Created**: 2026-06-01
**Status**: Draft

## User Scenarios & Testing

### User Story 1 - Filtrar cohorte y exportar a Excel (Priority: P1)

Un Jefe Médico construye una consulta sobre las respuestas finalizadas — por ejemplo "pacientes con trasplante hepático y bilirrubina > 5 mg/dL en los últimos 6 meses" — y exporta el resultado a Excel.

**Why this priority**: Es el caso de uso clave para investigación y análisis estadístico — uno de los objetivos explícitos del proyecto.

**Independent Test**: Generar un reporte con ≥3 filtros encadenados sobre un cuestionario publicado, exportar a Excel y verificar que las filas resultantes coinciden con la consulta directa a la base de datos.

**Acceptance Scenarios**:

1. **Given** ≥50 `QuestionnaireResponse` finalizadas, **When** se aplica un filtro `linkId=bilirrubina, op=>, value=5`, **Then** el resultado lista sólo respuestas que cumplan.
2. **Given** un reporte definido, **When** se solicita exportación Excel, **Then** se descarga un `.xlsx` con una fila por respuesta y una columna por pregunta del cuestionario.
3. **Given** un usuario con rol Observador, **When** intenta exportar, **Then** el sistema permite la consulta y descarga (solo lectura sin modificar datos).

---

### User Story 2 - Exportar a CSV (Priority: P1)

Misma funcionalidad pero con formato CSV (UTF-8 con BOM, separador `;` para compatibilidad con Excel en es-AR).

**Acceptance Scenarios**:

1. **Given** un reporte, **When** se solicita CSV, **Then** se descarga con encoding correcto y todas las columnas escapadas.

---

### User Story 3 - Guardar y reutilizar consultas (Priority: P2)

Un usuario guarda una consulta frecuente y la reutiliza luego, opcionalmente compartiéndola con su equipo.

**Acceptance Scenarios**:

1. **Given** una consulta construida, **When** el usuario la guarda con un nombre, **Then** queda disponible en su listado personal.
2. **Given** una consulta marcada como compartida con la especialidad, **When** otro Médico de la misma especialidad la abre, **Then** puede ejecutarla pero no editarla.

---

### User Story 4 - Vista tabular y estadísticas básicas (Priority: P3)

Antes de exportar, el usuario ve una previsualización tabular y agregados básicos (conteo, media, mediana, distribución para preguntas codificadas).

**Acceptance Scenarios**:

1. **Given** un reporte con preguntas numéricas, **When** se ejecuta, **Then** la UI muestra n, media, mediana y desvío para cada columna.

### Edge Cases

- Reporte sobre un cuestionario versionado: respuestas a versiones distintas → se permite con disclaimer o se filtra por versión.
- Resultado > 50k filas → exportación asincrónica con notificación.
- Filtros sobre preguntas tipo `attachment` → no permitidos (solo metadata: existencia/no, tamaño).
- Pregunta condicional no respondida → celda vacía con marca distintiva de "no aplica".

## Requirements

### Functional Requirements

- **FR-001**: El sistema DEBE permitir construir consultas con N filtros sobre `QuestionnaireResponse` finalizadas.
- **FR-002**: Filtros DEBEN soportar operadores: `=`, `!=`, `>`, `<`, `>=`, `<=`, `in`, `between`, `exists`, `contains` (texto), según tipo de pregunta.
- **FR-003**: Filtros DEBEN poder combinarse con AND/OR y agrupamiento por paréntesis.
- **FR-004**: El sistema DEBE permitir filtrar por metadata: rango de fechas, especialidad, autor, versión del cuestionario.
- **FR-005**: El sistema DEBE exportar a Excel (`.xlsx`) y CSV (UTF-8 con BOM, separador `;`).
- **FR-006**: Exportaciones > 5 MB o > 10k filas DEBEN ejecutarse en background con notificación de "listo para descarga".
- **FR-007**: El sistema DEBE permitir guardar consultas (privadas o compartidas con la especialidad).
- **FR-008**: El sistema DEBE mostrar estadísticas básicas (n, media, mediana, desvío, conteos) en preguntas numéricas y de opción.
- **FR-009**: El sistema DEBE registrar en auditoría toda exportación (qué consulta, quién, cuándo, cuántos registros).
- **FR-010**: El sistema DEBE soportar consultas que devuelven una `Observation` derivada por respuesta (vista FHIR opcional).

### Key Entities

- **SavedReport**: consulta nombrada con criterios serializados, ámbito (privado/especialidad), autor.
- **ReportExecution**: registro de una ejecución (autor, criterios efectivos, resultado contado, archivo generado, hash).
- **ExportArtifact**: archivo Excel/CSV generado, con TTL.

## Success Criteria

- **SC-001**: Una consulta con 5 filtros sobre 10k respuestas se ejecuta en < 3 s p95.
- **SC-002**: Exportación Excel de 5k filas < 10 s.
- **SC-003**: 100% de las exportaciones coinciden fila a fila con un `SELECT` directo de control (medido en tests de integración).
- **SC-004**: Usuarios de la prueba piloto generan ≥1 reporte por semana y reportan satisfacción CSAT ≥ 4/5.

## Assumptions

- Las respuestas finalizadas se materializan en `QuestionnaireResponseItem` (ver módulo 002) que ya está indexada para queries analíticas.
- Cuestionarios diferentes producen exports independientes (no se mezclan estructuras dispares).
- El usuario tiene permiso de lectura sobre todas las respuestas de las especialidades que su rol habilita.

## UI

Las pantallas E1–E6 son entregables del equipo (spec 006, `screens/04-analisis.md`). La pantalla estrella es **E2 (Query Builder)**, implementada como isla Vue `report-builder`. **E3 (Preview)** y **E4 (Exportar)** son Razor; **E5 (Estadísticas)** usa Chart.js sobre Razor.
