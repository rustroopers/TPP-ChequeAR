---
name: "calidad"
description: "PASO 1 de 3. Analiza el código y aplica mejoras de CALIDAD (no busca bugs): refactors donde haga falta (reuso, simplificación, eficiencia, nombres), y limpieza de comentarios innecesarios, conservando los comentarios 'por qué' que este repo valora. Deja el build y los tests verdes. Usar cuando pidan 'limpiar', 'refactorizar', 'mejorar calidad', 'sacar comentarios de más' o revisar el diff antes de commitear. Después de este paso, correr /code-audit (paso 2) para la revisión profunda de solo lectura, y /aplicar-auditoria (paso 3) para implementar lo aprobado."
argument-hint: "Opcional: archivo/carpeta, 'branch' (diff vs main), o vacío = cambios sin commitear"
user-invocable: true
disable-model-invocation: true
---

# Analizador de calidad de código (paso 1/3 — autofix seguro)

Objetivo: dejar el código **más limpio y conciso sin cambiar su comportamiento**. Es una
pasada de CALIDAD, no de bugs (para bugs está `/code-review`) y no de auditoría profunda
(para eso está `/code-audit`, paso 2, seguido de `/aplicar-auditoria`, paso 3). Dos frentes: **refactors**
donde haga falta y **limpieza de comentarios**. Al final: build + tests verdes y un reporte.

Esta skill **edita código**. Solo aplica cambios de los que está seguro que preservan
comportamiento; todo lo opinable o riesgoso queda como sugerencia para `/code-audit` o para
que lo decida la persona.

## Paso 1 — Alcance

- Con argumento archivo/carpeta → ese objetivo.
- Argumento `branch` → el diff contra `main` (`git diff main...HEAD --name-only`).
- Sin argumento → todo el sistema

**Solo tocar código dentro del alcance.** No refactorizar de paso archivos que nadie pidió.
Leé cada archivo completo antes de editarlo (no parchees a ciegas).

## Paso 2 — Qué arreglar (refactors)

Aplicá lo que sea **claro y seguro**; lo que cambie comportamiento o sea opinable, dejalo como
sugerencia en el reporte (no lo apliques).

- **Reuso**: código duplicado → extraer helper; reimplementar algo que ya existe en el repo
  (`SpecialtyMap`, `ArgentinaTime`, `FhirQuestionnaireReader`, helpers del propio servicio) →
  usar lo que hay. Antes de escribir un helper nuevo, buscá si ya está.
- **Simplificación**: condiciones y LINQ retorcidos, anidamiento innecesario, `if/else` que se
  colapsa, variables intermedias de un solo uso, ramas muertas / código inalcanzable.
- **Eficiencia**: consultas repetidas en loop (N+1), materializar de más, recalcular lo mismo
  varias veces (p. ej. `totalQuestions()`/`answeredCount()` dentro de un bucle). No microopt:
  solo lo que además queda más legible.
- **Nombres y forma**: nombres que no dicen nada, métodos larguísimos que piden partirse,
  código que no se parece al de alrededor. Igualá el estilo del archivo, no impongas otro.

Regla de oro: **si no estás seguro de que preserva el comportamiento, no lo apliques** —
proponelo en el reporte.

## Paso 3 — Comentarios: qué SÍ se saca y qué NO

Este repo comenta el **por qué** (decisiones, gotchas, razones de dominio) y eso es
deliberado y valioso. La limpieza apunta al ruido, **no** a esos comentarios.

**SACAR** (ruido):
- Repite lo que el código ya dice: `// incrementa la versión` sobre `version++`;
  `// loop sobre los grupos` sobre un `foreach`.
- Obvio / relleno: `// constructor`, `// getter`, `// TODO` viejos ya resueltos.
- Código comentado (muerto). Si hace falta, está en git.
- XML-doc que no agrega nada al nombre: `/// <summary>Obtiene el paciente.</summary>` sobre
  `GetPatient()`. Un `<summary>` que sí explica algo (unidades, invariantes, efectos) se queda.
- Comentarios **desactualizados o contradictorios** con el código: corregir o borrar (nunca
  dejar una mentira; si el comentario dice algo que el código ya no hace, es peor que nada).

**CONSERVAR** (son el motivo por el que el repo se entiende):
- Explican **por qué** algo es así y no de la forma obvia: elección de tipo, orden, workaround.
- Advierten de un **gotcha** o de las consecuencias de cambiarlo (concurrencia, Oracle,
  bfcache, FHIR, byte-order de GUID, `no-store`, etc.).
- Razón de **dominio** clínico/FHIR que no se deduce del código.

Ejemplos reales del repo que se CONSERVAN tal cual:
```
// TIMESTAMP y no DATE: DATE trunca a segundos y dos eventos del mismo segundo quedaban
// con igual Recorded -> el ORDER BY los devolvía en orden arbitrario.
// Los items llegan del cliente sin Id y EF no genera Guids: sin esto colisionan en la PK
// (ORA-00001) y sólo se podían guardar fichas de una sola pregunta.
```
Ante la duda con un comentario "por qué": **se queda**. Es más barato un comentario de más
que perder el motivo de una decisión.

## Paso 4 — Aplicar

- Cambios **chicos y revisables**, uno por motivo. Preferí varias ediciones acotadas a una
  reescritura grande.
- No cambiar API pública, firmas, ni contratos salvo que el refactor lo pida y sea interno.
- Respetá el idioma y estilo existentes (comentarios en español, convenciones del archivo).

## Paso 5 — Verificar (obligatorio)

Nada se da por terminado sin build + tests en verde. Mismos comandos que usa el proyecto
(ver también la skill `run-app`):

```bash
VSWHERE="/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe"
MSBUILD=$("$VSWHERE" -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe')
"$MSBUILD" "src/ChequeAR.sln" -t:Build -p:Configuration=Debug -m -clp:ErrorsOnly -nologo
dotnet test "src/ChequeAR.Tests/ChequeAR.Tests.csproj" --no-build
```

- Para JS de las islas (`Scripts/islands/**`): `node --check <archivo>` (no hay bundler).
- El warning `CS0108` de `AccountController.Profile()` es preexistente e inofensivo.
- Si algún test se pone en rojo, **revertí ese cambio**: un refactor de calidad no cambia
  comportamiento. Si el test estaba mal, decilo en el reporte, no lo "arregles" a la fuerza.

## Paso 6 — Reportar

Cerrá con un resumen corto:
- **Aplicado**: qué se refactorizó y qué comentarios se sacaron (agrupado por motivo).
- **Sugerido, no aplicado**: lo opinable o riesgoso, para que lo decida la persona.
- Estado de build/tests.
- Recordatorio: para la revisión profunda (tests faltantes, consistencia entre módulos,
  patrones/arquitectura) correr `/code-audit` sobre el mismo alcance, y luego
  `/aplicar-auditoria` para implementar lo que se apruebe de ese reporte.

No commitear salvo que lo pidan explícitamente (ver la memoria del proyecto sobre autorización).
