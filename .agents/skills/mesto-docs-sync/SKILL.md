---
name: mesto-docs-sync
description: >-
  Sincroniza la documentación de Mesto con el código real: detecta lo que está
  implementado pero NO reflejado en ARCHITECTURE.md, prds/, sdd/ o el docs/GUIA.md, y lo
  documenta siguiendo las plantillas y convenciones del proyecto. Úsala siempre que el
  usuario quiera "documentar los últimos cambios", "actualizar la arquitectura", "sacar o
  actualizar PRDs", "organizar los specs/SDDs", "actualizar el README", "dejar la
  documentación al día", "reflejar lo nuevo en los docs", o cuando se acabe de terminar
  una feature y falte la fase de documentación — aunque no diga la palabra "documentación"
  textualmente. Es la skill por defecto para cualquier trabajo sobre ARCHITECTURE.md, los
  PRDs, los SDDs o el docs/GUIA.md de este repositorio.
---

# Mesto — Sincronización de Documentación

Tu trabajo: encontrar la **brecha entre el código y la documentación** y cerrarla. El repo
sigue el flujo **PRD → ARCHITECTURE.md (+ADRs) → SDD → Código** (`PROCESS.md`), pero en la
práctica el código a veces corre adelante de los docs. Esta skill documenta *hacia atrás*: parte
de lo que **ya está implementado** y lo refleja fielmente en los cuatro artefactos de documentación
(ARCHITECTURE.md, PRDs, SDDs y el docs/GUIA.md como índice/estado).

Principio rector: **fidelidad al código, no invención.** Cada cosa que documentes debe estar
respaldada por código real que leíste. Si marcas algo como *(implementado)*, es porque lo está.
Si no estás seguro de que algo existe, ve y léelo antes de escribirlo.

Idioma: **todo en español**, igual que el resto de los docs.

---

## Flujo de trabajo

Sigue las fases en orden. No saltes la Fase 0 — sin un inventario de la brecha terminarás
documentando lo que ya estaba documentado y omitiendo lo nuevo.

### Fase 0 — Levantar el inventario de la brecha

Objetivo: una lista concreta de "esto existe en el código pero falta / está desactualizado en
los docs", cada ítem etiquetado con a qué documento(s) pertenece (ARCHITECTURE / PRD-XXX / SDD-XXX).

1. **Mira qué llegó recientemente.** `git log --oneline -30` y, si ayuda, los commits desde la
   última actualización de docs (`git log --oneline -- ARCHITECTURE.md | head -1` para ubicar el
   punto de referencia). Los mensajes de commit en español describen las features entregadas.
2. **Enumera las superficies reales del código y compáralas con los docs.** No leas todo: usa
   `grep` sobre encabezados y firmas. La tabla de mapeo de `references/discovery-map.md` te dice
   exactamente qué grepear y contra qué documento contrastarlo (endpoints↔SDD, esquemas DB↔
   ARCHITECTURE+SDD, hubs SignalR, apps/rutas frontend, módulos nuevos, opciones de config).
3. **Anota la brecha.** Para cada hallazgo: qué es, dónde vive en el código (`archivo:línea`), y
   qué doc lo debe recoger. Si tienes subagentes disponibles, reparte el descubrimiento por módulo
   en paralelo (uno por módulo backend / app frontend) y consolida — es más rápido y exhaustivo.

Presenta el inventario brevemente antes de escribir, salvo que el usuario ya te haya dicho que
avances sin preguntar (es lo habitual aquí). No pidas permiso para cada decisión: el usuario
delega el criterio de qué amerita PRD nuevo vs. actualización.

### Fase 1 — ARCHITECTURE.md

Documento vivo, numerado, con Tabla de Contenidos, cajas ASCII y diagramas `mermaid` (C4 +
esquemas DB). Edita **en sitio** con cambios quirúrgicos; no reescribas secciones que no tocaste.

Para cada ítem arquitectónico de la brecha:
- **Sección nueva o ampliada** describiendo el qué y el porqué, con su diagrama. Reusa el estilo
  vecino: caja ASCII para estructura/flujo, `mermaid` para C4 y esquemas de base de datos.
- **Marca el estado** con `*(implementado)*` o `*(actualizado)*` en el encabezado, como ya se hace
  (ej. `### Esquema PostgreSQL — Mapa de Mesas ... *(implementado)*`).
- **ADR** si el hallazgo encierra una decisión de arquitectura (siguiente `ADR-XXX` libre,
  plantilla en `references/doc-conventions.md`). Las ADRs van en §14.
- **Actualiza la Tabla de Contenidos** si agregaste una sección de primer nivel, y **§15 "Estado de
  Implementación y Features Entregadas"** para que liste la feature.

### Fase 2 — PRDs (a tu criterio: nuevo o actualizar)

Decide sin preguntar. Heurística:
- **PRD nuevo** cuando la feature es un dominio/épica de producto que ningún PRD cubre. Usa el
  **siguiente número libre** (`ls prds/` → el mayor + 1) y la plantilla completa de
  `references/doc-conventions.md`. Enlázalo en las dependencias de los PRDs relacionados.
- **Actualizar un PRD existente** cuando la feature extiende algo ya cubierto. Sube versión según la
  convención (`X.0` reescritura, `X.Y` adición/corrección), actualiza la tabla de **Metadata**
  (versión, estado, fecha), añade Historias/Épicas, `RF-XXXX` / `RT-XXXX`, y Criterios de Aceptación.
  Se **edita el archivo existente** — nunca carpetas `/v1/`, `/v2/`.

Mantén el cross-reference sano: si la feature toca varios PRDs, refléjala en todos los relacionados
(la regla de salida de Fase 1 del proceso exige que no haya features en un PRD y ausentes en otro
relacionado). Si el `docs/GUIA.md` lista PRDs con versión/estado, actualiza esa línea.

### Fase 3 — SDDs

Un SDD por módulo (`SDD-001-identity` … `SDD-009-platform`). Para cada módulo afectado, actualiza su
SDD con lo realmente implementado:
- **Esquema** (MongoDB/PostgreSQL/Redis) si cambió la forma de los datos.
- **Endpoints nuevos** con la plantilla de endpoint completa (Objetivo, Request, Response,
  Validaciones, Reglas de Negocio, Errores, Observabilidad, Casos de Aceptación) — ver
  `references/doc-conventions.md`.
- **Cabecera del servicio** al día: `PRDs de referencia` y `Dependencias` (Mongo/Redis/Blob/otros
  SDDs). Si la feature cruza módulos, toca cada SDD involucrado.

Si un dominio implementado no tiene SDD aún, créalo con el siguiente número que corresponda al módulo.

### Fase 4 — docs/GUIA.md (índice y estado del proyecto)

El `docs/GUIA.md` es la **visión de producto + el índice** del proyecto; él mismo delega el estado
operativo a `AGENTS.md`/`ARCHITECTURE.md`. Respeta ese límite: mantén índices y estado al día, **no
lo conviertas en changelog** ni dupliques el detalle de ARCHITECTURE §15. Tras tocar PRDs/SDDs/arquitectura,
sincroniza solo lo que corresponde:

- **Tabla de PRDs** (`### PRDs`): una fila por PRD. Añade la fila si creaste un PRD; ajusta la
  descripción si el alcance cambió de forma material (no por cada `X.Y`).
- **Tabla de SDDs** (`### SDDs`): columna "Notas recientes" por módulo — resume ahí lo nuevo del SDD
  (endpoints/esquema relevantes), igual que las notas que ya existen.
- **Estado del Proyecto** (`## Estado del Proyecto`): actualiza o agrega la fila de la feature con su
  estado (✅ / 🟨 Parcial / ⬜ Pendiente). Es el termómetro de alto nivel, no la lista exhaustiva.
- **Encabezado/estado** (línea "Estado:" y el rótulo de versión de la tabla de PRDs, p. ej.
  "PRDs — Versión 5.0"): súbelos solo si el conjunto global cambió (PRD nuevo, hito).

Detalles de qué mantener y qué no en `references/doc-conventions.md` (sección README).

### Fase 5 — Consistencia y cierre

- **Cross-check código↔docs**: cada endpoint real aparece en su SDD; cada tabla/colección real
  aparece en ARCHITECTURE y en el SDD; cada feature está consistente en los cuatro artefactos.
- **Integridad del documento**: ToC de ARCHITECTURE.md coincide con los encabezados; números de
  ADR/PRD/SDD/RF/RT sin colisiones ni huecos; fechas y versiones actualizadas.
- **README al día**: la tabla de PRDs lista todos los PRDs reales (`ls prds/`), la de SDDs refleja lo
  nuevo, y "Estado del Proyecto" recoge la feature. Sin contradicciones con ARCHITECTURE §15.
- **Resumen final** para el usuario: qué documentaste, qué PRD/SDD creaste vs. actualizaste, y qué
  decisiones de criterio tomaste (p. ej. "X lo metí como actualización de PRD-004 v3.1, no como PRD
  nuevo, porque extiende la épica de Branding").

---

## Reglas que evitan los errores típicos

- **Lee antes de escribir.** ARCHITECTURE.md (~2000 líneas) y los SDDs son grandes: navega con
  `grep -nE "^#{1,4} "` y lee solo las secciones relevantes. Nunca documentes de memoria.
- **No marques *(implementado)* lo que no verificaste en el código.** Es el sello de fidelidad del
  doc; un falso positivo lo envenena.
- **Cambios quirúrgicos, no reformateos.** Conserva el estilo, el español y la numeración vecinos.
  El valor está en lo nuevo, no en remover todo.
- **Respeta el versionado de PRDs** (edita en sitio, sube `X.Y`/`X.0`, sin carpetas de versión) y la
  jerarquía PRD→ARCH→SDD: un PRD describe *qué/por qué* (sin implementación), el SDD el *cómo* a
  nivel de contrato, ARCHITECTURE las decisiones y la forma del sistema.
- **El código manda sobre los docs viejos.** Si `docs/GUIA.md` dice "pre-desarrollo" pero el repo tiene
  la feature, gana el código (ver `AGENTS.md`/`AGENTS.md`).

## Archivos de referencia

- `references/discovery-map.md` — qué grepear en el código y contra qué documento contrastarlo, por
  módulo y por superficie. **Léelo en la Fase 0.**
- `references/doc-conventions.md` — plantillas exactas (PRD, SDD-endpoint, ADR), reglas de
  versionado, marcadores de estado y dónde vive cada cosa. **Léelo antes de escribir en Fases 1-3.**
