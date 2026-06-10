# Pantallas — FHIR / Admin (Módulo 003)

> El módulo 003 es predominantemente backend. Sólo expone dos pantallas administrativas.

## F1 — Panel de salud de integraciones

Tipo: dashboard · Prioridad: P2 · Implementación: Razor

```
┌────────────────────────────────────────────────────────────┐
│ Integraciones                                              │
│                                                            │
│ ┌──────────────────────────┐ ┌──────────────────────────┐  │
│ │ ● FHIR Hospital          │ │ ● Oracle Vistas          │  │
│ │   Estado: OK             │ │   Estado: OK             │  │
│ │   Último ping: 14:02:11  │ │   Último ping: 14:02:09  │  │
│ │   Latencia p95: 218 ms   │ │   Latencia p95: 96 ms    │  │
│ │   [Reintentar]           │ │   [Reintentar]           │  │
│ └──────────────────────────┘ └──────────────────────────┘  │
│                                                            │
│ ┌──────────────────────────┐ ┌──────────────────────────┐  │
│ │ ● Auth Hospital          │ │ ● Validador FHIR         │  │
│ │   Estado: OK             │ │   Estado: OK             │  │
│ │   Último ping: 14:02:08  │ │   Profiles: 12 cargados  │  │
│ └──────────────────────────┘ └──────────────────────────┘  │
│                                                            │
│ Últimos errores                                            │
│ ┌────────────────────────────────────────────────────────┐ │
│ │ 13:45 · FHIR · 502 al consultar Observation/12345      │ │
│ │ 11:12 · Oracle · timeout vista VW_PACIENTES            │ │
│ └────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────┘
```

Componentes: tarjetas KPI con `cq-badge-status` (verde/amarillo/rojo), tabla simple de errores recientes.
Estados: cada card refleja `OK | Degraded | Down | Unknown`.
Auto-refresh cada 30 s (configurable).

---

## F2 — Visor de CapabilityStatement

Tipo: detalle read-only · Prioridad: P3 · Implementación: Razor + viewer JSON

```
┌────────────────────────────────────────────────────────────┐
│ CapabilityStatement                  [Descargar JSON]      │
│                                                            │
│ FHIR Version: 5.0.0                                        │
│ Software: ChequeAR 1.0                                     │
│ Implementation: Hospital Garrahan / Producción              │
│                                                            │
│ Recursos soportados                                        │
│ ┌─────────────────────┬──────────────────────────────────┐│
│ │ Recurso             │ Operaciones                       ││
│ │ Questionnaire       │ read · search · create · update   ││
│ │ QuestionnaireResp.  │ read · search · create · update   ││
│ │ Patient             │ read · search                     ││
│ │ Observation         │ read · search                     ││
│ └─────────────────────┴──────────────────────────────────┘│
│                                                            │
│ Profiles                                                   │
│   · ChequeARQuestionnaire                                  │
│   · ChequeARQuestionnaireResponse                          │
└────────────────────────────────────────────────────────────┘
```

Sólo lectura. Apta para auditores externos.
