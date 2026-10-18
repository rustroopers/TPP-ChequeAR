# Implementation Plan: Reemplazo de variables clínicas

**Branch**: `dev` | **Date**: 2026-10-18 | **Spec**: [spec.md](spec.md)

## Summary

Una variable publicada queda fija en lo clínico. El cambio se hace creando otra variable que la reemplaza (`ReplacesId`), con código propio y por lo tanto linkId propio en las preguntas. Reportes, estadísticas y evolución siguen uniendo por linkId, así que la anterior y la nueva son columnas distintas; un helper les pone nombres que se distinguen.

## Data Model

### `ClinicalVariable` (cambia)

| Campo | Cambio |
|---|---|
| `ReplacesId` (Guid?) | Nuevo. Variable de la que se creó. FK a la misma tabla, sin cascade, con índice. |
| `DeactivateReplacedOnPublish` (bool) | Nuevo. Si al publicar esta se dio de baja la anterior; se elige en el modal de Publicar. |
| `PublishedAt` (DateTime?) | Nuevo. Reemplaza a `CurrentVersionId`: null si nunca se publicó. |

`Question` pierde `VariableVersionId` y `VariableRevision`; el builder FHIR deja de emitir `question-variable-version` (su StructureDefinition queda como `retired`, porque la llevan las versiones del cuestionario publicadas antes).

### Índices

`UX_ClinicalVariable_Snomed` (único entre todas) se reemplaza por `UX_CV_Snomed_Activa`, un índice único funcional sobre `CASE WHEN IsActive = 1 AND PublishedAt IS NOT NULL THEN SnomedCode END`. EF6 no sabe declararlo: va por SQL en la migración, que busca el índice viejo por columna (en la base se llama `IX_ClinicalVariable_SnomedCode`, no como lo pedía la migración original).

### Versiones

Se quitan. La migración `RemoveVariableVersions` agrega `PublishedAt` (copiada de la versión vigente), rehace el índice de SNOMED y deja la tabla de versiones y las columnas que la apuntan con nombres legacy (`CVV_Legacy`, `LegacyCurrentVersionId`, `LegacyVariableVersionId`). `VariableVersionRemoval`, en el Seed:

1. Pasa a cada variable publicada la definición clínica de su versión vigente (un borrador viejo con cambios sin publicar se descarta, como ya mostraba su pantalla).
2. Actualiza las preguntas armadas con otra versión con el snapshot de su variable y deja su cuestionario con cambios sin publicar. Las versiones publicadas del cuestionario no cambian.
3. Borra la tabla y las columnas legacy. Si algo falla no borra nada y reintenta en el próximo arranque.

## Flujos

1. **Guardar una publicada**: `UpdateAsync` compara la definición clínica con la guardada (`VariableDefinition.ClinicallySameAs`, que ignora enunciado, descripción y textos vacíos) y rechaza si cambió.
2. **Crear a partir de otra**: `DeriveAsync` arma el borrador desde la publicada, con código libre (`{código}-N`), sin nombre y con la baja marcada. `CreateAsync` exige que la fuente esté publicada y audita `$derive-from-{id}`.
3. **Publicar**: sólo la primera vez. Si reemplaza a otra, `EnsureDistinctFromSourceAsync` exige nombre distinto y cambio clínico; los conflictos de concepto y nombre ignoran a la fuente si se va a dar de baja. `PublishAsync(v, deactivate)` del repositorio da de baja la anterior y publica en una transacción, en ese orden, por el índice.
4. **Reactivar**: rechaza si otra activa usa el concepto o el nombre.
5. **Editor de cuestionarios**: `ToQuestionView` agrega la sucesora de cada variable; `replaceVariable` la trae de `GET api/variables/{id}`; el editor muestra el aviso y `replaceVariable` cambia la pregunta (linkId nuevo con `linkIdLibre`, condiciones re-apuntadas).
6. **Reportes y evolución**: `ReportExecutor` y `PatientTrendService` pasan la estructura publicada por `ColumnLabels.ApplyAsync`.

## Decisiones

| Tema | Decisión | Motivo |
|---|---|---|
| Qué crea variable nueva | Sólo lo clínico | Corregir una descripción no cambia el dato |
| Distinción | Nombre distinto siempre y algún cambio clínico | Se distinguen en catálogo, reportes y auditoría |
| Concepto SNOMED | Único entre activas y publicadas | La nueva puede repetirlo si la anterior se da de baja |
| Cuestionarios | Aviso y botón por pregunta | Lo publicado tiene que ser reproducible; el Jefe decide |
| Reportes | Columnas separadas y marcadas | Nunca se mezclan datos de definiciones distintas |
