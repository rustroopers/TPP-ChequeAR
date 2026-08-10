---
name: "code-audit"
description: "PASO 2 de 3. Audita el código a nivel senior — NO EDITA NADA, solo lee y reporta. Detecta inconsistencias entre módulos, tests faltantes o débiles, documentación de más o desactualizada, malas prácticas y violaciones de patrones/capas. Produce un reporte priorizado y NUMERADO (crítico/importante/sugerencia), nunca un veredicto de 'código perfecto'. Pensada para correr después de /calidad y antes de /aplicar-auditoria (paso 3), que implementa los hallazgos que la persona apruebe. Usar cuando pidan 'auditoría de código', 'revisión profunda', 'chequear consistencia', 'dónde faltan tests', o antes de una entrega grande."
argument-hint: "Opcional: archivo/carpeta/módulo a auditar, o vacío = todo src/ del repo"
user-invocable: true
disable-model-invocation: true
---

# Auditor de calidad de código (paso 2/3 — solo lectura)

Objetivo: dar una lectura de nivel ingeniero senior sobre **qué tan sólido está el código**,
sin tocar un solo archivo. Complementa a `/calidad`: esa skill ya aplicó los autofixes
seguros y limpió comentarios de ruido; esta va más profundo en cosas que **no se arreglan
solas** — tests, consistencia entre módulos, documentación, malas prácticas y patrones.

El reporte que produce es el **input directo de `/aplicar-auditoria` (paso 3)**: por eso
cada hallazgo necesita quedar numerado y ser autocontenido (alguien tiene que poder decir
"aplicá el 2 y el 5" sin volver a leer el código).

**Regla dura: esta skill nunca edita, nunca commitea, nunca corre autofix.** Si en el
camino ves algo tan claro y seguro como para arreglarlo en el momento, no lo hagas acá —
anotalo en el reporte como "aplicable con /calidad" o como hallazgo suelto.

**Regla dura #2: nunca declares el código "excelente", "perfecto" ni des un veredicto
binario de aprobado/desaprobado.** El resultado es un reporte priorizado; la decisión de
qué tan bien está y qué se corrige la toma la persona, no la skill.

## Paso 1 — Alcance

- Con argumento archivo/carpeta/módulo → ese objetivo.
- Sin argumento → todo `src/` del repo (excluyendo `bin/`, `obj/`, `packages/`, generados).

A diferencia de `/calidad`, acá el alcance puede — y conviene que — sea más amplio que un
diff: los hallazgos de **consistencia** y **patrones** solo tienen sentido comparando contra
el resto del repo, no mirando un archivo aislado. Si el argumento es un módulo chico, igual
mirá 1-2 módulos "hermanos" (mismo tipo de servicio/controlador) como referencia de qué es
"el patrón esperado" acá.

## Paso 2 — Categorías a revisar

Para cada categoría, buscá evidencia concreta (archivo + línea o método), no impresiones
genéricas. Si una categoría no tiene hallazgos, decilo explícitamente ("sin hallazgos") en
vez de omitirla — eso también es información.

### A. Tests faltantes o débiles
- Métodos públicos con lógica no trivial (branching, cálculos, mapeos FHIR) sin ningún test
  que los ejercite.
- Tests que solo verifican que "no tira excepción" sin assertar el resultado esperado.
- Tests acoplados a detalles de implementación (mocks verificando *cómo* se llegó al
  resultado) en vez de al comportamiento observable.
- Casos borde de dominio sin cubrir: fechas límite, IDs nulos del cliente (ver ejemplo de
  colisión de PK en `/calidad`), especialidades no mapeadas, respuestas FHIR incompletas.
- Si existe un reporte de cobertura (`dotnet-coverage` u otro) en el repo, leerlo y usarlo
  en vez de estimar a ojo.

### B. Documentación de más o desactualizada
- XML-doc o comentarios que contradicen lo que el código realmente hace hoy.
- Bloques de documentación extensos sobre código trivial (ruido, no valor).
- Falta de documentación justo donde el repo la necesita: reglas de negocio clínicas,
  mapeos FHIR/SNOMED no obvios, decisiones de concurrencia u Oracle.

### C. Inconsistencias entre módulos
- El mismo problema resuelto de dos formas distintas en el repo (p. ej. una parte usa
  `SpecialtyMap`/`ArgentinaTime`/`FhirQuestionnaireReader` y otra reimplementa lo mismo a
  mano).
- Manejo de errores mezclado: algunos servicios tiran excepción, otros devuelven `null` o un
  resultado vacío para el mismo tipo de falla.
- Convenciones de nombres, de capas o de organización de carpetas que un módulo nuevo no
  sigue respecto de sus "hermanos".
- Duplicación de lógica de dominio en más de un lugar (riesgo de que diverjan con el tiempo).

### D. Malas prácticas
- Posibles N+1 o consultas repetidas en loop.
- Falta de manejo de `null`/valores faltantes donde EF o un recurso FHIR puede devolverlos.
- Magic numbers/strings sin constante ni explicación, especialmente códigos SNOMED o de
  especialidad.
- Dependencias circulares o acoplamiento de capas altas a detalles bajos (Oracle, FHIR raw)
  que deberían estar encapsulados.
- Manejo de concurrencia sospechoso (lecturas-luego-escrituras sin control de versión/lock
  donde el dominio lo necesita).

### E. Patrones y arquitectura
- Lógica de dominio filtrada a controllers/vistas en vez de vivir en servicios/dominio.
- God classes o métodos que hacen demasiadas cosas distintas y piden partirse.
- Abstracciones a medio hacer (una interfaz con una sola implementación que no aporta nada,
  o al revés: falta de abstracción donde ya hay 3+ implementaciones parecidas).
- Puntos donde un patrón conocido (strategy, factory, repository) simplificaría algo que hoy
  está resuelto con `if/else` en cascada — solo si el cambio es sustancial, no por moda.

## Paso 3 — Cómo levantar contexto (sin editar)

- Recorré el/los archivo(s) en alcance completos, no fragmentos.
- Para consistencia, buscá 1-2 archivos "equivalentes" en otras partes del repo (mismo tipo
  de componente) y comparalos contra el que estás auditando.
- Usá `git log --oneline -- <archivo>` si hace falta entender si algo es legado estable o
  código nuevo con más chance de tener deuda.
- No corras build ni tests: esta skill no modifica nada, así que no hace falta verificar
  comportamiento.

## Paso 4 — Reporte final

Organizá el reporte por severidad, no por categoría, para que se pueda priorizar de arriba
hacia abajo. **Numerá cada hallazgo de forma correlativa (1, 2, 3...) sin reiniciar el
contador entre severidades** — ese número es el que se va a usar después para aprobar o
rechazar en `/aplicar-auditoria`.

- **Crítico**: riesgo real de bug, dato mal manejado, o inconsistencia que puede romper algo
  (p. ej. un caso de colisión de PK no cubierto por tests, un manejo de nulls faltante en un
  mapeo FHIR que se usa en producción).
- **Importante**: deuda técnica clara que conviene resolver pronto (inconsistencia entre
  módulos, tests débiles en lógica de negocio central, god method que dificulta cambios).
- **Sugerencia**: mejoras de patrón/arquitectura o documentación que son opinables o de bajo
  riesgo, para que la persona decida si vale la pena.

Para cada hallazgo: **número**, archivo + método/línea, qué se observa, por qué importa, y
una propuesta concreta de qué hacer (incluyendo si implica agregar/modificar tests) — sin
aplicarla. Si dos hallazgos están tan entrelazados que no tiene sentido aprobar uno sin el
otro, decilo explícitamente ("2 depende de 1").

Cerrá con:
- Un resumen de 3-5 líneas del estado general (sin calificarlo de "excelente/perfecto").
- Qué categorías no tuvieron hallazgos.
- Una línea recordando que para implementar cualquiera de estos hallazgos hay que correr
  `/aplicar-auditoria` indicando cuáles se aprueban (por número).

No commitear ni editar nada en ningún paso de esta skill.
