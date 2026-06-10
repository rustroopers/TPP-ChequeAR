# Quickstart — i18n

## Agregar una clave nueva

1. Definir clave con convención `modulo.pantalla.elemento`.
2. Agregar a `App_GlobalResources/Resources.es-AR.resx` con el texto en español.
3. Agregar a `App_GlobalResources/Resources.en.resx` con el texto en inglés (o copiar es-AR + TODO).
4. Si la clave la usa una isla Vue, agregar también a `Scripts/i18n/es-AR.json` y `en.json`.
5. Correr `scripts/check-i18n.ps1` — falla si las keys no están en todos los archivos.

## Usar una clave

**En Razor**:
```razor
<h1>@Cq.T("q1.title")</h1>
<span>@Cq.T("home.greeting", new { name = User.GivenName })</span>
```

**En Vue island**:
```js
const t = inject("$t");
return { t };
// template:
// <h1>{{ t("q1.title") }}</h1>
```

## Cambiar idioma manualmente

- En la app: click en navbar 🌐.
- Por cookie: `document.cookie = "chequear_lang=en; path=/; max-age=2592000"`.
- Forzar default: borrar cookie.

## Probar el fallback

Borrar la clave de `Resources.en.resx`, cambiar a `en`, refrescar — debe verse el texto en es-AR con badge `(es)` cuando aplica a textos del dominio. Para claves estáticas, el comportamiento en dev muestra `[[key]]`.
