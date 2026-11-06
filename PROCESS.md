# Proceso de Desarrollo de Mesto

Este documento describe el proceso de trabajo adoptado para construir Mesto: desde la definición del producto hasta el código en producción. Cada fase tiene criterios de entrada, criterios de salida y artefactos específicos.

---

## Visión General del Flujo

```
┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│   Fase 1     │ → │   Fase 2     │ → │   Fase 3     │ → │   Fase 4     │
│  Definición  │   │ Arquitectura │   │     SDD      │   │   Código     │
│  (PRDs)      │   │   (ADRs)     │   │  (Endpoints) │   │  (.NET + JS) │
└──────────────┘   └──────────────┘   └──────────────┘   └──────────────┘
     Qué             Cómo (alto         Cómo (detalle        Implementación
  construimos        nivel)            de cada servicio)
```

La regla fundamental: **no se escribe código hasta tener el SDD aprobado del módulo correspondiente**. Esto elimina el ciclo de "entender código para saber qué hace" y reemplaza la documentación ex-post por la definición ex-ante.

---

## Fase 1 — Definición del Producto (PRDs)

### Propósito

Definir **qué** construimos y **por qué**. Sin wireframes de alta fidelidad ni decisiones de implementación. Los PRDs son el contrato entre el producto y el equipo técnico.

### Criterios de Entrada

- Existe una idea o problema de negocio identificado
- Hay al menos un stakeholder de producto disponible para responder preguntas

### Artefactos Producidos

| Artefacto | Descripción | Ubicación |
|---|---|---|
| `PRD-XXX-nombre.md` | Un archivo por funcionalidad mayor | `/prds/` |
| `docs/GUIA.md` | Índice maestro de todos los PRDs + estado del proyecto | `/docs/GUIA.md` |

### Estructura de un PRD

Cada PRD sigue esta plantilla:

1. **Metadata** — ID, versión, estado, apps relacionadas, dependencias
2. **Propósito y Objetivos** — OBJs medibles (OBJ-01, OBJ-02...)
3. **Usuarios y Roles** — Personas que usan esta funcionalidad
4. **Alcance** — In Scope / Out of Scope explícitos
5. **Flujo / Wireframes** — Texto ASCII o diagrama; suficiente para entender la UX sin diseño visual
6. **Historias de Usuario** — Épicas con criterios de aceptación
7. **Requisitos Funcionales** — RF-XXXX numerados
8. **Requisitos Técnicos** — RT-XXXX numerados (hints de implementación, no la implementación)
9. **Criterios de Aceptación** — Lista de checkboxes verificables
10. **Open Questions** — Tabla con ID, pregunta, responsable y estado

### Criterios de Salida (Fase 1 → Fase 2)

- [ ] Todos los PRDs en estado `Aprobado`
- [ ] Todos los Open Questions (OQs) resueltos (columna Estado = Resuelto)
- [ ] No hay funcionalidades que aparecen en un PRD y no en otro cuando están relacionadas (cross-reference verificado)
- [ ] `docs/GUIA.md` lista todos los PRDs con versión y estado actualizado

### Convenciones

- **Versionado**: `X.0` para reescrituras; `X.Y` para OQs resueltos o correcciones menores. Se edita el archivo existente — no se crean carpetas `/v1/`, `/v2/`.
- **OQs**: Los OQs pendientes bloquean el SDD del módulo afectado. Un OQ sin respuesta = gap de definición.
- **Estado posible de un PRD**: `Borrador` / `En revisión` / `Aprobado` / `Obsoleto`

---

## Fase 2 — Arquitectura (ARCHITECTURE.md + ADRs)

### Propósito

Definir **cómo** construimos el sistema a alto nivel: stack tecnológico, estructura de módulos, modelos de datos, flujos de datos entre componentes y decisiones arquitectónicas importantes (ADRs).

### Criterios de Entrada

- Al menos los PRDs del MVP están en estado `Aprobado`
- El equipo técnico core está disponible para tomar decisiones arquitectónicas

### Artefactos Producidos

| Artefacto | Descripción | Ubicación |
|---|---|---|
| `ARCHITECTURE.md` | Arquitectura completa: stack, monorepo, módulos, schemas PostgreSQL/MongoDB/Redis, flujos SignalR | `/ARCHITECTURE.md` |
| ADR-XXX (dentro de ARCHITECTURE.md) | Una sección por decisión arquitectónica relevante (ej: ADR-010 Red de Lealtad, ADR-011 Roles Ops) | `/ARCHITECTURE.md §ADRs` |

### Estructura de un ADR

```
### ADR-XXX — Título de la decisión

**Contexto**: Qué problema o pregunta motiva esta decisión.
**Decisión**: Qué decidimos.
**Alternativas consideradas**: Qué más evaluamos.
**Consecuencias**: Trade-offs aceptados.
```

### Criterios de Salida (Fase 2 → Fase 3)

- [ ] Stack definido para cada capa (backend, frontend, DB, cache, real-time, infra)
- [ ] Schema de PostgreSQL completo con todas las tablas y relaciones
- [ ] Schema de MongoDB con los documentos principales
- [ ] Redis keys documentados con TTLs y patrones de invalidación
- [ ] Un ADR por cada decisión no obvia (alternativas evaluadas)
- [ ] El monorepo está estructurado con nombres de apps y módulos definidos

---

## Fase 3 — Service Design Documents (SDDs)

### Propósito

Definir **cada endpoint** de la API con detalle suficiente para que un desarrollador lo implemente sin necesitar contexto adicional. El SDD es el puente entre "qué hace el sistema" (PRD) y "cómo se programa" (código).

### Criterios de Entrada

- El PRD del módulo está en estado `Aprobado` con todos sus OQs resueltos
- El schema de datos del módulo está definido en `ARCHITECTURE.md`
- No hay dependencias hacia otros módulos cuyos SDDs aún no existen (o están claramente documentadas)

### Artefactos Producidos

| Artefacto | Descripción | Ubicación |
|---|---|---|
| `SDD-XXX-nombre-modulo.md` | Un archivo por módulo backend | `/sdd/` |

### Estructura de un SDD

Cada endpoint dentro de un SDD sigue esta plantilla de **10 secciones**:

```markdown
## Endpoint: METHOD /api/v1/ruta/{param}

### Objetivo
Una oración que explica qué hace este endpoint y por qué existe.

### Request
- **Auth**: JWT de [rol] requerido
- **Path params**: ...
- **Query params**: ...
- **Body** (JSON):
  {
    "campo": "tipo — descripción"
  }

### Response
- **200 OK** / **201 Created**:
  {
    "campo": "tipo — descripción"
  }

### Validaciones
- campo X: requerido, máx N caracteres, formato Y
- ...

### Reglas de Negocio
- Qué lógica de negocio aplica (referencias a PRD-XXX §Y si corresponde)
- ...

### Errores
| Código | Condición |
|---|---|
| 400 | ... |
| 403 | ... |
| 404 | ... |
| 409 | ... |

### Observabilidad
- Log: "mensaje de log al ejecutar esta acción" con campos relevantes
- Métricas: contador/histograma a incrementar
- Tracing: span name

### Casos de Aceptación
- [ ] Caso feliz: ...
- [ ] Validación falla: ...
- [ ] Permiso denegado: ...
```

### Orden de Escritura de SDDs

Los SDDs se escriben en este orden (dependencias primero):

1. **SDD-001 — Identity** (auth, tokens, staff, impersonación) — bloquea todo
2. **SDD-002 — Catalog** (menú, categorías, imágenes) — depende de Identity
3. **SDD-003 — Sessions** (mesas, sesiones, PIN) — depende de Identity + Catalog
4. **SDD-004 — Orders** (pedidos, KDS) — depende de Sessions + Catalog
5. **SDD-005 — Payments** (cobro, split-bill, propina, cortesía, turnos) — depende de Orders
6. **SDD-006 — Analytics** (dashboard, reportes, CSV) — depende de Orders + Payments
7. **SDD-007 — Notifications** (SignalR, email, plantillas) — depende de Identity + Orders
8. **SDD-008 — Loyalty** (puntos, canjes, contratos, settlement) — depende de Orders + Payments
9. **SDD-009 — Platform** (back-office Ops, tenants, billing, monitoreo) — depende de Identity

### Criterios de Salida (Fase 3 → Fase 4)

- [ ] Todos los endpoints listados en los PRDs y ARCHITECTURE.md tienen su SDD escrito
- [ ] Cada SDD tiene las 10 secciones completas (sin "TODO" ni "por definir")
- [ ] Los errores posibles están documentados con código HTTP y condición
- [ ] Los casos de aceptación cubren al menos: caso feliz, validación fallida, permiso denegado
- [ ] No hay endpoints con comportamiento contradictorio entre SDDs

---

## Fase 4 — Código

### Propósito

Implementar lo que los SDDs describen. El código es la traducción directa del SDD; si algo no está en el SDD, no va al código (se vuelve a Fase 1 o 3).

### Criterios de Entrada

- El SDD del módulo a implementar está completo (Fase 3 cumplida para ese módulo)
- El entorno de desarrollo está configurado (ver `ARCHITECTURE.md §Infra`)

### Stack de Implementación

| Capa | Tecnología |
|---|---|
| Backend API | .NET 8 — Modular Monolith (`mesto-api`) |
| Frontend cliente | Next.js 14 App Router — `apps/menu` |
| Frontend staff | Next.js 14 App Router — `apps/staff` (KDS + caja + mesero) |
| Frontend mesero | Next.js 14 App Router — `apps/waiter` |
| Frontend admin | Next.js 14 App Router — `apps/admin` |
| Frontend back-office | Next.js 14 App Router — `apps/platform` |
| Monorepo | Turborepo |
| Base de datos relacional | PostgreSQL (Azure Database) |
| Base de datos documental | MongoDB (Atlas) |
| Cache / tokens | Redis (Azure Cache) |
| Real-time | SignalR (ASP.NET Core) |
| Secretos | Azure Key Vault |
| Deploy API | Azure App Service |
| Deploy Frontend | Vercel |

### Convenciones de Código

- Cada módulo del monolith es una carpeta en `src/Modules/` con su propia capa de Application, Domain e Infrastructure
- Los endpoints siguen el contrato exacto del SDD: mismos nombres de campos, mismos códigos de error
- Un PR por módulo o endpoint; los PRs referencian el SDD correspondiente
- Los tests de integración implementan los Casos de Aceptación del SDD (uno a uno)

### Orden de Implementación (MVP)

```
Sprint 1:  Identity (auth staff + cliente)
Sprint 2:  Catalog (menú + imágenes)
Sprint 3:  Sessions (mesas, PIN, QR)
Sprint 4:  Orders (pedidos, KDS, SignalR)
Sprint 5:  Payments (cobro, split-bill, impresora)
Sprint 6:  Analytics básico + Reportes de turno
Sprint 7:  Frontends: menu + kitchen + admin
Sprint 8:  Loyalty (puntos + canjes)
Sprint 9:  Platform back-office (Ops)
Sprint 10: QA + beta privada
```

---

## Resumen de Fases y Estado Actual

| Fase | Artefacto | Estado |
|---|---|---|
| Fase 1 — Definición | `docs/GUIA.md` + `prds/PRD-001` a `PRD-019` | ✅ Completo hasta PRD-018; PRD-019 (Mesto Beach Night) en revisión (PR0), con preguntas pendientes del negocio (PRD-019 §14) |
| Fase 2 — Arquitectura | `ARCHITECTURE.md` | ✅ Completo (v3.1) |
| Fase 3 — SDD | `sdd/SDD-001` a `SDD-009` | ⬜ Pendiente |
| Fase 4 — Código | `mesto-api` + `mesto-web` | ⬜ Pendiente |

---

## Reglas de Trabajo

1. **No saltar fases**: No se escribe código de un módulo cuyo SDD no está completo.
2. **PRDs primero**: Si durante el SDD o el código aparece un requisito no cubierto en el PRD, se vuelve al PRD y se cierra el OQ antes de continuar.
3. **Un solo source of truth**: La verdad del comportamiento del sistema está en el SDD. El PRD explica el "por qué"; el SDD explica el "cómo exacto". Si hay contradicción, el SDD prevalece para la implementación (pero la contradicción se resuelve actualizando el PRD).
4. **Versionado de documentos**: Editar el archivo existente (no crear `/v1/`, `/v2/`). El historial de Git es el historial de versiones.
5. **OQs como bloqueadores**: Un OQ sin resolver en un PRD bloquea el SDD del módulo afectado. No se hace "asumiendo que..." en el SDD sin cerrar el OQ.

---

*PROCESS.md — Proceso de Desarrollo de Mesto*
*Versión 1.0 — Junio 2026*
