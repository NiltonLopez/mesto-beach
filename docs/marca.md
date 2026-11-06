# Sistema de diseño

Reglas visuales de `menu`, `staff` y `admin`. Los valores viven como tokens en cada app.

## Tokens

| Token | Valor | Uso |
|---|---|---|
| `--brand-primary` | `#AA8453` | Botones primarios, acentos y estados activos |
| `--brand-ink` | `#222222` | Titulares sobre claro |
| `--brand-text` | `#666666` | Texto secundario |
| `--brand-bg` | `#F8F5F0` | Fondo de la app del comensal |
| `--brand-surface` | `#FFFFFF` | Tarjetas |
| `--brand-font-display` | Gilda Display | Títulos y nombres de plato |
| `--brand-font-ui` | Barlow | Todo lo operativo |

## Dirección por superficie

- **`menu`:** crema y dorado, Gilda Display en categorías y platos, fotos protagonistas.
- **PDA y Runner:** se usan a pleno sol y con las manos ocupadas, así que manda el contraste: fondo
  claro, Barlow SemiBold y botones grandes.
- **KDS:** pantallas encendidas muchas horas; el ticket tiene que resaltar.
- **Host y Admin:** neutro claro con acentos dorados; el mapa es el protagonista.

## Principios

1. **El dorado es un acento:** botones primarios y líneas finas, nunca fondos grandes.
2. **La serif va a peso 400:** la fuente solo trae ese peso y un bold falseado la estropea.
3. **Aire:** los tamaños suben antes que los pesos.
4. **Líneas finas:** divisores de 1 px con dorado al 50–60 %.
5. **Cero emojis** en las tres apps: los dibuja el sistema operativo y cada uno los pinta distinto.
   Solo se permiten glifos monocromos: ✦ ◆ ✕ ✓ ▾.
6. **Sombras cálidas y suaves**, nunca negros duros.
7. **Chips y etiquetas:** peso medio y tracking sutil (`0.03em`).
8. **El precio se escribe a la española:** `14,50 €`, siempre con `Intl.NumberFormat("es-ES", …)`
   sobre el importe dividido entre 100, porque el sistema guarda céntimos de euro.

## Iconos

Librería única: [Phosphor Icons](https://phosphoricons.com) (`@phosphor-icons/react`, MIT).

| Contexto | Peso | Por qué |
|---|---|---|
| `admin` | `light` | Trazo fino; se usa en interior |
| `staff` | `regular` | Más cuerpo para no perderse con los reflejos |
| `menu` | Sin iconos | La serif y las fotos cargan el peso |

Siempre monocromos (`currentColor`), sin mezclar con otra familia, y acompañan al texto en vez de
sustituirlo.
