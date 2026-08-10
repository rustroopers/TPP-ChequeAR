# Data Model: Evolución longitudinal de parámetros por paciente

No se agregan entidades de dominio nuevas ni se modifica el esquema de base de datos. Esta feature es de solo lectura sobre datos ya persistidos (`Question`, `QuestionnaireResponse`, `QuestionnaireResponseItem`, expuestos también vía la vista `vw_FlatResponses`). Lo nuevo son DTOs de transporte (Application layer) que arman la forma de respuesta para el frontend.

## Entidades existentes reutilizadas

| Entidad | Campos relevantes para esta feature | Uso |
|---|---|---|
| `Question` (`ChequeAR.Domain.Entities`) | `LinkId`, `Text`, `Type` (`QuestionType`), `RepeatsAllowed`, `ValidationJson` | Determina qué preguntas son seleccionables (`Type` ∈ {Decimal, Integer, Date} y `RepeatsAllowed = false`) y de dónde sale el rango normal (`min`/`max` dentro de `ValidationJson`). |
| `QuestionnaireResponse` | `PatientHospitalId`, `QuestionnaireId`, `Status` (`ResponseStatus`), `AuthoredAt`/`LastSavedAt` | Solo se consideran fichas con `Status = Completed` — mismo criterio que ya usa `EncountersController.StatusMatches` para "finalizada". |
| `QuestionnaireResponseItem` (vía `vw_FlatResponses`) | `LinkId`, `ValueDecimal`, `ValueInteger`, `ValueDate` | Aporta el valor de cada punto del gráfico. |

`QuestionType` (enum existente, sin cambios): `Decimal, Integer, String, Text, Date, DateTime, Boolean, Choice, OpenChoice, Attachment, Scale`. Los tipos graficables para esta feature son **`Decimal`, `Integer`, `Date`** (no `DateTime`: se prioriza el caso de uso clínico habitual — valores medidos en una fecha de consulta — dejando `DateTime` fuera de un primer alcance salvo que aparezca un caso real que lo necesite).

## DTOs nuevos (`ChequeAR.Application/Dtos/PatientTrendDtos.cs`)

### `TrendParameterOptionDto`
Un ítem de la lista de parámetros seleccionables para un cuestionario dado.

| Campo | Tipo | Notas |
|---|---|---|
| `LinkId` | `string` | Identificador de la pregunta dentro del cuestionario. |
| `Text` | `string` | Texto de la pregunta, para mostrar en el selector. |
| `Type` | `string` | `"decimal" \| "integer" \| "date"` (serializado en minúscula). |
| `Unit` | `string?` | Si `ValidationJson` trae `unit` (mismo campo que ya usa el editor de cuestionarios). |
| `NormalRange` | `NormalRangeDto?` | `null` si la pregunta no tiene `min`/`max` configurado. |

### `NormalRangeDto`

| Campo | Tipo | Notas |
|---|---|---|
| `Min` | `decimal?` | |
| `Max` | `decimal?` | Puede venir solo uno de los dos (rango abierto de un lado), igual que hoy en `ValidationJson`. |

### `TrendSeriesDto`
Una serie completa (un gráfico) para un parámetro seleccionado.

| Campo | Tipo | Notas |
|---|---|---|
| `LinkId` | `string` | |
| `Text` | `string` | |
| `Unit` | `string?` | |
| `NormalRange` | `NormalRangeDto?` | |
| `Points` | `List<TrendPointDto>` | Ordenados cronológicamente. Vacío si no hay datos (el frontend distingue "vacío" de "error", ver FR-011). |

### `TrendPointDto`

| Campo | Tipo | Notas |
|---|---|---|
| `ResponseId` | `Guid` | Ficha de origen del punto — permite, a futuro, un link a `EncountersController.View`. |
| `AuthoredAt` | `DateTime` | Fecha de finalización de la ficha; eje X del gráfico. |
| `Value` | `decimal` | Valor numérico. Para preguntas `Date`, se serializa como ticks/epoch en el backend y el frontend lo formatea — a definir en el contrato (ver `contracts/`). |

## Reglas de armado (no de validación de negocio nueva — solo de proyección)

1. La lista de parámetros seleccionables (`TrendParameterOptionDto[]`) se arma **solo** a partir del `Questionnaire`/`Question` vigente — no depende de si hay datos cargados.
2. Las series (`TrendSeriesDto[]`) se arman **solo** con fichas `Status = Completed` del paciente para el `QuestionnaireId` pedido, sin importar qué versión del cuestionario respondieron (el spec no distingue por versión, solo por parámetro).
3. Un `LinkId` sin ningún valor cargado en ninguna ficha finalizada del paciente devuelve `Points: []`, no un error ni un 404.
4. Un `QuestionnaireId` que el paciente nunca completó devuelve la lista de parámetros vacía en el primer endpoint (edge case del spec: "no permitir seleccionar parámetros").
