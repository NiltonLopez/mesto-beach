# Mapa de descubrimiento — código ↔ documentación

Tabla operativa para la Fase 0. Para cada superficie del código: cómo enumerarla y contra qué
documento contrastarla. Trabaja desde la raíz del repo (`/Users/bran/Documents/Mesto`).

## Regla general

El objetivo es el **delta**: lo que el código tiene y el doc no (o tiene desactualizado). Enumera
la realidad del código con `grep`, enumera lo documentado con `grep` sobre encabezados, y compara
las dos listas. No leas archivos completos para esto.

---

## 1. Endpoints backend  →  SDD del módulo (+ §9.12 de ARCHITECTURE para sesiones)

Los controladores viven en `backend/src/Mesto.API/Controllers/<Area>/`. Enumera rutas reales:

```bash
grep -rnE '\[(Http(Get|Post|Put|Patch|Delete))(\("|\)])' backend/src/Mesto.API/Controllers/ | sed 's/:.*\[/ → [/'
grep -rn '\[Route(' backend/src/Mesto.API/Controllers/
```

Contrasta con los encabezados de endpoint del SDD correspondiente:

```bash
grep -nE '^### (GET|POST|PUT|PATCH|DELETE) ' sdd/SDD-00X-<modulo>.md
```

Mapa módulo → SDD: Identity→SDD-001, Catalog→SDD-002, Sessions→SDD-003, Orders→SDD-004,
Payments→SDD-005, Analytics/Inventory→SDD-006, Notifications→SDD-007, Loyalty→SDD-008,
Platform→SDD-009. Un endpoint en código sin `###` en el SDD = brecha.

## 2. Esquemas de datos  →  ARCHITECTURE §5 + sección "Schema" del SDD

- **PostgreSQL** (EF Core, una migración por módulo): busca entidades y migraciones.
  ```bash
  find backend/src/Modules -path '*Migrations*' -name '*.cs' | grep -v Designer | sort
  grep -rln 'ModelBuilder\|HasColumnName\|ToTable' backend/src/Modules/*/*.Infrastructure/Persistence/
  ```
  Contrasta con los diagramas `mermaid` de ARCHITECTURE §5 (`### Esquema PostgreSQL ...`) y la
  sección de schema del SDD. Tablas/columnas nuevas = brecha.
- **MongoDB** (catálogo): los POCOs `*Doc` y `[BsonElement]` en
  `backend/src/Modules/Catalog/Mesto.Catalog.Infrastructure/Persistence/CatalogRepository.cs`.
  Contrasta con `## MongoDB Schema` de `sdd/SDD-002-catalog.md`.
  > Nota viva: en el catálogo los embebidos se guardan con `_id` (el driver mapea la propiedad
  > `Id`→`_id`); documenta los esquemas con `_id`, no `id`.
- **Redis** (claves): contrasta con ARCHITECTURE §9.10 "Estructura de datos en Redis".
  ```bash
  grep -rnoE '"(catalog|session|pin|qr|invite|loyalty|...):[^"]*"' backend/src/Modules/ | sort -u
  ```

## 3. Tiempo real (SignalR)  →  ARCHITECTURE §6

Hubs montados en `Program.cs` y eventos emitidos:

```bash
grep -rn 'MapHub<' backend/src/Mesto.API/Program.cs
grep -rnE 'SendAsync\("|Clients\.(Group|All|Caller)' backend/src/Modules/ | head -40
```

Contrasta con §6 "Eventos de tiempo real" e "Implementación real de SignalR".

## 4. Frontend (apps, rutas, packages)  →  ARCHITECTURE §3 y §13

```bash
ls frontend/apps                       # apps reales y sus personas
find frontend/apps/*/src/app -name 'page.tsx' | sed 's#/src/app#  #'   # rutas App Router
ls frontend/packages                   # packages compartidos reales
```

Contrasta con §3 "Aplicaciones del Sistema" (una subsección por app, con su puerto/persona) y §13
"Estructura de Repositorios". Rutas o apps nuevas, o packages que aparecen/desaparecen = brecha.
Recuerda: `packages/ui` existe pero está vacío (no asumas componentes ahí).

## 5. Módulos backend  →  ARCHITECTURE §4 "Módulos en detalle" + §15

```bash
ls backend/src/Modules                                   # módulos reales
grep -rn 'Add.*Module(' backend/src/Mesto.API/Program.cs # registro de cada módulo
```

Cada módulo debe tener su `#### <Nombre> Module` en §4 y aparecer en §15.1. Un módulo en
`src/Modules/` sin entrada = brecha (ej. si Inventory se separó de Analytics).

## 6. Configuración / capacidades nuevas  →  PRD-004 / PRD-007 + SDD + ARCHITECTURE

Cosas que cambian capacidades del producto suelen vivir en config y dependencias:

```bash
grep -rnE 'BlobStorage|Azurite|MailKit|Totp|SignalR|Redis|Mongo' backend/src/Mesto.API/appsettings*.json docker-compose.yml
```

Servicios nuevos en `docker-compose.yml` (p. ej. Azurite/Blob Storage), o secciones de
`appsettings` nuevas, suelen implicar: una nota en ARCHITECTURE (§5 Image Storage / §12 Despliegue),
quizá un RT en un PRD, y la sección correspondiente del SDD del módulo que lo usa.

## 7. Reglas de negocio y validaciones  →  SDD (Validaciones / Reglas de Negocio)

Los validators de FluentValidation corren en runtime (vía `ValidationBehavior` de MediatR):

```bash
grep -rln 'AbstractValidator<' backend/src/Modules/
```

Reglas nuevas (min/máx, formatos, required) deben reflejarse en la sección "Validaciones" del
endpoint en el SDD.

---

## Pistas de obsolescencia (lo "desactualizado", no solo lo ausente)

- Encabezados de ARCHITECTURE **sin** `*(implementado)*`/`*(actualizado)*` cuyo contenido ya está en
  código → marca el estado.
- Metadata de un PRD con `Fecha`/`Versión` viejas frente a features que ya crecieron.
- `docs/GUIA.md` describiendo estado "pre-desarrollo" o SDDs "pendientes" cuando ya existen → el código
  y `AGENTS.md` son la fuente de verdad; corrige el README si el usuario lo pide, pero no dejes que
  te despiste en el inventario.
