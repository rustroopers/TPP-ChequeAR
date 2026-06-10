# Data Model — i18n

## ValueObject `LocalizedText`

```csharp
public class LocalizedText
{
    public string DefaultText { get; set; }     // fallback final, idioma original
    public List<Translation> Translations { get; set; } = new();
}

public class Translation
{
    public string Lang { get; set; }            // "es-AR", "en"
    public string Text { get; set; }
}
```

## Tabla `LocalizedTextEntry`

Para evitar columnas dinámicas y mantener un único esquema por entidad propietaria:

| Columna | Tipo | Notas |
|---|---|---|
| id | bigint PK | |
| owner_type | varchar(50) | `Questionnaire`, `QuestionGroup`, `Question`, `AnswerOption` |
| owner_id | bigint | FK lógica al propietario |
| field | varchar(50) | `title`, `text`, `helpText`, `label` |
| lang | varchar(10) | `es-AR`, `en`, … |
| text | nvarchar(max) | |
| updated_at | datetime2 | |
| updated_by | bigint FK User | |

Índice compuesto: `(owner_type, owner_id, field, lang)` único.

## Resolución en lectura

Service `LocalizedTextResolver.Resolve(ownerType, ownerId, field, lang)`:

1. Busca `(ownerType, ownerId, field, lang)`. Si encuentra, retorna.
2. Si no, busca `lang = "es-AR"` (fallback de proyecto).
3. Si tampoco, retorna el `DefaultText` embebido en la entidad (campo legacy `title`/`text` que se conserva por compatibilidad).
4. Si todo falla, retorna `[[ownerType.field#ownerId]]` (marker).

Se cachea por request (memoria de proceso) — el dataset es pequeño.

## Impacto en spec 001

- `Questionnaire.title` → `Questionnaire.TitleDefault` + `LocalizedText` entries.
- `QuestionGroup.title` → idem.
- `Question.text` → idem.
- `AnswerOption.label` → idem.

La API expone, en cada lectura, sólo el texto resuelto para el idioma activo (con header `Content-Language` en respuesta).

Cuando un editor escribe traducciones, se hace upsert por `(owner_type, owner_id, field, lang)`.

## Recursos estáticos

- `App_GlobalResources/Resources.<culture>.resx` para Razor.
- `Scripts/i18n/<culture>.json` para Vue, con misma estructura plana:

```json
{
  "nav.module.questionnaires": "Cuestionarios",
  "q1.title": "Cuestionarios",
  "q1.filters.specialty.placeholder": "Especialidad",
  "rules.range.outOfBounds": "Debe estar entre {min} y {max}"
}
```

Generación automática de `.json` desde `.resx` con script `scripts/sync-i18n.ps1` (opcional, evita doble escritura).
