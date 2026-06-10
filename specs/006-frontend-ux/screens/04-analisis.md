# Pantallas — Análisis y Exportación (Módulo 004)

## E1 — Listado de reportes guardados

Tipo: listado · Prioridad: P2 · Implementación: Razor

```
┌────────────────────────────────────────────────────────────────┐
│ Reportes                              [ + Nueva consulta ]     │
│                                                                │
│ [Buscar nombre…]   [Cuestionario ▾]   [Autor ▾]                │
│                                                                │
│ ┌────────────────────────────────────────────────────────────┐ │
│ │ Nombre              │ Cuestionario  │ Autor   │ Últ. uso    │ │
│ │ HTA pediátrica 2026 │ Cardio basal  │ Pérez   │ 2026-06-08  │ │
│ │ Cohorte EPOC        │ Neumo control │ Gómez   │ 2026-05-30  │ │
│ └────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────┘
```

Click en fila abre E2 con la consulta cargada.

---

## E2 — Query Builder (isla Vue)

Tipo: formulario dinámico · Prioridad: P1 · Implementación: **Vue island** `report-builder`

```
┌────────────────────────────────────────────────────────────────────┐
│ Nueva consulta                            [Guardar]  [Previsualizar]│
├────────────────────────────────────────────────────────────────────┤
│ Cuestionario base   [ Cardio basal ▾ ]    Versión [ todas ▾ ]      │
│ Rango de fechas     [ 2026-01-01 ] a [ 2026-06-10 ]                │
│ Especialidad        [ Cardiología ▾ ]                              │
│                                                                    │
│ Filtros sobre preguntas                                            │
│ ┌────────────────────────────────────────────────────────────────┐ │
│ │ [Edad ▾]            [entre ▾]   [ 5 ] y [ 12 ]            [✕]  │ │
│ │ [TA sistólica ▾]    [≥ ▾]       [ 120 ]                  [✕]  │ │
│ │ [HTA ▾]             [es ▾]      [ Sí ▾ ]                 [✕]  │ │
│ │ [+ Agregar filtro]                                             │ │
│ │ Combinación: (●) Y todas   ( ) O cualquiera                    │ │
│ └────────────────────────────────────────────────────────────────┘ │
│                                                                    │
│ Columnas en preview                                                │
│   [✓] Paciente (HC)   [✓] Edad   [✓] TA sist.   [✓] HTA            │
│   [+ Más columnas]                                                 │
│                                                                    │
│ Resultado estimado: 142 registros                                  │
└────────────────────────────────────────────────────────────────────┘
```

Componentes Vue: `<FilterRow>` (selector pregunta → operador → valor según tipo), `<ColumnPicker>`.
Estados: editando / validando / preview-loading / preview-ready / error.
Interacciones: operadores condicionados al tipo de pregunta; debounce 800 ms para recálculo del estimado.

---

## E3 — Preview tabular de resultados

Tipo: listado · Prioridad: P1 · Implementación: Razor (con datatable client-side sobre dataset paginado por servidor)

```
┌────────────────────────────────────────────────────────────────────┐
│ Preview · HTA pediátrica 2026          [ Volver ]  [ Exportar ▾ ]  │
│ Total: 142 registros                                               │
│                                                                    │
│ ┌──────┬──────┬───────────┬───────┐                                │
│ │ HC   │ Edad │ TA sist.  │ HTA   │                                │
│ │ 1234 │  9   │   125     │  Sí   │                                │
│ │ 1235 │  6   │   118     │  Sí   │                                │
│ │ …                                                                │
│ └──────┴──────┴───────────┴───────┘                                │
│ Mostrando 1-50 de 142    [ ◀ 1 2 3 ▶ ]                             │
└────────────────────────────────────────────────────────────────────┘
```

PII: cuando la consulta no requiere identidad, la columna HC se reemplaza por hash anónimo.

---

## E4 — Modal de exportación

Tipo: modal · Prioridad: P1

```
┌─────────────────────────────────────────────────────────┐
│ Exportar                                                │
│                                                         │
│ Formato     ( ●) Excel (.xlsx)   ( ) CSV                │
│ Codificación CSV   [ UTF-8 con BOM ▾ ]                  │
│                                                         │
│ ☑ Anonimizar (reemplazar HC por hash)                   │
│ ☑ Incluir metadatos (versión, fecha)                    │
│                                                         │
│ Filas a exportar: 142                                   │
│                                                         │
│          [ Cancelar ]    [ Descargar ]                  │
└─────────────────────────────────────────────────────────┘
```

Descarga > 10k filas dispara modo asíncrono ("Te notificamos cuando esté listo").

---

## E5 — Vista de estadísticas básicas

Tipo: dashboard · Prioridad: P3 · Implementación: Razor + Chart.js

```
┌─────────────────────────────────────────────────────────┐
│ Estadísticas · HTA pediátrica 2026                      │
│                                                         │
│ ┌─────────────────┐  ┌─────────────────┐                │
│ │ Distribución    │  │ HTA: Sí vs No   │                │
│ │ por edad        │  │                 │                │
│ │ [bars]          │  │ [pie]           │                │
│ └─────────────────┘  └─────────────────┘                │
│ ┌──────────────────────────────────────┐                │
│ │ TA sistólica · histograma            │                │
│ │ [bars]                               │                │
│ └──────────────────────────────────────┘                │
└─────────────────────────────────────────────────────────┘
```

---

## E6 — Modal guardar consulta

Tipo: modal · Prioridad: P2

Nombre + descripción + compartir (sólo yo / mi especialidad / todos). Se guarda como `SavedReport` en `ChequeAR.Domain`.
