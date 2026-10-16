# API Contract: Evolución longitudinal de parámetros por paciente

Ambos endpoints viven en `PatientTrendController.cs` (`[RoutePrefix("api/patients")]`, `[Authorize]`).

Como el resto de la Web API, el JSON sale en PascalCase y sin las propiedades nulas. Los ejemplos de abajo usan esos nombres.

## `GET api/patients/{hospitalId}/questionnaires/{questionnaireId}/trend-parameters`

Lista los parámetros seleccionables de un cuestionario para armar el selector de la pantalla `Trend`.

**Respuesta 200**:
```json
{
  "QuestionnaireId": "guid",
  "QuestionnaireTitle": "Vasculitis (Kawasaki/PAN) - Seguimiento",
  "Parameters": [
    {
      "LinkId": "peso",
      "Text": "Peso (kg)",
      "Type": "decimal",
      "Unit": "kg",
      "NormalRange": { "Min": 2.5, "Max": 120 }
    },
    {
      "LinkId": "fecha_proximo_control",
      "Text": "Fecha próximo control",
      "Type": "date"
    }
  ]
}
```

**Reglas**:
- Solo se incluyen preguntas con `Type` ∈ {`Decimal`, `Integer`, `Date`} y `RepeatsAllowed = false`.
- `parameters` puede venir vacío (cuestionario sin preguntas graficables) — no es un error.
- `404` si `questionnaireId` no existe.
- No requiere que el paciente tenga fichas — es metadata del cuestionario, no de las respuestas.

## `GET api/patients/{hospitalId}/trend?questionnaireId={guid}&linkId={a}&linkId={b}...`

Devuelve las series de evolución para los parámetros pedidos, usando solo fichas finalizadas del paciente.

**Query params**:
- `questionnaireId` (requerido)
- `linkId` (uno o más, repetido — sin límite de cantidad, según FR-004)

**Respuesta 200**:
```json
{
  "QuestionnaireId": "guid",
  "PatientHospitalId": "PAC-000043",
  "Series": [
    {
      "LinkId": "peso",
      "Text": "Peso (kg)",
      "Type": "decimal",
      "Unit": "kg",
      "NormalRange": { "Min": 2.5, "Max": 120 },
      "Points": [
        { "ResponseId": "guid-1", "Date": "2026-02-10", "Value": 14.2 },
        { "ResponseId": "guid-2", "Date": "2026-04-03", "Value": 14.8 }
      ]
    },
    {
      "LinkId": "resultado_lab",
      "Text": "Resultado laboratorio",
      "Type": "decimal",
      "Unit": "mg/dL",
      "Points": []
    }
  ]
}
```

- `Date` es el día en que se finalizó la ficha, en hora de Argentina (FR-008). Va como texto para que el navegador no lo corra por su zona horaria.
- `Type` le indica al frontend cómo formatear el eje Y. Los valores de tipo `date` viajan como epoch en milisegundos de la fecha a medianoche UTC.

**Reglas**:
- Un `linkId` sin datos en ninguna ficha finalizada devuelve `Points: []`, nunca un error — el frontend es responsable de mostrar el estado "sin datos suficientes" (FR-011, SC-005).
- Los `Points` de cada serie vienen ordenados cronológicamente por fecha de finalización (FR-008).
- Sólo contribuyen las fichas finalizadas (`Completed` o `Amended`); las en curso o descartadas nunca aportan puntos (FR-007) — filtro aplicado en el repositorio, no en el frontend, para que no dependa de que el cliente filtre correctamente.
- `linkId`s que no pertenecen al `questionnaireId` pedido, o que no son de tipo graficable, se ignoran silenciosamente (no rompen la respuesta del resto de los parámetros válidos).
- Sin límite en la cantidad de `linkId` aceptados por request (FR-004).
- `400` si falta `questionnaireId` o no se pasa ningún `linkId`.
- Mismo nivel de permisos (`[Authorize]`) que `EncountersController.Timeline`, y sólo con las fichas de las especialidades de quien consulta: un médico nunca ve datos de otra especialidad (decisión del 2026-10-13, que prevalece sobre FR-012).
