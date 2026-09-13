# Testing hecho por Facu — Feedback y Plan de Mejoras

Documento que consolida el feedback de una sesión de testing manual sobre la aplicación
ChequeAR. Está organizado en:

1. **Etapas de trabajo** — items agrupados por área/prioridad, listos para llevar a `tasks.md`.
2. **Cosas a definir** — decisiones de producto/UX que requieren discusión con Carolo y/o Gonza
   antes de implementar.
3. **Notas técnicas de referencia** — apuntes útiles para quien tome cada tarea.

> Autor del feedback: Facundo Calderán
> Rama: `testing-hecho-por-facu`
> Fecha: 2026-09-05

---

## Leyenda de prioridad

- 🔴 **P0** — bug funcional o bloqueante de usabilidad, debe entrar sí o sí.
- 🟠 **P1** — mejora de UX importante, alto impacto percibido.
- 🟡 **P2** — pulido / consistencia visual.
- 🔵 **?** — requiere definición antes de estimar (ver sección "A definir").

---

# Etapa 1 — Fixes rápidos y consistencia visual (bajo riesgo)

Cambios chicos, sin decisiones de producto de por medio. Se pueden agrupar en un solo PR de
"UI polish".

## 1.1 Sistema de colores (design tokens)

Hoy conviven varios azules "fuertes y feos" en botones primarios (Guardar, Buscar, Ver
evolución) y grises que se leen como *disabled* cuando en realidad son acciones habilitadas
(Agregar sección/pregunta, Historial, íconos de tacho).

Definir en el CSS/SCSS del proyecto (probablemente en `ChequeAR.Api/Content/` o donde vivan los
estilos de las islas Vue) una paleta con al menos:

- `--color-primary` — acción principal (Guardar, Publicar). Elegir un tono que no sea el azul
  saturado actual.
- `--color-secondary` — acciones secundarias importantes (Ver evolución, Ver historial).
- `--color-tertiary` — acciones neutras/utilitarias (hoy en gris).
- `--color-danger` — destructivo (Eliminar, Quitar código).
- `--color-disabled` — reservado *sólo* para elementos realmente inhabilitados.

**Regla:** ningún botón habilitado puede usar el token de `disabled`. 🟠 P1

## 1.2 Estados hover / cursor

- 🔴 Dropdown de acciones (⋯ tres puntos) en la lista de cuestionarios: los ítems no tienen
  hover, no se sabe qué se está por clickear.
- 🔴 Dropdown de "Tipo de respuesta" al agregar pregunta: al elegir un tipo no marca cuál está
  seleccionado.
- 🟠 Ícono ✕ rojo al lado del tipo de respuesta y el ícono de tacho de sección: sin hover, no
  hay feedback visual al pasar por encima.
- 🟠 En el dropdown al hacer hover sobre un tipo, el cursor debería ser `pointer` (hoy aparece
  la "manito" sólo en algunos casos, revisar consistencia).

## 1.3 Textos / mensajes vacíos (empty states)

- 🔴 Cuando no hay usuarios logueados en el sistema, la home muestra las columnas pero **no
  aparece ningún mensaje** de "no hay usuarios". Agregar empty state explícito.
- 🔴 Al tocar "Guardar" sin cambios reales, aparece un cartel azul feo diciendo *"no hay datos
  para guardar"* aunque la estructura decía *"cambios sin guardar"*. Contradicción de estado:
  hay que unificar cómo se calculan los cambios pendientes y qué mensaje se muestra.
- 🟡 Card "Sin movimientos registrados" en el detalle de ficha: está muy chica, extenderla al
  ancho de la card contenedora.
- 🟡 Card "Sin fichas previas" en pantalla de paciente: no llega al extremo derecho, se ve
  cortada.

## 1.4 Íconos que hay que reemplazar / estilizar

- 🟡 Ícono del tachito de "Eliminar pregunta": buscar un ícono más moderno y meterlo dentro de
  una card/button con borde, para que se lea como botón y no como decoración.
- 🟡 Ícono del tachito de sección: mismo tratamiento.
- 🟡 Al publicar el cuestionario, cada código SNOMED aparece con un ✓ verde + link "Quitar".
  Reemplazar por una ✕ (o tacho chico) con **tooltip on hover** que diga *"Quitar código"*, y
  fondo tipo pill/badge.
- 🔵 **A debatir**: reemplazar el ícono actual de "Pacientes" en la sidebar por uno más acorde
  (ver sección "A definir").

## 1.5 Cursor "manito" en tipos de respuesta

🟠 En la creación de cuestionario, al pasar el cursor por encima de los distintos "tipos"
aparece cursor `pointer` aunque no todos son clickeables. Revisar y aplicar `cursor: pointer`
sólo donde efectivamente hay acción.

## 1.6 Ruteo cosmético

🟡 `https://localhost:44301/Encounters/Search` — la palabra "Search" no debería estar en la URL,
la búsqueda es interna vía request. Renombrar la ruta (p.ej. `/Encounters` o
`/Encounters/Buscar` si tiene que ser explícito), y mover el filtro a un query param.

---

# Etapa 2 — Rediseño de la Home (dashboard demostrativo)

**Problema:** hoy la home tiene botones de Ficha / Cuestionarios / Reportes que sólo duplican
la barra lateral. No aporta valor.

**Objetivo:** que la home muestre *información* en vez de accesos.

## 2.1 Bloques / cards por dominio

Cada card muestra el contenido real (o un empty state), no un botón:

- **Card "Reportes"** — renderiza un gráfico del último reporte disponible. Si no hay reportes,
  el gráfico queda vacío pero visible (skeleton o placeholder). Un `<select>` en la esquina
  superior derecha permite cambiar qué reporte se muestra.
- **Card "Ficha reciente"** — muestra un resumen de la última ficha cargada (paciente,
  parámetros clave). Empty state si no hay fichas.
- **Card "Cuestionarios"** — muestra un preview del cuestionario más usado o más reciente, con
  dropdown para cambiar cuál se previsualiza.

## 2.2 Sidebar colapsable

🔴 **P0** La barra lateral no colapsa a íconos. Implementar:

- Botón de toggle (hamburguesa o chevron) que colapsa a modo *icon-only*.
- Persistir preferencia en `localStorage`.
- **Agrandar tanto los íconos como el texto** — hoy ambos se ven chicos.
- Tooltip on hover cuando está colapsada, mostrando la etiqueta.

## 2.3 Tooltips guía para médicos

🟠 Agregar tooltips explicativos en los puntos clave del flujo (crear cuestionario, agregar
sección, publicar, cargar ficha, ver evolución) para orientar a médicos que no son *power
users*. Considerar además un **tour guiado** opcional la primera vez.

---

# Etapa 3 — Editor de cuestionarios (rework mayor)

Este es el bloque más grande. El feedback es unánime: la UI actual es engorrosa y no guía.

## 3.1 Flujo step-by-step

🔴 **P0** Reemplazar la pantalla actual por un wizard con pasos claros:

1. Metadatos (título, especialidad, tipo).
2. Estructura (secciones y preguntas).
3. Códigos SNOMED / validación clínica.
4. Preview.
5. Publicación.

En cada paso, un panel lateral con ayuda contextual ("qué es esto", "cómo llenarlo").

## 3.2 SNOMED CT — autocomplete y validación

- 🔴 **Bug**: el autocomplete de término clínico no funciona y **no aparece el error en la
  pestaña Network**. Investigar:
  - ¿Se está haciendo la request a Snowstorm?
  - ¿Falla silenciosamente en el cliente (try/catch que se traga el error)?
  - Revisar `ChequeAR.Infrastructure` (cliente HTTP de Snowstorm) y la isla Vue que consume el
    endpoint.
- 🔴 **Validación**: si el médico escribe manualmente el término y el código SNOMED, no hay
  chequeo de que coincidan. ¿Qué garantía tenemos de que lo puso bien? Opciones:
  - Bloquear la entrada manual y forzar selección desde autocomplete.
  - Permitir entrada manual pero validar contra Snowstorm al blur / on save y marcar
    inconsistencias.
  - Mostrar el `preferredTerm` oficial al lado del código escrito para confirmación visual.
- 🔵 **A definir**: si el médico *no encuentra* el término exacto en Snowstorm, ¿qué hacemos?
  ¿Lo obligamos a buscar el SNOMED CT primero? ¿Permitimos texto libre marcado como "no
  codificado"? Ver sección "A definir".

## 3.3 Tipos de respuesta — bugs y limitaciones

- 🔴 Al agregar una pregunta, el "tipo de respuesta" aparece en gris claro (parece disabled).
  Sacar ese estilo.
- 🔴 **No deja modificar el tipo de respuesta** de una pregunta ya creada: obliga a borrar y
  volver a agregar. Permitir edición in-place (con confirmación si cambiar el tipo invalida
  datos existentes).
- 🟠 Card "TIPOS" — si no sirve para nada, eliminarla. Es información inútil que ocupa espacio.

## 3.4 Sección / carpeta — replantear

🟠 El ícono de carpeta con nombre de sección no aporta nada si al agregar una nueva pregunta
igual se separan en cards distintas. Definir:

- Si las secciones son agrupadores visuales reales → colapsar sus preguntas dentro.
- Si no lo son → eliminar el concepto y quedarse sólo con preguntas planas ordenables.

## 3.5 Categorización cuando el input es lenguaje natural

🔵 **A definir con Carolo**: el médico está interpretando un relato ("dice \"me duele acá\"")
y traduciéndolo a términos clínicos. Casos como Munchausen requieren capturar la cita textual
del paciente. Opciones:

- Campo libre paralelo al término codificado ("Texto del paciente" + "Interpretación clínica").
- Anotación tipo highlight sobre un párrafo libre.
- Ver cómo lo resuelven otras herramientas de historia clínica.

## 3.6 Comentarios en cuestionarios / preguntas

🔵 **A definir**: ¿se pueden agregar comentarios al cuestionario o a cada pregunta? Hoy no
existe la feature. Discutir si tiene sentido incorporarla (colaboración entre médicos,
notas para revisión, feedback pre-publicación).

## 3.7 Pérdida de datos en Propiedades

🔴 **Bug**: en algún momento se pierden los datos cargados dentro del panel de Propiedades.
Reproducir y arreglar. Probablemente un problema de reactividad en la isla Vue (referencia
compartida que se resetea al cambiar de pregunta seleccionada).

## 3.8 Mobile

🔴 **P0** — **IMPORTANTE**: el editor de cuestionarios (y en general toda la app) debe
funcionar **perfectamente en celular**. Auditoría full de responsive:

- Breakpoints declarados vs. realidad.
- Sidebar → drawer en mobile.
- Editor → probablemente el wizard step-by-step (3.1) ya ayuda mucho.
- Inputs, dropdowns, tooltips → verificar touch targets ≥ 44px.

---

# Etapa 4 — Lista de cuestionarios

## 4.1 Click en toda la fila

🟠 Hoy hay que tocar exactamente el título para entrar al detalle. Hacer que toda la fila
(o al menos la card entera) sea clickeable.

## 4.2 Ordenamiento

🔵 **A definir**: ¿vale la pena permitir ordenar? ¿Por qué campo por defecto (fecha creación,
último uso, alfabético)? Discutir con Gonza.

## 4.3 Menú "⋯" — hover y opciones

- 🔴 Los ítems del dropdown no tienen hover (ya listado en 1.2, se repite acá por contexto).
- 🔵 **A definir**: hoy sólo permite **archivar**. ¿Corresponde también permitir borrar?
  Reglas:
  - Si el cuestionario ya fue usado en alguna ficha → **no** se puede borrar (integridad
    referencial), sólo archivar.
  - Si nunca fue usado → se puede borrar duro.
  - Definir con Carolo si archivar cambia el `estado` del cuestionario o es un flag aparte.

## 4.4 Estado vs. archivado

🔵 Relacionado con 4.3: si el archivado ya modifica el estado, ¿tiene sentido mostrar la
columna "Estado" separada? Revisar la máquina de estados.

## 4.5 Título "TRABAJO"

🔵 **A definir**: el título actual "TRABAJO" no dice nada. Opciones:
- "Mis cuestionarios"
- "Cuestionarios del servicio"
- "Cuestionarios activos"
- Depende del scope real (¿son míos, del equipo, del hospital?).

## 4.6 Pantalla de pacientes — searcher vs. listado

🔵 **A definir con Carolo**: hoy `/Pacientes` es un **searcher**, no un listado de todos con
filtros. Preguntar el porqué de esta decisión:

- ¿Es por privacidad (LOPD / secreto médico)?
- ¿Es por volumen (no se puede paginar 200k pacientes)?
- ¿Es una decisión de UX intencional?

Según la respuesta, evaluar si conviene sumar un modo "listado + filtros" o mantener sólo
búsqueda.

---

# Etapa 5 — Pantalla de paciente

## 5.1 Datos de la tabla

- 🔴 **Falta la columna DNI** en la tabla de resultados.
- 🟠 En vez de "Edad", mostrar **fecha de nacimiento con edad entre paréntesis**:
  `12/03/1985 (40)`.

## 5.2 Botones y colores

- 🔴 Botón "Historial" se ve gris → parece disabled. Aplicar el token `--color-secondary` de
  la etapa 1.1.
- 🟠 Botón "Ver evolución" está en azul primary, igual que "Guardar". Bajarlo a secondary
  (o tertiary si corresponde).

## 5.3 Navegación

🔴 **P0** — Al entrar a "Ver historial" no hay botón para volver. Agregar breadcrumb o botón
"← Volver" claro.

## 5.4 Layout de filtros

🟠 Los filtros (Cuestionario, Especialidad, Autor, Estado, Período) están en 2 filas: los 4
primeros arriba y Período debajo. **Ponerlos todos en la misma fila** (o justificar el
salto con un separador visual si hay que agrupar semánticamente).

## 5.5 Card de fichas ancho

🟡 Ver 1.3, la card "Sin fichas previas" no llega al extremo derecho.

## 5.6 Título "Historial · <Paciente>"

🟠 Reemplazar por:

```
Historial                          ← título en negrita, tamaño grande
<Nombre del paciente> (40)         ← subtítulo, tamaño menor
```

## 5.7 Detalle de ficha

🔵 **A definir**:
- ¿Para qué sirve el detalle de la ficha?
- ¿Qué cosas se pueden cargar ahí?
- La card interna con "Sin movimientos registrados" es chica: ¿es diseño intencional o debería
  extenderse? (ver 1.3).

Necesitamos que Carolo/Gonza clarifiquen el propósito antes de rediseñar.

---

# Cosas a definir (para hablar con Carolo / Gonza)

Resumen consolidado de todos los 🔵 dispersos, para llevar a una única reunión:

| # | Tema | Decisión pendiente |
|---|---|---|
| D1 | Ícono de "Pacientes" en sidebar | ¿Cuál usamos? (proponer 2-3 opciones) |
| D2 | SNOMED sin match exacto | ¿Texto libre marcado, o forzar búsqueda previa? |
| D3 | Captura de "dice el paciente..." | ¿Campo paralelo, highlight, otro patrón? |
| D4 | Comentarios en cuestionarios/preguntas | ¿Feature deseada? ¿Alcance? |
| D5 | Concepto de "sección" | ¿Es agrupador real o se elimina? |
| D6 | Ordenamiento de lista de cuestionarios | ¿Campo por defecto? |
| D7 | Archivar vs. borrar cuestionarios | Reglas según uso previo |
| D8 | Estado vs. archivado | ¿Se solapan? ¿Se mantiene columna? |
| D9 | Título "TRABAJO" | Scope real del listado |
| D10 | Pacientes como searcher | ¿Por qué? ¿Sumamos modo listado? |
| D11 | Propósito del detalle de ficha | Qué se carga, qué se muestra |

---

# Notas técnicas de referencia

- **Islas Vue**: viven en `src/ChequeAR.Api/Scripts/islands/`. Los cambios de UI del editor de
  cuestionarios, dashboards y filtros probablemente caen ahí.
- **Estilos**: revisar dónde están hoy centralizados. Si no hay un archivo de tokens/variables,
  crearlo antes de tocar colores individuales (tarea 1.1).
- **Snowstorm**: cliente HTTP en `ChequeAR.Infrastructure`. El bug de autocomplete (3.2) hay
  que rastrearlo desde la isla Vue del editor → endpoint del Api → cliente Snowstorm.
- **Máquina de estados de cuestionarios**: revisar el enum en `ChequeAR.Domain` antes de tocar
  archivado/borrado (4.3, 4.4).
- **Integridad referencial** para borrar cuestionarios: chequear FKs desde `Encounters` /
  fichas hacia `Questionnaires`.
- **Responsive**: no hay evidencia de tests de mobile en el repo. La auditoría de 3.8 debería
  incluir capturas antes/después en al menos iPhone SE (375px), iPhone 14 (390px), y un tablet.
- **Empty states**: buscar en el repo si ya existe un componente `<EmptyState>` reutilizable;
  si no, crearlo (varias tareas de 1.3 lo van a necesitar).

---

# Sugerencia de orden de ejecución

1. **Sprint 1** — Etapa 1 completa (fixes rápidos + tokens de color). Base sobre la que se
   apoyan las siguientes etapas.
2. **Sprint 2** — Etapa 2 (home + sidebar colapsable) y Etapa 4 (lista de cuestionarios).
3. **Reunión de definiciones** — llevar la tabla de "A definir" a Carolo/Gonza.
4. **Sprint 3** — Etapa 5 (pantalla de paciente) con decisiones ya tomadas.
5. **Sprint 4-5** — Etapa 3 (editor de cuestionarios), el rework más grande. Meter mobile
   (3.8) en paralelo, no al final.
