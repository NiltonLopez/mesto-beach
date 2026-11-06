# Convenciones y plantillas de documentación de Mesto

Plantillas exactas y reglas de versionado. Léelo antes de escribir en las Fases 1-3. La fuente
canónica del proceso es `PROCESS.md`; esto es el extracto operativo para escribir rápido y
consistente. Mantén el español y el estilo de los archivos vecinos.

---

## Jerarquía: qué va en cada documento

| Documento | Responde | NO incluye |
|---|---|---|
| **PRD** (`prds/`) | *Qué* y *por qué* — producto, usuarios, alcance, historias, RF/RT | implementación concreta |
| **ARCHITECTURE.md** | Forma del sistema y *decisiones* (secciones + diagramas + ADRs) | contratos endpoint por endpoint |
| **SDD** (`sdd/`) | *Cómo* a nivel de contrato — esquema, endpoints, reglas, errores | código |

Un mismo cambio suele tocar los tres niveles. No dupliques: enlaza (el SDD cita "PRDs de
referencia"; el PRD cita ADRs/secciones cuando aplica).

---

## PRD — plantilla

Numeración: siguiente libre en `prds/` (PRD-001…). Archivo `PRD-XXX-slug-en-kebab.md`.
Versionado: `X.0` reescritura; `X.Y` adición/corrección menor. **Se edita el archivo existente**;
nunca carpetas `/v1/`. Estado posible: `Borrador` / `En revisión` / `Aprobado` / `Obsoleto`.

```markdown
# PRD-XXX — Título

## Metadata

| Campo | Valor |
|---|---|
| **ID** | PRD-XXX |
| **Versión** | X.Y |
| **Estado** | Aprobado — vX.Y |
| **Fecha** | Mes Año |
| **Apps relacionadas** | `admin`, `menu`, ... |
| **Módulos de API** | Catalog, Orders, ... |
| **Depende de** | PRD-00A, PRD-00B |
| **PRDs que dependen de este** | PRD-00C |

---

## 1. Propósito y Objetivos
### Propósito
### Objetivos
| # | Objetivo | Métrica |
|---|---|---|
| OBJ-01 | ... | ... |

## 2. Contexto y Problema que Resuelve
## 3. Usuarios y Roles
## 4. Alcance
### In Scope
### Out of Scope
## 5. Flujo / Wireframes        (ASCII; suficiente para entender la UX sin diseño visual)
## 6. Historias de Usuario      (Épicas con criterios de aceptación; marca [vX.Y] lo nuevo)
## 7. Requisitos Funcionales    (RF-XXXX numerados, alineados al rango del PRD: PRD-004 → RF-4xx)
## 8. Requisitos Técnicos       (RT-XXXX; hints de implementación, NO la implementación)
## 9. Criterios de Aceptación   (checkboxes verificables)
## 10. Open Questions           (tabla: ID | Pregunta | Responsable | Estado)
```

Al **actualizar** un PRD existente: sube la Metadata (Versión/Estado/Fecha), añade las nuevas
Épicas/RF/RT marcadas con la versión (`[v3.1]`), y agrega criterios de aceptación. No reescribas lo
que sigue vigente.

---

## SDD — plantilla de cabecera + endpoint

Un SDD por módulo (`SDD-00X-modulo.md`). Cabecera del servicio:

```markdown
# SDD-00X — <Módulo>

## Servicio
**Módulo**: <Nombre>
**Base path**: `/api/v1/<ruta>`
**Responsabilidad**: <una o dos frases; fuente de verdad de los datos>

**PRDs de referencia**: PRD-00A (§...), PRD-00B (§...)

**Dependencias**:
- PostgreSQL / MongoDB / Redis: <colecciones/claves/TTL>
- Azure Blob Storage / SignalR / otros SDDs

---

## <Schema>            (MongoDB/PostgreSQL/Redis, según el módulo)
## <Otras secciones de dominio>   (p. ej. Cache Strategy, Image Storage, ...)
## Endpoints
```

Cada endpoint sigue **exactamente** esta plantilla (de `PROCESS.md` §"Estructura de un SDD"):

```markdown
### MÉTODO /api/v1/ruta/{param}

#### Objetivo
Una frase.

#### Request
- Headers / Path / Query / Body (con tipos y ejemplo JSON)

#### Response
- 200/201 con ejemplo JSON

#### Validaciones
- Reglas de entrada (formatos, min/máx, required) — refleja los AbstractValidator reales

#### Reglas de Negocio
- Qué hace el handler, invariantes, side-effects (cache invalidation, eventos SignalR)

#### Errores
| Código | HTTP | Cuándo |
|---|---|---|

#### Observabilidad
- Logs estructurados que emite (event names), métricas

#### Casos de Aceptación
- [ ] ...
```

Orden de endpoints dentro del SDD: agrúpalos por recurso, igual que el archivo existente. Mantén la
cabecera (`PRDs de referencia`, `Dependencias`) al día cuando una feature añade una dependencia
(p. ej. Blob Storage para imágenes).

---

## ADR — plantilla (ARCHITECTURE.md §14)

Siguiente `ADR-XXX` libre (mira el mayor en §14). Marca la versión del producto si aplica
(`*(v3.0)*`, `*(implementación)*`).

```markdown
### ADR-XXX: Título de la decisión *(implementación)*
**Contexto**: por qué hubo que decidir.
**Decisión**: qué se eligió.
**Consecuencias**: trade-offs, qué se gana y qué se sacrifica.
```

---

## ARCHITECTURE.md — marcadores y estructura

- Documento numerado con **Tabla de Contenidos** al inicio (§2 en el archivo): si agregas una
  sección de primer nivel (`## N. ...`), añádela también al ToC.
- **Marcadores de estado** en encabezados de subsección, tal como ya se usan:
  - `*(implementado)*` — la sección refleja algo que ya está en código.
  - `*(actualizado)*` — se revisó para alinearla con la implementación actual.
- **Diagramas**: caja ASCII entre <code>```</code> para estructura/flujo; `mermaid` para C4
  (`## 2. Diagrama C4`) y para esquemas de base de datos (`erDiagram`/`graph`). Reusa el estilo del
  diagrama vecino más parecido.
- **§15 "Estado de Implementación y Features Entregadas"**: la lista de lo entregado. Cada feature
  nueva documentada debe aparecer aquí (§15.1 backend, §15.2 frontend, §15.3 persistencia).

---

## docs/GUIA.md — qué mantener (y qué no)

El `docs/GUIA.md` es la **visión de producto + el índice** del repo. Su propio encabezado delega el
estado operativo a `AGENTS.md`/`CLAUDE.md` y el detalle técnico a `ARCHITECTURE.md` (§15). No lo
conviertas en changelog ni dupliques implementación; mantén solo estos bloques:

| Bloque del README | Cuándo tocarlo | Qué poner |
|---|---|---|
| `### PRDs` (tabla) | Creaste un PRD, o cambió el alcance de uno de forma material | Una fila `\| PRD-XXX — Título \| Descripción \| Audiencia \|`. El rótulo "PRDs — Versión X.0" se sube solo en un hito del conjunto, no por cada `X.Y` |
| `### SDDs` (tabla) | Un SDD ganó endpoints/esquema notables | Resume en la columna "Notas recientes" (mismo tono que las existentes, p. ej. "+ auto-confirmación al crear") |
| `## Estado del Proyecto` (tabla) | Una feature aterrizó o cambió de madurez | Fila con `✅ Implementado` / `🟨 Parcial` / `⬜ Pendiente` y una frase. Es el termómetro de alto nivel |
| Línea `**Estado:**` (cabecera) | Cambió el estado global del proyecto | Frase corta de una línea |
| Tabla de Contenidos | Agregaste una sección de primer nivel | Añade el ancla |

Regla de límite: si dudas entre el README y ARCHITECTURE §15, el detalle va en §15 y el README solo
lo resume. No debe haber contradicciones entre ambos.

---

## Checklist de cierre (Fase 5)

- [ ] Todo lo del inventario de la brecha quedó documentado en su nivel correcto.
- [ ] ToC de ARCHITECTURE.md ↔ encabezados reales.
- [ ] Sin colisiones ni huecos en ADR-XXX / PRD-XXX / SDD-XXX / RF-XXXX / RT-XXXX.
- [ ] Metadata de PRDs tocados con Versión/Estado/Fecha al día; cross-references mutuos.
- [ ] Cada endpoint/tabla/colección nueva del código aparece en su SDD (y en ARCHITECTURE si es
      estructural).
- [ ] README: tabla de PRDs ↔ `ls prds/`, tabla de SDDs y "Estado del Proyecto" al día, sin
      contradecir ARCHITECTURE §15.
- [ ] Nada marcado *(implementado)* sin respaldo en código leído.
