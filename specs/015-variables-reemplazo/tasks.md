# Tasks: Reemplazo de variables clínicas

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Fase 1: Modelo y migración

- [x] T001 `ClinicalVariable.ReplacesId` y `DeactivateReplacedOnPublish`.
- [x] T002 Migración `AddVariableReplacement`: columnas, FK e índice por SQL, índice único funcional de SNOMED entre activas y publicadas.

## Fase 2: Servicio (US1)

- [x] T003 `VariableDefinition.ClinicallySameAs`; textos vacíos equivalen a ninguno.
- [x] T004 `UpdateAsync` rechaza cambios clínicos en una publicada.
- [x] T005 `DeriveAsync` y `CreateAsync` con `ReplacesId`; auditoría `$derive-from-`.
- [x] T006 `PublishAsync` sólo la primera vez; distinción con la fuente y baja en la misma transacción.
- [x] T007 Conflictos de concepto y nombre entre activas y publicadas; reactivación protegida.
- [x] T008 Auditoría con `$activate` / `$deactivate`.

## Fase 3: Pantallas (US1, US2)

- [x] T009 Pantalla de variable: campos clínicos bloqueados, "Crear a partir de esta", casillero de baja, "Reemplaza a" / "Reemplazada por".
- [x] T010 Listado: badge "Reemplazada", "Crear a partir de esta", "Publicar" sólo para no publicadas.
- [x] T011 Editor de cuestionarios: aviso y botón "Reemplazar".
- [x] T012 Clonar: el mensaje nombra la variable nueva.

## Fase 4: Reportes y evolución (US3)

- [x] T013 `ColumnLabels` en `ReportExecutor` y `PatientTrendService`.

## Fase 5: Limpieza y pruebas

- [x] T014 Fuera: versión N+1 de una publicada, notas del cambio, "cambios sin publicar", `UpdateVersionSourceAsync`, `PublishVariableDto`.
- [x] T015 Tests de servicio y de `ColumnLabels`.
- [x] T016 Prueba de punta a punta en local: crear, publicar, cargar ficha, reemplazar, reportes, evolución, reactivar y auditoría.

## Fase 6: Sin versiones de variables

- [x] T017 `ClinicalVariable.PublishedAt` en lugar de `CurrentVersionId`; fuera `ClinicalVariableVersion`, `Question.VariableVersionId` y `VariableRevision`.
- [x] T018 Servicio y repositorio sin versiones: publicar marca la fecha; comparar, derivar y validar usan la variable guardada.
- [x] T019 `QuestionnaireService`: una pregunta nueva toma la variable publicada; fuera el chequeo "usa una definición anterior" al publicar.
- [x] T020 FHIR: fuera `question-variable-version`; su StructureDefinition queda `retired`.
- [x] T021 Pantallas: fuera la columna y la tabla de versiones, el aviso "Actualizar a vN" y `api/variables/{id}/versions`.
- [x] T022 Migración `RemoveVariableVersions` y `VariableVersionRemoval` (preguntas desactualizadas al día, cuestionario con cambios sin publicar).
- [x] T023 Tests y prueba en local: crear, publicar, rechazo de cambio clínico, derivar con baja, editor y publicación del cuestionario actualizado.
