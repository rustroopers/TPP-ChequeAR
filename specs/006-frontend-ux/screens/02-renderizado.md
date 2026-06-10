# Pantallas — Motor de Renderizado / Carga Clínica (Módulo 002)

## R1 — Buscador de paciente

Tipo: listado/buscador · Prioridad: P1 · Implementación: Razor

```
┌───────────────────────────────────────────────────────────────┐
│ Buscar paciente                                                │
│                                                                │
│  [DNI / Nº de HC / Apellido…              ] [Buscar]           │
│                                                                │
│  Resultados                                                    │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │ HC    │ Nombre          │ Edad │ Especialidad │ Abrir     ││
│  │ 12345 │ García, Ana     │ 8a   │ Cardio       │ → cargar  ││
│  │ 12346 │ López, Mateo    │ 4a   │ Neumo        │ → cargar  ││
│  └──────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────┘
```

Estados: empty inicial ("Buscá por DNI o HC para empezar") / no-results / loading.
Auditoría: registrar consulta de paciente.

---

## R2 — Selector de cuestionario para paciente

Tipo: modal · Prioridad: P1

```
┌────────────────────────────────────────────────────────┐
│ ¿Qué ficha cargás hoy para Ana García (HC 12345)?      │
│                                                        │
│ [Buscar cuestionario…]    Espec.: [Cardiología ▾]      │
│                                                        │
│  ○ Cardio basal           v3   Última carga: 2026-05   │
│  ○ Cardio seguimiento     v1   Última carga: nunca     │
│  ○ Cardio post-quirúrgico v2   Última carga: 2026-03   │
│                                                        │
│              [ Cancelar ]   [ Comenzar carga ]         │
└────────────────────────────────────────────────────────┘
```

---

## R3 — Render dinámico de ficha clínica (isla Vue)

Tipo: formulario dinámico · Prioridad: P1 · Implementación: **Vue island** `questionnaire-renderer`

```
┌──────────────────────────────────────────────────────────────────────┐
│ Cardio basal · v3 · Ana García (HC 12345)        [💾 Borrador 12:04]│
├──────────────────────┬───────────────────────────────────────────────┤
│ Índice               │ Datos del paciente                            │
│                      │                                                │
│ ✓ Datos paciente     │ Edad *           [  8  ] años                  │
│ ● Signos vitales     │ Sexo *           ( ●F  ○M  ○Otro )             │
│ ○ Antecedentes       │                                                │
│ ○ Examen físico      │ ─────────────────────────────────────────────  │
│ ○ Conclusión         │ Signos vitales                                 │
│                      │                                                │
│ 12 / 28 completas    │ TA sistólica *   [ 110 ] mmHg                  │
│                      │ TA diastólica *  [  70 ] mmHg                  │
│                      │ FC *             [  95 ] lpm                   │
│                      │   ⚠ Por encima de p95 para edad                │
│                      │                                                │
│                      │ Antecedentes                                   │
│                      │ ¿HTA? *          ( ○Sí  ●No )                  │
│                      │ ¿Diabetes?       ( ○Sí  ●No )                  │
│                      │ (si HTA=Sí → aparece "Tratamiento actual")     │
│                      │                                                │
├──────────────────────┴───────────────────────────────────────────────┤
│ [ Guardar borrador ]                          [ Finalizar y enviar ] │
└──────────────────────────────────────────────────────────────────────┘
```

Componentes Vue: `<SectionNav>` (índice), `<QuestionGroup>`, `<Question type=...>` por tipo.
Estados:
- por pregunta: `pristine | dirty | valid | invalid | loading-validation`.
- global: `loading | editing | saving | saved | submitting | error`.
- borrador: badge con timestamp del último autosave.

Interacciones:
- Autosave debounced (2s) tras cambio.
- `enableWhen` re-evalúa al cambiar dependencias; preguntas ocultas no se validan ni envían.
- Validación servidor al finalizar (rebote de errores con scroll-to-first-error).
- Accesibilidad: `aria-live` para apariciones condicionales; foco gestionado.

---

## R4 — Banner de pre-carga FHIR

Tipo: alerta · Prioridad: P2 · Implementación: parte de R3

```
┌─────────────────────────────────────────────────────────────┐
│ ℹ 5 campos se completaron desde el sistema hospitalario.    │
│   [Ver origen]  [Descartar pre-carga]                       │
└─────────────────────────────────────────────────────────────┘
```

---

## R5 — Confirmación guardado parcial / reanudación

Tipo: modal · Prioridad: P2

Al re-entrar a la ficha con borrador existente:

```
┌──────────────────────────────────────────────────┐
│ Tenés un borrador del 09/06 14:22                │
│                                                  │
│      [ Descartar ]     [ Reanudar ]              │
└──────────────────────────────────────────────────┘
```

---

## R6 — Visor de ficha histórica (read-only)

Tipo: detalle · Prioridad: P3 · Implementación: Razor

Renderiza una `QuestionnaireResponse` finalizada en modo solo lectura. Muestra autor, fecha, especialidad, opcional comparación con versión actual del cuestionario.

---

## R7 — Timeline de fichas previas del paciente

Tipo: listado vertical · Prioridad: P2

```
┌────────────────────────────────────────────────────────────┐
│ Historial · Ana García (HC 12345)                          │
│                                                            │
│  ● 2026-06-09  Cardio basal v3       Pérez, A.    [Abrir]  │
│  │                                                         │
│  ● 2026-05-15  Cardio seguimiento v1 Gómez, R.    [Abrir]  │
│  │                                                         │
│  ● 2026-03-02  Cardio basal v2       Pérez, A.    [Abrir]  │
└────────────────────────────────────────────────────────────┘
```
