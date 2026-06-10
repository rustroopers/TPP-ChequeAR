# Sistema de Diseño — ChequeAR

> Base: **Bootstrap 5.3** con sobreescritura de variables CSS. Inspiración visual: shadcn/ui (proyecto Nexus). Branding: Hospital Garrahan.

## Principios

1. **Calmo antes que llamativo.** ChequeAR se usa en jornadas largas; reservar saturación para errores y acciones críticas.
2. **Bootstrap, no fork de Bootstrap.** Toda customización pasa por variables CSS y una capa SCSS delgada (`chequear-theme.scss`). Nunca editar `bootstrap.css`.
3. **Densidad media.** El usuario primario maneja mucha información; el ritmo vertical es más ajustado que el Bootstrap por defecto.
4. **Estado visible siempre.** Cada componente expone `idle | loading | success | error | empty`.

## Paleta

### Marca (Garrahan)

Paleta extraída del `user.css` del sitio oficial (`garrahan.gov.ar`). El sitio institucional usa **lila (#9283be) como color predominante** y **verde menta (#73C4A4) como acento**.

| Token | Hex | Uso |
|---|---|---|
| `--cq-primary` | `#6f5fa5` | Lila saturado, color primario (navbar, botones primarios, links). Variante más oscura que la del sitio para asegurar contraste AA sobre blanco. |
| `--cq-primary-600` | `#574785` | Hover de primario |
| `--cq-primary-400` | `#9283be` | Lila original del sitio — fondos suaves, badges activos |
| `--cq-primary-100` | `#ece8f5` | Background lila tenue |
| `--cq-accent` | `#73C4A4` | Verde menta del sitio — CTAs secundarios, success suave |
| `--cq-accent-600` | `#4ea98a` | Hover de acento |
| `--cq-deep` | `#001B4C` | Azul institucional, para encabezados serios e ilustraciones |

### Acentos por especialidad (zonas de color del hospital)

| Token | Hex | Especialidad / zona |
|---|---|---|
| `--cq-zone-red` | `#c53030` | Oso rojo |
| `--cq-zone-violet` | `#7c3aed` | Mono violeta |
| `--cq-zone-green` | `#16a34a` | Sapo verde |
| `--cq-zone-yellow` | `#ca8a04` | León amarillo |
| `--cq-zone-blue` | `#2563eb` | Pajarito azul |
| `--cq-zone-orange` | `#ea580c` | Conejo naranja |

> Uso: chip de "especialidad" en listados y filtros. No uso decorativo libre.

### Neutros

| Token | Hex | Uso |
|---|---|---|
| `--cq-neutral-50` | `#f8fafc` | Fondo de página |
| `--cq-neutral-100` | `#f1f5f9` | Fondo de cards en hover |
| `--cq-neutral-200` | `#e2e8f0` | Bordes |
| `--cq-neutral-300` | `#cbd5e1` | Separadores fuertes |
| `--cq-neutral-500` | `#64748b` | Texto secundario, iconos |
| `--cq-neutral-700` | `#334155` | Texto cuerpo |
| `--cq-neutral-900` | `#0f172a` | Headings sobre fondo claro |

### Semánticos

| Token | Hex | Uso |
|---|---|---|
| `--cq-success` | `#15803d` | Confirmaciones, estado publicado |
| `--cq-warning` | `#b45309` | Estado borrador, alertas suaves |
| `--cq-danger` | `#b91c1c` | Errores, archivado, destructivo |
| `--cq-info` | `#0369a1` | Tooltips, ayudas |

## Tipografía

- **Family**: stack del sitio oficial — `-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, "Noto Sans", sans-serif`.
- **Escala**:

| Token | Tamaño / line-height | Uso |
|---|---|---|
| `--fs-display` | 28 / 36 | H1 de pantallas, headers de wizard |
| `--fs-h1` | 22 / 30 | Headers de página |
| `--fs-h2` | 18 / 26 | Sub-secciones, cards principales |
| `--fs-h3` | 16 / 24 | Sub-sub-secciones, modales |
| `--fs-body` | 14 / 20 | Texto general |
| `--fs-sm` | 13 / 18 | Labels, helpers, captions |
| `--fs-xs` | 12 / 16 | Badges, metadatos |

- **Pesos**: 400 (regular), 500 (medium, labels), 600 (semibold, headings), 700 (sólo display).
- **Tracking**: por defecto. Headings con `letter-spacing: -0.01em`.

## Espaciado

Escala basada en `4px`. Tokens:

`--sp-1: 4px · --sp-2: 8px · --sp-3: 12px · --sp-4: 16px · --sp-5: 20px · --sp-6: 24px · --sp-8: 32px · --sp-10: 40px · --sp-12: 48px · --sp-16: 64px`.

- **Padding interno de card**: `--sp-6`.
- **Gap entre cards**: `--sp-4`.
- **Vertical rhythm en formularios**: `--sp-4` entre campos, `--sp-6` entre fieldsets.

## Radii y sombras

| Token | Valor | Uso |
|---|---|---|
| `--cq-radius-sm` | 6px | Inputs, botones, badges |
| `--cq-radius-md` | 10px | Cards, modales, popovers |
| `--cq-radius-lg` | 16px | Sheets, dialogs grandes |

| Token | Valor | Uso |
|---|---|---|
| `--cq-shadow-1` | `0 1px 2px rgba(15,23,42,.06)` | Cards |
| `--cq-shadow-2` | `0 4px 12px rgba(15,23,42,.08)` | Dropdowns, popovers |
| `--cq-shadow-3` | `0 16px 32px rgba(15,23,42,.14)` | Modales |

## Componentes (mapeo con Bootstrap)

| Componente ChequeAR | Bootstrap base | Customización |
|---|---|---|
| `cq-btn-primary` | `.btn .btn-primary` | bg `--cq-primary`, radius `--cq-radius-sm`, peso 500 |
| `cq-btn-ghost` | `.btn .btn-link` | sin underline, color `--cq-neutral-700`, hover bg `--cq-neutral-100` |
| `cq-input` | `.form-control` | border `--cq-neutral-200`, focus ring `--cq-primary-400` 2px |
| `cq-card` | `.card` | sin shadow por default, `--cq-shadow-1` en hover en listados |
| `cq-badge-status` | `.badge` | semánticos por estado: Draft/Published/Archived |
| `cq-table` | `.table` | header con bg `--cq-neutral-50`, divisores `--cq-neutral-200`, hover de fila `--cq-neutral-50` |
| `cq-modal` | `.modal` | header con borde-bottom suave, footer con divider, max-width 560 (sm) / 720 (md) / 960 (lg) |
| `cq-toast` | `.toast` | bottom-right por default, autodismiss 4s success / sticky en error |
| `cq-empty-state` | n/a (custom) | ilustración SVG mínima + título + CTA |
| `cq-stepper` | n/a (custom) | usado en wizards (Q2 editor) |
| `cq-tree` | n/a (custom) | usado en árbol de grupos del editor Q2 |

## Iconografía

- **Bootstrap Icons** vía sprite local (`wwwroot/lib/bootstrap-icons/bootstrap-icons.svg`).
- Tamaño base 16px. Para acciones primarias 20px. En navbar 18px.
- Color: heredado de texto. Acciones destructivas siempre con `--cq-danger`.

## Logo

- `wwwroot/Content/img/garrahan-logo.png` (descargado desde sitio oficial), versión monocromo blanco para navbar (`garrahan-logo-white.svg`).
- Tamaño en navbar: 28px alto. Padding lateral 12px.
- Co-branding ChequeAR: wordmark adyacente con tipografía semibold + tagline opcional "Fichas Clínicas Interoperables".

## Accesibilidad

- Contraste mínimo 4.5:1 para texto < 18px. Validado en CI con `axe-core`.
- Foco visible: ring 2px `--cq-primary-400` con offset 2px sobre todos los interactivos. Nunca usar `outline: none` sin reemplazo.
- Touch targets ≥ 40×40 px.
- Todos los inputs con `<label>` asociado. Errores con `aria-describedby`.
- Modales con `role="dialog"`, `aria-modal="true"`, foco trap, ESC para cerrar.
- Live regions (`aria-live="polite"`) para toasts y validaciones asíncronas.
- Soporte de tema reducido movimiento: respetar `prefers-reduced-motion`.

## Don'ts

- ❌ Gradientes decorativos.
- ❌ Sombras coloreadas.
- ❌ Animaciones > 200 ms (excepto entrada de modales).
- ❌ Más de 3 niveles de jerarquía visual por pantalla.
- ❌ Iconos sin label en botones primarios.
