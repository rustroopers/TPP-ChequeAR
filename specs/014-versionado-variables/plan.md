# Implementation Plan: Versionado de variables clínicas

**Branch**: `fix/logica-clinica-fhir` | **Date**: 2026-10-15 | **Spec**: [spec.md](spec.md)

## Summary

La variable sigue el mismo modelo que el cuestionario: la entidad `ClinicalVariable` es el borrador y cada publicación agrega una `ClinicalVariableVersion` inmutable. Las preguntas pasan a atarse a una versión concreta. Activar o desactivar toca sólo la cabecera, el borrado sólo procede si la variable nunca se publicó, y código, tipo y repetición quedan fijos desde la primera publicación.

## Technical Context

- .NET Framework 4.7.1, EF6 sobre Oracle (XE en desarrollo, Autonomous en producción), migraciones Code First.
- Capas: Domain ← Application ← Infrastructure ← Api. Los tests referencian sólo Domain y Application.
- UI: MVC (`VariablesController`, formularios con antiforgery) + islas Vue (`variable-form`, `questionnaire-editor`).

## Data Model

### `ClinicalVariable` (cambia)

| Campo | Cambio |
|---|---|
| `CurrentVersionId` (Guid?) | Nuevo. Versión vigente; null si nunca se publicó. |
| `Revision` (int) | Sigue subiendo en cada guardado del borrador y pasa a ser token de concurrencia. Activar o desactivar ya no la toca. Ya no se copia a las preguntas. |
| Resto | Sin cambios: es la definición en borrador. |

"Cambios sin publicar" se deriva: `Revision != versión vigente.SourceRevision`. No hace falta otro flag.

### `ClinicalVariableVersion` (nueva tabla)

| Campo | Tipo | Nota |
|---|---|---|
| `Id` | Guid | PK |
| `VariableId` | Guid | FK explícita a `ClinicalVariable`, sin cascade |
| `VersionNumber` | int | Único por variable (índice con nombre corto, menos de 30 caracteres) |
| `SourceRevision` | int | Revisión del borrador desde la que se publicó |
| `DefinitionJson` | CLOB (`IsMaxLength`) | Definición completa: código, nombre, enunciado por defecto, tipo, repeticiones, unidad, rango, largo, patrón, formatos, SNOMED, término y opciones |
| `PublishedBy`, `PublishedAt`, `ChangeNote` | | Igual que `QuestionnaireVersion` |

Se guarda como JSON: es inmutable, no se consulta por campos y no depende de que las columnas del borrador sigan existiendo. Un DTO de Application (`VariableDefinition`) es la única forma de esa definición: lo serializan la publicación y el backfill, y lo consume el snapshot de la pregunta. Así los tests no necesitan Infrastructure.

### `Question` (cambia)

| Campo | Cambio |
|---|---|
| `VariableVersionId` (Guid?) | Nuevo. Versión cuya definición copió la pregunta. Null en preguntas desactualizadas de antes de la migración. |
| `VariableRevision` | Pasa a guardar el número de esa versión (0 si no tiene). |

### Versión publicada del cuestionario (FHIR)

Extensión nueva `question-variable-version` (valueInteger), junto a `question-variable-id`, con su StructureDefinition en `Infrastructure/Fhir/Profiles/`. Se emite sólo si la pregunta tiene versión. El parser la deja en `VariableRevision`. Las versiones ya publicadas no se reescriben: son inmutables y nada las necesita.

## Flujos

1. **Crear variable**: queda en borrador, sin versión.
2. **Guardar borrador** (MVC y `PUT` de la API): recibe la revisión esperada como parámetro aparte (`UpdateAsync(id, patch, userId, expectedRevision)`), y si no coincide responde 409 con aviso. Rechaza cambios de código, tipo o repetición si hay versiones. Incrementa `Revision`. El repositorio traduce `DbUpdateConcurrencyException` a `ConcurrencyConflictException`. El MVC muestra el conflicto y no reenvía la revisión vieja.
3. **Publicar** (botón MVC con antiforgery y `POST api/variables/{id}/$publish`): recibe la revisión esperada y valida. Compara en memoria con la vigente, valor por valor y no como texto, porque los decimales cambian de escala. Si no hay cambios, no crea versión. Si los hay, crea la N+1 con `SourceRevision`, actualiza `CurrentVersionId` y audita.
4. **Activar o desactivar**: `UpdateHeaderAsync` sólo con `IsActive` y los datos de auditoría. No toca opciones, versión ni `Revision`.
5. **Borrar**: elimina sólo si no tiene versión **y** ninguna pregunta la usa; si no, desactiva.
6. **Buscar en el catálogo** (editor): filtro nuevo "sólo publicadas y activas" que devuelve id, nombre, código y número de versión vigente. El listado del Administrador sigue mostrando el borrador.
7. **Agregar una pregunta** (editor): trae la definición con `GET api/variables/{id}/versions/{n}` (permiso `QuestionnaireRead`) y la copia en el cliente con `variableRevision = n`.
8. **Actualizar a vN** (editor): el mismo GET; reemplaza en el cliente tipo, unidad, rango, opciones y coding, conserva enunciado, obligatoriedad, condición y linkId, y re-evalúa las condiciones contra las opciones nuevas. `normQ` incluye `variableRevision` para que el merge de conflictos no pierda la actualización.
9. **Guardar el cuestionario** (`BindVariablesAsync`), con `pedida` = número que llega en la extensión:
   - Pregunta guardada, misma variable y `pedida` vacía o igual → `KeepSnapshot` (copia también `VariableVersionId`).
   - Pregunta guardada con `pedida` distinta, o pregunta nueva con `pedida` → aplica esa versión (cualquiera que exista; el editor sólo ofrece la vigente, pero el import conserva la del origen).
   - Pregunta nueva sin `pedida` (FHIR externo) → la vigente.
   - Versión inexistente o variable sin publicar → 422.
   - El repositorio carga las versiones en lote por pares (variable, número).
10. **Publicar el cuestionario**: rechaza preguntas sin versión, con un mensaje que manda a usar "Actualizar".
11. **Clonar**: `CopyQuestion` copia `VariableVersionId` y el número.
12. **Importar preguntas** (editor): conserva la versión del cuestionario de origen; si hay una más nueva, aparece el aviso con el botón.
13. **Pantalla del cuestionario** (`QuestionnairesController.ToQuestionView`): `VariableCurrentRevision` pasa a ser el número de la versión vigente. Lo usan Edit, EditorModel y Preview.

## Migración

1. Migración EF: tabla `ClinicalVariableVersion` con nombres cortos de índice y FK; `CurrentVersionId` en `ClinicalVariable`; `VariableVersionId` en `Question`. Columnas NOT NULL nuevas con default explícito por SQL (el provider ignora `defaultValue`, ORA-01758).
2. Backfill en C# desde el Seed, después de `SeedClinicalVariables`, idempotente y por variable:
   - Publica la versión 1 con la definición actual de cada variable sin `CurrentVersionId`.
   - Para **todas** las preguntas de esa variable compara contenido, no revisión (activar o desactivar subía `Revision` sin cambiar nada): tipo, repetición, `ValidationJson`, `CodingJson` y opciones en orden. Si coincide → `VariableVersionId` = v1 y `VariableRevision` = 1. Si no → `VariableVersionId` null y `VariableRevision` = 0, para que el editor la marque como desactualizada.

## Decisiones

| Tema | Decisión | Motivo |
|---|---|---|
| Publicación | Explícita, con nota | Igual que los cuestionarios; los Jefes no ven cambios a medio hacer |
| Propagación | Aviso y botón por pregunta | Lo publicado tiene que ser reproducible; el Jefe decide |
| Código, tipo y repetición | Fijos desde la primera publicación | Reportes, evolución y retarget unen por linkId |
| Formato de la versión | JSON inmutable vía DTO de Application | No se consulta por campos; un único serializador |
| Cambios sin publicar | Derivado de `SourceRevision` | Un flag menos que sincronizar |
| Preguntas sin versión | Bloquean la publicación del cuestionario | FR-004; se resuelve con un clic |
| SNOMED único | Validado contra los borradores, como hoy | Dos versiones vigentes con el mismo concepto es un caso raro y no rompe nada |

## Impacto en otros hallazgos

- **CLI-10**: resuelto completo.
- **CLI-15**: las versiones guardadas permiten validar en el servidor que un linkId reusado siga siendo la misma variable.
- **CLI-07**: el esquema de reportes tiene que tolerar versiones publicadas sin la extensión nueva.

## Riesgos

- El backfill toca todas las variables y preguntas en el primer arranque; es idempotente y por variable.
- Las preguntas con definición distinta de la actual no recuperan la original: no estaba guardada.
- El editor de cuestionarios es grande (unas 2200 líneas); el cambio se limita al buscador, al aviso, al botón, a `normQ` y al import.
