# Quickstart: Evolución longitudinal de parámetros por paciente

Guía manual para validar la feature una vez implementada, sin depender todavía de tests automatizados end-to-end (el proyecto valida este tipo de flujos a mano contra Oracle real, mismo enfoque que se usó para `specs/009-completado-asincronico-cuestionarios`, ver su sección "Testing hecho").

## Prerrequisitos

1. Levantar la app local siguiendo la skill `run-app` (Oracle nativo + IIS Express en `https://localhost:44301`).
2. Tener al menos un paciente con **dos o más fichas finalizadas** del mismo cuestionario, con valores distintos en al menos un parámetro numérico (por ejemplo, el paciente `PAC-000043` con "Vasculitis (Kawasaki/PAN) - Seguimiento" ya usado como caso de prueba en la spec 009, si tiene fichas finalizadas además de la que quedó `InProgress`).

## Pasos

1. Ir a **Pacientes → buscar al paciente → Historial** (`EncountersController.Timeline`, R7).
2. Click en **"Ver evolución"** → navega a la nueva pantalla `Trend`.
3. Elegir el cuestionario del que el paciente tiene fichas finalizadas.
   - **Verificar**: la lista de parámetros seleccionables solo muestra preguntas numéricas o de fecha, no texto/choice/repetibles.
4. Seleccionar un parámetro con al menos dos valores cargados en fichas distintas.
   - **Verificar**: aparece un gráfico con los puntos en las fechas correctas, ordenados cronológicamente.
   - **Verificar**: si la pregunta tiene rango normal configurado, se ve la franja de referencia.
5. Seleccionar 4-5 parámetros más a la vez.
   - **Verificar**: aparece un gráfico independiente por cada uno, todos apilados y alineados por fecha, sin tope de cantidad.
6. Elegir un parámetro sin ningún valor cargado en fichas finalizadas.
   - **Verificar**: su gráfico muestra el estado "sin datos suficientes", no un gráfico vacío sin explicación.
7. Elegir un cuestionario que el paciente nunca completó.
   - **Verificar**: no se puede seleccionar ningún parámetro y el sistema lo indica.
8. (Si el paciente tiene una ficha `InProgress` del mismo cuestionario, como en el caso de spec 009) Confirmar que sus valores **no** aparecen en ningún gráfico — solo las finalizadas.

## Casos a cubrir con tests unitarios (`PatientTrendServiceTests.cs`)

- Selección de parámetros: excluye `RepeatsAllowed = true`, excluye tipos no numéricos/fecha (`String`, `Choice`, `Boolean`, etc.).
- Armado de series: solo fichas `Completed`, orden cronológico, `points: []` cuando no hay datos.
- Rango normal: presente cuando `ValidationJson` trae `min`/`max`, ausente (no error) cuando no los trae o el JSON es inválido — mismo criterio de tolerancia que ya usa `FhirQuestionnaireBuilder` ("ValidationJson ausente o corrupto: se publica sin restricciones").
- `linkId`s pedidos que no pertenecen al cuestionario o no son graficables: se ignoran sin romper el resto de la respuesta.
