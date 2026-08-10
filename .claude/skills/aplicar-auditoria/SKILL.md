---
name: "aplicar-auditoria"
description: "PASO 3 de 3. Implementa hallazgos de un reporte de /code-audit, pero SOLO los que la persona aprobó explícitamente por número (nunca todos por defecto, nunca los 'sugerencia' sin que se pidan). A diferencia de /calidad, acá los cambios pueden alterar comportamiento a propósito (arreglar un bug real, agregar tests faltantes) — por eso cada hallazgo se implementa en un cambio chico y separado, con build+tests verdes antes de seguir con el siguiente. Usar después de correr /code-audit y de que la persona diga qué hallazgos aprueba."
argument-hint: "Números de hallazgo aprobados del último reporte de /code-audit (ej: '1,3,5' o 'todos los críticos'). Si no hay reporte previo en la conversación, pedirlo antes de arrancar."
user-invocable: true
disable-model-invocation: true
---

# Implementador de hallazgos de auditoría (paso 3/3)

Objetivo: convertir hallazgos aprobados de `/code-audit` en cambios reales, uno por uno,
sin sorpresas. A diferencia de `/calidad` (que solo aplica cambios que preservan
comportamiento), acá el objetivo explícito puede ser **cambiar** comportamiento — corregir
un bug, agregar el test que faltaba, resolver una inconsistencia entre módulos — así que la
disciplina no es "no tocar el comportamiento", es "no tocar nada que no esté aprobado".

## Paso 0 — Confirmar qué está aprobado (obligatorio, no asumir)

- Necesitás el reporte de `/code-audit` (de esta conversación o pegado por la persona) y la
  lista de números aprobados.
- **Nunca implementes un hallazgo que no fue pedido explícitamente**, aunque te parezca
  obviamente correcto. Si la persona dice "todos los críticos", implementá solo los
  marcados como crítico en ese reporte — no los importantes ni las sugerencias.
- Si un hallazgo aprobado depende de otro no aprobado (el reporte lo marca como tal),
  avisá antes de arrancar y preguntá si también se aprueba el que falta.
- Si no hay reporte de auditoría disponible en la conversación ni pegado, pedilo — no
  improvises hallazgos propios en esta skill (para eso está `/code-audit`).

## Paso 1 — Un hallazgo a la vez

Para cada hallazgo aprobado, en el orden del reporte (respetando dependencias):

1. Releé el archivo completo involucrado (no confíes solo en lo que dice el reporte, puede
   haber cambiado desde que se generó).
2. Implementá el cambio mínimo que resuelve el hallazgo. Si el hallazgo pedía un test
   faltante, escribilo cubriendo el caso real descripto (no un test trivial que solo
   "pasa").
3. Si al meterte en el código el arreglo resulta más grande o más riesgoso de lo que el
   reporte sugería (toca más archivos, cambia una API pública, afecta otro módulo), **parate
   y avisá** en vez de seguir de largo — eso vuelve a `/code-audit` o a una decisión manual,
   no se improvisa acá.
4. Corré build + tests **después de cada hallazgo**, no al final de todos:

```bash
MSBUILD="C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
VSTEST="C:/Program Files/Microsoft Visual Studio/2022/Community/Common7/IDE/Extensions/TestPlatform/vstest.console.exe"
"$MSBUILD" "src/ChequeAR.sln" -t:Build -p:Configuration=Debug -m -clp:ErrorsOnly -nologo
"$VSTEST" "src/ChequeAR.Tests/bin/Debug/net471/ChequeAR.Tests.dll"
```

   - Si algo rompe: no sigas con el próximo hallazgo hasta resolverlo o revertir el actual.
   - El warning `CS0108` de `AccountController.Profile()` es preexistente e inofensivo.
   - Para JS de las islas (`Scripts/islands/**`): `node --check <archivo>`.

## Paso 2 — Qué no hacer

- No agrupes varios hallazgos en un solo cambio grande, aunque toquen el mismo archivo:
  facilita revisar y revertir si algo sale mal.
- No aproveches para "de paso" arreglar otra cosa que viste — eso es exactamente el tipo de
  scope creep que `/calidad` y `/code-audit` ya evitan a propósito. Si ves algo nuevo,
  anotalo para una futura auditoría.
- No cambies la firma de algo público a menos que el hallazgo aprobado lo requiera
  explícitamente.
- No commitear salvo que lo pidan explícitamente (ver la memoria del proyecto sobre
  autorización).

## Paso 3 — Reportar

Cerrá con:
- **Implementado**: hallazgo por número, qué se hizo, qué archivo(s) tocó, si se agregó/
  modificó algún test.
- **Pausado / no implementado**: hallazgos aprobados que resultaron más riesgosos de lo
  esperado al tocarlos, con la razón puntual.
- Estado final de build/tests.
- Si quedaron hallazgos del reporte original sin aprobar, listalos brevemente por si la
  persona quiere sumarlos en una próxima pasada.
