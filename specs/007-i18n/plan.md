# Implementation Plan: Internacionalización

**Branch**: `007-i18n` | **Date**: 2026-06-10 | **Spec**: ./spec.md

> **Descartada (2026-10-13):** la app queda solo en es-AR. Este plan describe un diseño que ya no existe en el código
> (no hay `CultureMiddleware`, selector de idioma ni `en.json`). Ver `spec.md`.

## Summary

Implementar i18n end-to-end usando `.resx` de ASP.NET para Razor + JSON simples para las islas Vue. Resolución de cultura por cookie `chequear_lang` con fallback a `Accept-Language` y default `es-AR`. Modelo de dominio extendido con `LocalizedText` para textos de cuestionarios.

## Technical Context

**Language/Version**: C# 7.3 + JavaScript ES2020
**Primary Dependencies**: `System.Globalization.CultureInfo`, ASP.NET resource provider, `Intl.DateTimeFormat`, `Intl.NumberFormat`
**Storage**: `.resx` en `App_GlobalResources/`, JSON en `Scripts/i18n/`, SQL para `LocalizedText` del dominio
**Testing**: xUnit (resolución de cultura, fallback), Playwright (E2E switch)
**Constraints**: sin librerías externas pesadas; sin requests extra en page load

## Estado de implementación (2026-06-10)

Implementación final difiere del plan original en una decisión clave:

- **No usamos `.resx`** — los textos viven en JSON planos bajo `src/ChequeAR.Api/Content/i18n/{culture}.json`. Razón: editables por no-desarrolladores, sin generador de código, fáciles de versionar/diff, y consumibles tanto desde Razor (`Cq.T`) como desde JS (las islas Vue pueden cargar el mismo archivo con `fetch`).
- **Helper único `Cq.T(key, args)`** en `Helpers/CqLocalizer.cs` con cache por cultura y fallback `cultura → es-AR → [[key]]`.
- **Middleware OWIN `CultureMiddleware`** setea `Thread.CurrentCulture/UICulture` desde la cookie `chequear_lang`.
- **Cobertura actual**: chrome (navbar, sidebar, language switcher) + Home + Q1 + R1 + E1 + Auditoría + Usuarios + Integraciones. Contenido en tablas/modales y vistas P2/P3 quedan en español; pasada completa pendiente.

## Resolución de cultura — Razor

Middleware OWIN en `Startup.cs`:

```csharp
app.Use(async (ctx, next) => {
    var lang = ctx.Request.Cookies["chequear_lang"]?.Value
            ?? NegotiateFromAcceptLanguage(ctx.Request.Headers["Accept-Language"])
            ?? "es-AR";
    var culture = new CultureInfo(lang);
    Thread.CurrentThread.CurrentCulture = culture;
    Thread.CurrentThread.CurrentUICulture = culture;
    await next();
});
```

Razor consume vía helper:

```csharp
@Cq.T("home.greeting", new { name = User.GivenName })
```

Internamente busca clave en `Resources.<culture>.resx` con fallback en cascada.

## Resolución de cultura — Vue islands

`bootstrap.js` lee la cookie y carga el JSON correspondiente:

```js
const lang = readCookie("chequear_lang") || "es-AR";
const messages = await fetch(`/Scripts/i18n/${lang}.json`).then(r => r.json());
app.provide("$t", (key, params) => translate(messages, key, params));
```

En componentes:

```js
const t = inject("$t");
t("renderer.q.requiredError", { field: "Edad" });
```

## Formato regional

| Operación | Razor | Vue |
|---|---|---|
| Fecha | `value.ToString("d", culture)` | `new Intl.DateTimeFormat(lang).format(date)` |
| Número | `value.ToString("N2", culture)` | `new Intl.NumberFormat(lang).format(n)` |
| Moneda | n/a en MVP | n/a |

## Modelo de textos dinámicos

Ver `data-model.md`. Resumen: `Questionnaire.title`, `QuestionGroup.title`, `Question.text`, `AnswerOption.label` son `LocalizedText`.

## Project Structure

```text
specs/007-i18n/
├── spec.md
├── plan.md
├── data-model.md
├── tasks.md
└── quickstart.md
```

### Source Code

```text
src/ChequeAR.Api/
├── Content/i18n/
│   ├── es-AR.json                    # default (origen)
│   └── en.json                       # destino
├── Helpers/CqLocalizer.cs            # Cq.T(...) helper + CultureMiddleware
└── Startup.cs                        # registra CultureMiddleware

src/ChequeAR.Domain/
└── ValueObjects/LocalizedText.cs     # (pendiente — textos dinámicos del dominio)

src/ChequeAR.Infrastructure/
└── EF/Configurations/LocalizedTextConfiguration.cs  # (pendiente)
```

## Carpeta `Content/i18n/`

Por qué ahí y no en `App_GlobalResources/`: ese path es específico de Web Forms / Razor pages y obligaba al uso de `.resx`. Servir desde `Content/i18n/` permite:

- Mismo archivo consumido por Razor (server) y por islas Vue (`fetch('/Content/i18n/en.json')`).
- Edición directa por traductores sin Visual Studio.
- Diff plano amigable a code review.

## Convenciones de claves

```
<module>.<screen-or-area>.<element>[.<modifier>]
```

Ejemplos:
- `nav.module.questionnaires`
- `q1.title`
- `q1.filters.specialty.placeholder`
- `r3.q.required.error`
- `common.button.save`
- `rules.range.outOfBounds`  (mensaje de validación)

## Errores de validación

Servidor responde:

```json
{ "errors": [{ "field": "edad", "code": "rules.range.outOfBounds", "params": { "min": 0, "max": 120 } }] }
```

Cliente:

```js
t(error.code, error.params)  // "Debe estar entre 0 y 120"
```

## Phasing

| Fase | Alcance |
|---|---|
| F0 | Resource files vacíos + helper `Cq.T` + middleware de cultura + cookie |
| F1 | Selector de idioma en navbar + persistencia |
| F2 | Traducciones es-AR completas para pantallas P1 |
| F3 | Traducciones en para pantallas P1 |
| F4 | `LocalizedText` en dominio + UI de edición en Q3 |
| F5 | Tests E2E + auditoría de claves faltantes |

## Risks

| Riesgo | Mitigación |
|---|---|
| Claves duplicadas / typos | Lint script que valida que ambos `.resx` tengan exactamente las mismas keys |
| Fallback silencioso oculta keys faltantes | En dev, modo "missing key marker" (renderiza `[[key]]`) |
| `LocalizedText` complica EF mapping | Tabla auxiliar `localized_text_entry (id, owner_table, owner_id, lang, text)` |

## Dependencies

- spec 006 — define dónde se usan los textos.
- spec 001 — modelo `Questionnaire` ahora referencia `LocalizedText`.
