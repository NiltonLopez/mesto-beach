# SDD-002 — Catalog

> **v2.1 — Octubre 2026 — Carta única con visibilidad por modo y branding Night.**
> *(diseñado — pendiente de implementación, PRD-019 — ADR-041 · ADR-039)*. El local opera de día como
> restaurante y de noche como fiesta **con la misma carta**: cada categoría e ítem declara en qué modos se ve
> (`visibleIn: ["RESTAURANT","NIGHT"]`), el branding gana un subdocumento `night` de **overrides** (null =
> hereda del día) y un tema `night`, y `GET /catalog/{tenantId}` devuelve la carta **ya filtrada** por el modo
> activo con el branding **resuelto** y el campo `venueMode`. La caché `catalog:{tenantId}` guarda el catálogo
> **sin filtrar** (Orders valida contra ella con `ITEM_NOT_IN_MODE`, SDD-004). Se reservan nombres para lo que
> queda fuera (`nightPriceCents`, `allowedTableIds`) y se recoge una deuda con el código *(actualizado)*:
> `branding.backgroundColor`.

## Servicio
**Módulo**: Catalog  
**Base path**: `/api/v1/catalog`  
**Responsabilidad**: Gestión del menú del restaurante (categorías, productos, imágenes) y servicio del catálogo público al cliente. Fuente de verdad del menú: MongoDB. Cache en Redis.

**PRDs de referencia**: PRD-004 (Configuración — §Menú), PRD-016 (Admin Panel — §Módulo 2 Menú), **PRD-019** (Mesto Beach Night — carta por modo, branding Night)

**Dependencias**:
- MongoDB: colección `catalogs`
- Redis: `catalog:{tenantId}` TTL 5min — **sin filtrar por modo** *(diseñado — PRD-019)*
- Azure Blob Storage: imágenes de productos y branding (logo, hero; *(diseñado — PRD-019)* también `night-logo`/`night-hero`). Dev: emulador **Azurite** (puerto 10000)
- SDD-001 Identity: JWT validation; **`IVenueModeProvider`** (modo activo del local) y `VenueModeChangedNotification` (invalidación defensiva de caché) *(diseñado — PRD-019)*

---

## MongoDB Schema

**Collection: `catalogs`** (one document per tenant)

```json
{
  "_id": "ObjectId",
  "tenantId": "UUID string",
  "categories": [
    {
      "id": "UUID string",
      "name": "string",
      "displayOrder": "number",
      "visibleIn": "['RESTAURANT' | 'NIGHT'][] — (diseñado, PRD-019) modos en los que se muestra; ausente = ambos",
      "createdAt": "ISODate"
    }
  ],
  "items": [
    {
      "id": "UUID string",
      "categoryId": "UUID string",
      "name": "string (max 150 chars)",
      "description": "string (max 300 chars, optional)",
      "priceCents": "number (integer, > 0) — euros en céntimos (v2.0, ADR-031; antes priceCop)",
      "images": ["string (Azure Blob URL) — max 3, index 0 = main"],
      "available": "boolean (default true)",
      "stockEnabled": "boolean (default false)",
      "stockQty": "number (integer >= 0)",
      "lowStockThreshold": "number (integer >= 0)",
      "displayOrder": "number",
      "modifierGroupIds": ["UUID string — references modifierGroups[].id"],
      "stationId": "UUID string — estación KDS destino del plato (v2.0, ADR-029)",
      "translations": {
        "en": { "name": "string", "description": "string" }
      },
      "allowNotes": "boolean (default false) — permite comentario libre del cliente",
      "visibleIn": "['RESTAURANT' | 'NIGHT'][] — (diseñado, PRD-019) modos en los que se muestra; ausente = ambos. Ver §Visibilidad por modo (campos reservados nightPriceCents / allowedTableIds: NO implementar)",
      "deletedAt": "ISODate or null",
      "createdAt": "ISODate",
      "updatedAt": "ISODate"
    }
  ],
  "modifierGroups": [
    {
      "id": "UUID string",
      "name": "string (max 120 chars)",
      "type": "SINGLE | MULTI | REMOVE",
      "required": "boolean",
      "minSelect": "number (integer >= 0)",
      "maxSelect": "number or null (sin límite)",
      "options": [
        {
          "id": "UUID string",
          "name": "string (max 120 chars)",
          "priceDeltaCents": "number (integer >= 0; siempre 0 para REMOVE) — euros en céntimos (v2.0, ADR-031; antes priceDeltaCop)",
          "available": "boolean (default true)",
          "isDefault": "boolean — preselección, solo aplica a SINGLE"
        }
      ],
      "createdAt": "ISODate",
      "updatedAt": "ISODate"
    }
  ],
  "branding": {
    "displayName": "string (max 80 chars) — nombre visible del restaurante en el menú",
    "slogan": "string (max 140 chars)",
    "logoUrl": "string (Azure Blob URL) or null",
    "heroUrl": "string (Azure Blob URL) or null — imagen de portada",
    "primaryColor": "string (#RRGGBB) — color de acento",
    "theme": "light | cream | dark | night — preset de fondo ('night' diseñado, PRD-019)",
    "backgroundColor": "string (#RRGGBB) or null — (actualizado) fondo libre; si está, prevalece sobre theme",
    "fontKey": "string — clave de fuente curada (ej. manrope, playfair)",
    "night": {
      "enabled": "boolean — (diseñado, PRD-019) ausente = true; solo si es false se sirve de noche el branding de día tal cual, sin tema night",
      "displayName": "string or null — override; null = hereda del día",
      "slogan": "string or null",
      "logoUrl": "string (Azure Blob URL) or null",
      "heroUrl": "string (Azure Blob URL) or null",
      "primaryColor": "string (#RRGGBB) or null — default efectivo '#AA8453'",
      "theme": "light | cream | dark | night or null — default efectivo 'night'",
      "backgroundColor": "string (#RRGGBB) or null — null = sin fondo propio (NO hereda el de día; manda theme)",
      "fontKey": "string or null"
    }
  },
  "updatedAt": "ISODate"
}
```

## Visibilidad por modo y branding Night *(diseñado — pendiente de implementación, PRD-019 — ADR-041 · ADR-039)*

El local tiene **una sola carta** para dos negocios. En vez de duplicar el catálogo, cada categoría e ítem
declara en qué modos del local se ve, y el branding gana una capa de overrides para la noche. El modo activo
lo da `IVenueModeProvider` (SDD-001 §Venue); Catalog lo lee, nunca lo escribe.

### `visibleIn` en categoría e ítem

- `visibleIn: string[]` con valores en `{ "RESTAURANT", "NIGHT" }`, **sin duplicados y nunca vacío**
  (`400 VISIBLE_IN_EMPTY`). **Normalización al escribir**: cada valor se recorta y se pasa a mayúsculas
  (`night` → `NIGHT`), los duplicados se eliminan en silencio y se persiste en orden canónico
  `["RESTAURANT","NIGHT"]`. Así el filtro del catálogo público y Orders comparan siempre contra
  `RESTAURANT`/`NIGHT` exactos. Se puede fijar al crear (POST) o al editar (PATCH); si se omite al crear,
  default `["RESTAURANT","NIGHT"]`.
- **Documentos sin el campo lo heredan al leer**: `CategoryDoc`/`ItemDoc` lo materializan como
  `["RESTAURANT","NIGHT"]` al deserializar (`[BsonIgnoreIfNull]` + default en el modelo), así la carta
  existente no necesita migración de datos. Se persiste solo cuando el admin lo fija (al crear o al editar).
- En dominio, `CatalogItem` y `CatalogCategory` ganan `VisibleIn` (`List<string>`, inicializado a
  `["RESTAURANT","NIGHT"]`) con setter privado y **`[JsonInclude]`**, igual que `Images`/`AllowNotes`/`StationId`
  (`CatalogItem.cs`). La caché `catalog:{t}` reconstruye el `TenantCatalog` de dominio con System.Text.Json en
  cada acierto, y sin ese atributo el campo vuelve a su default en silencio: la carta de noche mostraría platos de
  día mientras Orders los rechaza con 409. Alternativa válida: pasarlo como parámetro del constructor. Una entrada
  de caché sin el campo (escrita antes del despliegue) se lee como ambos modos.
- Un ítem **se muestra** en un modo si su propio `visibleIn` incluye el modo **y** el de su categoría también;
  una categoría oculta arrastra a sus platos aunque ellos digan lo contrario. Una categoría visible sin ítems
  visibles en ese modo **no viaja** en el catálogo público.
- `visibleIn` **no es disponibilidad**: `available`/stock siguen mandando dentro del modo. Un plato solo-de-día
  agotado sigue siendo solo-de-día.
- **Orders valida contra el modo activo**, no contra lo que vio el cliente. Regla única: pedible en el modo M ⇔
  `item.visibleIn ∋ M ∧ category(item.categoryId).visibleIn ∋ M`. Si `visibleIn` falta o es null en el ítem o
  en la categoría, vale `["RESTAURANT","NIGHT"]`. Si la categoría del ítem no se encuentra, su `visibleIn` vale
  ambos modos. Un ítem no pedible en el modo activo se rechaza con `409 ITEM_NOT_IN_MODE`. Orders lo valida con
  sus propios read models (SDD-004 §POST /orders).
- Si el menú recibe un modo distinto al de la carta cargada, recarga la carta y quita del carrito lo que ya no
  está (PRD-019 RF-1915). Es **depuración defensiva**: con la transición cerrada solo ocurre si una sesión se
  abrió durante el cambio.

### `branding.night` — overrides, no copia

`branding.night` es un subdocumento **opcional** `{ enabled, displayName?, slogan?, logoUrl?, heroUrl?,
primaryColor?, theme?, backgroundColor?, fontKey? }`. Resolución:

```
effective = (mode == NIGHT && night.enabled != false)       // night o enabled ausentes = true
          ? merge(day, defaultsNight, night sin nulls)
            con effective.backgroundColor = night.backgroundColor
          : day
```

Campo a campo, null/ausente = hereda el de día, **salvo `backgroundColor`, que nunca hereda** (null = sin fondo
propio, manda `theme`).

- **Defaults de Night** (se aplican antes del merge, solo a los campos que el admin no fijó):
  `theme: "night"`, `primaryColor: "#AA8453"` (dorado de la marca). `backgroundColor` no hereda nunca del día:
  si `night.backgroundColor` es null, el resuelto lleva `backgroundColor: null` y manda `theme` (por defecto
  `night`). Así un fondo libre de día no aclara la noche. Todo lo demás hereda del día. La palabra
  «Night» junto al nombre la compone el menú (`BrandMark`, ADR-039), no el branding. Los defaults rigen **sin
  configurar nada**: en `NIGHT` el menú sale oscuro y dorado aunque el admin nunca haya abierto la pestaña Night
  (D-06 de PRD-019, «oscuro en las tres apps»; criterio `branding.theme: "night"` de PRD-019 §9). `enabled: false`
  es la salida explícita: sirve el branding de día tal cual.
- `theme` admite un **preset nuevo `night`** en el validador (`MenuBrandingCommands.cs:64`, hoy
  `["light","cream","dark"]`) y en `THEMES` del menú: fondo `#161616`, papel `#1E1C1A`, tinta `#F4ECE2`.
- La API pública **siempre devuelve el branding resuelto** (`GET /catalog/{tenantId}`): el menú no sabe de
  overrides ni de `night`, aplica lo que recibe con `applyMenuBranding`. El admin edita el subdocumento crudo
  por `GET/PUT /branding/night`.
- `night.logoUrl`/`heroUrl` tienen blobs propios (`branding/{tenantId}/night-{logo|hero}-{guid}.webp`);
  borrarlos deja `null` → vuelve a heredar la imagen de día.
- **Día y Night se escriben por caminos disjuntos.** Las escrituras de día (`PUT /branding`,
  `POST /branding/{kind}/image`, `DELETE /branding/{kind}/image`) hacen `$set` **por campo**
  (`branding.displayName`, `branding.slogan`, `branding.logoUrl`, `branding.heroUrl`, `branding.primaryColor`,
  `branding.theme`, `branding.backgroundColor`, `branding.fontKey`) y nunca escriben `branding.night`. Hoy
  `UpdateBrandingAsync` hace `$set branding` con un `BrandingDoc` nuevo (`CatalogRepository.cs`) y deja de
  hacerlo. Las escrituras de Night (`PUT /branding/night`, `POST/DELETE /branding/night/{kind}/image`) usan un
  método propio (p. ej. `UpdateNightBrandingAsync`) que hace solo `$set branding.night`. El mapeo Mongo→dominio
  (`MapToDomain`) lee `night`.

### Campos reservados (no implementar)

Nombres fijados en PRD-019 para lo que **queda fuera** de la primera entrega, para que nadie los reinvente.
No existen en el modelo, no se validan, no viajan:

| Campo | Dónde iría | Para qué |
|---|---|---|
| `nightPriceCents?: int` | `ItemDoc` | Precio nocturno distinto al de día (PRD-019 OQ-1905) |
| `allowedTableIds?: string[]` | `ItemDoc` | Productos solo pedibles desde ciertas mesas (reservados) |

## Modificadores / adiciones (biblioteca por tenant)

`modifierGroups` es una **biblioteca reutilizable a nivel tenant**: los items la referencian por id (`modifierGroupIds`), de modo que editar un grupo actualiza todos los productos que lo usan. Tres tipos:

- **SINGLE** (radio): el cliente elige exactamente una opción; si `required`, es obligatoria. Ej: término de la carne (1/2, 3/4). `maxSelect` se fuerza a 1; una opción puede marcarse `isDefault`.
- **MULTI** (checkbox con costo): 0..`maxSelect` opciones, respetando `minSelect`/`required`. Ej: + Michelada, + Queso.
- **REMOVE** ("sin X"): quitar ingredientes, sin costo (`priceDeltaCents` forzado a 0). Ej: sin cebolla.

`allowNotes` por item habilita un comentario libre del cliente. El **catálogo público** resuelve y embebe los grupos por item (`modifierGroups` con sus opciones) para que el menú los renderice; **Orders es la autoridad de precio** (recalcula `base + Σ deltas`, valida required/min/max y disponibilidad). Al eliminar un grupo se desvincula de todos los items (`$pull` sobre `items[].modifierGroupIds`).

## Menu Branding (Apariencia)

El subdocumento `branding` (uno por tenant, dentro del doc de `catalogs`) define la apariencia de la **app del cliente** (`menu`). El dueño la edita en `admin` (página **Apariencia**, `/menu-appearance`) con vista previa en vivo. Modelo de dominio `MenuBranding`:

- **`displayName`** / **`slogan`**: textos del header/hero del menú.
- **`primaryColor`** (`#RRGGBB`): color de acento (botones, chips, acentos). Se valida formato hex.
- **`theme`**: preset de fondo `light` | `cream` | `dark` (no es un color libre, es una paleta curada); *(diseñado — PRD-019)* se suma `night`.
- **`backgroundColor`** *(actualizado)*: `#RRGGBB` opcional (`MenuBranding.BackgroundColor`, `BrandingDoc.backgroundColor`). Si está, el menú lo usa como fondo e **ignora `theme`**; se valida como hex y se guarda en minúsculas. Lo exponen `GET/PUT /branding` y viaja en el catálogo público, pero este documento no lo recogía.
- **`fontKey`**: fuente de una **lista curada en el cliente** (se carga vía Google Fonts en el menú).
- **`night`** *(diseñado — PRD-019)*: overrides para el modo `NIGHT`; ver §Visibilidad por modo y branding Night.
- **`logoUrl`** / **`heroUrl`**: imágenes en Blob Storage (logo del header, portada del hero).

El menú aplica el branding como variables CSS (`--m-bg/--m-surface/--m-text/--m-accent/--m-font-*`) más un `<link>` dinámico de la fuente; al salir del menú se resetea para que el **login del menú conserve la identidad Mesto** (solo se tematiza la sesión del cliente, no el login). El branding viaja embebido en el catálogo público (ver `GET /api/v1/catalog/{tenantId}`), de modo que comparte el caché Redis `catalog:{tenantId}` y se invalida con cualquier escritura de branding.

## Estación por plato *(v2.0 — ADR-029)*

Hasta v1.x la estación se resolvía por **categoría**: una estación declaraba qué categorías atendía
(`kds_stations.category_ids`) y el pedido entero se enrutaba a una sola. Mesto Beach necesita el
enrutado **por plato**, porque una misma comanda se reparte entre Cocina, Parrilla y Cocktails.

- Cada ítem lleva su `stationId`. Al crear la comanda, Orders lo copia al `order_items.station_id`
  y a partir de ahí el plato vive en esa cola (SDD-004 §Ciclo de vida por ítem).
- El mapeo por categoría se mantiene como **valor por defecto al crear un plato**: al elegir la
  categoría, el admin propone la estación de esa categoría y el usuario puede cambiarla. Así la carta
  existente sigue funcionando y los casos raros (un postre que sale de Parrilla) dejan de ser un
  problema de modelado.
- Un plato **sin** `stationId` es un error de configuración: el admin lo señala y el pedido lo
  rechaza con `ITEM_WITHOUT_STATION` antes de llegar a cocina.

> **No hay platos compuestos** (ADR-029): un plato pertenece a una sola estación. La coordinación
> entre estaciones se resuelve en el tablero, no en el catálogo.

---

## Carta bilingüe *(v2.0 — ADR-031)*

Cada categoría e ítem admite un subdocumento `translations` con las variantes por idioma. Reglas:

- El **español es el original**: `name` y `description` son la fuente. `translations.en` es opcional.
- Si falta la traducción de un plato, el menú **cae al español** para ese plato en vez de ocultarlo.
- El idioma se resuelve por este orden: elección explícita del comensal → país declarado al
  identificarse (ADR-031) → `Accept-Language` → español.
- La caché de Redis guarda el catálogo **ya resuelto por idioma**: `catalog:{tenantId}:{lang}`. El
  TTL y la invalidación no cambian; solo se multiplican las claves por idioma.

---

## Cache Strategy

- Key: `catalog:{tenantId}` — JSON serialization of the full catalog document  
- TTL: 300 seconds (5 min)  
- Read: check Redis first; on miss, query MongoDB and populate cache  
- Invalidation: `DEL catalog:{tenantId}` on every write operation (add/edit/delete item or category)
- *(diseñado — PRD-019)* La clave guarda el catálogo **completo y sin filtrar por modo** (todos los `visibleIn`, el branding de día y el subdocumento `night`). El **filtro se aplica al servir** (`GetPublicCatalogQuery`, después de leer la caché; requiere `[JsonInclude]` en `VisibleIn`, ver §Visibilidad por modo) y **al validar** (Orders `CatalogValidationService` → `409 ITEM_NOT_IN_MODE`), nunca al cachear: una sola entrada sirve a los dos modos y a Orders, y no hay riesgo de servir la carta de día desde una caché poblada de noche. Orders no usa los modelos de Catalog: su `CatalogCacheDto` (L1) y su `CatalogReadDocument` (L2) se amplían con `visibleIn` de ítems y categorías (SDD-004 §POST /orders).
- *(diseñado — PRD-019)* Al recibir `VenueModeChangedNotification` (SDD-001), un handler de `MestoBeach.Catalog.Application` (`VenueModeChangedCacheHandler`, usa `ICatalogCacheService.InvalidateAsync`) hace `DEL catalog:{tenantId}` **de forma defensiva**: no hace falta para la corrección (la caché no depende del modo), pero garantiza que el primer `GET` tras el cambio no arrastre un documento de hasta 5 min si justo se editó la carta.

## Image Storage

- **Backend**: Azure Blob Storage en producción; en **dev**, emulador **Azurite** (`docker-compose`, puerto `10000`). Config en `appsettings` sección `BlobStorage` (`ConnectionString`, `ContainerName=catalog-images`, `BaseUrl`). El contenedor se crea con acceso público de blob (`PublicAccessType.Blob`) para que el `<img>` del cliente lo lea anónimo.
- Container paths:
  - Imágenes de producto: `catalog-images/{tenantId}/{itemId}/{index}.webp`
  - Branding: `branding/{tenantId}/{logo|hero}-{guid}.webp` (path único por subida → evita caché obsoleto; la imagen anterior se borra tras subir la nueva)
- Max file size: 2 MB por imagen de producto; 4 MB para branding  
- Accepted input formats: JPG, PNG, WebP  
- Backend converts/resizes product images to two variants on upload: Main 800×600 px WebP; Thumbnail 200×150 px WebP. *(actualizado)* Branding tiene tratamiento propio: portada ≤ 2048×1152 WebP (calidad 86) y logo ≤ 1024×1024 WebP sin pérdida con transparencia (ver `POST /branding/{kind}/image`).
- Max 3 images per item; index 0 is the main image used in listings  
- Deletion removes the blob from Azure and splices the `images` array; remaining images retain their original indices (array is re-indexed after removal)

## QR Generation

> El QR lo genera **Sessions** (`GET /tables/{tableId}/qr`, SDD-003); esta sección es un resumen. *(actualizado)* hoy el host va fijo en código (`localhost:3000/{slug}?t=`); *(diseñado — PRD-019)* la URL codificada pasa a **`{MENU_PUBLIC_URL}/mesa?tenant={tenantId}&table={tableCode}`**.

- Library: `QRCoder` (.NET)  
- Output format: PNG 1000×1000 px  
- Encoded URL: `https://mesto.app/{tenantSlug}?t={tableCode}`  
- Restaurant logo overlaid at center if `branding/{tenantId}/logo.png` exists in Azure Blob  
- QR is generated on-demand and streamed in the response (not persisted)

---

## Endpoints

---

### GET /api/v1/catalog/{tenantId}

**Objetivo**
Retorna el catálogo completo del restaurante filtrado para consumo público (sin items eliminados ni sin stock), usado por la app de menú del cliente.

**Auth**
Sin autenticación requerida.

**Request**
- Path params: `{tenantId}` — UUID del tenant
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "tenantId": "550e8400-e29b-41d4-a716-446655440000",
  "branding": {
    "displayName": "La Parrilla del Bry",
    "slogan": "pa' que sepa qué es comer bueno",
    "logoUrl": "https://blob.azure.net/catalog-images/branding/tenant-uuid/logo-ab12.webp",
    "heroUrl": "https://blob.azure.net/catalog-images/branding/tenant-uuid/hero-cd34.webp",
    "primaryColor": "#f4b214",
    "theme": "light",
    "fontKey": "playfair"
  },
  "categories": [
    {
      "id": "cat-uuid-1",
      "name": "Entradas",
      "displayOrder": 1
    }
  ],
  "items": [
    {
      "id": "item-uuid-1",
      "categoryId": "cat-uuid-1",
      "name": "Empanadas de pipián",
      "description": "Tres empanadas rellenas de pipián con ají",
      "priceCents": 1200,
      "imageUrl": "https://blob.azure.net/catalog-images/tenant-uuid/item-uuid-1/0.webp",
      "available": true,
      "stockEnabled": true,
      "stockQty": 8,
      "displayOrder": 1,
      "allowNotes": true,
      "modifierGroups": [
        {
          "id": "grp-uuid-1",
          "name": "Término de la carne",
          "type": "SINGLE",
          "required": true,
          "minSelect": 1,
          "maxSelect": 1,
          "options": [
            { "id": "opt-1", "name": "Tres cuartos", "priceDeltaCents": 0, "available": true, "isDefault": true },
            { "id": "opt-2", "name": "Medio", "priceDeltaCents": 0, "available": true, "isDefault": false }
          ]
        }
      ]
    }
  ]
}
```

**Response 200 — con el local en `NIGHT`** *(diseñado — pendiente de implementación, PRD-019)*
```json
{
  "tenantId": "550e8400-e29b-41d4-a716-446655440000",
  "venueMode": "NIGHT",
  "branding": {
    "displayName": "Mesto Beach",
    "slogan": "La noche empieza en la orilla",
    "logoUrl": "https://blob.azure.net/catalog-images/branding/tenant-uuid/night-logo-ef56.webp",
    "heroUrl": "https://blob.azure.net/catalog-images/branding/tenant-uuid/hero-cd34.webp",
    "primaryColor": "#AA8453",
    "theme": "night",
    "backgroundColor": null,
    "fontKey": "mestobeach"
  },
  "categories": [
    { "id": "cat-bar", "name": "Cócteles", "displayOrder": 2 }
  ],
  "items": [
    { "id": "item-gin",  "categoryId": "cat-bar", "name": "Gin tonic", "priceCents": 1200, "imageUrl": null, "available": true, "stockEnabled": false, "stockQty": 0, "displayOrder": 1, "allowNotes": false, "modifierGroups": [] },
    { "id": "item-shot", "categoryId": "cat-bar", "name": "Shot",      "priceCents": 400,  "imageUrl": null, "available": true, "stockEnabled": false, "stockQty": 0, "displayOrder": 2, "allowNotes": false, "modifierGroups": [] }
  ]
}
```
> «Chuletón» (`visibleIn: ["RESTAURANT"]`) y su categoría «Parrilla» no viajan; «Shot» (`["NIGHT"]`) sí.
> `branding` es el **resuelto**: `slogan` y `logoUrl` vienen de `night`, `heroUrl` y `fontKey` heredan del
> día, `theme`/`primaryColor` son los defaults de Night. `visibleIn` **no se expone**: la respuesta ya viene
> filtrada. En `RESTAURANT` la forma de la respuesta es la de siempre más `"venueMode": "RESTAURANT"`, pero se
> aplican los mismos filtros: no viajan los ítems solo de noche ni las categorías sin ítems visibles. Hoy sí
> viajan las categorías vacías (`GetPublicCatalogQuery.cs`). El menú y la caja ya las ocultaban; la PDA
> (`ComandaDrawer`) deja de mostrar chips vacíos.

**Validaciones**
- `tenantId`: UUID v4 válido; si el documento de catálogo no existe para el tenant, retorna `items: []` y `categories: []`

**Reglas de Negocio**
- *(diseñado — PRD-019)* **Filtro por modo**: el handler resuelve el modo activo con `IVenueModeProvider` (sin parámetro de query: el cliente no elige el modo) **después** de leer la caché, y descarta las categorías e ítems cuyo `visibleIn` no lo incluye, y después las categorías sin ningún ítem visible en el modo activo. Aplica en **los dos modos** y también a las categorías que ya estaban vacías o solo tienen platos borrados. Un ítem es visible si tiene `deletedAt: null` y su `visibleIn` y el de su categoría incluyen el modo. Los agotados (`available: false`) cuentan como visibles: viajan y mantienen la categoría
- *(diseñado — PRD-019)* `venueMode` (`"RESTAURANT"` | `"NIGHT"`) viaja en la raíz para que el menú aplique el tema y, si el modo no coincide con el de la carta cargada, recargue la carta y depure el carrito (caso defensivo); el **sondeo** de modo lo hace contra `GET /api/v1/venue/{tenantId}` (SDD-001), más barato
- *(diseñado — PRD-019)* `branding` es el **resuelto** para el modo (§Visibilidad por modo y branding Night); en `RESTAURANT`, o con `night.enabled = false`, es el de día tal cual
- *(diseñado — PRD-019)* `Cache-Control: no-store`: la respuesta depende del modo activo, que cambia sin que la URL cambie
- Solo se incluyen items con `deletedAt: null`
- Si `stockEnabled = true` y `stockQty = 0`, el item se incluye con `available: false`
- Los campos internos `stockQty`, `lowStockThreshold`, `stockEnabled`, `createdAt`, `updatedAt`, `deletedAt` se excluyen de la respuesta pública
- Solo se retorna `imageUrl` (index 0 del array `images`); las imágenes adicionales no se exponen en este endpoint
- La respuesta se intenta leer desde `catalog:{tenantId}` en Redis; en miss se lee de MongoDB y se escribe en Redis con TTL 300s
- Los items se ordenan por `displayOrder` ASC dentro de cada categoría; las categorías se ordenan por `displayOrder` ASC
- Se incluye el objeto `branding` (Apariencia) si el tenant lo configuró; si no, se devuelven los valores por defecto del menú Mesto. El menú lo aplica como tema (colores/fuente/hero/logo)

**Errores**
| Código | Condición |
|---|---|
| 400 | `tenantId` no es un UUID v4 válido |
| 404 | Tenant no encontrado en el sistema (no existe en Identity DB) |
| 503 | MongoDB y Redis ambos inaccesibles |
| 503 | *(diseñado — PRD-019)* `VENUE_MODE_UNAVAILABLE`: no se puede saber el modo (Redis y Postgres caídos); nunca se sirve la carta sin filtrar |

**Observabilidad**
- Log: `"catalog.public.get"` `{ tenantId, cacheHit: bool, itemCount, durationMs }` — *(diseñado — PRD-019)* añade `venueMode` y `hiddenByMode` (ítems descartados por el filtro)
- Métrica: `catalog.public_get` (counter)
- Métrica: `catalog.cache_hit` (counter, label: `tenantId`)
- Métrica: `catalog.cache_miss` (counter, label: `tenantId`)

**Casos de Aceptación**
- [ ] Retorna 200 con items y categorías ordenados cuando el catálogo existe y hay cache hit
- [ ] Retorna 200 leyendo de MongoDB cuando Redis no tiene la clave (cache miss), y luego popula cache
- [ ] Items con `deletedAt != null` no aparecen en la respuesta
- [ ] Item con `stockEnabled: true` y `stockQty: 0` aparece con `available: false`
- [ ] Retorna `items: []` y `categories: []` si el tenant existe pero no tiene catálogo creado
- [ ] Retorna 400 si `tenantId` no es UUID válido
- [ ] Retorna 404 si el tenant no existe
- [ ] *(diseñado — PRD-019)* Con el local en `NIGHT`, tanto en cache MISS como en cache HIT (segunda petición), «Chuletón» (`["RESTAURANT"]`) y «Parrilla» no aparecen y «Shot» (`["NIGHT"]`) sí; `venueMode: "NIGHT"`, `branding.theme: "night"` y `primaryColor: "#AA8453"` si el admin no fijó otro
- [ ] *(diseñado — PRD-019)* Una categoría con `visibleIn: ["RESTAURANT"]` sigue oculta en `NIGHT` en el segundo GET (HIT)
- [ ] *(diseñado — PRD-019)* En `RESTAURANT`, una categoría sin ítems, o cuyos ítems están todos borrados o son `["NIGHT"]`, no aparece en `categories`; una categoría con un solo ítem agotado sí aparece
- [ ] *(diseñado — PRD-019)* Día con `backgroundColor: "#faf4e9"` y Night sin configurar → en `NIGHT` `branding.backgroundColor` ausente/null, `theme: "night"`, `primaryColor: "#AA8453"`, y el menú pinta el fondo `#161616`
- [ ] *(diseñado — PRD-019)* Con `night.enabled = false`, en `NIGHT` el branding es el de día y los ítems siguen filtrados por `visibleIn`
- [ ] *(diseñado — PRD-019)* La clave Redis contiene «Chuletón» aunque el local esté en `NIGHT` (caché sin filtrar)

---

### GET /api/v1/catalog/{tenantId}/categories

**Objetivo**
Retorna la lista completa de categorías del catálogo para gestión en el panel Admin.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params: `{tenantId}` — UUID del tenant
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "categories": [
    {
      "id": "cat-uuid-1",
      "name": "Entradas",
      "displayOrder": 1,
      "itemCount": 4,
      "createdAt": "2025-11-01T14:00:00Z"
    },
    {
      "id": "cat-uuid-2",
      "name": "Platos fuertes",
      "displayOrder": 2,
      "itemCount": 9,
      "createdAt": "2025-11-01T14:05:00Z"
    }
  ]
}
```

**Validaciones**
- `tenantId`: UUID v4 válido
- El JWT debe pertenecer al mismo `tenantId` o ser Platform Admin

**Reglas de Negocio**
- `itemCount` refleja únicamente items con `deletedAt: null` asociados a cada categoría
- *(diseñado — PRD-019)* Cada categoría incluye `visibleIn` (el admin pinta los chips «Día / Noche»); las ausentes en Mongo se devuelven como `["RESTAURANT","NIGHT"]`
- Ordenadas por `displayOrder` ASC
- Este endpoint lee desde MongoDB directamente (no cache), para garantizar consistencia administrativa
- Un Platform Admin puede consultar el catálogo de cualquier tenant

**Errores**
| Código | Condición |
|---|---|
| 400 | `tenantId` no es un UUID v4 válido |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero pertenece a un tenant diferente y no es Platform Admin |
| 404 | Tenant no encontrado |

**Observabilidad**
- Log: `"catalog.admin.categories.list"` `{ tenantId, requesterId, categoryCount }`
- Métrica: `catalog.admin_categories_list` (counter)

**Casos de Aceptación**
- [ ] Retorna 200 con lista de categorías ordenadas por `displayOrder` para Admin del mismo tenant
- [ ] Platform Admin puede consultar categorías de cualquier tenant
- [ ] `itemCount` excluye items con `deletedAt != null`
- [ ] Retorna 401 si no hay JWT
- [ ] Retorna 403 si el JWT es de un tenant diferente y no es Platform Admin

---

### POST /api/v1/catalog/{tenantId}/categories

**Objetivo**
Crea una nueva categoría en el catálogo del restaurante.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params: `{tenantId}` — UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "name": "Postres",
  "displayOrder": 5,
  "visibleIn": ["NIGHT"]
}
```
> `visibleIn` *(diseñado — pendiente de implementación, PRD-019)*.

**Response 201**
```json
{
  "id": "cat-uuid-new",
  "name": "Postres",
  "displayOrder": 5,
  "visibleIn": ["NIGHT"],
  "createdAt": "2025-12-01T10:00:00Z"
}
```

**Validaciones**
- `name`: requerido, string, 1–100 caracteres, no puede ser solo espacios en blanco
- `displayOrder`: requerido, integer, >= 0
- `visibleIn` *(diseñado — PRD-019)*: opcional; si se provee, array de strings que, tras normalizar (mayúsculas, sin duplicados), debe quedar no vacío → si no, `400 VISIBLE_IN_EMPTY`; un valor que ya en mayúsculas no sea `RESTAURANT` ni `NIGHT` → `400 INVALID_MODE`; los duplicados no son error

**Reglas de Negocio**
- Se genera un UUID v4 nuevo para el campo `id` de la categoría
- *(diseñado — PRD-019)* `visibleIn`: si falta, el registro nace visible en ambos modos; el campo no se escribe en Mongo y se materializa al leer; si se provee, se guarda normalizado (§Visibilidad por modo)
- Se hace `$push` al array `categories` del documento del tenant en MongoDB
- Se actualiza `updatedAt` del documento raíz
- Se invalida la cache: `DEL catalog:{tenantId}`
- No hay restricción de unicidad en `name`, pero es recomendable que el cliente Admin prevenga duplicados en la UI
- Si el documento de catálogo del tenant no existe, se crea el documento con la primera categoría

**Errores**
| Código | Condición |
|---|---|
| 400 | `name` ausente, vacío o excede 100 caracteres |
| 400 | `displayOrder` ausente o no es entero >= 0 |
| 400 | *(diseñado — PRD-019)* `visibleIn` vacío (`VISIBLE_IN_EMPTY`) o con un valor fuera de `RESTAURANT`/`NIGHT` (`INVALID_MODE`) |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero tenant incorrecto y no es Platform Admin |
| 404 | Tenant no encontrado |

**Observabilidad**
- Log: `"catalog.admin.category.created"` `{ tenantId, categoryId, name, requesterId }`
- Métrica: `catalog.category_created` (counter)

**Casos de Aceptación**
- [ ] Crea categoría correctamente y retorna 201 con el objeto creado
- [ ] Invalida `catalog:{tenantId}` en Redis tras la creación
- [ ] Crea el documento de catálogo si no existía para el tenant
- [ ] Retorna 400 si `name` está vacío o supera 100 caracteres
- [ ] Retorna 400 si `displayOrder` no es entero
- [ ] Retorna 401 si no hay JWT
- [ ] Retorna 403 si el JWT es de otro tenant
- [ ] *(diseñado — PRD-019)* Crear una categoría con `["NIGHT"]` → 201 con `visibleIn: ["NIGHT"]`; con el local en `RESTAURANT` ni ella ni sus ítems aparecen en `GET /catalog/{tenantId}`; sin `visibleIn` → visible en ambos; `{ "visibleIn": [] }` → 400 `VISIBLE_IN_EMPTY`

---

### PATCH /api/v1/catalog/{tenantId}/categories/{categoryId}

**Objetivo**
Actualiza el nombre y/o el orden de visualización de una categoría existente.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params:
  - `{tenantId}` — UUID del tenant
  - `{categoryId}` — UUID de la categoría
- Query params: Ninguno
- Body (todos los campos son opcionales; se aplica patch parcial):
```json
{
  "name": "Postres especiales",
  "displayOrder": 6,
  "visibleIn": ["RESTAURANT"]
}
```
> `visibleIn` *(diseñado — pendiente de implementación, PRD-019)*.

**Response 200**
```json
{
  "id": "cat-uuid-1",
  "name": "Postres especiales",
  "displayOrder": 6,
  "createdAt": "2025-11-01T14:00:00Z"
}
```

**Validaciones**
- `categoryId`: UUID v4 válido y debe existir en el array `categories` del documento del tenant
- `name`: si se provee, string, 1–100 caracteres, no puede ser solo espacios en blanco
- `displayOrder`: si se provee, integer, >= 0
- `visibleIn` *(diseñado — PRD-019)*: si se provee, array de strings. Tras normalizar (mayúsculas, sin duplicados) debe quedar no vacío → si no, `400 VISIBLE_IN_EMPTY`. Un valor que, ya en mayúsculas, no sea `RESTAURANT` ni `NIGHT` → `400 INVALID_MODE`. Los duplicados no son error
- Al menos un campo debe estar presente en el body

**Reglas de Negocio**
- Solo se actualizan los campos presentes en el body (PATCH semántico)
- Se usa `$set` sobre el subdocumento de la categoría en MongoDB (`categories.$[elem]` con `arrayFilters`)
- *(diseñado — PRD-019)* `visibleIn` se guarda normalizado (§Visibilidad por modo). Cambiarlo **no toca** el `visibleIn` de sus ítems: una categoría oculta arrastra a sus platos al servir, pero cada plato conserva su propio valor por si la categoría vuelve a abrirse. Se permite ocultar la categoría en el **modo activo** aunque haya pedidos en curso (la comanda ya tiene su snapshot)
- Se actualiza `updatedAt` del documento raíz
- Se invalida la cache: `DEL catalog:{tenantId}`

**Errores**
| Código | Condición |
|---|---|
| 400 | Body vacío (ningún campo modificable presente) |
| 400 | `name` excede 100 caracteres o es solo espacios |
| 400 | `displayOrder` no es entero >= 0 |
| 400 | *(diseñado — PRD-019)* `visibleIn` vacío (`VISIBLE_IN_EMPTY`) o con un valor fuera de `RESTAURANT`/`NIGHT` (`INVALID_MODE`) |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero tenant incorrecto y no es Platform Admin |
| 404 | Tenant no encontrado o `categoryId` no existe |

**Observabilidad**
- Log: `"catalog.admin.category.updated"` `{ tenantId, categoryId, changedFields, requesterId }`
- Métrica: `catalog.category_updated` (counter)

**Casos de Aceptación**
- [ ] Actualiza `name` y `displayOrder` correctamente retornando 200
- [ ] Actualiza solo `name` si `displayOrder` no está en el body
- [ ] Invalida `catalog:{tenantId}` en Redis tras la actualización
- [ ] Retorna 400 si el body está vacío
- [ ] Retorna 404 si `categoryId` no existe en el tenant
- [ ] Retorna 401/403 según corresponda
- [ ] *(diseñado — PRD-019)* `{ "visibleIn": ["RESTAURANT"] }` → 200 y, con el local en `NIGHT`, la categoría y sus ítems desaparecen de `GET /catalog/{tenantId}`; `{ "visibleIn": [] }` → 400 `VISIBLE_IN_EMPTY`

---

### DELETE /api/v1/catalog/{tenantId}/categories/{categoryId}

**Objetivo**
Elimina permanentemente una categoría del catálogo, únicamente si no tiene items activos asociados.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params:
  - `{tenantId}` — UUID del tenant
  - `{categoryId}` — UUID de la categoría
- Query params: Ninguno
- Body: Ninguno

**Response 204**
Sin body.

**Validaciones**
- `categoryId`: UUID v4 válido y debe existir en el array `categories` del documento del tenant

**Reglas de Negocio**
- Se verifica que no existan items con `categoryId = {categoryId}` y `deletedAt: null`; si existen, se retorna 409
- Si la validación pasa, se elimina el elemento del array `categories` con `$pull`
- Se actualiza `updatedAt` del documento raíz
- Se invalida la cache: `DEL catalog:{tenantId}`
- Los items soft-deleted (con `deletedAt != null`) no bloquean la eliminación de la categoría
- La eliminación es permanente (no hay soft-delete para categorías)

**Errores**
| Código | Condición |
|---|---|
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero tenant incorrecto y no es Platform Admin |
| 404 | Tenant no encontrado o `categoryId` no existe |
| 409 | La categoría tiene uno o más items activos (`deletedAt: null`) asociados |

**Observabilidad**
- Log: `"catalog.admin.category.deleted"` `{ tenantId, categoryId, requesterId }`
- Log (bloqueado): `"catalog.admin.category.delete_blocked"` `{ tenantId, categoryId, activeItemCount, requesterId }`
- Métrica: `catalog.category_deleted` (counter)
- Métrica: `catalog.category_delete_blocked` (counter)

**Casos de Aceptación**
- [ ] Elimina la categoría y retorna 204 cuando no tiene items activos
- [ ] Retorna 409 si la categoría tiene items activos
- [ ] Categoría con solo items soft-deleted puede eliminarse correctamente
- [ ] Invalida `catalog:{tenantId}` en Redis tras la eliminación
- [ ] Retorna 404 si `categoryId` no existe
- [ ] Retorna 401/403 según corresponda

---

### GET /api/v1/catalog/{tenantId}/items

**Objetivo**
Retorna la lista de items del catálogo con filtros opcionales para gestión en el panel Admin.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params: `{tenantId}` — UUID del tenant
- Query params:
  - `categoryId` (opcional) — UUID: filtra por categoría
  - `available` (opcional) — `true`|`false`: filtra por disponibilidad
  - `includeDeleted` (opcional) — `true`|`false`, default `false`: incluye items con `deletedAt != null`
  - `page` (opcional) — integer >= 1, default `1`
  - `pageSize` (opcional) — integer 1–100, default `50`
- Body: Ninguno

**Response 200**
```json
{
  "items": [
    {
      "id": "item-uuid-1",
      "categoryId": "cat-uuid-1",
      "categoryName": "Entradas",
      "name": "Empanadas de pipián",
      "description": "Tres empanadas rellenas de pipián con ají",
      "priceCents": 1200,
      "images": [
        "https://blob.azure.net/catalog-images/tenant-uuid/item-uuid-1/0.webp",
        "https://blob.azure.net/catalog-images/tenant-uuid/item-uuid-1/1.webp"
      ],
      "available": true,
      "stockEnabled": true,
      "stockQty": 8,
      "lowStockThreshold": 3,
      "displayOrder": 1,
      "deletedAt": null,
      "createdAt": "2025-11-01T14:10:00Z",
      "updatedAt": "2025-11-20T09:00:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "total": 1
  }
}
```

**Validaciones**
- `categoryId`: si se provee, UUID v4 válido
- `available`: si se provee, debe ser `"true"` o `"false"`
- `page`: entero >= 1
- `pageSize`: entero entre 1 y 100

**Reglas de Negocio**
- Por defecto (`includeDeleted: false`) solo se retornan items con `deletedAt: null`
- Con `includeDeleted: true` se retornan también los eliminados (útil para auditoría)
- `categoryName` se resuelve haciendo lookup en el array `categories` del mismo documento
- *(diseñado — PRD-019)* Cada item incluye `visibleIn` (ausente en Mongo → `["RESTAURANT","NIGHT"]`); el filtro «Ver: Todo | Día | Noche» del admin se aplica en el cliente, este endpoint no filtra por modo
- Se lee directo de MongoDB (sin cache) para consistencia administrativa
- Ordenado por `displayOrder` ASC, luego `createdAt` DESC como desempate

**Errores**
| Código | Condición |
|---|---|
| 400 | `categoryId` no es UUID v4 válido |
| 400 | `page` o `pageSize` fuera de rango |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero tenant incorrecto y no es Platform Admin |
| 404 | Tenant no encontrado |

**Observabilidad**
- Log: `"catalog.admin.items.list"` `{ tenantId, filters: { categoryId, available, includeDeleted }, page, pageSize, resultCount, requesterId }`
- Métrica: `catalog.admin_items_list` (counter)

**Casos de Aceptación**
- [ ] Retorna lista paginada con todos los items activos por defecto
- [ ] Filtro `categoryId` retorna solo items de esa categoría
- [ ] Filtro `available=false` retorna solo items no disponibles
- [ ] `includeDeleted=true` incluye items con `deletedAt != null`
- [ ] Paginación funciona correctamente con `page` y `pageSize`
- [ ] Retorna 400 para parámetros de query inválidos
- [ ] Retorna 401/403 según corresponda

---

### POST /api/v1/catalog/{tenantId}/items

**Objetivo**
Crea un nuevo item en el catálogo del restaurante.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params: `{tenantId}` — UUID del tenant
- Query params: Ninguno
- Body:
```json
{
  "categoryId": "cat-uuid-1",
  "name": "Empanadas de pipián",
  "description": "Tres empanadas rellenas de pipián con ají",
  "priceCents": 1200,
  "available": true,
  "stockEnabled": true,
  "stockQty": 20,
  "lowStockThreshold": 5,
  "displayOrder": 1,
  "visibleIn": ["NIGHT"]
}
```
> `visibleIn` *(diseñado — pendiente de implementación, PRD-019)*.

**Response 201**
```json
{
  "id": "item-uuid-new",
  "categoryId": "cat-uuid-1",
  "name": "Empanadas de pipián",
  "description": "Tres empanadas rellenas de pipián con ají",
  "priceCents": 1200,
  "images": [],
  "available": true,
  "stockEnabled": true,
  "stockQty": 20,
  "lowStockThreshold": 5,
  "displayOrder": 1,
  "visibleIn": ["NIGHT"],
  "deletedAt": null,
  "createdAt": "2025-12-01T10:00:00Z",
  "updatedAt": "2025-12-01T10:00:00Z"
}
```

**Validaciones**
- `categoryId`: requerido, UUID v4 válido, debe existir en el array `categories` del documento del tenant
- `name`: requerido, string, 1–150 caracteres, no puede ser solo espacios en blanco
- `description`: opcional, string, máximo 300 caracteres
- `priceCents`: requerido, integer, > 0 (euros en céntimos)
- `available`: opcional, boolean, default `true`
- `stockEnabled`: opcional, boolean, default `false`
- `stockQty`: requerido si `stockEnabled: true`, integer, >= 0; ignorado si `stockEnabled: false` (se almacena como 0)
- `lowStockThreshold`: opcional, integer, >= 0, default `0`
- `displayOrder`: requerido, integer, >= 0
- `visibleIn` *(diseñado — PRD-019)*: opcional; si se provee, array de strings que, tras normalizar (mayúsculas, sin duplicados), debe quedar no vacío → si no, `400 VISIBLE_IN_EMPTY`; un valor que ya en mayúsculas no sea `RESTAURANT` ni `NIGHT` → `400 INVALID_MODE`; los duplicados no son error

**Reglas de Negocio**
- Se genera un UUID v4 nuevo para `id`
- `images` se inicializa como array vacío; las imágenes se suben posteriormente con el endpoint de imágenes
- *(diseñado — PRD-019)* `visibleIn`: si falta, el registro nace visible en ambos modos; el campo no se escribe en Mongo y se materializa al leer; si se provee, se guarda normalizado (§Visibilidad por modo). Su efecto depende también del `visibleIn` de la categoría
- Se hace `$push` al array `items` del documento del tenant
- Se actualiza `updatedAt` del documento raíz
- Se invalida la cache: `DEL catalog:{tenantId}`
- `createdAt` y `updatedAt` se fijan al timestamp actual UTC
- `deletedAt` se establece en `null`

**Errores**
| Código | Condición |
|---|---|
| 400 | `name` ausente, vacío, o excede 150 caracteres |
| 400 | `description` excede 300 caracteres |
| 400 | `priceCents` ausente, no es entero o es <= 0 |
| 400 | `displayOrder` ausente o no es entero >= 0 |
| 400 | `stockEnabled: true` pero `stockQty` ausente o negativo |
| 400 | `categoryId` ausente o no es UUID v4 válido |
| 400 | *(diseñado — PRD-019)* `visibleIn` vacío (`VISIBLE_IN_EMPTY`) o con un valor fuera de `RESTAURANT`/`NIGHT` (`INVALID_MODE`) |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero tenant incorrecto y no es Platform Admin |
| 404 | Tenant no encontrado o `categoryId` no existe en el tenant |

**Observabilidad**
- Log: `"catalog.admin.item.created"` `{ tenantId, itemId, categoryId, name, priceCents, requesterId }`
- Métrica: `catalog.item_created` (counter)

**Casos de Aceptación**
- [ ] Crea item correctamente y retorna 201 con el objeto completo
- [ ] `images` retorna array vacío en la respuesta de creación
- [ ] Invalida `catalog:{tenantId}` en Redis tras la creación
- [ ] Retorna 400 si `categoryId` no existe en el tenant
- [ ] Retorna 400 si `priceCents` es 0 o negativo
- [ ] Retorna 400 si `stockEnabled: true` y `stockQty` no se provee
- [ ] Retorna 401/403 según corresponda
- [ ] *(diseñado — PRD-019)* Crear «Shot» con `["NIGHT"]` → 201; con el local en `RESTAURANT` nunca aparece en `GET /catalog/{tenantId}` y pedirlo devuelve `409 ITEM_NOT_IN_MODE`; sin `visibleIn` → visible en ambos

---

### PATCH /api/v1/catalog/{tenantId}/items/{itemId}

**Objetivo**
Actualiza los atributos de un item existente del catálogo (excluye imágenes).

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params:
  - `{tenantId}` — UUID del tenant
  - `{itemId}` — UUID del item
- Query params: Ninguno
- Body (todos los campos son opcionales; se aplica patch parcial):
```json
{
  "name": "Empanadas de pipián (x3)",
  "description": "Tres empanadas rellenas de pipián con ají amarillo",
  "priceCents": 1350,
  "categoryId": "cat-uuid-2",
  "available": false,
  "stockEnabled": true,
  "stockQty": 15,
  "lowStockThreshold": 4,
  "displayOrder": 2,
  "visibleIn": ["NIGHT"]
}
```
> `visibleIn` *(diseñado — pendiente de implementación, PRD-019)*.

**Response 200**
```json
{
  "id": "item-uuid-1",
  "categoryId": "cat-uuid-2",
  "name": "Empanadas de pipián (x3)",
  "description": "Tres empanadas rellenas de pipián con ají amarillo",
  "priceCents": 1350,
  "images": [
    "https://blob.azure.net/catalog-images/tenant-uuid/item-uuid-1/0.webp"
  ],
  "available": false,
  "stockEnabled": true,
  "stockQty": 15,
  "lowStockThreshold": 4,
  "displayOrder": 2,
  "deletedAt": null,
  "createdAt": "2025-11-01T14:10:00Z",
  "updatedAt": "2025-12-01T11:00:00Z"
}
```

**Validaciones**
- `itemId`: UUID v4 válido, debe existir en el array `items` del tenant con `deletedAt: null`
- `name`: si se provee, string, 1–150 caracteres, no puede ser solo espacios en blanco
- `description`: si se provee, string, máximo 300 caracteres; enviar `null` para eliminar la descripción
- `priceCents`: si se provee, integer, > 0 (euros en céntimos)
- `categoryId`: si se provee, UUID v4 válido, debe existir en el array `categories` del tenant
- `available`: si se provee, boolean
- `stockEnabled`: si se provee, boolean
- `stockQty`: si se provee, integer, >= 0
- `lowStockThreshold`: si se provee, integer, >= 0
- `displayOrder`: si se provee, integer, >= 0
- `visibleIn` *(diseñado — PRD-019)*: si se provee, array de strings. Tras normalizar (mayúsculas, sin duplicados) debe quedar no vacío → si no, `400 VISIBLE_IN_EMPTY`. Un valor que, ya en mayúsculas, no sea `RESTAURANT` ni `NIGHT` → `400 INVALID_MODE`. Los duplicados no son error
- Al menos un campo debe estar presente en el body

**Reglas de Negocio**
- Solo se actualizan los campos presentes en el body (PATCH semántico)
- Se usa `$set` sobre el subdocumento del item con `arrayFilters`
- *(diseñado — PRD-019)* `visibleIn` se guarda normalizado (§Visibilidad por modo) y **sin fusionarlo** con el de la categoría; su efecto depende también del `visibleIn` de la categoría (§Visibilidad por modo). Quitar el modo activo a un ítem **no** cancela comandas en curso (snapshot en Orders); los pedidos **nuevos** con ese ítem reciben `409 ITEM_NOT_IN_MODE` en cuanto la caché se invalida
- `updatedAt` del item y del documento raíz se actualizan al timestamp actual UTC
- Se invalida la cache: `DEL catalog:{tenantId}`
- El cambio de precio no genera snapshot en este módulo; el snapshot ocurre en el módulo de Orders al momento de confirmar el pedido
- No se puede editar el campo `images` desde este endpoint; usar el endpoint de imágenes

**Errores**
| Código | Condición |
|---|---|
| 400 | Body vacío |
| 400 | `name` vacío o excede 150 caracteres |
| 400 | `description` excede 300 caracteres |
| 400 | `priceCents` no es entero o es <= 0 |
| 400 | `displayOrder` no es entero >= 0 |
| 400 | `categoryId` no existe en el tenant |
| 400 | *(diseñado — PRD-019)* `visibleIn` vacío (`VISIBLE_IN_EMPTY`) o con un valor fuera de `RESTAURANT`/`NIGHT` (`INVALID_MODE`) |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero tenant incorrecto y no es Platform Admin |
| 404 | Tenant no encontrado, `itemId` no existe, o item está soft-deleted |

**Observabilidad**
- Log: `"catalog.admin.item.updated"` `{ tenantId, itemId, changedFields, requesterId }`
- Métrica: `catalog.item_updated` (counter)

**Casos de Aceptación**
- [ ] Actualiza múltiples campos y retorna 200 con el objeto actualizado
- [ ] Actualiza solo el campo `available` a `false` correctamente
- [ ] Cambia `categoryId` a una categoría existente correctamente
- [ ] Invalida `catalog:{tenantId}` en Redis tras la actualización
- [ ] Retorna 404 si el item tiene `deletedAt != null`
- [ ] Retorna 400 si el body está vacío
- [ ] Retorna 401/403 según corresponda
- [ ] *(diseñado — PRD-019)* «Chuletón» con `{ "visibleIn": ["RESTAURANT"] }` → 200; en `NIGHT` no sale en el catálogo público y pedirlo devuelve `409 ITEM_NOT_IN_MODE` (Orders); `{ "visibleIn": [] }` → 400 `VISIBLE_IN_EMPTY`

---

### DELETE /api/v1/catalog/{tenantId}/items/{itemId}

**Objetivo**
Realiza un soft-delete de un item del catálogo, marcándolo como eliminado sin removerlo de la base de datos.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params:
  - `{tenantId}` — UUID del tenant
  - `{itemId}` — UUID del item
- Query params: Ninguno
- Body: Ninguno

**Response 204**
Sin body.

**Validaciones**
- `itemId`: UUID v4 válido, debe existir en el array `items` del tenant
- El item no debe tener ya `deletedAt != null` (retorna 404 si ya fue eliminado)

**Reglas de Negocio**
- Se aplica `$set: { "items.$.deletedAt": <timestamp UTC>, "items.$.updatedAt": <timestamp UTC> }` usando `arrayFilters`
- El documento del item se conserva en MongoDB indefinidamente (auditoría)
- El item deja de aparecer en `GET /catalog/{tenantId}` (endpoint público) inmediatamente
- Se invalida la cache: `DEL catalog:{tenantId}`
- Se actualiza `updatedAt` del documento raíz

**Errores**
| Código | Condición |
|---|---|
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero tenant incorrecto y no es Platform Admin |
| 404 | Tenant no encontrado, `itemId` no existe, o item ya tiene `deletedAt != null` |

**Observabilidad**
- Log: `"catalog.admin.item.deleted"` `{ tenantId, itemId, requesterId }`
- Métrica: `catalog.item_soft_deleted` (counter)

**Casos de Aceptación**
- [ ] Marca `deletedAt` en el item y retorna 204
- [ ] Item no aparece en el endpoint público tras el soft-delete
- [ ] Invalida `catalog:{tenantId}` en Redis
- [ ] Retorna 404 si el item ya estaba soft-deleted
- [ ] Retorna 401/403 según corresponda

---

### POST /api/v1/catalog/{tenantId}/items/{itemId}/images

**Objetivo**
Sube una imagen para un item del catálogo, la convierte a WebP en dos resoluciones y la almacena en Azure Blob Storage.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params:
  - `{tenantId}` — UUID del tenant
  - `{itemId}` — UUID del item
- Query params: Ninguno
- Body: `multipart/form-data`
  - Campo `file`: archivo de imagen (JPG, PNG o WebP), máximo 2 MB

**Response 201**
```json
{
  "imageIndex": 1,
  "url": "https://blob.azure.net/catalog-images/tenant-uuid/item-uuid-1/1.webp",
  "thumbnailUrl": "https://blob.azure.net/catalog-images/tenant-uuid/item-uuid-1/1_thumb.webp",
  "totalImages": 2
}
```

**Validaciones**
- `file`: requerido, Content-Type debe ser `image/jpeg`, `image/png` o `image/webp`
- Tamaño máximo: 2 097 152 bytes (2 MB)
- El item debe tener `deletedAt: null`
- El item no debe tener ya 3 imágenes en el array `images`

**Reglas de Negocio**
- El índice asignado es `images.length` actual (0, 1 o 2)
- El archivo se procesa con ImageSharp (.NET): se convierte a WebP y se generan dos resoluciones:
  - Main: 800×600 px → ruta `catalog-images/{tenantId}/{itemId}/{index}.webp`
  - Thumbnail: 200×150 px → ruta `catalog-images/{tenantId}/{itemId}/{index}_thumb.webp`
- La URL main se agrega al array `images` del item con `$push`
- `updatedAt` del item y del documento raíz se actualizan
- Se invalida la cache: `DEL catalog:{tenantId}`
- Index 0 es la imagen principal; el orden del array es el orden de visualización
- Si el upload a Azure Blob falla, la operación se revierte (no se modifica MongoDB)

**Errores**
| Código | Condición |
|---|---|
| 400 | Archivo no enviado en el campo `file` |
| 400 | Formato de archivo no soportado (no es JPG, PNG ni WebP) |
| 400 | Archivo excede 2 MB |
| 400 | El item ya tiene 3 imágenes (máximo alcanzado) |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero tenant incorrecto y no es Platform Admin |
| 404 | Tenant no encontrado, `itemId` no existe, o item está soft-deleted |
| 502 | Fallo en la subida a Azure Blob Storage |

**Observabilidad**
- Log: `"catalog.admin.item.image_uploaded"` `{ tenantId, itemId, imageIndex, sizeBytes, requesterId }`
- Log (error): `"catalog.admin.item.image_upload_failed"` `{ tenantId, itemId, error, requesterId }`
- Métrica: `catalog.image_uploaded` (counter)
- Métrica: `catalog.image_upload_failed` (counter)

**Casos de Aceptación**
- [ ] Sube imagen JPG válida, la convierte a WebP y retorna 201 con URL y thumbnailUrl
- [ ] Sube segunda y tercera imagen correctamente con índices 1 y 2
- [ ] Retorna 400 al intentar subir una cuarta imagen
- [ ] Retorna 400 si el archivo excede 2 MB
- [ ] Retorna 400 si el formato no es JPG, PNG ni WebP
- [ ] Invalida `catalog:{tenantId}` en Redis tras la subida
- [ ] Retorna 401/403 según corresponda

---

### DELETE /api/v1/catalog/{tenantId}/items/{itemId}/images/{imageIndex}

**Objetivo**
Elimina una imagen específica de un item del catálogo tanto de Azure Blob Storage como del array `images` en MongoDB.

**Auth**
JWT Staff con rol `Admin` o JWT Platform Admin.

**Request**
- Path params:
  - `{tenantId}` — UUID del tenant
  - `{itemId}` — UUID del item
  - `{imageIndex}` — Entero 0, 1 o 2: posición actual de la imagen en el array
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "remainingImages": [
    "https://blob.azure.net/catalog-images/tenant-uuid/item-uuid-1/0.webp"
  ],
  "totalImages": 1
}
```

**Validaciones**
- `imageIndex`: entero, debe ser 0, 1 o 2
- `imageIndex` debe estar dentro del rango del array `images` actual del item (no puede eliminar índice inexistente)
- El item debe tener `deletedAt: null`

**Reglas de Negocio**
- Se elimina el blob `catalog-images/{tenantId}/{itemId}/{imageIndex}.webp` de Azure Blob
- Se elimina también el thumbnail `catalog-images/{tenantId}/{itemId}/{imageIndex}_thumb.webp`
- Se remueve el elemento en la posición `imageIndex` del array `images` del item con `$unset` + `$pull` (o equivalente en MongoDB para arrays)
- El array resultante mantiene las URLs restantes; no se re-numeran los blobs en Azure (las URLs se actualizan en MongoDB solamente)
- `updatedAt` del item y del documento raíz se actualizan
- Se invalida la cache: `DEL catalog:{tenantId}`
- Si la eliminación de Azure Blob falla, igual se procede a actualizar MongoDB (best-effort en blob, con log de error)

**Errores**
| Código | Condición |
|---|---|
| 400 | `imageIndex` no es entero o no es 0, 1 o 2 |
| 400 | `imageIndex` es mayor o igual al número de imágenes actuales del item |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero tenant incorrecto y no es Platform Admin |
| 404 | Tenant no encontrado, `itemId` no existe, o item está soft-deleted |

**Observabilidad**
- Log: `"catalog.admin.item.image_deleted"` `{ tenantId, itemId, imageIndex, requesterId }`
- Log (warning): `"catalog.admin.item.image_blob_delete_failed"` `{ tenantId, itemId, imageIndex, blobPath, error }`
- Métrica: `catalog.image_deleted` (counter)

**Casos de Aceptación**
- [ ] Elimina imagen en índice 0 y retorna 200 con las imágenes restantes
- [ ] Elimina imagen en índice 1 de un item con 3 imágenes y retorna las 2 restantes
- [ ] Retorna 400 si `imageIndex` es 3 o mayor
- [ ] Retorna 400 si `imageIndex` supera el total de imágenes del item
- [ ] Log de warning si la eliminación de Azure Blob falla (pero MongoDB se actualiza igualmente)
- [ ] Invalida `catalog:{tenantId}` en Redis
- [ ] Retorna 401/403 según corresponda

---

### POST /api/v1/catalog/{tenantId}/cache-invalidate

**Objetivo**
Fuerza la invalidación del cache de catálogo en Redis para un tenant específico; destinado a acciones de soporte operativo.

**Auth**
JWT Platform Admin únicamente.

**Request**
- Path params: `{tenantId}` — UUID del tenant
- Query params: Ninguno
- Body: Ninguno

**Response 200**
```json
{
  "message": "Cache invalidado correctamente.",
  "key": "catalog:550e8400-e29b-41d4-a716-446655440000",
  "timestamp": "2025-12-01T12:00:00Z"
}
```

**Validaciones**
- `tenantId`: UUID v4 válido

**Reglas de Negocio**
- Ejecuta `DEL catalog:{tenantId}` en Redis
- No modifica MongoDB
- Si la clave no existe en Redis, la operación es exitosa de todas formas (idempotente)
- Solo accesible con rol Platform Admin; un Admin de restaurante no puede invocar este endpoint

**Errores**
| Código | Condición |
|---|---|
| 400 | `tenantId` no es un UUID v4 válido |
| 401 | JWT ausente o inválido |
| 403 | JWT válido pero no es Platform Admin |
| 503 | Redis inaccesible |

**Observabilidad**
- Log: `"catalog.ops.cache_invalidated"` `{ tenantId, requesterId, keyDeleted }`
- Métrica: `catalog.cache_force_invalidated` (counter)

**Casos de Aceptación**
- [ ] Platform Admin invoca el endpoint y retorna 200; la siguiente request al catálogo hace cache miss
- [ ] Retorna 200 aunque la clave no existiera en Redis (idempotente)
- [ ] Retorna 403 si el JWT es de un Admin de restaurante (no Platform Admin)
- [ ] Retorna 401 si no hay JWT
- [ ] Retorna 503 si Redis no responde

### Modifier groups (biblioteca de adiciones)

CRUD de la biblioteca de modificadores del tenant. Todos requieren rol `StaffAdmin` y validación de tenant. Cada escritura invalida la caché del catálogo (`DEL catalog:{tenantId}`).

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/v1/catalog/{tenantId}/modifier-groups` | Lista los grupos con `usedByItemCount` |
| POST | `/api/v1/catalog/{tenantId}/modifier-groups` | Crea un grupo (201) |
| PATCH | `/api/v1/catalog/{tenantId}/modifier-groups/{groupId}` | Reemplaza nombre/tipo/opciones del grupo |
| DELETE | `/api/v1/catalog/{tenantId}/modifier-groups/{groupId}` | Elimina el grupo y lo desvincula de todos los items |

**Request body (POST/PATCH)**
```json
{
  "name": "Término de la carne",
  "type": "SINGLE",
  "required": true,
  "minSelect": 1,
  "maxSelect": 1,
  "options": [
    { "name": "Tres cuartos", "priceDeltaCents": 0, "available": true, "isDefault": true },
    { "name": "Medio", "priceDeltaCents": 0, "available": true, "isDefault": false }
  ]
}
```

**Reglas**
- `type ∈ {SINGLE, MULTI, REMOVE}`; al menos una opción.
- REMOVE fuerza `priceDeltaCents = 0` y `required = false`.
- SINGLE fuerza `maxSelect = 1`; si `required`, `minSelect = 1`.
- `maxSelect >= minSelect` cuando está presente.
- Los ítems referencian grupos vía `modifierGroupIds` (extiende create/update item).

**Casos de Aceptación**
- [ ] Crear un grupo SINGLE requerido (1/2, 3/4) y un MULTI con costo (+Michelada) → aparecen en la lista con `usedByItemCount`
- [ ] Adjuntar un grupo a un item y pedir desde el menú → el catálogo público devuelve los grupos resueltos por item
- [ ] Editar el grupo afecta a todos los items que lo usan (biblioteca por referencia)
- [ ] Eliminar un grupo en uso lo desvincula de todos los items sin romper pedidos ya realizados (snapshot en Orders)

---

## Branding (Apariencia del menú)

Endpoints para administrar el subdocumento `branding` del tenant. Todos requieren JWT de staff con rol **Admin** (`StaffAdmin`) y que el `tenantId` del token coincida con el de la ruta. Las imágenes se procesan a WebP y se suben a Blob Storage (Azurite en dev); ver `## Image Storage`.

---

### GET /api/v1/catalog/{tenantId}/branding

**Objetivo**
Retorna la configuración de apariencia actual del tenant para precargar la página **Apariencia** del admin.

**Auth**
JWT de staff, rol Admin.

**Request**
- Path params: `{tenantId}` — UUID del tenant
- Body: Ninguno

**Response 200**
```json
{
  "displayName": "La Parrilla del Bry",
  "slogan": "pa' que sepa qué es comer bueno",
  "logoUrl": "https://blob.azure.net/catalog-images/branding/tenant-uuid/logo-ab12.webp",
  "heroUrl": "https://blob.azure.net/catalog-images/branding/tenant-uuid/hero-cd34.webp",
  "primaryColor": "#f4b214",
  "theme": "light",
  "backgroundColor": null,
  "fontKey": "playfair"
}
```
> `backgroundColor` *(actualizado)*: ya lo devuelve el código (`MenuBrandingDto`). El subdocumento `night` **no** viaja aquí: tiene su propio `GET /branding/night` *(diseñado — PRD-019)*.

**Reglas de Negocio**
- Si el tenant no tiene branding configurado, retorna los valores por defecto del menú Mesto (`primaryColor` base, `theme: light`, fuente por defecto, sin logo/hero).

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `Forbidden` | 403 | El `tenantId` del token no coincide con el de la ruta |

**Observabilidad**
- Log: `catalog.admin.branding.get` `{ tenantId, requesterId }`

**Casos de Aceptación**
- [ ] Tenant sin branding → devuelve defaults; tenant con branding → devuelve lo guardado

---

### PUT /api/v1/catalog/{tenantId}/branding

**Objetivo**
Actualiza los campos de texto/color/tema/fuente del branding (las imágenes se manejan en endpoints aparte).

**Auth**
JWT de staff, rol Admin.

**Request**
- Path params: `{tenantId}`
- Body:
```json
{
  "displayName": "La Parrilla del Bry",
  "slogan": "pa' que sepa qué es comer bueno",
  "primaryColor": "#f4b214",
  "theme": "light",
  "backgroundColor": null,
  "fontKey": "playfair"
}
```

**Response 200**
El objeto `MenuBranding` completo y actualizado (mismo shape que el GET).

**Validaciones**
- `displayName`: máx 80 chars · `slogan`: máx 140 chars
- `primaryColor`: hex `#RRGGBB`
- `backgroundColor` *(actualizado)*: opcional; si viene, hex `#RRGGBB` (se guarda en minúsculas; vacío = `null`)
- `theme ∈ {light, cream, dark}`; *(diseñado — PRD-019)* `+ night`
- `fontKey`: requerido, máx 40 chars. El backend no lo valida contra una lista: la lista curada vive en el cliente (`FONTS` de `apps/menu` y `apps/admin` `src/lib/menuBranding.ts`) y el menú cae a `mestobeach` si la clave es desconocida. *(diseñado — PRD-019)* El selector del admin (`apps/admin/src/lib/menuBranding.ts`) gana `mestobeach`; el menú ya la tiene.

**Reglas de Negocio**
- Conserva `logoUrl`/`heroUrl` existentes (no se tocan aquí); *(diseñado — PRD-019)* tampoco toca `night` (escritura `$set` por campo, ver §`branding.night`).
- Invalida el caché `catalog:{tenantId}` para que el menú vea el cambio en ≤ TTL.

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `Validation` | 400 | Algún campo viola las reglas |
| `Forbidden` | 403 | Tenant del token ≠ ruta |

**Observabilidad**
- Log: `catalog.admin.branding.updated` `{ tenantId, theme, font, requesterId }`

**Casos de Aceptación**
- [ ] Guardar acento + tema + fuente + textos → el menú del cliente refleja el cambio tras invalidar caché
- [ ] `primaryColor` inválido o `theme` fuera de la lista → 400
- [ ] *(diseñado — PRD-019)* Con Night configurado (slogan + logo de Night), cambiar el acento de día → `GET /branding/night` devuelve los mismos overrides y el blob `night-logo-*` sigue existiendo

---

### POST /api/v1/catalog/{tenantId}/branding/{kind}/image

**Objetivo**
Sube el **logo** o la **portada (hero)** del menú, la convierte a WebP y la almacena en Blob Storage.

**Auth**
JWT de staff, rol Admin.

**Request**
- Path params: `{tenantId}`, `{kind}` ∈ `logo | hero`
- Body: `multipart/form-data` con campo `file` (JPG/PNG/WebP)

**Response 201**
El objeto `MenuBranding` actualizado con el nuevo `logoUrl`/`heroUrl`.

**Validaciones**
- `kind ∈ {logo, hero}`
- Formato ∈ {JPG, PNG, WebP}; tamaño ≤ 4 MB

**Reglas de Negocio**
- *(actualizado)* `logo` se procesa con `ResizeLogoAsync` (≤ 1024×1024, WebP sin pérdida, con alfa); `hero` con `ResizeHeroAsync` (≤ 2048×1152, WebP calidad 86). Ya no se usan las variantes thumbnail ni main del producto.
- Path único por subida (`branding/{tenantId}/{kind}-{guid}.webp`); tras guardar la nueva URL se **borra el blob anterior** (evita huérfanos y caché obsoleto).
- *(diseñado — PRD-019)* No toca `night` ni borra blobs `night-*` (escritura `$set` por campo).
- Invalida el caché `catalog:{tenantId}`.

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `Validation` | 400 | `kind` inválido, formato no soportado o archivo > 4 MB |
| `Failure` | 500 | Falla al subir al almacenamiento |
| `Forbidden` | 403 | Tenant del token ≠ ruta |

**Observabilidad**
- Log: `catalog.admin.branding.image_uploaded` `{ tenantId, kind, bytes, requesterId }`

**Casos de Aceptación**
- [ ] Subir logo y portada → URLs de Blob resuelven públicamente y el menú las muestra
- [ ] Reemplazar la imagen → la anterior se elimina del almacenamiento
- [ ] *(diseñado — PRD-019)* Con Night configurado, subir y borrar el logo de día → `GET /branding/night` devuelve los mismos overrides y el blob `night-logo-*` sigue existiendo

---

### DELETE /api/v1/catalog/{tenantId}/branding/{kind}/image

**Objetivo**
Elimina el logo o la portada actual.

**Auth**
JWT de staff, rol Admin.

**Request**
- Path params: `{tenantId}`, `{kind}` ∈ `logo | hero`

**Response 200**
El objeto `MenuBranding` actualizado con `logoUrl`/`heroUrl` en `null`.

**Reglas de Negocio**
- Borra el blob correspondiente (best-effort) y limpia la URL del subdoc.
- *(diseñado — PRD-019)* No toca `night` ni borra blobs `night-*` (escritura `$set` por campo).
- Invalida el caché `catalog:{tenantId}`.

**Errores**
| Código | HTTP | Cuándo |
|---|---|---|
| `Validation` | 400 | `kind` inválido |
| `Forbidden` | 403 | Tenant del token ≠ ruta |

**Observabilidad**
- Log: `catalog.admin.branding.image_deleted` `{ tenantId, kind, requesterId }`

**Casos de Aceptación**
- [ ] Borrar el logo → el menú vuelve a mostrar el header sin logo; el blob se elimina
- [ ] *(diseñado — PRD-019)* Con Night configurado, subir y borrar el logo de día → `GET /branding/night` devuelve los mismos overrides y el blob `night-logo-*` sigue existiendo

---

## Branding Night *(diseñado — pendiente de implementación, PRD-019 — ADR-039)*

Endpoints para el subdocumento `branding.night` (overrides del modo `NIGHT`; ver §Visibilidad por modo y
branding Night). Mismas reglas de acceso que `## Branding`: JWT de staff con rol **Admin** y `tenantId` del
token = ruta. Toda escritura invalida `catalog:{tenantId}`. El admin los usa en la pestaña **«Día | Night»** de
Apariencia, con «Heredar del día» por campo (= enviar `null`), excepto `backgroundColor`, cuyo `null` significa
«Usar el tema»; la pestaña Night no ofrece «Heredar del día» para el fondo.

---

### GET /api/v1/catalog/{tenantId}/branding/night

#### Objetivo
Devolver el subdocumento `night` **crudo** (overrides, con sus `null`) y el branding **resuelto** que vería el
cliente de noche, para precargar la pestaña Night con su vista previa.

#### Request
- **Auth**: JWT de staff, rol Admin.
- **Path params**: `{tenantId}` — UUID del tenant.
- **Body**: ninguno.

#### Response
`200 OK`:

```json
{
  "night": {
    "enabled": true,
    "displayName": null,
    "slogan": "La noche empieza en la orilla",
    "logoUrl": "https://blob.azure.net/catalog-images/branding/tenant-uuid/night-logo-ef56.webp",
    "heroUrl": null,
    "primaryColor": null,
    "theme": null,
    "backgroundColor": null,
    "fontKey": null
  },
  "effective": {
    "displayName": "Mesto Beach",
    "slogan": "La noche empieza en la orilla",
    "logoUrl": "https://blob.azure.net/catalog-images/branding/tenant-uuid/night-logo-ef56.webp",
    "heroUrl": "https://blob.azure.net/catalog-images/branding/tenant-uuid/hero-cd34.webp",
    "primaryColor": "#AA8453",
    "theme": "night",
    "backgroundColor": null,
    "fontKey": "mestobeach"
  }
}
```

- `night` es lo guardado; `null` significa «hereda del día» (salvo `backgroundColor`: «usar el tema»). Si el tenant nunca configuró Night, `night` es
  `{ "enabled": true }` con el resto en `null`: Night está activo por defecto y `false` es una decisión explícita del admin.
- `effective` aplica los defaults de Night (`theme: "night"`, `primaryColor: "#AA8453"`) y el merge con el día
  **independientemente de `enabled`**: es la vista previa, no lo que se sirve ahora. `effective.backgroundColor`
  es siempre `night.backgroundColor` (null = manda `theme`), nunca el de día.

#### Validaciones
- `tenantId` del token = ruta.

#### Reglas de Negocio
- Lee de MongoDB directamente (no caché), como el resto de endpoints de administración.

#### Errores
| Código | HTTP | Cuándo |
|---|---|---|
| `Forbidden` | 403 | Tenant del token ≠ ruta |

#### Observabilidad
- Log: `catalog.admin.branding_night.get` `{ tenantId, enabled, requesterId }`

#### Casos de Aceptación
- [ ] Tenant sin Night → `night.enabled: true`, todo `null`, y `effective` = día con `theme: "night"` y `primaryColor: "#AA8453"`
- [ ] Con `slogan` fijado y `heroUrl` en `null`, `effective.heroUrl` es el hero de día
- [ ] Día con `backgroundColor: "#faf4e9"` y Night sin configurar → `effective.backgroundColor` null, `effective.theme: "night"`, `effective.primaryColor: "#AA8453"`, y la vista previa pinta el fondo `#161616`

---

### PUT /api/v1/catalog/{tenantId}/branding/night

#### Objetivo
Guardar los overrides de texto/color/tema/fuente de Night y el interruptor `enabled` (las imágenes van aparte).

#### Request
- **Auth**: JWT de staff, rol Admin.
- **Path params**: `{tenantId}`.
- **Body** (reemplazo completo del subdocumento salvo `logoUrl`/`heroUrl`; `null` = hereda, salvo en `backgroundColor`, donde `null` = usar el tema):

```json
{
  "enabled": true,
  "displayName": null,
  "slogan": "La noche empieza en la orilla",
  "primaryColor": "#AA8453",
  "theme": "night",
  "backgroundColor": null,
  "fontKey": null
}
```

#### Response
`200 OK` — mismo shape que el `GET` (`night` + `effective`).

#### Validaciones
- `enabled`: requerido, boolean.
- `displayName`: null o ≤ 80 chars · `slogan`: null o ≤ 140 chars.
- `primaryColor` / `backgroundColor`: null o hex `#RRGGBB` (se guardan en minúsculas, como los de día).
- `theme`: null o ∈ `{light, cream, dark, night}`.
- `fontKey`: null o ≤ 40 chars, igual que en el día (no se valida contra una lista en el servidor; la lista curada es la del cliente).

#### Reglas de Negocio
- Conserva `night.logoUrl`/`night.heroUrl` existentes; no toca el branding de día.
- `enabled: false` **no borra** los overrides: el admin puede apagar Night y recuperarlo intacto.
- Invalida `catalog:{tenantId}`: el siguiente `GET /catalog/{tenantId}` ya sirve el branding nuevo, sin esperar al TTL. No se emite evento y el sondeo de modo (`GET /api/v1/venue/{tenantId}`, que solo devuelve el modo) no lo detecta: si el local está en `NIGHT`, los menús ya abiertos lo ven cuando vuelven a cargar la carta (recarga de la página o al volver a entrar en la sesión). Es aceptable porque el branding se edita fuera de servicio.

#### Errores
| Código | HTTP | Cuándo |
|---|---|---|
| `Validation` | 400 | Algún campo viola las reglas (incluye `theme` fuera de la lista) |
| `Forbidden` | 403 | Tenant del token ≠ ruta |

#### Observabilidad
- Log: `catalog.admin.branding_night.updated` `{ tenantId, enabled, theme, overriddenFields, requesterId }`

#### Casos de Aceptación
- [ ] Guardar `slogan` + `enabled: true` → de noche el menú muestra ese eslogan y el `displayName` de día
- [ ] `theme: "sepia"` → 400; `theme: "night"` → 200
- [ ] `enabled: false` → `GET /catalog/{tenantId}` en `NIGHT` devuelve el branding de día; volver a `true` recupera los overrides

---

### POST /api/v1/catalog/{tenantId}/branding/night/{kind}/image

#### Objetivo
Subir el **logo** o la **portada** de Night (`kind` = `logo` | `hero`), convertirla a WebP y guardarla aparte de la de día.

#### Request
- **Auth**: JWT de staff, rol Admin.
- **Path params**: `{tenantId}`, `{kind}` ∈ `logo | hero`.
- **Body**: `multipart/form-data` con campo `file` (JPG/PNG/WebP, ≤ 4 MB).

#### Response
`201 Created` — mismo shape que el `GET` (`night` + `effective`) con la nueva `night.logoUrl`/`night.heroUrl`.

#### Validaciones
- `kind ∈ {logo, hero}`; formato ∈ {JPG, PNG, WebP}; tamaño ≤ 4 MB (mismas reglas que el branding de día).

#### Reglas de Negocio
- Mismo procesado que el día: `logo` → `ResizeLogoAsync` (≤ 1024×1024, WebP sin pérdida, conserva la
  transparencia); `hero` → `ResizeHeroAsync` (≤ 2048×1152, WebP calidad 86); con path propio
  `branding/{tenantId}/night-{kind}-{guid}.webp`; tras guardar la URL se **borra el blob anterior de Night**
  (nunca el de día).
- No toca `enabled`: subir una imagen no lo enciende ni lo apaga.
- Invalida `catalog:{tenantId}`.

#### Errores
| Código | HTTP | Cuándo |
|---|---|---|
| `Validation` | 400 | `kind` inválido, formato no soportado o archivo > 4 MB |
| `Failure` | 500 | Falla al subir al almacenamiento |
| `Forbidden` | 403 | Tenant del token ≠ ruta |

#### Observabilidad
- Log: `catalog.admin.branding_night.image_uploaded` `{ tenantId, kind, bytes, requesterId }`

#### Casos de Aceptación
- [ ] Subir el logo de Night no toca el logo de día; de noche el menú muestra el nuevo y de día el de siempre
- [ ] Reemplazarlo elimina el blob `night-logo-*` anterior

---

### DELETE /api/v1/catalog/{tenantId}/branding/night/{kind}/image

#### Objetivo
Quitar el logo o la portada de Night para **volver a heredar** la imagen de día.

#### Request
- **Auth**: JWT de staff, rol Admin.
- **Path params**: `{tenantId}`, `{kind}` ∈ `logo | hero`.

#### Response
`200 OK` — mismo shape que el `GET`, con `night.logoUrl`/`night.heroUrl` en `null` y `effective` mostrando la imagen de día.

#### Reglas de Negocio
- Borra el blob de Night (best-effort) y deja el campo en `null`; no es «sin imagen», es «la de día».
- Invalida `catalog:{tenantId}`.

#### Errores
| Código | HTTP | Cuándo |
|---|---|---|
| `Validation` | 400 | `kind` inválido |
| `Forbidden` | 403 | Tenant del token ≠ ruta |

#### Observabilidad
- Log: `catalog.admin.branding_night.image_deleted` `{ tenantId, kind, requesterId }`

#### Casos de Aceptación
- [ ] Borrar el hero de Night → de noche el menú muestra el hero de día; el blob `night-hero-*` desaparece
