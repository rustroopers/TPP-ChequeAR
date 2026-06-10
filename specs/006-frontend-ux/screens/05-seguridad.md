# Pantallas — Seguridad y Auditoría (Módulo 005)

## A1 — Login

Tipo: auth · Prioridad: P1 · Implementación: Razor

```
┌─────────────────────────────────────────────────┐
│                                                 │
│                  [ logo Garrahan ]              │
│                                                 │
│                   ChequeAR                      │
│        Fichas clínicas interoperables           │
│                                                 │
│   Usuario                                       │
│   [                                  ]          │
│   Contraseña                                    │
│   [                                  ]          │
│                                                 │
│   [☐] Recordarme                                │
│                                                 │
│              [ Ingresar ]                       │
│                                                 │
│   ¿Olvidaste tu contraseña?                     │
│                                                 │
│   v1.0 · Hospital de Pediatría Garrahan         │
└─────────────────────────────────────────────────┘
```

Sin navbar/sidebar. Background con logo institucional sutil. Soporta delegación al sistema del hospital (form post → endpoint hospital).

Estados: idle / submitting / error (mensaje genérico, no enumera usuarios).
Auditoría: registro de cada intento con resultado.

---

## A2 — Sesión expirada / re-login

Tipo: modal/página · Prioridad: P1

Cuando un endpoint devuelve 401 en una sesión activa, mostrar overlay:

```
┌──────────────────────────────────────────────┐
│ Tu sesión expiró                             │
│                                              │
│ Ingresá tu contraseña para continuar         │
│ trabajando sin perder cambios.               │
│                                              │
│ Usuario: aperez (bloqueado)                  │
│ Contraseña [                       ]         │
│                                              │
│       [ Cerrar sesión ]   [ Continuar ]      │
└──────────────────────────────────────────────┘
```

Preserva el estado de la página (form en proceso, borrador, etc.).

---

## A3 — Selector de especialidad activa

Tipo: modal · Prioridad: P2

Si el usuario tiene varias especialidades asignadas, aparece al login y desde el menú de perfil:

```
┌────────────────────────────────────┐
│ ¿Con qué rol vas a trabajar hoy?   │
│                                    │
│ ( ●) Cardiología                   │
│ ( ) Neumonología                   │
│ ( ) Oncología                      │
│                                    │
│             [ Confirmar ]          │
└────────────────────────────────────┘
```

Persiste en cookie `chequear_specialty`.

---

## A4 — Listado de usuarios

Tipo: listado · Prioridad: P2 · Implementación: Razor

```
┌───────────────────────────────────────────────────────────┐
│ Usuarios                       [ + Importar desde hosp. ] │
│                                                           │
│ [Buscar…]  [Rol ▾]  [Especialidad ▾]  [Estado ▾]          │
│                                                           │
│ ┌───────────────────────────────────────────────────────┐ │
│ │ Usuario │ Nombre        │ Rol     │ Espec. │ Estado   │ │
│ │ aperez  │ Pérez, Ana    │ Médico  │ Cardio │ ●Activo  │ │
│ │ rgomez  │ Gómez, Raúl   │ Refer.  │ Neumo  │ ●Activo  │ │
│ │ jlopez  │ López, Juan   │ Admin   │ —      │ ●Inactivo│ │
│ └───────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────┘
```

> Existencia condicional a si la gestión queda en ChequeAR o se delega.

---

## A5 — Detalle / edición de roles de un usuario

Tipo: formulario · Prioridad: P2 · Implementación: Razor

```
┌────────────────────────────────────────────────────────┐
│ Pérez, Ana (aperez)                                    │
│                                                        │
│ Datos                                                  │
│   Email      aperez@garrahan.gov.ar                    │
│   DNI        12345678                                  │
│   Estado     ( ●Activo  ○Inactivo )                    │
│                                                        │
│ Roles por especialidad                                 │
│   Cardiología       [ Médico ▾ ]                       │
│   Neumonología      [ Sin acceso ▾ ]                   │
│   [+ Agregar especialidad]                             │
│                                                        │
│ Permisos especiales                                    │
│   ☐ Acceso a auditoría                                 │
│   ☐ Creación de reportes                               │
│                                                        │
│              [ Cancelar ]   [ Guardar ]                │
└────────────────────────────────────────────────────────┘
```

Auditoría: cada cambio genera `audit_event` tipo `role_change`.

---

## A6 — Visor de auditoría

Tipo: listado · Prioridad: P1 · Implementación: Razor + DataTables

```
┌─────────────────────────────────────────────────────────────────┐
│ Auditoría                                  [ Exportar ▾ ]       │
│                                                                  │
│ [Usuario ▾]  [Acción ▾]  [Recurso ▾]  [Desde 2026-06-01]        │
│                                       [Hasta 2026-06-10]        │
│                                                                  │
│ ┌──────────────────────────────────────────────────────────────┐│
│ │ Fecha/hora       │ Usuario │ Acción  │ Recurso       │ IP    ││
│ │ 2026-06-10 13:42 │ aperez  │ UPDATE  │ Questionnaire │ 10.0..││
│ │ 2026-06-10 13:30 │ rgomez  │ READ    │ Patient/12345 │ 10.0..││
│ │ 2026-06-10 12:11 │ aperez  │ EXPORT  │ Report/77     │ 10.0..││
│ └──────────────────────────────────────────────────────────────┘│
│ 1-50 de 1.230    [ ◀ 1 2 3 … 25 ▶ ]                              │
└─────────────────────────────────────────────────────────────────┘
```

Estados: empty (sin filtros: muestra últimas 24h) / loading / error.
Click en fila abre A7.

---

## A7 — Detalle de evento de auditoría

Tipo: detalle · Prioridad: P1

```
┌─────────────────────────────────────────────────────────┐
│ Evento de auditoría · #aud-29348                        │
│                                                         │
│ Cuándo       2026-06-10 13:42:11                        │
│ Usuario      aperez (Pérez, Ana)                        │
│ Rol          Médico · Cardiología                       │
│ Acción       UPDATE                                     │
│ Recurso      Questionnaire/47 (Cardio basal v3)         │
│ IP           10.0.12.45                                 │
│ User-Agent   Mozilla/5.0 (Win NT 10.0; …) Chrome/124    │
│                                                         │
│ Cambios (diff)                                          │
│   ─ questionGroups[2].questions[5].required: false      │
│   + questionGroups[2].questions[5].required: true       │
│                                                         │
│ [Ver recurso] [Volver]                                  │
└─────────────────────────────────────────────────────────┘
```
