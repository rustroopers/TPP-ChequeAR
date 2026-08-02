# Quickstart — Verificación de la feature 009

**Feature**: `009-conformidad-fhir-capas` | **Rama**: `009-conformidad-fhir-capas`

Cómo comprobar que la feature quedó bien, en orden. Cada bloque mapea a criterios de éxito de
la spec.

---

## 0. Levantar el entorno

```
/run-app
```

Arranca el TNS Listener de Oracle (servicio nativo de Windows, sin Docker), compila con MSBuild
de VS2022 y levanta IIS Express en `https://localhost:44301`. También verifica Snowstorm (el
servidor terminológico SNOMED CT); si no está, el autocompletado de códigos avisa y el resto
del sistema funciona igual.

```bash
# Pruebas
dotnet test src/ChequeAR.Tests
```

---

## 1. Compuerta de la Fase 1 — el SDK carga

**La más importante y la que va primero.** Si esto falla, el resto de la feature cambia de
forma (ver R1 en `research.md`).

- [ ] La solución compila con MSBuild.
- [ ] La app levanta **sin `FileLoadException` ni `MissingMethodException`** — los síntomas
      clásicos de binding redirects mal resueltos en .NET Framework.
- [ ] La versión de assembly de `System.Text.Json` en `bin/` **coincide** con el
      `bindingRedirect` de `Web.config`. Verificar el DLL, no asumir el número del paquete:

```powershell
[Reflection.AssemblyName]::GetAssemblyName("src\ChequeAR.Api\bin\System.Text.Json.dll").Version
```

> Si esto no se puede resolver: bajar a `Hl7.Fhir.R5` 5.9.x (arrastra `System.Text.Json` 8.x).

---

## 2. Capas — dos comprobaciones objetivas (SC-006, SC-007)

Ambas deben devolver **cero resultados**:

```bash
# FR-019 — ningún controller inyecta repositorios
grep -rn "I[A-Za-z]*Repository" src/ChequeAR.Api/Controllers/

# FR-018 — la capa de negocio no depende del motor de base de datos
grep -n "Oracle" src/ChequeAR.Application/ChequeAR.Application.csproj
```

- [ ] Ambos `grep` vacíos.
- [ ] `LayerDependencyTests` pasa (FR-022): falla si alguien reintroduce la dependencia.
- [ ] Las **11 pruebas** de `ReportQueryTests.cs` siguen pasando, **incluida la de inyección
      SQL** (línea 212). ← SC-009, es la que no se puede perder.
- [ ] `ChequeAR.Tests.csproj` sigue referenciando **sólo** Domain y Application (FR-021).

---

## 3. Conformidad de lectura (SC-001, US1)

### Tipo de contenido por omisión (FR-002)

**Sin** mandar `Accept`:

```bash
curl -ki https://localhost:44301/fhir/r5/metadata | head -20
```

- [ ] La respuesta trae `Content-Type: application/fhir+json`.
- [ ] Es un `CapabilityStatement` con `fhirVersion: "5.0.0"`.
- [ ] Declara `Questionnaire`, `QuestionnaireResponse`, `AuditEvent`, `Patient`, `Encounter`,
      `ValueSet`, y `Observation` como **no soportado** (FR-013).

### Bundle conforme (FR-003)

```bash
TOKEN=<jwt>
curl -ks -H "Authorization: Bearer $TOKEN" \
  "https://localhost:44301/fhir/r5/Questionnaire?_count=5" | python -m json.tool
```

- [ ] `resourceType: "Bundle"`, `type: "searchset"`, `total` = total real (no el de la página).
- [ ] Cada `entry` tiene `fullUrl` **y** `resource`.
- [ ] Cada `resource.resourceType == "Questionnaire"`.

### Recurso individual

```bash
curl -ks -H "Authorization: Bearer $TOKEN" \
  "https://localhost:44301/fhir/r5/Questionnaire/{id}" | python -m json.tool
```

- [ ] Tiene `resourceType`, `item[]` anidado (grupos como `type: "group"`), `answerOption`,
      `enableWhen`, extensiones — **no** `Title` / `Status` / `ConcurrencyVersion`.
- [ ] `meta.versionId` presente y coincide con el `ETag` (FR-007).

### 🔑 Validador oficial (SC-001)

**Es la prueba que hace verificable la afirmación de la tesis.** Pegar la salida de al menos
un `Questionnaire`, un `QuestionnaireResponse`, un `Patient` y un `Bundle` en
<https://validator.fhir.org/>:

- [ ] Cero errores contra R5.
- [ ] Cero errores contra el perfil declarado en `meta.profile`.

---

## 4. Errores como `OperationOutcome` (SC-003, US3)

Recorrer cada categoría del catálogo de `contracts/fhir-api.md`:

| Provocación | Esperado |
|---|---|
| `GET /fhir/r5/Questionnaire/{guid-inexistente}` | `404` + `issue.code = not-found` |
| `PUT /fhir/r5/Questionnaire/{id}` sin `If-Match` | `428` + `required` |
| `PUT` con `If-Match: W/"1"` sobre una versión más nueva | `409` + `conflict`, con esperada y encontrada en `diagnostics` |
| `$publish` sobre un cuestionario sin preguntas | `422` + `business-rule` |
| `POST` con cuerpo vacío | `400` + `invalid` |
| `$expand` con el servidor terminológico apagado | `503` + `transient` |

- [ ] **Todas** devuelven `resourceType: "OperationOutcome"`.
- [ ] `issue[0].diagnostics` trae texto legible en español.
- [ ] Las pantallas muestran ese texto, no un mensaje genérico (FR-009).

---

## 5. Perfiles y validación real (SC-005, US4)

- [ ] Los dos `StructureDefinition` existen en
      `src/ChequeAR.Infrastructure/Fhir/Profiles/` y se copian a la salida de compilación.
- [ ] Publicar un cuestionario con **dos preguntas que comparten `linkId`** → `422` indicando
      la regla incumplida.
- [ ] Publicar un cuestionario **sin especialidad** → `422`.
- [ ] Publicar uno correcto → funciona, y el contenido congelado pasa el validador oficial.
- [ ] **FR-014**: una versión publicada **antes** de esta feature se sigue consultando sin
      error, aunque no cumpla el perfil vigente. ← fácil de romper, verificar explícitamente.

---

## 6. 🔑 Ida y vuelta sin pérdida (SC-004, US2)

**El mayor riesgo de la feature.** En el navegador, editor de cuestionarios:

1. Crear un cuestionario nuevo con **una pregunta de cada uno de los 11 tipos**.
2. En al menos una: rango mín/máx **y** unidad.
3. En una de texto: largo máximo **y** patrón.
4. En una de adjunto: formatos aceptados.
5. Una **escala**.
6. Una con **lógica condicional** apuntando a otra pregunta.
7. Una con **código SNOMED CT elegido del autocompletado** y su **término descriptivo**.
8. Una de opción múltiple con varias opciones.
9. Guardar. Recargar la página con F5.

- [ ] **Nada se perdió**: enunciado, tipo, obligatoriedad, opciones, rangos, unidad, largo
      máximo, patrón, formatos aceptados, condición, código y **término**.
- [ ] En particular el **término descriptivo** (viaja en la extensión `question-term` — es el
      campo que se pierde si esa extensión no se implementó).
- [ ] La escala vuelve como escala, no como número común.
- [ ] La condición apunta a la misma pregunta y con el tipo de valor correcto.

### Concurrencia (US2, escenarios 3 y 4)

- [ ] Abrir el mismo cuestionario en dos pestañas, guardar en la primera, después en la
      segunda → la segunda recibe `409` y **no pisa** el trabajo de la primera.

---

## 7. Ciclo clínico completo (SC-010)

- [ ] Publicar el cuestionario del paso 6.
- [ ] Desde Pacientes → elegir un paciente → responder la ficha.
- [ ] Las validaciones en línea funcionan (rangos, obligatorios, patrón).
- [ ] La lógica condicional muestra y oculta preguntas correctamente.
- [ ] Finalizar → redirige a `/Encounters/View/{id}` y la ficha se ve completa.
- [ ] **Anclaje de versión**: empezar una ficha, publicar una versión nueva del cuestionario
      desde otra pestaña, volver y finalizar → la ficha conserva **las preguntas originales**.
- [ ] Volver con el botón "atrás" del navegador → muestra el aviso de ficha ya finalizada, no
      un formulario en blanco.

---

## 8. Pacientes y encuentros (US5)

```bash
curl -ks -H "Authorization: Bearer $TOKEN" \
  "https://localhost:44301/fhir/r5/Patient?name=perez" | python -m json.tool
curl -ks -H "Authorization: Bearer $TOKEN" \
  "https://localhost:44301/fhir/r5/Encounter?subject={hospitalId}" | python -m json.tool
```

- [ ] Ambos devuelven `Bundle` conformes; validan en el validador oficial.
- [ ] **Modo degradado (FR-016)**: detener el listener de Oracle y repetir la búsqueda → siguen
      apareciendo los pacientes derivados de las fichas propias, y **ningún dato inventado**.

---

## 9. Reportes — no regresión (SC-009, US6)

El camino que atraviesa el ejecutor de consultas nuevo. Es donde una regresión sería sutil:

- [ ] Construir una consulta con: cuestionario base, rango de fechas, y al menos un filtro de
      cada clase (igualdad, `contains`, `in`, `between`, `exists`).
- [ ] La previsualización devuelve las mismas filas que antes del cambio.
- [ ] El combo de cuestionarios **sigue agrupando por estado** (Publicado / Borrador /
      Archivado) — ⚠️ el estado pasó de numérico a código FHIR (`active` / `draft` / `retired`).
- [ ] Los filtros por pregunta se siguen construyendo (el constructor de reportes ahora recorre
      `item[]` **recursivamente**, porque los grupos anidan).
- [ ] Exportar a Excel: el archivo abre y los identificadores **no** aparecen como
      `System.Byte[]` ← verifica que la normalización del identificador binario se movió bien.
- [ ] Las estadísticas se calculan sobre la cohorte completa, no sobre la primera página.
- [ ] Guardar la consulta, cerrarla, reabrirla desde "Editar consulta" → los filtros se
      rehidratan.

---

## 10. Reglas de negocio testeables (US7)

- [ ] `SavedReportServiceTests`: una persona **no** puede modificar ni eliminar la consulta
      guardada de otra — probado **sin** instanciar nada de la capa web.
- [ ] `AuditQueryServiceTests`: filtrado por actor y recurso, y paginación con total real.
- [ ] Auditoría en pantalla: mismos resultados que antes, con filtros y paginación.
- [ ] Tablero de inicio: misma información; con la base caída se vacía con el aviso de error,
      **sin datos inventados**.

---

## Resumen de las tres verificaciones que no se pueden saltear

| # | Qué | Por qué |
|---|---|---|
| **1** | El SDK carga en `net471` (paso 1) | Compuerta: si falla, toda la feature cambia de forma |
| **6** | Ida y vuelta sin pérdida (paso 6) | Riesgo de pérdida **silenciosa** de datos clínicos |
| **3** | Validador oficial de HL7 (paso 3) | Es la evidencia que hace verificable la afirmación de la tesis |
