# Pantallas — Gestor de Cuestionarios (Módulo 001)

## Q1 — Listado de cuestionarios

Tipo: listado · Prioridad: P1 · Implementación: Razor + DataTables

```
┌─────────────────────────────────────────────────────────────────┐
│ Cuestionarios                          [ + Nuevo cuestionario ] │
│                                                                  │
│ [Buscar título…]  [Especialidad ▾]  [Estado ▾]  [Autor ▾]       │
│                                                                  │
│ ┌──────────────────────────────────────────────────────────────┐│
│ │ Título           │ Espec.   │ Versión │ Estado  │ Actualizado││
│ │ Cardio basal     │ ●Cardio  │ v3      │ ●Public │ 2026-06-08 ││
│ │ Neumo control    │ ●Neumo   │ v1      │ ●Borrad │ 2026-06-09 ││
│ │ Onco seguimiento │ ●Onco    │ v7      │ ●Archiv │ 2026-04-12 ││
│ │ …                                                             ││
│ └──────────────────────────────────────────────────────────────┘│
│ Mostrando 1-20 de 47          [ ◀ 1 2 3 ▶ ]                     │
└─────────────────────────────────────────────────────────────────┘
```

Componentes: barra de filtros (form-inline), `cq-badge-status`, chip de especialidad coloreado.
Estados: loading (skeleton de 5 filas) / empty ("Aún no hay cuestionarios" + CTA crear) / filtered-empty / error.
Interacciones: click en fila abre Q5 preview; menú contextual con Editar/Publicar/Archivar/Duplicar/Versiones.

---

## Q2 — Editor de cuestionario (isla Vue)

Tipo: wizard/canvas · Prioridad: P1 · Implementación: **Vue island** `questionnaire-editor`

```
┌───────────────────────────────────────────────────────────────────────┐
│ Cardio basal · v3 (borrador)            [Vista previa] [Guardar] [⋮] │
├──────────────┬────────────────────────────────────┬───────────────────┤
│ Tipos        │ Estructura                          │ Propiedades       │
│              │                                     │                   │
│ ▸ Texto      │ ▾ 📁 Datos del paciente             │ Pregunta          │
│ ▸ Número     │   • Edad (integer)                  │ Tipo: integer     │
│ ▸ Decimal    │   • Sexo (choice)                   │ Texto: "Edad"     │
│ ▸ Booleano   │ ▾ 📁 Signos vitales                 │ Unidad: años      │
│ ▸ Opción     │   • TA sistólica (decimal)          │ Min: 0 / Max: 120 │
│ ▸ Fecha      │   • TA diastólica (decimal)         │ Obligatorio: ☑    │
│ ▸ Adjunto    │   • FC (integer)                    │ enableWhen: …     │
│              │ ▾ 📁 Antecedentes                    │ Código LOINC: …   │
│              │   • HTA (boolean)                   │                   │
│              │ + Agregar grupo  + Agregar pregunta │ [Eliminar]        │
│              │                                     │                   │
│              │ [drag para reordenar]               │                   │
└──────────────┴────────────────────────────────────┴───────────────────┘
```

Componentes Vue: `<TypePalette>`, `<GroupTree>` (SortableJS), `<PropertiesPanel>`.
Estados: loading (carga JSON) / saving (autosave debounced 2s) / dirty / clean / error.
Interacciones:
- Drag-and-drop de tipos sobre estructura para crear preguntas.
- Drag-and-drop dentro del árbol para reordenar / re-anidar.
- Selección de nodo → propiedades a la derecha.
- Atajos: `Cmd/Ctrl+S` guardar, `Cmd/Ctrl+P` preview.

---

## Q3 — Editor de pregunta (modal)

Tipo: modal · Prioridad: P1 · Implementación: parte de la isla Q2

Variantes según `type`. Campos mínimos: texto pregunta, código FHIR, ayuda, obligatoriedad, validaciones específicas del tipo.

---

## Q4 — Editor de lógica condicional `enableWhen`

Tipo: modal · Prioridad: P2 · Implementación: parte de la isla Q2

```
┌──────────────────────────────────────────────────────┐
│ Mostrar esta pregunta sólo si…                       │
│                                                      │
│ [Pregunta ▾ "HTA"]  [Operador ▾ "es igual a"]  [Sí▾] │
│ [+ Agregar condición]    Combinación: (●) Y  ( ) O   │
│                                                      │
│                       [ Cancelar ]  [ Guardar ]      │
└──────────────────────────────────────────────────────┘
```

---

## Q5 — Vista previa del cuestionario

Tipo: detalle read-only · Prioridad: P1 · Implementación: Razor + isla R3 en modo `preview`

Renderiza el mismo cuestionario como lo verá el médico, pero sin guardar respuestas. CTA "Volver al editor".

---

## Q6 — Modal Publicar

Tipo: modal · Prioridad: P1

```
┌──────────────────────────────────────────────────┐
│ Publicar cuestionario                            │
│                                                  │
│ Versión actual: v3 (borrador)                    │
│ Nueva versión: v4  (publicada)                   │
│                                                  │
│ Notas del cambio                                 │
│ ┌──────────────────────────────────────────────┐ │
│ │ Agregada pregunta "Antecedentes familiares"  │ │
│ └──────────────────────────────────────────────┘ │
│                                                  │
│ ⚠ Una vez publicada, esta versión queda inmutable│
│                                                  │
│             [ Cancelar ]   [ Publicar ]          │
└──────────────────────────────────────────────────┘
```

---

## Q7 — Modal Archivar

Tipo: modal · Prioridad: P1

Confirmación simple. Indica que las respuestas existentes se conservan; sólo se desactiva para nuevas cargas.

---

## Q8 — Gestor de adjuntos por pregunta

Tipo: modal · Prioridad: P2 · Implementación: parte de la isla Q2

Sube imágenes / PDFs asociados a una pregunta (ej.: ECG de referencia). Validaciones cliente: tipo, tamaño ≤ 5MB.

---

## Q9 — Historial de versiones

Tipo: listado · Prioridad: P2 · Implementación: Razor

```
┌──────────────────────────────────────────────────────────┐
│ Historial · Cardio basal                                  │
│                                                           │
│ ┌───┬─────────────┬──────────┬───────────┬──────────────┐│
│ │ v │ Fecha       │ Autor    │ Estado    │ Acciones      ││
│ │ 3 │ 2026-06-01  │ Pérez    │ ●Publica  │ Ver · Comparar││
│ │ 2 │ 2026-05-15  │ Pérez    │ ●Archiva  │ Ver · Comparar││
│ │ 1 │ 2026-04-20  │ Gómez    │ ●Archiva  │ Ver · Comparar││
│ └───┴─────────────┴──────────┴───────────┴──────────────┘│
└──────────────────────────────────────────────────────────┘
```
