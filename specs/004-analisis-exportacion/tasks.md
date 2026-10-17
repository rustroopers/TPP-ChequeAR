# Tasks: Módulo de Análisis y Exportación

## Fase 0 – Setup

- [ ] T001 [S4] Agregar dependencias ClosedXML, CsvHelper, Hangfire. *(XLSX y CSV propios, sin paquetes; sin Hangfire)*
- [x] T002 [S4][P] Crear migración con vista `vw_FlatResponses` e índices auxiliares.
- [ ] T003 [S4][P] Implementar `tools/seed-reports.ps1` con ≥100 respuestas semilla.

## Fase 1 – Query builder y endpoint síncrono

- [x] T010 [S5] `ColumnResolver` que valida `linkId` contra el `Questionnaire` y devuelve tipo esperado. *(PublishedStructure / QueryBuilder)*
- [x] T011 [S5] `QueryBuilder` con AND/OR anidados; emite SQL parametrizado.
- [x] T012 [S5] `ReportExecutor` que ejecuta y arma `{columns, rows, total}`.
- [x] T013 [S5] `ReportsController.Query` con paginación.
- [x] T014 [S5][P] Tests unitarios del QueryBuilder (inyección, operadores por tipo).

## Fase 2 – Exporters sincrónicos

- [x] T020 [S5] `XlsxExporter` con hoja "Datos" + hoja "Acerca de". *(sin hoja "Acerca de" (fuera de alcance (2026-10-15)))*
- [x] T021 [S5] `CsvExporter` con UTF-8 BOM, `;`, escape RFC 4180.
- [x] T022 [S5] `ReportsController.Export` sincrónico con límites configurables.
- [x] T023 [S5][P] Snapshot tests del XLSX y CSV generados.

## Fase 3 – Asincrónico

- [ ] T030 [S6] Configurar Hangfire (storage SQL Server, dashboard protegido por rol). *(fuera de alcance (2026-10-15))*
- [ ] T031 [S6] `ExportJobWorker` que reusa exporters y persiste `ExportArtifact`. *(fuera de alcance (2026-10-15))*
- [ ] T032 [S6] Endpoints `/jobs/{id}` y `/artifacts/{id}` con TTL. *(fuera de alcance (2026-10-15))*
- [ ] T033 [S6][P] Job de limpieza de artifacts vencidos. *(fuera de alcance (2026-10-15))*

## Fase 4 – Saved reports

- [x] T040 [S6] CRUD `/api/reports/saved`.
- [x] T041 [S6] Permisos: `scope=private` ↔ autor; `scope=specialty` ↔ usuarios con esa especialidad.
- [ ] T042 [S6][P] `/saved/{id}/run` shortcut.

## Fase 5 – Estadísticas y previsualización

- [x] T050 [S7] `StatsCalculator` (n, media, mediana, stddev).
- [x] T051 [S7] Endpoint `/preview-columns`.
- [x] T052 [S7][P] Conteos y distribuciones para preguntas tipo `choice`.

## Fase 6 – Auditoría e integración

- [x] T060 [S7] Emitir `AuditEvent` en cada export con `entity.what`, `agent.who`, `outcome`.
- [ ] T061 [S7][P] Test E2E: construir → ejecutar → exportar → verificar artifact y auditoría.

## DoD

- Snapshot tests aprueban (XLSX, CSV deterministas).
- Performance verificado con dataset de 10k respuestas.
- Documentar limitación de licencia de la librería Excel elegida.
