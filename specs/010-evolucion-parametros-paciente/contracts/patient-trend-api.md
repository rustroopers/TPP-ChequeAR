# API Contract: Evolución longitudinal de parámetros por paciente

Ambos endpoints se agregan a `PatientController.cs` (`[RoutePrefix("api/patients")]`), mismo esquema de auth que el resto del controller (`[Authorize]`).

## `GET api/patients/{hospitalId}/questionnaires/{questionnaireId}/trend-parameters`

Lista los parámetros seleccionables de un cuestionario para armar el selector de la pantalla `Trend`.

**Respuesta 200**:
```json
{
  "questionnaireId": "guid",
  "questionnaireTitle": "Vasculitis (Kawasaki/PAN) - Seguimiento",
  "parameters": [
    {
      "linkId": "peso",
      "text": "Peso (kg)",
      "type": "decimal",
      "unit": "kg",
      "normalRange": { "min": 2.5, "max": 120 }
    },
    {
      "linkId": "fecha_proximo_control",
      "text": "Fecha próximo control",
      "type": "date",
      "unit": null,
      "normalRange": null
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
  "questionnaireId": "guid",
  "patientHospitalId": "PAC-000043",
  "series": [
    {
      "linkId": "peso",
      "text": "Peso (kg)",
      "unit": "kg",
      "normalRange": { "min": 2.5, "max": 120 },
      "points": [
        { "responseId": "guid-1", "authoredAt": "2026-02-10T00:00:00", "value": 14.2 },
        { "responseId": "guid-2", "authoredAt": "2026-04-03T00:00:00", "value": 14.8 }
      ]
    },
    {
      "linkId": "resultado_lab",
      "text": "Resultado laboratorio",
      "unit": "mg/dL",
      "normalRange": null,
      "points": []
    }
  ]
}
```

**Reglas**:
- Un `linkId` sin datos en ninguna ficha finalizada devuelve `points: []`, nunca un error — el frontend es responsable de mostrar el estado "sin datos suficientes" (FR-011, SC-005).
- Los `points` de cada serie vienen ordenados cronológicamente por `authoredAt` (FR-008).
- Fichas con `Status` distinto de `Completed` (en progreso, con guardado parcial) nunca contribuyen puntos (FR-007) — filtro aplicado en el repositorio, no en el frontend, para que no dependa de que el cliente filtre correctamente.
- `linkId`s que no pertenecen al `questionnaireId` pedido, o que no son de tipo graficable, se ignoran silenciosamente (no rompen la respuesta del resto de los parámetros válidos).
- Sin límite en la cantidad de `linkId` aceptados por request (FR-004).
- `400` si falta `questionnaireId` o no se pasa ningún `linkId`.
- Mismo nivel de permisos (`[Authorize]`) que ya protege el resto de `PatientController` y `EncountersController.Timeline` — sin rol adicional (FR-012).
