# Pantallas Transversales

## T1 — Layout principal (shell)

Tipo: shell · Prioridad: P1 · Implementación: Razor `_Layout.cshtml`

```
┌────────────────────────────────────────────────────────────────────┐
│ [LOGO Garrahan] ChequeAR        [🔔]  [🌐 ES ▾]  [Dr. Pérez ▾]      │  ← navbar 56px
├──────────┬─────────────────────────────────────────────────────────┤
│          │ Inicio > Cuestionarios > Editar                          │  ← breadcrumbs 36px
│ 🏠 Home  ├─────────────────────────────────────────────────────────┤
│ 📋 Cuest │                                                          │
│ 👤 Pcts  │           Contenido de la página                         │
│ 📊 Repts │                                                          │
│ 🔍 Audit │                                                          │
│ ⚙️ Admin │                                                          │
│          │                                                          │
│          ├─────────────────────────────────────────────────────────┤
│          │                              [Toast stack ↗ bottom-right]│
└──────────┴─────────────────────────────────────────────────────────┘
   240px                          fluido
```

Componentes: `_Navbar`, `_Sidebar` (módulos según rol), `_Breadcrumbs`, `_Toasts`, `_LanguageSwitcher`.
Estados: sin estado propio. Pasa props a las páginas hijas vía `ViewBag.PageId`.
Interacciones: sidebar colapsable (≥ 1024px sticky; < 1024px overlay).

---

## T2 — Home / Dashboard

Tipo: dashboard · Prioridad: P1 · Implementación: Razor

```
┌──────────────────────────────────────────────────────────────────┐
│ Buen día, Dra. Pérez                                              │
│ Tu actividad de hoy                                               │
├──────────────────────────────────────────────────────────────────┤
│ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐      │
│ │ ▶ Cargar ficha  │ │ 📋 Cuestionarios│ │ 📊 Reportes     │      │
│ │ Iniciar carga   │ │ Gestionar       │ │ Cohortes        │      │
│ └─────────────────┘ └─────────────────┘ └─────────────────┘      │
│                                                                   │
│ Fichas recientes                              [Ver todas →]      │
│ ┌──────────────────────────────────────────────────────────────┐ │
│ │ Paciente   │ Cuestionario  │ Estado   │ Actualizada  │ Abrir │ │
│ │ García, A. │ Cardio basal  │ ●Final.  │ hoy 10:23    │ →     │ │
│ │ López, M.  │ Neumo control │ ●Borrad. │ ayer         │ →     │ │
│ └──────────────────────────────────────────────────────────────┘ │
│                                                                   │
│ Métricas (sólo admin)                                            │
│ ┌────────┬────────┬────────┬────────┐                            │
│ │ Cuest. │ Fichas │ Usuar. │ Espec. │                            │
│ │  47    │ 1.230  │  85    │   4    │                            │
│ └────────┴────────┴────────┴────────┘                            │
└──────────────────────────────────────────────────────────────────┘
```

Componentes: `cq-card` (acciones), `cq-table` (recientes), `cq-badge-status` (estados), tarjetas KPI.
Estados: loading skeleton de 3 cards · empty ("Sin actividad reciente") · error (banner con CTA reintentar).
Interacciones: las 3 acciones son links profundos; tarjetas KPI sólo visibles para Admin.

---

## T3 — Selector de idioma

Tipo: modal · Prioridad: P1 · Implementación: dropdown Bootstrap en navbar

```
[🌐 ES ▾]
       ┌───────────────┐
       │ ● Español     │
       │ ○ English     │
       └───────────────┘
```

Setea cookie `chequear_lang` y refresca página manteniendo URL.

---

## T4 — Página de error

Tipo: error · Prioridad: P1 · Implementación: Razor

```
┌───────────────────────────────────────────────────┐
│                                                   │
│              [ilustración 160x160]                │
│                                                   │
│              404 · Página no encontrada           │
│   La sección que buscás no existe o se movió.     │
│                                                   │
│         [ Volver al inicio ]  [ Reintentar ]      │
│                                                   │
└───────────────────────────────────────────────────┘
```

Variantes: 401 ("Tu sesión expiró"), 403 ("No tenés permisos"), 500 ("Algo salió mal"), 404 (default).
Toda página de error oculta navbar + sidebar — sólo logo y CTAs.

---

## T5 — Perfil del usuario

Tipo: formulario · Prioridad: P2 · Implementación: Razor

```
┌──────────────────────────────────────────────────┐
│ Mi perfil                                         │
│ ──────────────────────────────────────────────── │
│ Nombre        Pérez, Ana                          │
│ Usuario       aperez                              │
│ Email         aperez@garrahan.gov.ar              │
│ Especialidad  Cardiología (principal)             │
│ Rol           Médico                              │
│                                                   │
│ Preferencias                                      │
│   Idioma          [ Español ▾ ]                   │
│   Notificaciones  [☑] Email   [☐] In-app          │
│                                                   │
│              [ Guardar cambios ]                  │
└──────────────────────────────────────────────────┘
```

Estados: idle / saving / saved (toast) / error (toast).
Datos básicos son read-only (vienen del hospital); sólo preferencias son editables.
