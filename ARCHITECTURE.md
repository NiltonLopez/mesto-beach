# Mesto Beach — Arquitectura Técnica

> Documento de arquitectura técnica del sistema a medida de **Playa Demo**. Incluye diagramas, decisiones de diseño y especificaciones de cada componente.
> Cualquier IA o desarrollador debe leer este documento para entender cómo está construido Mesto Beach y cómo extenderlo.

> **⚙️ Origen y estado.** Este documento nació como la arquitectura de **Mesto**, la plataforma SaaS multi-tenant de la que Mesto Beach se separó en agosto de 2026 (ADR-023). Describe un sistema **en evolución**: donde el diseño difiera del código real, **manda el código**. Estado actual:
> - **7 módulos de dominio**: `Identity`, `Catalog`, `Sessions` (separado de Orders), `Orders`, `Payments`, `Analytics`, `Notifications`. Se retiraron `Platform`, `Ledger` y `Loyalty` — maquinaria SaaS que no aplica a un local único (ADR-023). **No existe** un módulo `Inventory` independiente: el stock vive en `Catalog`.
> - **3 aplicaciones**: `menu` (comensal), `staff` (cocina, caja y sala) y `admin` (administración del local).
> - Hubs SignalR reales: `/hubs/sessions` (`SessionsHub`), `/hubs/orders` (`OrdersHub`), `/hubs/realtime` (`RealtimeHub`). Los eventos viajan como `SessionEvent` / `OrderEvent` con un campo `type`.
> - PostgreSQL es **una sola base `mestobeach`** compartida por todos los módulos (cada módulo maneja sus migraciones EF); MongoDB es `mestobeach_catalog`.
> - Puertos dev: frontends `3000`, `3001` y `3003`; backend `http://localhost:5011`.
> - **Lo diseñado y aún no implementado** para Mesto Beach (coursing en el móvil del comensal, calibración e imagen de fondo del plano, hamaca de primera clase con alta en lote, edición del plano en vivo desde Recepción, carta bilingüe ES/EN, despliegue híbrido, integración con el TPV) vive en **PRD-018** y en los **ADR-024…035**; **§15.4 es la lista autoritativa** — el ciclo de vida por plato, la cola del pase, el PDA, el Runner y Recepción **ya están construidos**.
---

## Tabla de Contenidos

1. [Visión General del Sistema](#1-visión-general-del-sistema)
2. [Diagrama C4 — Contexto](#2-diagrama-c4--contexto)
3. [Aplicaciones del Sistema](#3-aplicaciones-del-sistema)
4. [Backend — Modular Monolith](#4-backend--modular-monolith)
5. [Bases de Datos](#5-bases-de-datos)
6. [Arquitectura de Tiempo Real](#6-arquitectura-de-tiempo-real)
7. [Seguridad y Autenticación](#7-seguridad-y-autenticación)
8. [Modelo Multi-Tenant](#8-modelo-multi-tenant)
9. [Seguridad del QR y Sesiones de Mesa](#9-seguridad-del-qr-y-sesiones-de-mesa)
10. [Flujo de Datos — Pedido Completo](#10-flujo-de-datos--pedido-completo)
11. [Flujo de Pagos](#11-flujo-de-pagos)
12. [Estrategia de Despliegue — Híbrido local/nube](#12-estrategia-de-despliegue--híbrido-localnube)
13. [Estructura de Repositorios](#13-estructura-de-repositorios)
14. [Decisiones de Arquitectura (ADRs)](#14-decisiones-de-arquitectura-adrs)
15. [Estado de Implementación y Features Entregadas](#15-estado-de-implementación-y-features-entregadas)

---

## 1. Visión General del Sistema

Mesto Beach es el sistema de pedidos y servicio del chiringuito **Playa Demo**: un producto **a medida de un solo local**, que **convive con el TPV del cliente** (Ágora) en lugar de reemplazarlo (ADR-028).

- **3 aplicaciones frontend** (Next.js 14 App Router, monorepo pnpm + Turborepo)
- **1 backend modular** (.NET 8) con **7 módulos de dominio** (Identity, Catalog, Sessions, Orders, Payments, Analytics, Notifications)
- **3 bases de datos** con responsabilidades distintas (PostgreSQL `mestobeach`, MongoDB `mestobeach_catalog`, Redis)
- **Tiempo real** vía SignalR con 3 hubs (`/hubs/sessions`, `/hubs/orders`, `/hubs/realtime`)
- **Despliegue híbrido**: el servidor del local es la fuente de verdad del servicio; la nube publica el menú del comensal (ADR-024)

> El sistema conserva el modelo **multi-tenant** heredado de Mesto (§8): todo cuelga de un `tenant_id`. En Mesto Beach hay **un único tenant**, sembrado al instalar. Se mantiene porque no estorba y deja la puerta abierta a un segundo local sin rediseñar el modelo de datos.

```
        EN EL LOCAL (servidor propio)                    EN LA NUBE
 ┌──────────────────────────────────────────┐    ┌───────────────────────────┐
 │  WiFi del chiringuito                    │    │  menu (Next.js PWA)       │
 │                                          │    │  la carta del comensal    │
 │  [PDA] [Runner] [KDS] [Caja] [Recepción] │    │                           │
 │       staff/ · admin/  (Next.js)         │    │  [Móvil del comensal]     │
 └────────────────────┬─────────────────────┘    └─────────────┬─────────────┘
                      │ REST + SignalR                         │ pedidos del
                      │                                        │ comensal
 ┌────────────────────▼────────────────────────────────────────▼─────────────┐
 │                    MestoBeach.API (.NET 8) — servidor del local             │
 │   ┌──────────┐ ┌─────────┐ ┌──────────┐ ┌────────┐ ┌──────────┐ ┌───────┐ │
 │   │ Identity │ │ Catalog │ │ Sessions │ │ Orders │ │ Payments │ │Analyt.│ │
 │   └──────────┘ └─────────┘ └──────────┘ └────────┘ └──────────┘ └───────┘ │
 │                        └ Notifications (correo)                           │
 └──────────┬───────────────────┬────────────────────┬───────────────────────┘
            │                   │                    │
     ┌──────▼──────┐     ┌──────▼─────┐      ┌───────▼─────┐     ┌──────────┐
     │ PostgreSQL  │     │  MongoDB   │      │    Redis    │     │  Blob    │
     │  mestobeach   │     │  catálogo  │      │  tiempo real│     │ imágenes │
     └─────────────┘     └────────────┘      └─────────────┘     └──────────┘

  El TPV del local sigue emitiendo la cuenta y la factura — ADR-028.
  Si cae internet, el servicio continúa; solo se degrada el pedido por QR — ADR-024.
```

---

## 2. Diagrama C4 — Contexto

```mermaid
graph TB
    subgraph Clientes["Usuarios Externos"]
        C1[Cliente Final\nCelular propio]
        C2[Staff Cocina/Barra\nTablet / TV]
        C3[Mesero\nCelular propio]
        C4[Cajero\nTablet / PC]
        C5[Dueño / Admin\nPC o Tablet]
    end

    subgraph Mesto Beach["Mesto Beach"]
        APP1[menu\nNext.js PWA\nExperiencia del cliente]
        APP2[staff\nNext.js\nKDS + Cajero + Mesero\n/board · /cashdesk · /salon]
        APP4[admin\nNext.js\nDashboard dueño]
        API[Mesto Beach API\n.NET Modular Monolith]
    end

    subgraph Datos["Almacenamiento"]
        PG[(PostgreSQL\nSesiones, Pedidos,\nPagos, Tenants)]
        MG[(MongoDB\nCatálogo\nde Menú)]
        RD[(Redis\nTokens, Cache,\nEstado RT)]
        BL[(Blob Storage\nImágenes,\nBranding)]
    end

    subgraph Externos["Servicios Externos"]
        WP[Wompi / MercadoPago\nPasarela de Pago]
        EM[Servicio de Email\nOnboarding / Alertas]
    end

    C1 --> APP1
    C2 --> APP2
    C3 --> APP2
    C4 --> APP2
    C5 --> APP4

    APP1 --> API
    APP2 --> API
    APP4 --> API

    API --> PG
    API --> MG
    API --> RD
    API --> BL
    API --> WP
    API --> EM
```

---

## 3. Aplicaciones del Sistema

Tres aplicaciones, una por persona del sistema. La cuarta que existía —`platform`, la consola de
Ops de Mesto— se retiró al separar el producto (ADR-023).

> **Ya construidas** *(implementado)*: dentro de `staff` viven las vistas **PDA** (`/pda`),
> **Runner** (`/runner`) y **Recepción** (`/host`), las tres de PRD-018. La vista del anfitrión se
> **rotula "Recepción"** en la interfaz; la ruta `/host` y el rol `Host` **no cambian**.

### 3.1 `menu` — App del Cliente Final

| Atributo | Valor |
|---|---|
| Framework | Next.js 14 App Router |
| Tipo | PWA (instalable en celular) |
| Audiencia | Cliente final del restaurante |
| URL | `mesto.app/[tenantSlug]` |
| Autenticación | Token de sesión anónimo (generado al escanear QR) |
| Dispositivos objetivo | Móvil (iOS y Android) |

**Rutas principales:**
```
/[tenantSlug]                    → Landing / verificación de sesión de mesa
/[tenantSlug]/menu               → Menú navegable por categorías
/[tenantSlug]/menu/[productId]   → Detalle del producto con personalizaciones
/[tenantSlug]/carrito            → Carrito antes de confirmar
/[tenantSlug]/mis-pedidos        → Estado en tiempo real de mis pedidos
/[tenantSlug]/cuenta             → Resumen de cuenta + opciones de pago
/[tenantSlug]/perfil             → (Opcional) Cuenta del cliente si está registrado
```

**Características técnicas:**
- Service Worker para cachear el catálogo del menú (funciona offline para navegación)
- Conexión SignalR al canal `client-{sessionId}` para recibir updates de estado
- Token de sesión almacenado en memoria (no localStorage — se invalida al cerrar el navegador)
- i18n con `next-intl` (español por defecto, inglés disponible)

---

### 3.2 `staff` — App operativa del staff (siete módulos, uno por rol) *(renombrada desde `kitchen` — ADR-019)*

Antes llamada `kitchen`; renombrada a `staff` porque concentra **las responsabilidades del staff en piso**. Nació con tres —cocina/barra (KDS), caja y mesero (ADR-019)— y hoy son **siete**: se le sumaron Recepción, PDA, Runner e Historial (ADR-027). El login rutea por rol a la vista correspondiente: `Cajero`/`Admin`→`/cashdesk`, `KDS`→`/board`, `Runner`→`/runner`, `Host`→`/host`, `PDA`→`/pda`, y `Mesero`→`/salon` como último recurso.

| Atributo | Valor |
|---|---|
| Framework | Next.js 14 App Router |
| Tipo | Web app (display grande para KDS/caja; `/salon` mobile-first para mesero) |
| Audiencia | Cocineros, cajeros, camareros, PDA, runners y Recepción |
| URL | `staff.mesto.app` (o `mesto.app/staff`) |
| Autenticación | JWT Staff con rol `KDS` / `Cajero` / `Mesero` / `PDA` / `Runner` / `Host` / `Admin` |
| Dispositivos objetivo | Tablet 10"+ / TV / PC (KDS, caja) · celular (mesero) |

**Rutas principales:**
```
/login                   → Autenticación del staff (rutea por rol)
/board                   → KDS cocina/barra (cuatro columnas por plato — ADR-032)
/cashdesk                → Cajero: mesas, sesiones, QRs, cobro, turnos
/salon                   → Mesero: mapa de salón, llamados, abrir mesa (mobile-first)
/pda                     → PDA: comandas de sus mesas, marchar platos, observación  *(implementado)*
/runner                  → Runner: cola del pase, viaje de entrega y mapa del sector  *(implementado)*
/host                    → Recepción: mapa de la sala y llegada de comensales  *(implementado)*
/history                 → Historial de sesiones cerradas
/change-password         → Cambio de contraseña temporal
```

**Modo tablero (ADR-034):** los siete módulos ocultan el shell de la app y piden el fullscreen
nativo desde `<FullscreenButton />`; el estado propio manda, se recuerda en `localStorage`
(`kds_kiosk`) y deja una pastilla flotante "Salir" mientras está activo.

**Chrome compartido [v2.x]:** todas las vistas viven bajo el grupo de rutas `(console)` con un
**`StaffShell`** común — **sidebar izquierdo gateado por rol** (estilo `admin`: `StaffNav` lista los
siete módulos —Cocina, Caja, Recepción, PDA, Salón, Runner, Historial— y muestra solo aquellos cuyo
rol tiene el usuario; `Admin` los ve todos) y **footer compartido** (restaurante + usuario +
chips de rol + logout, sirviendo lo mismo para un usuario con un rol o con varios). En celular el
sidebar se colapsa en una barra superior con menú ☰ (drawer); el footer queda siempre visible.
`/salon` conserva su tema "ember" mobile-first scopeado bajo `.salon-root` dentro del shell.

**Características técnicas:**
- Conexión SignalR a `kitchen-{tenantId}-{stationId}` y `bar-{tenantId}`
- Re-fetch completo del estado al reconectar SignalR (fix vs. prototipo)
- Generación de QR con `qrcode` library para el cajero
- Layout de pantalla grande optimizado (cards grandes, tipografía legible a distancia)
- Modo "pantalla completa" (modo tablero) en los 7 módulos — ADR-034 *(implementado)*
- Diálogos propios (`useDialog`: toast / confirm / prompt); cero `alert`/`confirm` nativos — ADR-033

---

### 3.3 Mesero — vista `/salon` de `staff` *(actualizado — ADR-019)*

La app del mesero **no es un deploy aparte**: vive como la ruta `/salon` dentro de `staff`,
gateada por rol (`Mesero`/`Admin`). Comparte `@mesto/api-client`, el store de auth y la conexión
SignalR con el resto de `staff`, por lo que cajero y mesero quedan sincronizados sin esfuerzo.

| Atributo | Valor |
|---|---|
| Ubicación | `staff/src/app/salon/` (ruta `/salon`) |
| Tipo | Vista mobile-first dentro de `staff` (no PWA separada) |
| Audiencia | Meseros (y Admin para supervisión) |
| Autenticación | JWT Staff con rol `Mesero` (login de `staff` rutea Mesero→`/salon`) |
| Dispositivos objetivo | Móvil (celular del mesero) |
| Estilos | `salon.css` **scopeado bajo `.salon-root`** para no afectar cashdesk/board |

**Qué hace:** mapa del salón (mesas libres/ocupadas), sección "Necesitan atención" (pedidos
listos para entregar, llamados de mesa, PIN solicitado), abrir mesa (QR derivado de la mesa;
Dinámico → muestra el QR), y drawer de sesión con PIN / invite / QR vivo.

**Características técnicas:**
- Conexión SignalR a `waiter-{tenantId}` (grupo del backend; el nombre del grupo se conserva)
  y a `tenant:{tenantId}` para pedidos; reacciona a `WAITER_CALLED`/`PIN_REQUESTED`/`SESSION_OPENED`/`SESSION_CLOSED`/`order.status_changed`.
- Diseño ultra-compacto para uso con una mano mientras se carga una bandeja.

---

### 3.4 `admin` — Dashboard del Dueño

| Atributo | Valor |
|---|---|
| Framework | Next.js 14 App Router |
| Tipo | Web app |
| Audiencia | Dueño / Administrador del restaurante |
| URL | `admin.mesto.app` |
| Autenticación | JWT con rol RestaurantAdmin |
| Dispositivos objetivo | PC o tablet |

**Rutas principales:**
```
/onboarding              → Wizard de configuración inicial (nuevo restaurante)
/menu                    → Gestión del catálogo (productos, categorías, estaciones)
/menu/productos          → CRUD de productos
/menu/categorias         → CRUD de categorías y orden
/mesas                   → Editor de sala: sobre el mismo plano a escala que ve el equipo, el
                           admin arrastra puestos, añade de los mismos tipos, cambia forma/plazas
                           y elimina; las posiciones se guardan en lote  *(implementado)*
/branding                → Logo, colores, nombre del restaurante
/horarios                → Configuración de menús por franja horaria
/staff                   → Gestión de usuarios y roles del staff
/analytics               → Dashboard de métricas y reportes
/inventario              → Stock y costos por plato
/configuracion           → Configuración general, modelo de cobro, integraciones
```

---

## 4. Backend — Modular Monolith

### Justificación de la decisión

Un monolito modular es la elección correcta para el equipo actual (3-5 personas) porque:
- Elimina la complejidad operativa de microservicios (service discovery, tracing distribuido, etc.)
- Mantiene límites de dominio claros (preparado para extraer módulos cuando escale)
- Comunicación entre módulos via eventos internos en memoria (sin latencia de red)
- Un solo deploy, un solo pipeline de CI/CD
- Una sola configuración de base de datos, logging, health checks

### Estructura interna

Cada módulo sigue Clean Architecture en **3 proyectos** (los controllers viven en `MestoBeach.API`, no dentro del módulo):

```
Módulo/  (MestoBeach.<Modulo>.Domain / .Application / .Infrastructure)
├── Domain/          → Entidades, enums, interfaces del repositorio, eventos de dominio
├── Application/     → Casos de uso (MediatR + Result<T>), validators (FluentValidation
│                       vía ValidationBehavior), DTOs, interfaces de servicios
└── Infrastructure/  → Repositorios EF, DbContext + migraciones propias, adaptadores externos

MestoBeach.API/Controllers/<Modulo>/   → Controllers (Presentation) de ese módulo
```

> Cada módulo **maneja sus propias migraciones EF** sobre la BD compartida `mestobeach`. El entry point `src/MestoBeach.API/Program.cs` registra cada módulo con `Add<Modulo>Module(...)`. `src/Shared/MestoBeach.Shared` es el kernel compartido (Result, errores, contexto de tenant).

```mermaid
graph TD
    subgraph API["MestoBeach.API (Entry Point)"]
        MW[Middleware Stack\nAuth / Tenant / Logging / Rate Limit]
        DI[Dependency Injection\nRegistración de módulos]
    end

    subgraph Modules["Módulos de Dominio (9)"]
        ID[Identity\nTenants, Staff, Auth\nJWT RS256, Device Token]
        CA[Catalog\nProductos, Categorías\nEstaciones KDS, Stock]
        SE[Sessions\nMesas, Pisos/Mapa, PIN\nParticipantes, QR, llamados]
        OR[Orders\nPedidos, routing a estación\nciclo de vida, SignalR]
        PA[Payments\nCobro, turnos de caja\nsplit, propina, efectivo]
        AN[Analytics\nReportes, métricas]
        NO[Notifications\nEmail MailKit\nMailHog en dev]
    end

    subgraph Shared["Shared Kernel"]
        EV[Domain Events Bus\nComunicación entre módulos]
        RS[Result Pattern\nManejo de errores tipado]
        TN[Tenant Context\nResuelto por middleware]
        LG[Logging / Correlation ID]
    end

    MW --> Modules
    DI --> Modules
    Modules --> EV
    Modules --> RS
    Modules --> TN

    SE -->|abre/cierra sesión| OR
    OR -->|valida ítems / routing| CA
    OR -->|order.created| AN
    PA -->|registra pago / cierra sesión| SE
    SE -->|activación staff / reset| NO
```

### Módulos en detalle

#### Identity Module
```
Responsabilidades:
  - CRUD de Tenants (restaurantes en la plataforma)
  - Identidad de usuario UNIFICADA: un usuario global (correo único + 1 contraseña)
    puede pertenecer a varios tenants vía membresías, cada una con uno o más roles.
      · users            → identidad global (credenciales)
      · user_memberships → vínculo usuario↔tenant + roles[] + estado por restaurante
    (reemplazan la antigua tabla `staff` per-tenant — ver SDD-001 v1.3)
  - Emisión de JWT para staff (claims: sub=userId, tenantId, role primario + roles[])
  - Emisión de Session Tokens para clientes anónimos
  - Invalidación de tokens via Redis
  - Roles de staff (por membresía): Admin, Cajero, Mesero, KDS

Login en 2 pasos (sin pedir el id del restaurante):
  POST /identity/auth/login/resolve     → email+password → restaurantes accesibles
                                           (front: 1 → entra directo; varios → selector)
  POST /identity/auth/login             → con el tenantId elegido → Staff JWT

Endpoints principales:
  POST  /identity/auth/login            → JWT para staff (tenant elegido)
  POST  /identity/tenants               → Crear restaurante (crea/vincula owner global)
  GET   /identity/staff                 → Listar membresías del tenant
  POST  /identity/staff/invite          → Invitar (correo existente → acceso inmediato)
  PATCH /identity/staff/[userId]/roles  → Cambiar roles en este tenant
  DELETE /identity/staff/[userId]       → Quitar acceso a ESTE tenant (no borra la cuenta)
```

#### Catalog Module
```
Responsabilidades:
  - CRUD de productos (con imágenes, variantes, grupos de detalles)
  - CRUD de categorías (con ordenamiento drag-and-drop)
  - CRUD de estaciones de preparación (cocina, barra, parrilla, etc.)
  - Menús por horario (desayuno 6-11am, almuerzo 11am-3pm, etc.)
  - Branding del restaurante (nombre, logo, colores, descripción)
  - Disponibilidad de productos (activo/inactivo/agotado)

Almacenamiento: MongoDB (documentos de catálogo por tenant)

Endpoints principales:
  GET  /catalog/menu                    → Menú completo del tenant (público con session token)
  GET  /catalog/products                → Lista de productos (admin)
  POST /catalog/products                → Crear producto
  PUT  /catalog/products/[id]           → Actualizar producto
  PATCH /catalog/products/[id]/stock    → Cambiar disponibilidad
  GET  /catalog/categories              → Categorías ordenadas
  POST /catalog/stations                → Crear estación
  GET  /catalog/branding                → Branding del restaurante (público)
```

#### Sessions Module  *(separado de Orders en la implementación)*
```
Responsabilidades:
  - Mesas (DiningTable) y PISOS/MAPA (Floor): layout drag-and-drop, muros, posiciones
  - Sesiones de mesa: abrir/cerrar/estado, modos QR (FIXED/DYNAMIC) y servicio (STAFFED/SELF_SERVICE)
  - PIN de sesión (Redis) + validación + "pedir PIN al mesero"
  - Participantes (multi-persona), QR fijo por mesa (join-by-table), QR dinámico, invite link
  - Llamados de mesa: "llamar mesero", "pedir la cuenta", "pedir PIN" (SignalR → waiter-{tenantId})
  - Turno de caja: lectura para exigir turno abierto al abrir mesas (proyección de Payments.shifts)

Almacenamiento: PostgreSQL (tables, floors, sessions, session_participants) + Redis (PIN, sesión activa, llamados)
Real-time: SessionsHub (/hubs/sessions), grupo waiter-{tenantId}, evento "SessionEvent" {type}

Endpoints principales (todos bajo /api/v1):
  POST   /sessions                       → Abrir sesión (STAFFED: staff; SELF_SERVICE: anónimo)
  POST   /sessions/join                  → Unirse por QR dinámico / invite
  POST   /sessions/join-by-table         → Unirse por QR FIJO de la mesa (tenant + tableCode)
  POST   /sessions/{id}/pin/validate     → Validar PIN de mesa
  POST   /sessions/{id}/request-pin      → Cliente pide el PIN al mesero (PIN_REQUESTED)
  POST   /sessions/{id}/call-waiter      → Llamar mesero (WAITER_CALLED)
  POST   /sessions/{id}/request-bill     → Pedir la cuenta (BILL_REQUESTED)
  POST   /sessions/{id}/ask-eta          → "¿Cuándo sale?" (ETA_REQUESTED → sala, nunca a
                                           cocina; no pisa un PIN/WAITER pendiente — ADR-035)
  POST   /sessions/{id}/pin/regenerate   → Regenerar PIN (cajero)
  POST   /sessions/{id}/close            → Cerrar sesión
  GET    /sessions/closed                → Historial de sesiones cerradas
  GET/POST/PATCH/DELETE /tables          → CRUD de mesas (+ layout: floorId, pos, seats, shape)
  PATCH  /tables/layout                  → Guardado masivo de posiciones (drag)
  GET    /floors · POST/PATCH/DELETE     → CRUD de pisos (nombre, orden, canvas, muros)
  GET    /floorplan                      → Pisos + mesas + estado. LECTURA para cualquier staff
                                           autenticado (StaffJwt): lo consumen el editor de admin,
                                           el KDS (sector de cada mesa), la caja, Recepción, el PDA
                                           y el Runner. La EDICIÓN sigue en StaffAdmin *(actualizado)*
```

#### Orders Module
```
Responsabilidades:
  - Creación y gestión de pedidos (referencia a sessionId del módulo Sessions)
  - Validación de ítems contra el catálogo MongoDB (Redis L1 + Mongo L2)
  - Routing automático de pedidos a la estación KDS correcta (kds_stations)
  - Ciclo de vida DEL PLATO (order_items — ADR-025 + ADR-032):  *(actualizado)*
      HELD → QUEUED → IN_PREPARATION → READY → IN_DELIVERY → DELIVERED
      (+ RETURNED si el runner no pudo entregarlo, + CANCELLED)
    · "en cola" (QUEUED) separa las dos decisiones: MARCHAR (HELD→QUEUED) mete el plato en la
      cola de su estación y lo pueden hacer cocina y PDA; la ORDEN DE PREPARACIÓN
      (QUEUED→IN_PREPARATION) es solo de la estación. Un plato pedido "ahora" NACE en QUEUED:
      nada entra en fuegos sin que la cocina lo lance.
    · Cada transición sella su hora (queued_at, preparation_started_at, marched_at, ready_at,
      delivered_at) — de ahí sale el "cuánto lleva en cada paso" del KDS y del menú.
  - Ciclo de vida DEL PEDIDO: PENDING → CONFIRMED → ... → DELIVERED (+ CANCELLED, FINALIZED).
    Se DERIVA de sus ítems (Order.RecalculateStatusFromItems) y la comanda se finaliza sola.
    · El pedido del cliente se AUTO-CONFIRMA al crearse (confirmar el carrito = confirmación);
      nace CONFIRMED y aparece de inmediato en el KDS (no se queda en PENDING).
  - Observación de la comanda (orders.observacion, ≤500): texto libre que escribe el comensal
    en el carrito o el PDA, y que se muestra en KDS, PDA, caja, salón y runner.
  - Notificaciones en tiempo real via SignalR
  - Historial de pedidos por sesión; estaciones KDS (CRUD)

Almacenamiento: PostgreSQL (orders, order_items, delivery_trips, kds_stations)
Real-time: OrdersHub (/hubs/orders), grupos session/tenant/station, evento "OrderEvent"

Endpoints principales (/api/v1):
  POST   /orders                         → Nuevo pedido ([AllowAnonymous]; cliente o staff)
  GET    /orders?sessionId=              → Pedidos de la sesión
  GET    /orders/active                  → Tablero KDS autoritativo (delta ?since + activeIds)
  PATCH  /orders/{id}/status             → Avanzar estado (staff / KDS device token)
  POST   /orders/{id}/cancel             → Cancelar pedido
  POST   /orders/items/{id}/march        → Marchar: HELD → QUEUED (PDA, KDS o Admin)
  POST   /orders/items/{id}/start-preparation → ¡A preparar!: QUEUED → IN_PREPARATION (solo
                                           la estación: KDS o Admin) — ADR-032
  POST   /orders/items/{id}/ready        → Al pase: IN_PREPARATION → READY (KDS o Admin)
  GET    /delivery-queue                 → Cola del pase del runner (Runner o Admin)
  POST   /delivery-trips · /{id}/complete · /{id}/release · GET /mine → Viajes de entrega
  GET/POST/PATCH/DELETE /kds-stations    → Estaciones de preparación
```

#### Payments Module
```
Responsabilidades:
  - Cobro en efectivo por sesión (monto + propina), con cálculo de vuelto en el front
  - Split bill (igualitario y por persona) con auto-cierre de sesión al completar
  - Propina con % sugerido (config por tenant) o monto libre
  - TURNOS DE CAJA (Shift): abrir/cerrar turno + reporte de caja; los pagos se estampan
    con el shift abierto. Exigido para abrir mesas (Sessions lo lee como proyección).
  - Config de pago por tenant (tipEnabled, tipOptions)
  - (Pasarelas digitales Wompi/MercadoPago: diseñadas, pendientes de implementación)

Almacenamiento: PostgreSQL (payments, shifts, tenant_payment_config)

Endpoints principales (/api/v1):
  POST /payments/cash                         → Registrar pago en efectivo (+ split index/total)
  GET  /payments/config                       → Config de propina del tenant
  GET  /payments/sessions/{id}/status         → Estado del pago de la sesión
  POST /shifts · POST /shifts/{id}/close      → Abrir / cerrar turno de caja
  GET  /shifts?open=true · /shifts/{id}/report → Turno abierto / reporte de caja
```

#### Notifications Module
```
Responsabilidades:
  - Envío de email transaccional (activación de staff, reset de contraseña, etc.)
  - Implementación real con MailKit (MailKitEmailSender). En dev usa MailHog (SMTP 1025, UI 8025)
  - Nota dev: la activación/reset de staff hoy es stub que no llega a MailHog;
    el admin usa "Copiar enlace" o el log para obtener el link.
Almacenamiento: ninguno propio (servicio de envío). Real-time: RealtimeHub (/hubs/realtime).
```

#### Analytics Module
```
Responsabilidades:
  - Reportes de ventas (diario, semanal, mensual)
  - Productos más pedidos y menos pedidos
  - Horas pico de pedidos
  - Ticket promedio por mesa
  - Tiempo promedio de preparación por estación
  - Tasa de conversión (mesas que pedideron vs. que solo escanearon)
  - El día de negocio se corta a medianoche de Europe/Madrid (VenueTime en el rango de
    fechas, AT TIME ZONE en las consultas), no en UTC ni restando un desfase fijo
    (diseñado — PR 11): corte a las 06:00 vía VenueTime.BusinessDayStartHour, para
    Analytics y Payments

Almacenamiento: PostgreSQL (aggregation queries sobre tablas de Orders)

Endpoints principales:
  GET /analytics/sales/summary                → Resumen de ventas del período
  GET /analytics/products/top                 → Productos más pedidos
  GET /analytics/sessions/averages            → Ticket promedio, tiempo promedio
  GET /analytics/heatmap/hourly               → Mapa de calor por hora
```

#### Inventory Module
```
Responsabilidades:
  - Control de stock por producto
  - Alertas de stock bajo (configurable por el admin)
  - Registro de costos de ingredientes por plato
  - Cálculo de margen bruto por producto
  - Descuento automático de stock al confirmar pedido (opcional)

Almacenamiento: PostgreSQL

Endpoints principales:
  GET  /inventory/products                    → Stock actual de todos los productos
  PATCH /inventory/products/[id]/stock        → Ajuste manual de stock
  POST /inventory/products/[id]/cost         → Registrar costo de producción
  GET  /inventory/alerts                      → Productos con stock bajo
```

---

## 5. Bases de Datos

### Decisión: Por qué tres bases de datos

```mermaid
graph LR
    subgraph PG["PostgreSQL — Datos Relacionales"]
        T[tenants]
        U[users]
        S[table_sessions]
        O[orders]
        OI[order_items]
        P[payments]
        ST[stock]
        CO[costs]
    end

    subgraph MG["MongoDB — Datos Documentales"]
        CAT[catalogs\npor tenant]
        PROD[products\ncon variantes\ne imágenes]
        BR[branding]
        SCH[schedules\nmenú por horario]
    end

    subgraph RD["Redis — Estado Efímero"]
        TK[session_tokens\ncliente → sessionId]
        JB[jwt_blacklist\ntokens invalidados]
        CM[catalog_cache\nmenú por tenant TTL 5min]
        RT[realtime_state\npedidos activos]
    end
```

**PostgreSQL** para: todo lo que requiere transacciones ACID (pedidos, pagos, stock), datos relacionales (tenant → staff → sesión → pedido), y queries analíticas.

**MongoDB** para: el catálogo de menú — un producto tiene variantes anidadas, grupos de detalle, imágenes múltiples, precios por horario. El modelo documental encaja perfectamente y permite cambios de schema sin migraciones.

**Redis** para: tokens de sesión del cliente (necesitan expirar y ser invalidados instantáneamente), caché del menú (reduce carga a MongoDB en horarios pico), y lista negra de JWTs revocados.

### Esquema PostgreSQL (principal)

```mermaid
erDiagram
    tenants {
        uuid id PK
        string slug UK
        string name
        string plan
        string billing_model
        string qr_mode
        string operation_mode
        bool pin_required
        string timezone
        timestamp created_at
        bool is_active
    }

    waiter_assignments {
        uuid id PK
        uuid tenant_id FK
        uuid waiter_id FK
        string table_code
    }

    users {
        uuid id PK
        uuid tenant_id FK
        string email UK
        string password_hash
        string role
        bool is_active
    }

    delivery_places {
        uuid id PK
        uuid tenant_id FK
        string name
        string zone
        string table_code UK
        bool is_active
    }

    table_sessions {
        uuid id PK
        uuid tenant_id FK
        uuid delivery_place_id FK
        string status
        string mode
        string qr_mode
        string pin
        timestamp opened_at
        timestamp closed_at
    }

    session_participants {
        uuid id PK
        uuid session_id FK
        uuid tenant_id FK
        string participant_id
        uuid customer_account_id FK "nullable — null si anónimo"
        string customer_name
        timestamp joined_at
    }

    orders {
        uuid id PK
        uuid tenant_id FK
        uuid session_id FK
        string customer_name
        string station_id
        string station_destination
        string step
        string status
        decimal total_price
        timestamp created_at
        timestamp updated_at
    }

    order_items {
        uuid id PK
        uuid order_id FK
        string product_id
        string product_name
        int quantity
        decimal unit_price
        jsonb selected_details
    }

    payments {
        uuid id PK
        uuid tenant_id FK
        uuid session_id FK
        string method
        decimal amount
        decimal tip
        string gateway_reference
        string status
        timestamp paid_at
    }

    stock {
        uuid id PK
        uuid tenant_id FK
        string product_id
        int available_units
        int low_stock_alert_threshold
    }

    tenants ||--o{ users : "tiene"
    tenants ||--o{ delivery_places : "tiene"
    tenants ||--o{ table_sessions : "tiene"
    tenants ||--o{ waiter_assignments : "tiene"
    users ||--o{ waiter_assignments : "asignado a mesas"
    table_sessions ||--o{ session_participants : "tiene"
    table_sessions ||--o{ orders : "contiene"
    orders ||--o{ order_items : "contiene"
    table_sessions ||--o{ payments : "tiene"
    stock }o--|| tenants : "pertenece a"
```

> **Nota de nombres (implementación):** en el código las tablas se llaman `tables`,
> `sessions`, `session_participants`, `orders`, `order_items`, `payments`, `kds_stations`,
> `staff`, `tenants` (el diagrama anterior usa nombres de diseño legacy como `delivery_places`/
> `table_sessions`). Abajo, las tablas **nuevas ya implementadas**.

### Esquema PostgreSQL — Ciclo del plato y entrega  *(implementado)*

El estado dejó de vivir en el pedido y pasó al plato (ADR-025), con la cola del pase de ADR-032
encima. Estas son las columnas **reales** de `order_items` y `delivery_trips` (de `orders` solo se
listan las pertinentes), según las migraciones `AddItemLifecycle`, `AddOrderObservacion` y
`AddItemQueuedStateAndStamps`:

```mermaid
erDiagram
    orders {
        uuid id PK
        uuid tenant_id
        uuid session_id
        uuid participant_id
        string status "derivado de los ítems; + FINALIZED"
        string observacion "varchar(500), nullable — nota libre de la comanda"
        uuid station_id "nullable"
        timestamp confirmed_at "nullable"
        timestamp ready_at "nullable"
        timestamp cancelled_at "nullable"
        timestamp updated_at "cursor del sondeo autoritativo — ADR-022"
    }

    order_items {
        uuid id PK
        uuid order_id FK
        uuid tenant_id
        string catalog_item_id
        string item_name_snapshot
        int item_price_snapshot_cents
        int quantity
        string selected_modifiers "nullable"
        string notes "nullable"
        string status "HELD | QUEUED | IN_PREPARATION | READY | IN_DELIVERY | DELIVERED | RETURNED | CANCELLED"
        string timing "NOW | LATER — coursing (ADR-026)"
        uuid station_id "nullable — estación que lo prepara"
        uuid delivery_trip_id FK "nullable — viaje que lo lleva"
        timestamp marched_at "nullable — HELD → QUEUED"
        uuid marched_by "nullable — quién marchó"
        timestamp queued_at "nullable — entrada en la cola de la estación"
        timestamp preparation_started_at "nullable — QUEUED → IN_PREPARATION"
        uuid preparation_ordered_by "nullable — quién dio el ¡a preparar!"
        timestamp ready_at "nullable — al pase"
        timestamp delivered_at "nullable"
        timestamp updated_at
    }

    delivery_trips {
        uuid id PK
        uuid tenant_id
        uuid runner_id "índice único parcial ux_delivery_trips_runner_open (status = OPEN)"
        uuid station_id "el runner va adscrito a una estación"
        string status "OPEN | COMPLETED | RELEASED"
        timestamp created_at
        timestamp completed_at "nullable"
    }

    orders ||--o{ order_items : "contiene"
    delivery_trips ||--o{ order_items : "carga en la bandeja"
```

> **Sellos por transición.** `queued_at`, `preparation_started_at` y `preparation_ordered_by`
> (migración `AddItemQueuedStateAndStamps`) son los que permiten al KDS y al menú decir **cuánto
> lleva el plato en cada paso** sin instrumentación aparte (ADR-032). Índices de trabajo:
> `(tenant_id, status)`, `(tenant_id, station_id, status)` y `(tenant_id, updated_at)`.

### Esquema PostgreSQL — Mapa de Mesas, Pisos y Turnos de Caja  *(implementado)*

```mermaid
erDiagram
    floors {
        uuid id PK
        uuid tenant_id
        string name "Planta baja, Terraza, Piso 2"
        int sort_order
        int width "canvas px"
        int height
        jsonb walls "[{x1,y1,x2,y2}] muros"
        timestamp created_at
        timestamp updated_at
    }

    tables {
        uuid id PK
        uuid tenant_id
        string table_code UK
        string zone "nullable"
        bool active
        uuid floor_id FK "nullable, ON DELETE SET NULL"
        float pos_x "nullable — posición en el mapa"
        float pos_y "nullable"
        int seats "nullable"
        string shape "SQUARE | ROUND"
        string qr_mode "FIXED | DYNAMIC — default FIXED"
        bool requires_pin "default true; forzado false si DYNAMIC"
    }

    shifts {
        uuid id PK
        uuid tenant_id
        uuid staff_id
        string status "Open | Closed"
        int opening_float_cents
        timestamp opened_at
        timestamp closed_at "nullable"
        text note "nullable"
    }

    payments {
        uuid id PK
        uuid session_id
        uuid shift_id FK "nullable — turno en que se cobró"
        string method
        int amount_cents
        int tip_cents
        int split_index "nullable"
        int split_total "nullable"
        string status
    }

    floors ||--o{ tables : "ubica (sin ubicar si floor_id null)"
    shifts ||--o{ payments : "agrupa para el reporte de caja"
```

> **Layout = datos puros.** `floors.walls` y `tables.pos_x/pos_y` solo describen el mapa visual;
> no afectan sesiones ni pedidos. Borrar un piso deja sus mesas con `floor_id = null` ("sin ubicar"),
> no las borra. Coordenadas en el espacio del piso `0..width / 0..height`; el front escala para ajustar.

### Esquema PostgreSQL — Cuentas de Cliente

```mermaid
erDiagram
    customer_accounts {
        uuid id PK
        string email UK
        string name
        string phone "nullable"
        date birth_date "nullable"
        string password_hash "nullable"
        string google_id "nullable"
        int loyalty_points
        timestamp created_at
        timestamp last_login
    }

    customer_favorites {
        uuid customer_id FK
        uuid tenant_id FK
        timestamp added_at
    }

    customer_accounts ||--o{ customer_favorites : "tiene"
```

### Esquema PostgreSQL — Modo del local y distribución por modo  *(diseñado — pendiente de implementación, PRD-019)*

El modo del local (ADR-036) atraviesa cuatro módulos. Estas son las tablas y columnas **previstas**
(migraciones `AddTenantVenueModeAndChanges`, `AddKdsStationProfileAndModes`,
`AddOrderVenueModeAndPriority`, `AddTableLayoutsPerModeAndKind` y `AddSessionVenueMode`); de las
tablas existentes solo se listan las columnas nuevas o pertinentes:

```mermaid
erDiagram
    tenants {
        uuid id PK
        string slug UK
        string venue_mode "varchar(16) RESTAURANT | NIGHT — default RESTAURANT (ADR-036)"
        timestamptz venue_mode_changed_at "nullable"
    }

    venue_mode_changes {
        uuid id PK
        uuid tenant_id FK
        string from_mode "varchar(16)"
        string to_mode "varchar(16)"
        uuid changed_by "staff que ordeno el cambio"
        timestamptz changed_at
        jsonb preflight "{openSessions, activeItems, openTrips, orphanItems, tables[]} en el momento del cambio"
    }

    kds_stations {
        uuid id PK
        uuid tenant_id
        string name
        bool is_active "default true (ADR-037)"
        string flow_profile "varchar(16) FULL_PASS | BAR — default FULL_PASS"
        jsonb active_in "default [RESTAURANT, NIGHT]; encendida = is_active AND active_in contiene el modo"
    }

    floors {
        uuid id PK
        uuid tenant_id
        string name
        jsonb visible_in "default [RESTAURANT, NIGHT] — la Playa cierra de noche (ADR-038)"
    }

    tables {
        uuid id PK
        uuid tenant_id
        string table_code UK
        string kind "varchar(16) TABLE | HIGH_TABLE | RESERVADO — default TABLE"
        float pos_x "legado — copiado a table_layouts (RESTAURANT) por la migracion"
        float pos_y "legado"
    }

    table_layouts {
        uuid id PK
        uuid tenant_id
        uuid table_id FK
        string mode "varchar(16) — unico (table_id, mode)"
        uuid floor_id FK "nullable, ON DELETE SET NULL"
        float pos_x "nullable"
        float pos_y "nullable"
        bool active "false = 409 TABLE_CLOSED_IN_MODE en ese modo"
        uuid station_id "nullable — barra de la mesa; solo pesa si el plato resuelve a BAR o a ninguna"
        timestamptz updated_at
    }

    sessions {
        uuid id PK
        uuid tenant_id
        uuid table_id
        string venue_mode "varchar(16) — default RESTAURANT; snapshot al abrir (ADR-040)"
    }

    orders {
        uuid id PK
        uuid tenant_id
        uuid session_id
        int priority "default 0 — 1 si la comanda nace en NIGHT desde una mesa RESERVADO; las colas ordenan priority DESC"
        string venue_mode "varchar(16) — default RESTAURANT; snapshot al crear"
    }

    tenants ||--o{ venue_mode_changes : "audita"
    tenants ||--o{ kds_stations : "tiene"
    tables ||--o{ table_layouts : "una fila por modo"
    floors ||--o{ table_layouts : "ubica en ese modo"
    kds_stations ||--o{ table_layouts : "barra de la mesa (station_id)"
    tables ||--o{ sessions : "abre"
    sessions ||--o{ orders : "contiene"
```

> **Nada de esto muta al cambiar de modo.** El cambio escribe `tenants.venue_mode`, una fila en
> `venue_mode_changes` y la clave Redis `venue:{tenantId}:mode` (§9.10); «estación encendida»,
> «mesa en servicio» e «ítem visible» se **calculan** leyendo el modo activo. `orders.priority`,
> `orders.venue_mode` y `sessions.venue_mode` son **snapshots** para que colas y reportes no cambien
> retroactivamente. La migración de `table_layouts` copia `tables.pos_x/pos_y` a la fila `RESTAURANT`
> y la clona a `NIGHT`; `tables.pos_*` quedan como legado hasta retirarlas en una migración posterior,
> cuando ningún módulo las lea (Orders pasa a leer las coordenadas de `table_layouts`). Toda mesa
> nueva nace con sus dos filas de `table_layouts`: activa en el modo desde el que se crea e inactiva
> en el otro; la mesa ad-hoc de la caja, activa en los dos y sin ubicar.

## 6. Arquitectura de Tiempo Real

### Grupos de SignalR por tenant

Cada mensaje de tiempo real va a un grupo específico. Nunca se hace `Clients.All`.

```mermaid
graph TD
    API[Mesto Beach API\nSignalR Hub]

    subgraph Tenant_A["Restaurante A (tenantId: aaa)"]
        KA[kitchen-aaa-cocina\nCocineros de A]
        BA[bar-aaa\nBartenders de A]
        WA[waiter-aaa\nMeseros de A]
        CA["client-session-xyz\n(Mesa 5 de A)"]
    end

    subgraph Tenant_B["Restaurante B (tenantId: bbb)"]
        KB[kitchen-bbb-cocina\nCocineros de B]
        BB[bar-bbb\nBartenders de B]
        WB[waiter-bbb\nMeseros de B]
        CB["client-session-uvw\n(Mesa 3 de B)"]
    end

    API -->|nuevo pedido comida| KA
    API -->|nuevo pedido bebida| BA
    API -->|pedido listo| WA
    API -->|estado actualizado| CA
    API -->|nuevo pedido comida| KB
    API -->|nuevo pedido bebida| BB
    API -->|pedido listo| WB
    API -->|estado actualizado| CB
```

### Eventos de tiempo real

| Evento | Canal | Payload | Receptor |
|---|---|---|---|
| `order.new` | `kitchen-{tid}-{station}` | Pedido completo | KDS cocina |
| `order.new` | `bar-{tid}` | Pedido completo | KDS barra |
| `order.step_changed` | `kitchen-{tid}-{station}` | `{orderId, step}` | KDS |
| `order.ready` | `waiter-{tid}` | `{orderId, tableCode}` | Mesero |
| `order.status_changed` | `client-{sessionId}` | `{orderId, status}` | Cliente |
| `waiter.called` | `waiter-{tid}` | `{tableCode, reason}` | Mesero |
| `session.bill_updated` | `client-{sessionId}` | `{total}` | Cliente |

### Implementación real de SignalR  *(actualizado)*

3 hubs montados en `Program.cs`. La auth del browser va por **query string** `?access_token=` (intencional, los hubs no leen el header Authorization en el handshake de WebSocket).

| Hub | Ruta | Grupos | Evento | Tipos (`type`) |
|---|---|---|---|---|
| `SessionsHub` | `/hubs/sessions` | `waiter-{tenantId}` (`JoinWaiterGroup`) | `SessionEvent` | `WAITER_CALLED`, **`PIN_REQUESTED`**, `BILL_REQUESTED`, **`ETA_REQUESTED`**, `WAITER_CALL_ACKNOWLEDGED`, `SESSION_OPENED`, `PIN_REGENERATED`, `SESSION_CLOSED` |
| `OrdersHub` | `/hubs/orders` | tenant / estación / sesión (`JoinTenantGroup`, `JoinStationGroup`, `JoinSessionGroup`) | `OrderEvent` | `order.created`, `order.status_changed` |
| `RealtimeHub` | `/hubs/realtime` | — | (genérico) | — |

- **KDS (`/board`):** se une a `JoinTenantGroup`, escucha `OrderEvent` y **re-consulta** los pedidos de la sesión (no confía solo en el payload). Como los pedidos nacen `CONFIRMED`, aparecen al instante.
- **Cashdesk + Waiter:** se unen a `waiter-{tenantId}` y reaccionan a `PIN_REQUESTED` / `BILL_REQUESTED` / `WAITER_CALLED` (alerta + sonido). El cashdesk también refresca el mapa de mesas.
- **`ETA_REQUESTED` ("¿cuándo sale?") *(implementado — ADR-035)*:** viaja por el mismo grupo `waiter-{tenantId}`, lo atiende `/salon` con un aviso de tono tranquilo (suena más grave y más bajo: es una pregunta, no una urgencia) y **el KDS no está suscrito a este hub**, así que nunca llega a cocina. El motivo `ETA` se persiste junto a `WAITER`/`PIN`/`BILL`, pero **no pisa** un `PIN` ni un `WAITER` pendientes.
- **`VENUE_MODE_CHANGED` (cambio de modo del local) *(diseñado — pendiente de implementación, PRD-019 / ADR-036)*:** viaja por **`OrdersHub`** al grupo **`tenant:{tenantId}`** como `OrderEvent{type:"VENUE_MODE_CHANGED", mode, previous, changedAt}` (los mismos campos que devuelve `POST /api/v1/venue/mode`, sin `changed`). Lo emite Orders al recibir la `VenueModeChangedNotification` de Identity, y lo escuchan `staff` y `admin` (conexión propia en el layout, `JoinTenantGroup`) para re-temar, recargar las estaciones encendidas y disparar la transición. **El menú no lo recibe**: es anónimo y no entra en el grupo, así que **sondea** `GET /api/v1/venue/{tenantId}` (`no-store`) cada 30 s y al volver a primer plano (`visibilitychange`); el cambio tarda hasta ~30 s en llegarle, lo que es aceptable porque el pre-flight garantiza que no queda ninguna mesa abierta.
- Consumido en el frontend con `@microsoft/signalr` (`HubConnectionBuilder().withUrl(URL+'?access_token='+jwt).withAutomaticReconnect()`).

### Reconexión y consistencia

```
Al reconectar SignalR (cliente o staff):
  1. El frontend detecta el evento onreconnected
  2. Hace HTTP GET al endpoint de estado actual
     (GET /orders/sessions/active para cocina,
      GET /orders/sessions/{id}/orders para cliente)
  3. Reemplaza el estado local completo con la respuesta
  4. El estado es siempre la fuente de verdad del servidor
```

---

## 7. Seguridad y Autenticación

```mermaid
graph TD
    subgraph Staff["Autenticación Staff — Modelo híbrido por rol (v3.0)"]
        SL[Login con email + password]
        SJ[JWT RS256\nPayload: userId, tenantId, role\nExpiración: 8h — todos los roles]

        subgraph Refresh["Refresh — varía por rol"]
            RW[Mesero + Admin\nRefresh Token JWT 7 días\nHttpOnly cookie]
            RK[Kitchen / Bar — KDS fijo\nDevice Token JWT 30 días rolling\nlocalStorage del dispositivo]
            RN[Cajero + PlatformAdmin\nSin refresh — re-login al vencer]
        end
    end

    subgraph Client["Autenticación Cliente"]
        QR[Cliente escanea QR\no llega por invite/QR dinámico]
        CV[API verifica sesión ABIERTA\npara esa mesa]
        ST[Session Token — JWT simétrico\nPayload: sessionId, tenantId,\nparticipantId, tableCode\nExpiración: 4h o cierre de sesión]
        IN[JWT invalidado inmediatamente\ncuando el cajero cierra la sesión\nvía Redis blacklist por sessionId]
    end

    subgraph Roles["Control de Acceso por Rol"]
        PA[PlatformAdmin\nAcceso total — sin tenantId en JWT]
        RA[RestaurantAdmin\nAcceso total a SU tenant]
        CA[Cashier\nSesiones, PIN, pagos, cierre]
        KI[Kitchen\nVer y actualizar pedidos de cocina]
        BR[Bar\nVer y actualizar pedidos de barra]
        WR[Waiter\nCrear sesiones, PIN, alertas, mesas asignadas]
    end

    SL --> SJ
    SJ --> RW
    SJ --> RK
    SJ --> RN
    QR --> CV
    CV -->|Sesión activa| ST
    CV -->|Sin sesión STAFFED| ERROR[403: Mesa no disponible]
    CV -->|SELF_SERVICE en horario| AUTO[Sesión creada automáticamente]
    ST --> IN
```

> **Ver PRD-014** para la especificación completa de todos los flujos de autenticación: login, invitación staff, password reset, Device Token KDS, MFA Platform Admin, y cliente con cuenta.

### Middleware de Tenant

Cada request al API pasa por el middleware de tenant que:
1. Extrae el `tenantId` del JWT (staff) o del Session Token (cliente)
2. Inyecta el `TenantContext` en el pipeline de la request
3. Todos los repositorios aplican automáticamente el filtro `WHERE tenant_id = @tenantId`
4. Es imposible acceder a datos de otro tenant aunque se conozca el ID

### Headers de seguridad

```
Strict-Transport-Security: max-age=31536000; includeSubDomains
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Content-Security-Policy: (configurado por app)
Referrer-Policy: strict-origin-when-cross-origin
```

---

## 8. Modelo Multi-Tenant

### Aislamiento de datos

```mermaid
graph LR
    subgraph Requests
        R1[Request de Restaurante A]
        R2[Request de Restaurante B]
    end

    subgraph Middleware
        TM[Tenant Middleware\nExtrae tenantId del JWT]
    end

    subgraph Repositories
        REP[Repository genérico\nIQueryable con filtro automático\nWHERE tenant_id = context.TenantId]
    end

    subgraph DB
        DA[(Datos de A\ntenant_id = aaa)]
        DB[(Datos de B\ntenant_id = bbb)]
    end

    R1 --> TM
    R2 --> TM
    TM --> REP
    REP -->|Solo lee aaa| DA
    REP -->|Solo lee bbb| DB
```

### Alta del local *(ADR-023)*

Mesto Beach sirve a **un solo local**, así que no hay consola de Ops ni alta por API: el
restaurante y su cuenta de administración se siembran **una vez al instalar**.

```
docker cp scripts/bootstrap-mestobeach.sql mestobeach_postgres:/tmp/bootstrap.sql
docker exec mestobeach_postgres psql -U mestobeach -d mestobeach -f /tmp/bootstrap.sql
```

El script es idempotente y crea: la fila de `tenants` (zona horaria `Europe/Madrid`), el usuario
administrador y su pertenencia con rol `Admin`. A partir de ahí, todo se gestiona desde `admin`:
carta, mapa de mesas, estaciones y personal.

---

## 9. Seguridad del QR y Sesiones de Mesa

> **v3.0 — Rediseño completo.** Este diseño incorpora seguridad por capas: modo QR configurable (FIJO o DINÁMICO), PIN de 4 dígitos por sesión que el **mesero** lleva físicamente a la mesa, link de invitación con token firmado para grupos remotos, y separación explícita de roles entre Mesero y Cajero.

### 9.1 Principios de diseño

| Principio | Descripción |
|---|---|
| **QR configurable** | Cada restaurante elige entre QR **FIJO** (permanente, impreso en la mesa) o **DINÁMICO** (generado por sesión, mostrado por el mesero en su pantalla o impreso desde el cajero). |
| **PIN como puerta de orden** | En modo FIJO con PIN requerido, el cliente puede navegar el menú y armar su carrito libremente. El PIN de 4 dígitos solo se pide al **confirmar el primer pedido**. El mesero lleva el PIN a la mesa. |
| **Mesero como portador del acceso** | El mesero crea la sesión desde su app → el PIN (o QR dinámico) aparece en su pantalla → va físicamente a la mesa y lo muestra al cliente. Esto vincula el acceso a presencia física. |
| **Invite link como puerta alterna** | Cualquier participante dentro de la sesión puede generar un link con token firmado para invitar a alguien por WhatsApp. El destinatario entra sin teclear el PIN. |
| **Cajero como única autoridad de regeneración** | Solo el cajero puede regenerar el PIN activo de una sesión. Al hacerlo, todos los invite links anteriores quedan inválidos automáticamente. |
| **Sesión compartida** | N personas se unen a la misma sesión. Cada una tiene su SessionToken y `participantId`. Todos ven la factura completa y lo que pidió cada uno. |
| **Blacklist por sessionId** | Al cerrar la sesión, el `sessionId` va a la blacklist de Redis. Todos los tokens de todos los participantes quedan invalidados en O(1). |

---

### 9.2 Configuración del sistema QR (por mesa) *(actualizado)*

El tipo de QR/acceso se configura **por mesa**, no a nivel de tenant. Cada fila de `tables`
persiste dos columnas (migración `AddTableQrConfig`) que el administrador elige en `admin → Mesas`:

| Columna (`tables`) | Tipo | Opciones | Default |
|---|---|---|---|
| `qr_mode` | `VARCHAR(16)` | `FIXED` / `DYNAMIC` | `FIXED` |
| `requires_pin` | Bool | `true` / `false` | `true` |

El `operation_mode` (`STAFFED` / `SELF_SERVICE`) sigue siendo del tenant. El admin no manipula
`qr_mode`/`requires_pin` crudos: elige uno de **tres presets** que los combinan:

| Preset (admin → Mesas) | `qr_mode` | `requires_pin` | Acceso del cliente |
|---|---|---|---|
| **QR Fijo (con PIN)** | FIXED | true | QR impreso reusable; PIN al confirmar el primer pedido. |
| **QR Dinámico** | DYNAMIC | false | QR nuevo por sesión, impreso/mostrado por el staff; sin PIN. |
| **Sin seguridad** | FIXED | false | QR fijo **abierto**: ni PIN ni nada; escanea y ordena directo. |

`DiningTable.SetQrConfig(qrMode, requiresPin)` normaliza la invariante: en `DYNAMIC` fuerza
`requiresPin = false` (el QR de un solo uso ya autentica la mesa).

**Reglas de compatibilidad:**
- `DYNAMIC` siempre implica `STAFFED`. No existe `DYNAMIC + SELF_SERVICE` (el backend lo rechaza 400).
- `SELF_SERVICE` solo está disponible con `FIXED`.

> **Derivación implementada de `pinRequired`:** al abrir una sesión, el backend lo deriva **de la mesa**,
> no del comando: `pinRequired = table.QrMode == FIXED && table.RequiresPin`. El `qrMode` que mande el
> cliente al abrir se **ignora para mesas existentes** (la mesa manda); solo se usa al auto-crear una mesa
> ad-hoc inexistente. Al abrir desde cashdesk/mesero ya **no se pregunta** el tipo de QR: Fijo/Sin seguridad
> crean la sesión de inmediato; Dinámico, tras crearla, ofrece imprimir/mostrar el QR de la mesa.
> Ver ADR-018. (El parseo `STAFFED/SELF_SERVICE → enum` usa mapeo explícito para no romper con el guion bajo.)
> *(diseñado — PRD-019, ADR-040)* Excepción: con el local en `NIGHT`, `pinRequired = true` al abrir
> cualquier sesión (`venueMode == NIGHT || (table.QrMode == FIXED && table.RequiresPin)`), sin tocar el
> preset de la mesa.

---

### 9.3 Matriz de flujos de operación

| Preset | qrMode | operationMode | pinRequired | ¿Quién crea la sesión? | Cómo entra el cliente |
|---|---|---|---|---|---|
| QR Fijo | FIXED | STAFFED | true | Staff (cashdesk / `salon`) | QR fijo → navega menú → PIN al confirmar pedido |
| QR Fijo | FIXED | SELF_SERVICE | true | Cliente (al escanear QR fijo) | QR fijo → navega menú → mesero viene → PIN al confirmar |
| Sin seguridad | FIXED | STAFFED | false | Staff (cashdesk / `salon`) | QR fijo → navega y ordena directo |
| Sin seguridad | FIXED | SELF_SERVICE | false | Cliente (al escanear QR fijo) | QR fijo → navega y ordena directo |
| QR Dinámico | DYNAMIC | STAFFED | false | Staff (cashdesk / `salon`) | QR dinámico impreso/mostrado → navega y ordena directo |

---

### 9.4 Flujo — FIXED QR + STAFFED + PIN requerido

El flujo de mayor control: el mesero abre la sesión, va a la mesa con el PIN en su pantalla, y el PIN es requerido para confirmar pedidos.

```mermaid
sequenceDiagram
    participant MS as Mesero (vista /salon)
    participant API as Mesto Beach API
    participant RD as Redis
    participant PG as PostgreSQL
    participant CJ as Cajero (staff app)
    participant C1 as Cliente 1
    participant C2 as Cliente 2
    participant SK as SignalR Hub

    Note over MS,PG: 1 — Mesero crea la sesión de la mesa
    MS->>API: POST /api/v1/sessions { tableCode: "mesa-5" }
    API->>PG: INSERT table_sessions (status: OPEN, mode: STAFFED, qr_mode: FIXED)
    PG-->>API: sessionId: "sess-xyz"
    API->>API: Genera PIN = "4821"
    API->>RD: SET session:{tid}:sess-xyz:pin "4821"  TTL 12h
    API->>RD: SET table:{tid}:mesa-5:active "sess-xyz"  TTL 12h
    API-->>MS: { sessionId, pin: "4821" }
    Note over MS: Pantalla del mesero muestra<br/>4  8  2  1<br/>Va a la mesa y lo muestra al cliente

    Note over C1,API: 2 — Cliente 1 escanea el QR fijo de la mesa
    C1->>API: GET /api/v1/sessions/join?t=mesa-5&slug=la-parrilla
    API->>RD: GET table:{tid}:mesa-5:active → "sess-xyz"
    API->>API: Genera SessionToken { sessionId, participantId: uid-001 }
    API-->>C1: { sessionToken, pinRequired: true, participantCount: 0 }
    C1->>C1: Ingresa nombre "Ana"
    Note over C1: Navega el menú y arma su carrito libremente

    Note over C1,API: 3 — Cliente 1 confirma su primer pedido (PIN requerido)
    C1->>API: POST /api/v1/sessions/sess-xyz/orders\n{ items: [...], pin: "4821" }
    API->>RD: GET session:{tid}:sess-xyz:pin → "4821"
    API->>API: Valida PIN correcto
    API->>PG: INSERT orders
    API->>SK: Emite order.new al KDS correspondiente
    API-->>C1: 201 — Pedido confirmado
    Note over C1: Pedidos posteriores del mismo\nparticipante no requieren PIN

    Note over C2,API: 4 — Cliente 2 se une (ya sabe el PIN porque se lo dijeron)
    C2->>API: GET /api/v1/sessions/join?t=mesa-5&slug=la-parrilla
    API-->>C2: { sessionToken, participantId: uid-002, pinRequired: true }
    C2->>C2: Ingresa nombre "Luis"

    Note over CJ,RD: 5 — Cajero regenera el PIN (caso inusual)
    CJ->>API: POST /api/v1/sessions/sess-xyz/regenerate-pin
    API->>API: Genera nuevo PIN = "9037"
    API->>RD: SET session:{tid}:sess-xyz:pin "9037"  TTL restante
    API->>RD: DEL invite:{tid}:sess-xyz:token
    API->>SK: Emite session.pin_changed al grupo cashdesk-{tid}
    API-->>CJ: { newPin: "9037" }
    Note over CJ: Links de invitacion anteriores<br/>quedan invalidos

    Note over CJ,RD: 6 — Cajero cierra la sesión al finalizar
    CJ->>API: PATCH /api/v1/sessions/sess-xyz/close
    API->>PG: UPDATE table_sessions status=CLOSED
    API->>RD: DEL table:{tid}:mesa-5:active
    API->>RD: DEL session:{tid}:sess-xyz:pin
    API->>RD: DEL invite:{tid}:sess-xyz:token
    API->>RD: SADD blacklist:{tid} "sess-xyz"  TTL 24h
    API->>SK: Emite session.closed al grupo client-sess-xyz
    API-->>CJ: Sesion cerrada
```

---

### 9.5 Flujo — FIXED QR + SELF_SERVICE + PIN requerido

El cliente crea la sesión al escanear. El mesero es notificado y va a la mesa a entregar el PIN.

```mermaid
sequenceDiagram
    participant C1 as Cliente 1
    participant API as Mesto Beach API
    participant RD as Redis
    participant PG as PostgreSQL
    participant MS as Mesero (vista /salon)
    participant SK as SignalR Hub

    Note over C1,API: 1 — Cliente escanea QR (sin sesion previa)
    C1->>API: GET /api/v1/sessions/join?t=mesa-5&slug=la-parrilla
    API->>RD: GET table:{tid}:mesa-5:active → vacio
    API->>PG: Verifica horario de operacion del tenant → dentro del horario
    API->>PG: INSERT table_sessions (status: OPEN, mode: SELF_SERVICE)
    PG-->>API: sessionId: "sess-abc"
    API->>API: Genera PIN = "5523"
    API->>RD: SET session:{tid}:sess-abc:pin "5523"  TTL 12h
    API->>RD: SET table:{tid}:mesa-5:active "sess-abc"  TTL 12h
    API->>API: Genera SessionToken { participantId: uid-001 }
    API-->>C1: { sessionToken, pinRequired: true, isNewSession: true }
    C1->>C1: Ingresa nombre "Ana"
    Note over C1: Puede navegar el menu y agregar al carrito<br/>Al tocar Confirmar pedido ve pantalla de PIN

    Note over API,MS: 2 — Notificacion al mesero asignado (o a todos)
    API->>SK: Emite session.created al grupo waiter-{tid}
    SK-->>MS: Alerta push: "Ve a Mesa 5 y entrega el PIN de ingreso"
    Note over MS: Mesero va a la mesa<br/>y muestra el PIN en su pantalla

    Note over C1,API: 3 — Cliente intenta confirmar su primer pedido
    C1->>API: POST /api/v1/sessions/sess-abc/orders\n{ items: [...], pin: "5523" }
    API->>RD: GET session:{tid}:sess-abc:pin → "5523"
    API->>API: Valida PIN correcto
    API->>PG: INSERT orders
    API-->>C1: 201 — Pedido confirmado

    Note over C1,API: 4 — Intento fuera de horario
    C1->>API: GET /api/v1/sessions/join?t=mesa-5&slug=la-parrilla
    API->>PG: Horario → 02:00 AM, fuera de horario
    API-->>C1: 403 { code: RESTAURANT_CLOSED, message: "Abrimos a las 11:00 am" }
```

---

### 9.6 Flujo — DYNAMIC QR + STAFFED

El mesero genera la sesión y el QR cambia con cada apertura. No hay QR permanente en la mesa.

```mermaid
sequenceDiagram
    participant MS as Mesero (vista /salon)
    participant API as Mesto Beach API
    participant RD as Redis
    participant PG as PostgreSQL
    participant CJ as Cajero (staff app)
    participant C1 as Cliente 1

    Note over MS,PG: 1 — Mesero crea sesion con QR dinamico
    MS->>API: POST /api/v1/sessions { tableCode: "mesa-5", qrMode: DYNAMIC }
    API->>PG: INSERT table_sessions (status: OPEN, qr_mode: DYNAMIC)
    PG-->>API: sessionId: "sess-dyn"
    API->>API: Genera QR token firmado\n{ sessionId, tenantId, tableCode, exp: +12h }
    API->>RD: SET qr:{tid}:sess-dyn:token "{token}"  TTL 12h
    API->>RD: SET table:{tid}:mesa-5:active "sess-dyn"  TTL 12h
    API-->>MS: { sessionId, qrToken: "{token}" }
    Note over MS: App del mesero muestra el QR<br/>en pantalla grande<br/>Muestra al cliente O lo imprime desde el cajero

    Note over CJ: Alternativa: cajero imprime el QR<br/>desde su panel y lo lleva a la mesa

    Note over C1,API: 2 — Cliente escanea el QR DINAMICO del mesero
    C1->>API: GET /api/v1/sessions/join?q={qrToken}
    API->>RD: GET qr:{tid}:sess-dyn:token → valida firma y expiracion
    API->>API: Genera SessionToken { participantId: uid-001 }
    API-->>C1: { sessionToken, pinRequired: false }
    C1->>C1: Ingresa nombre y ordena directamente

    Note over CJ,RD: 3 — Al cerrar sesion el QR dinamico muere
    CJ->>API: PATCH /api/v1/sessions/sess-dyn/close
    API->>RD: DEL qr:{tid}:sess-dyn:token
    API->>RD: DEL table:{tid}:mesa-5:active
    API->>RD: SADD blacklist:{tid} "sess-dyn"
    Note over C1: Si intenta usar el QR viejo recibe 410 Gone
```

---

### 9.7 Invite Link — Acceso por invitación remota

Cualquier participante dentro de la sesión puede generar un link para invitar a alguien por WhatsApp o cualquier canal. El link tiene el PIN embebido en un token firmado — el destinatario solo ingresa su nombre.

```mermaid
sequenceDiagram
    participant P1 as Participante (dentro de la sesion)
    participant API as Mesto Beach API
    participant RD as Redis
    participant P2 as Invitado (por WhatsApp)
    participant SK as SignalR Hub

    Note over P1,API: 1 — Participante genera el link de invitacion
    P1->>API: POST /api/v1/sessions/sess-xyz/invite-link
    API->>RD: GET invite:{tid}:sess-xyz:token → (existe o crear nuevo)
    API->>API: Genera invite token firmado (JWT RS256)\n{ sessionId, tenantId, tableCode, type: invite }
    API->>RD: SET invite:{tid}:sess-xyz:token "{token}"  TTL = sesion
    API-->>P1: { inviteUrl: "mesto.app/join?i={token}" }
    Note over P1: Comparte por WhatsApp/SMS<br/>Link usable por multiples personas

    Note over P2,API: 2 — Invitado toca el link
    P2->>API: GET /api/v1/sessions/join?i={token}
    API->>API: Verifica firma del token
    API->>RD: SISMEMBER blacklist:{tid} sessionId → false (sesion activa)
    API->>API: Genera SessionToken { participantId: uid-new }
    API-->>P2: { sessionToken, pinRequired: false }
    Note over P2: Solo ingresa su nombre<br/>PIN ya va en el token

    API->>SK: Emite session.participant_joined\n{ name, via: invite }
    SK-->>P1: Toast: "Maria se unio (por invitacion)"

    Note over API,RD: 3 — Si cajero regenera el PIN
    Note over RD: DEL invite:{tid}:sess-xyz:token<br/>El link anterior expira inmediatamente<br/>P1 debe generar un nuevo link si quiere invitar a alguien mas
```

**Propiedades del invite token:**
- Tipo JWT, firmado con RS256 (misma clave que los Session Tokens del staff)
- Claims: `{ sid: sessionId, tid: tenantId, tbl: tableCode, type: "invite", exp }`
- Almacenado en Redis: una sola key por sesión — el mismo link sirve para múltiples personas
- No expone el PIN en la URL ni en el payload del JWT (el PIN se valida por separado al confirmar el primer pedido — comportamiento configurable)
- Expiración: misma que la sesión activa en Redis

---

### 9.8 Roles del staff en la sesión

| Acción | Mesero | Cajero |
|---|---|---|
| Crear sesión (STAFFED) | ✅ | ✅ |
| Ver PIN activo de sus mesas | ✅ | ✅ |
| Mostrar QR dinámico en app | ✅ | ✅ |
| Imprimir QR dinámico | ❌ | ✅ |
| **Regenerar PIN** | ❌ | ✅ único responsable |
| Cerrar sesión | ❌ | ✅ |
| Procesar pagos | ❌ | ✅ |
| Ver pedidos de cualquier mesa | ✅ | ✅ |
| Forzar ingreso de participante | ❌ | ✅ |
| Cancelar pedidos en estado Nuevo | ❌ | ✅ |
| Ver todas las mesas (global) | ✅ mesas asignadas | ✅ todas |
| Recibir notificación de nueva sesión SELF_SERVICE | ✅ mesas asignadas | ✅ todas |

**Asignación de mesas:**
La tabla `waiter_assignments` permite asignar mesas específicas a cada mesero. Si un mesero no tiene asignaciones configuradas, recibe las notificaciones de todas las mesas del restaurante. El cajero siempre tiene visibilidad global.

---

### 9.9 Vista compartida de la sesión (multi-persona)

Todos los participantes ven la factura completa y lo que pidió cada persona.

```mermaid
graph TD
    subgraph Session["Sesion Mesa 5 — sess-xyz"]
        P1["Participante: Ana — uid-001"]
        P2["Participante: Luis — uid-002"]
        P3["Participante: Maria — uid-003 (por invitacion)"]
    end

    subgraph Orders["Pedidos de la sesion"]
        O1["Chorizo x1 — Ana — 9,50 €"]
        O2["Costilla x1 — Luis — 16,50 €"]
        O3["Limonada x2 — Ana + Maria — 7,00 €"]
    end

    subgraph Bill["Factura visible para todos los participantes"]
        B1["Ana: 13,00 €"]
        B2["Luis: 16,50 €"]
        B3["Maria: 3,50 €"]
        B4["TOTAL: 33,00 €"]
    end

    P1 --> O1
    P1 --> O3
    P2 --> O2
    P3 --> O3
    Session --> Bill
```

> Los importes viajan como enteros en **céntimos de euro** (`3300` = 33,00 €); arriba se muestran
> ya formateados, como los ve el comensal (ADR-031).

---

### 9.10 Estructura de datos en Redis

```
# Sesion activa por mesa
table:{tenantId}:{tableCode}:active        → sessionId (STRING, TTL 12h)

# PIN activo de la sesion
session:{tenantId}:{sessionId}:pin         → "4821" (STRING, hoy TTL 4h; pasa a 12h con PRD-019 — ver abajo)

# Intentos fallidos de PIN por dispositivo (brute force protection)
pin_attempts:{tenantId}:{tableCode}:{deviceId}  → contador (STRING, TTL 10min)
# Maximo 5 intentos. Al superar → 429 con Retry-After: 600

# Invite link token (reutilizable, un solo token por sesion)
invite:{tenantId}:{sessionId}:token       → token JWT firmado (STRING, TTL = sesion)

# QR token dinamico (solo en qrMode: DYNAMIC)
qr:{tenantId}:{sessionId}:token           → token JWT firmado (STRING, TTL = sesion)

# Blacklist de sesiones cerradas
blacklist:{tenantId}                       → SET de sessionIds (TTL 24h por miembro)

# Rate limiting de creacion de sesiones (anti-spam en self-service)
ratelimit:sessions:{tenantId}:{tableCode}  → contador (STRING, TTL 1h)
# Maximo 5 sesiones nuevas por mesa por hora

# Cache del horario de operacion
schedule:{tenantId}                        → JSON de horarios (STRING, TTL 10min)

# Cache del catalogo del menu
catalog:{tenantId}                         → JSON del catalogo (STRING, TTL 5min)

# Saldo de puntos Mesto del cliente (cache en sesion activa)
loyalty:balance:{customerId}               → INTEGER (STRING, TTL = duracion de la sesion)
# Invalida al confirmar pedido con canje o al finalizar sesion

# Contrato de fidelizacion activo del tenant (cache)
loyalty:contract:{tenantId}               → JSON {pointsRate, redeemableItems} (TTL 10min)
# Invalida cuando Admin modifica el contrato en el back-office

# Roles del agente de Ops (cache del JWT)
ops_roles:{opsUserId}                     → JSON array ["soporte","billing"] (STRING, TTL 8h)
# Invalida cuando Admin cambia roles del agente

# ---- Disenado, pendiente de implementacion (PRD-019) ----

# Modo del local (ADR-036) — copia de tenants.venue_mode; si falta, se re-siembra desde Postgres
# (SET NX, solo si el tenant existe; nunca se escribe para un id sin fila en tenants).
# Redis caido → se lee Postgres sin SET; nunca se inventa un modo
venue:{tenantId}:mode                      → "RESTAURANT" | "NIGHT" (STRING, TTL 10 min)
# La escriben POST /api/v1/venue/mode (cambio y rama idempotente, SET EX 600) y la re-siembra del proveedor;
# la lee IVenueModeProvider (Catalog, Orders, Sessions)

# PINs reservados por las sesiones abiertas del local (ADR-040) — garantiza PIN unico entre mesas
pins_in_use:{tenantId}                     → HASH campo=PIN, valor=sessionId (sin TTL; HSETNX al generar, se libera por sessionId al cerrar o regenerar; se autorrepara si colisiona con una sesion ya no abierta)

# Estacion del runner (ADR-038) — cache de user_memberships.assigned_station_id para IStaffStationResolver
# (JwtValidationMiddleware)
staff_station:{tenantId}:{userId}          → stationId o "-" (STRING, TTL 60s)
# Reasignar solo la estacion ya no invalida el JWT: el cambio se refleja en <= 60 s.
# La borran PATCH assignment, PATCH roles y DELETE staff

# Cambio de TTL (ADR-040): session:{tenantId}:{sessionId}:pin y
# pin_validated:{tenantId}:{sessionId}:{participantId} pasan de 4h a 12h en TODOS los modos.
# (el join por invitacion ya escribia pin_validated con 12h); table:{tenantId}:{tableCode}:active NO cambia (sigue en 4h)
# Postgres (session_participants.pin_validated) es la verdad; si la clave falta, se consulta y se re-siembra
# con TTL fijo de 12h
```

---

### 9.11 Seguridad anti-abuso

| Proteccion | Implementacion | Configurable |
|---|---|---|
| **PIN como gate de orden** | 4 digitos requeridos al confirmar primer pedido (si pin_required = true) | Si, por tenant |
| **Brute force del PIN** | Max 5 intentos por dispositivo por 10 min → 429 Too Many Requests | No |
| **Rate limit de sesiones** | Max 5 sesiones nuevas / mesa / hora en self-service | Si, por tenant |
| **Rate limit de pedidos** | Max 30 pedidos / sesion / hora | Si, por tenant |
| **Horario de operacion** | QR no crea sesiones fuera del horario en self-service | Si |
| **QR dinamico expira** | El QR dinamico muere con la sesion — fotos viejas son inutiles | No |
| **Invite link expira con sesion** | El token de invitacion tiene TTL = sesion activa en Redis | No |
| **Regenerar PIN invalida links** | Al regenerar el PIN, DEL invite:{tid}:{sid}:token — links anteriores muertos | No |
| **Blacklist de sesiones** | Al cerrar, sessionId va al SET de blacklist — O(1) por request | No |
| **Tenant isolation** | tableCode + tenantId validados en cada request — imposible cross-tenant | No |
| **Notificacion al cajero** | En self-service, el cajero ve cada nueva sesion en tiempo real | Si |
| **Token en memoria** | SessionToken en memoria del browser — invalido al cerrar el tab | No |

---

### 9.12 Especificacion de endpoints de sesion

```
# Crear sesion (Mesero o Cajero — requiere JWT de staff)
POST /api/v1/sessions
Body: { tableCode: string }
Response 201: { sessionId, pin, qrToken? (si qrMode=DYNAMIC), tableCode }

# Unirse a sesion via QR fijo (cliente anonimo — sin token)
GET /api/v1/sessions/join?t={tableCode}&slug={tenantSlug}
Response 200: {
  sessionToken, sessionId, participantId,
  pinRequired: bool,
  isNewSession: bool,
  participantCount: int,
  restaurantName, tableCode
}
Errores:
  403 NO_ACTIVE_SESSION    — STAFFED sin sesion abierta
  403 RESTAURANT_CLOSED    — self-service fuera de horario
  429 TOO_MANY_SESSIONS    — rate limit excedido

# Unirse a sesion via QR dinamico (cliente — sin token)
GET /api/v1/sessions/join?q={qrToken}
Response 200: { sessionToken, sessionId, participantId, pinRequired: bool }
Errores:
  410 Gone — QR expirado (sesion cerrada)
  401      — Token invalido o firma incorrecta

# Unirse a sesion via invite link (cliente — sin token)
GET /api/v1/sessions/join?i={inviteToken}
Response 200: { sessionToken, sessionId, participantId, pinRequired: false }
Errores:
  410 Gone — Invite token expirado o invalido

# Confirmar primer pedido con PIN (cliente — requiere SessionToken)
POST /api/v1/sessions/{sessionId}/orders
Body: { items: [...], pin?: string }
Note: pin requerido solo para el primer pedido del participante si pin_required=true
Errores:
  401 INVALID_PIN           — PIN incorrecto
  429 PIN_ATTEMPTS_EXCEEDED — Demasiados intentos (5 max en 10 min)

# Generar invite link (cliente o staff autenticado)
POST /api/v1/sessions/{sessionId}/invite-link
Response 200: { inviteUrl: "mesto.app/join?i={token}" }

# Regenerar PIN (solo Cajero)
POST /api/v1/sessions/{sessionId}/regenerate-pin
Response 200: { newPin: string }
Note: invalida invite token existente. Cashier role requerido.

# Cerrar sesion (solo Cajero)
PATCH /api/v1/sessions/{sessionId}/close
Response 200: { closedAt: timestamp }
```

---

### 9.13 Implementación — Modal de PIN y "pedir PIN al mesero"  *(actualizado)*

Detalle de cómo opera hoy el gate de PIN en el QR fijo:

1. El cliente arma el carrito y confirma. Si la sesión exige PIN y aún no lo validó, el backend
   responde **`403 PIN_REQUIRED`** (no crea el pedido).
2. La app `menu` abre un **modal de PIN** (4 casillas OTP) en vez de un error. Mensaje: *"Pídele el PIN al
   mesero. Es el mismo para toda la mesa."*
3. Botón **"Pedir el PIN al mesero"** → `POST /sessions/{id}/request-pin` → emite `PIN_REQUESTED` al
   grupo `waiter-{tenantId}` (durable en Redis con `reason: "PIN"`, igual que un llamado). El mesero/cajero
   lo ve resaltado ("🔑 Pide PIN") + sonido, y lee el PIN de su pantalla para dictarlo.
4. El cliente teclea el PIN → `POST /sessions/{id}/pin/validate` (marca `pin_validated` en Redis) → el
   pedido se **reintenta automáticamente** y entra como `CONFIRMED`.

Endpoint nuevo:
```
POST /api/v1/sessions/{sessionId}/request-pin     → [AllowAnonymous]; PIN_REQUESTED al mesero
```
> El pedido del cliente (`POST /orders`) es `[AllowAnonymous]`: el cliente anónimo queda implícitamente
> acotado a la sesión que se unió (su `participantId`, emitido al unirse, es la prueba de capacidad); el
> staff sí debe coincidir en `tenantId`.

---

## 10. Flujo de Datos — Pedido Completo

```mermaid
sequenceDiagram
    participant C as Cliente
    participant MA as menu (Next.js)
    participant API as Mesto Beach API
    participant MG as MongoDB (Catalog)
    participant PG as PostgreSQL (Orders)
    participant SK as SignalR Hub
    participant KS as KDS Cocina
    participant BR as KDS Barra
    participant WA as waiter (Mesero)

    Note over C,MA: 1. Obtener el menú
    C->>MA: Abre mesto.app/la-parrilla
    MA->>API: GET /catalog/menu [Bearer: sessionToken]
    API->>MG: Busca catálogo del tenant\nfiltrado por horario actual
    MG-->>API: Catálogo completo
    API-->>MA: Menú + branding
    MA-->>C: Muestra menú con categorías

    Note over C,MA: 2. Construir y confirmar pedido
    C->>MA: Agrega productos al carrito
    C->>MA: Confirma pedido
    MA->>API: POST /orders/sessions/[id]/orders\n[{productId, qty, details, customerName}]

    Note over API,PG: 3. Procesar pedido (lógica de routing)
    API->>MG: Consulta stationId de cada producto
    API->>PG: INSERT orders (separados por estación)
    Note right of API: Producto de cocina → orden para "cocina"\nProducto de barra → orden para "barra"
    PG-->>API: orderId[]

    Note over API,WA: 4. Notificar en tiempo real
    API->>SK: Emite order.new al grupo kitchen-{tid}-cocina
    API->>SK: Emite order.new al grupo bar-{tid}
    API->>SK: Emite order.status al grupo client-{sessionId}
    SK-->>KS: Nuevo pedido aparece en KDS cocina
    SK-->>BR: Nuevo pedido aparece en KDS barra
    SK-->>C: Estado: "Tu pedido fue recibido"

    Note over KS,SK: 5. Cocina actualiza el estado
    KS->>API: PATCH /orders/[id]/step { step: "Preparing" }
    API->>PG: UPDATE order step
    API->>SK: Emite order.step_changed
    SK-->>KS: UI actualizada
    SK-->>C: Estado: "Tu pedido está en preparación"

    Note over KS,WA: 6. Pedido listo
    KS->>API: PATCH /orders/[id]/step { step: "Ready" }
    API->>PG: UPDATE order step
    API->>SK: Emite order.ready al grupo waiter-{tid}
    API->>SK: Emite order.status al grupo client-{sessionId}
    SK-->>WA: Alerta push: "Mesa 5 lista — Chorizo"
    SK-->>C: Estado: "Tu pedido está listo"

    Note over WA,API: 7. Mesero entrega
    WA->>API: PATCH /orders/[id]/status { status: "Delivered" }
    API->>PG: UPDATE order status
    API->>SK: Emite order.status_changed
    SK-->>C: Estado: "Entregado — Buen provecho"
```

---

## 11. Flujo de Pagos

```mermaid
graph TD
    subgraph Opciones["Opciones de pago al cerrar cuenta"]
        A[Pago en caja\nEfectivo o datáfono propio]
        B[Pago digital completo\ndesde el celular del cliente]
        C[Split bill\nCada persona paga su parte]
    end

    subgraph FlujoCaja["Flujo A — Caja"]
        A --> CA1[Cajero genera resumen\nGET /orders/sessions/id/summary]
        CA1 --> CA2[Cajero registra pago\nPOST /payments/cash]
        CA2 --> CA3[Sistema cierra sesión\nInvalida tokens del cliente]
    end

    subgraph FlujoDigital["Flujo B — Digital"]
        B --> DG1[Cliente solicita pagar\nen la app del menú]
        DG1 --> DG2[API crea intento de pago\nen Wompi / MercadoPago]
        DG2 --> DG3[Cliente completa pago\nen checkout del proveedor]
        DG3 --> DG4[Webhook confirma pago\nPOST /payments/webhook/wompi]
        DG4 --> DG5[Sistema registra pago\nCierra sesión automáticamente]
    end

    subgraph FlujoSplit["Flujo C — Split Bill"]
        C --> SP1[Cliente o cajero\ndefine N personas]
        SP1 --> SP2[Sistema divide cuenta\nen N partes iguales o custom]
        SP2 --> SP3[Cada persona paga\nsu parte — digital o caja]
        SP3 --> SP4{¿Todas las\npartes pagas?}
        SP4 -->|Sí| SP5[Sesión cerrada\nautomáticamente]
        SP4 -->|No| SP3
    end
```

---

## 12. Estrategia de Despliegue — Híbrido local/nube

El servicio no puede depender de que internet llegue bien a la orilla. La topología separa
**lo que no puede parar** (la operación del local) de **lo que puede degradarse** (el pedido
por QR del comensal). Ver **ADR-024**.

```mermaid
graph TD
    subgraph Local["EN EL LOCAL — fuente de verdad"]
        SRV["Servidor del local<br/>MestoBeach.API + Postgres + Mongo + Redis<br/>(Docker Compose)"]
        WIFI["WiFi del chiringuito"]
        PDA["PDA — comandas"]
        RUN["Runner — entrega"]
        KDS["KDS — Cocina · Parrilla · Cocktails"]
        CAJA["Caja"]
        HOST["Recepción — mapa y ocupación"]
        PDA --- WIFI
        RUN --- WIFI
        KDS --- WIFI
        CAJA --- WIFI
        HOST --- WIFI
        WIFI --- SRV
    end

    subgraph Nube["EN LA NUBE — cara al comensal"]
        MENU["menu (Next.js)<br/>la carta pública"]
        MOVIL["Móvil del comensal<br/>(sus propios datos)"]
        MOVIL --- MENU
    end

    MENU -->|"pedidos del comensal"| SRV
    SRV -->|"carta y estado de los pedidos"| MENU
    SRV -.->|"comandas al TPV (ADR-028)"| TPV["TPV externo<br/>cuenta y factura"]
```

### Qué pasa si se cae el enlace a internet

| Superficie | Comportamiento |
|---|---|
| PDA, Runner, KDS, Caja, Recepción | **Siguen operando** contra el servidor del local: se comanda, se cocina, se entrega y se cobra |
| Menú del comensal | **Se degrada**: la carta sigue viéndose, pero el pedido invita a llamar al camarero |
| Sincronización | Al volver el enlace, la nube reenvía lo que quedó en cola (ADR-024) |

### Entornos

```
Desarrollo (la PC del desarrollador):
  NEXT_PUBLIC_API_URL=http://localhost:5011
  ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=mestobeach;Username=mestobeach;Password=mestobeach_dev
  ConnectionStrings__MongoDB=mongodb://mestobeach:mestobeach_dev@localhost:27017/mestobeach_catalog?authSource=admin
  ConnectionStrings__Redis=localhost:6379,password=mestobeach_dev
  BlobStorage__ConnectionString=[Azurite — emulador local, puerto 10000]
  Todo en contenedores; ver docs/DESARROLLO-LOCAL.md

Producción (el local):
  Servidor propio en el chiringuito con el mismo Docker Compose endurecido
  Secretos fuera del código, en el entorno del servidor
  Copias de seguridad de PostgreSQL y del almacén de imágenes — pendiente de definir (OQ)
  El menú del comensal se publica aparte, con dominio propio del cliente
```

> **Pendientes de esta estrategia** (abiertos en PRD-018): política de copias de seguridad y
> restauración, actualización remota del servidor del local, y monitorización de que el enlace
> está vivo. Ninguno bloquea el diseño de las fases F1–F4.

---


## 13. Estructura de Repositorios

### `mesto-web` (Turborepo)

```
mesto-web/
├── apps/
│   ├── menu/
│   │   ├── app/
│   │   │   ├── [tenantSlug]/
│   │   │   │   ├── page.tsx              → Landing / verificación QR
│   │   │   │   ├── menu/page.tsx         → Menú navegable
│   │   │   │   ├── mis-pedidos/page.tsx  → Estado en tiempo real
│   │   │   │   └── cuenta/page.tsx       → Resumen + pago
│   │   │   └── layout.tsx
│   │   ├── public/
│   │   │   └── manifest.json             → PWA manifest
│   │   └── next.config.ts
│   ├── staff/
│   │   ├── app/
│   │   │   ├── board/page.tsx            → KDS cocina/barra (4 columnas) [ADR-032]
│   │   │   ├── cashdesk/page.tsx         → Cajero / sesiones
│   │   │   ├── salon/page.tsx            → Mesero (vista mobile-first) [ADR-019]
│   │   │   ├── pda/page.tsx              → PDA: comandas, marchar, observación
│   │   │   ├── runner/page.tsx           → Runner: cola del pase y viaje [ADR-027]
│   │   │   ├── host/page.tsx             → Recepción (rol Host): mapa de la sala
│   │   │   ├── history/page.tsx          → Historial de sesiones
│   │   │   └── login/page.tsx            → rutea por rol a la vista de cada uno
│   │   └── next.config.mjs
│   ├── admin/
│   │   ├── app/
│   │   │   ├── onboarding/page.tsx
│   │   │   ├── menu/page.tsx
│   │   │   ├── mesas/page.tsx
│   │   │   ├── analytics/page.tsx
│   │   │   └── inventario/page.tsx
│   │   └── next.config.ts
│   └── platform/
│       └── app/
│           └── tenants/page.tsx
├── packages/
│   ├── ui/
│   │   ├── components/
│   │   │   ├── OrderCard/
│   │   │   ├── ProductCard/
│   │   │   ├── SessionSummary/
│   │   │   └── QRDisplay/
│   │   └── package.json
│   ├── types/
│   │   ├── generated/           → Auto-generado desde OpenAPI spec
│   │   │   ├── orders.ts
│   │   │   ├── catalog.ts
│   │   │   └── payments.ts
│   │   └── package.json
│   └── utils/
│       ├── currency.ts          → Formateo de moneda (euros; `es-ES` sobre céntimos)
│       ├── dates.ts
│       └── package.json
└── turbo.json
```

### `mestobeach-api` (.NET)

```
mestobeach-api/
├── src/
│   ├── MestoBeach.API/
│   │   ├── Program.cs
│   │   ├── Middleware/
│   │   │   ├── TenantMiddleware.cs
│   │   │   ├── CorrelationIdMiddleware.cs
│   │   │   └── RateLimitMiddleware.cs
│   │   └── appsettings.json       → Sin secretos, solo estructura
│   ├── Modules/
│   │   ├── Identity/
│   │   │   ├── Domain/
│   │   │   ├── Application/
│   │   │   ├── Infrastructure/
│   │   │   └── Presentation/
│   │   ├── Catalog/
│   │   ├── Orders/
│   │   │   ├── Infrastructure/
│   │   │   │   └── SignalR/
│   │   │   │       ├── OrdersHub.cs
│   │   │   │       └── HubGroups.cs    → Constantes de grupos
│   │   ├── Payments/
│   │   ├── Analytics/
│   │   └── Inventory/
│   └── Shared/
│       ├── Domain/
│       │   ├── Entity.cs              → Base entity con TenantId
│       │   ├── IDomainEvent.cs
│       │   └── Result.cs              → Result pattern
│       ├── Infrastructure/
│       │   ├── Persistence/
│       │   │   ├── PostgresContext.cs
│       │   │   └── MongoContext.cs
│       │   └── Cache/
│       │       └── RedisService.cs
│       └── Kernel/
│           └── DomainEventBus.cs
└── tests/
    ├── MestoBeach.UnitTests/
    └── MestoBeach.IntegrationTests/
```

---

## 14. Decisiones de Arquitectura (ADRs)

### ADR-001: Modular Monolith sobre Microservicios
**Decisión:** Backend como monolito modular.
**Razón:** Equipo de 3-5 personas. Los microservicios añaden complejidad operativa que no está justificada en esta etapa.
**Consecuencia:** Cuando un módulo necesite escalar independientemente (ej. Analytics con queries pesadas), se puede extraer sin cambiar las interfaces.

### ADR-002: PostgreSQL para Pedidos, MongoDB para Catálogo
**Decisión:** Dos bases de datos con responsabilidades separadas.
**Razón:** Los pedidos y pagos requieren transacciones ACID. El catálogo de menú es naturalmente un documento con estructura variable.
**Consecuencia:** Se necesita gestionar dos conexiones en el backend. El beneficio es schema apropiado para cada tipo de dato.

### ADR-003: Tokens de Sesión en Redis con Invalidación Inmediata
**Decisión:** Los JWT del cliente se invalidan via blacklist en Redis cuando la sesión de mesa se cierra.
**Razón:** Seguridad — evita que clientes pidan después de irse o que una sesión vieja quede activa.
**Consecuencia:** Cada request del cliente requiere un lookup a Redis (O(1), latencia mínima).

### ADR-004: Next.js App Router para todos los frontends
**Decisión:** Un único framework para las apps frontend (4 apps: menu, staff, admin, platform; el mesero es la vista `/salon` de staff — ADR-019).
**Razón:** Un solo lenguaje (TypeScript), tipos compartidos via Turborepo, un solo proceso de deploy.
**Consecuencia:** Curva de aprendizaje del App Router para el equipo si vienen de Angular.

### ADR-005: SignalR con grupos por tenant y estación
**Decisión:** Nunca usar `Clients.All`. Siempre grupos con naming `{tipo}-{tenantId}[-{stationId}]`.
**Razón:** Aislamiento entre tenants + filtrado por estación (cocina vs barra).
**Consecuencia:** Al conectar, el cliente debe unirse explícitamente al grupo correcto con su JWT.

### ADR-006: QR fijo + Modo de sesión configurable por restaurante *(v2.0)*
**Decisión:** El QR de mesa es permanente (se imprime una sola vez). El modo de inicio de sesión es configurable por tenant: `STAFFED` (cajero abre la sesión) o `SELF_SERVICE` (cliente la abre al escanear).
**Razón:** El QR permanente elimina la necesidad de reimpresión. El modo configurable adapta el sistema a distintos tipos de negocio: un restaurante de servicio completo necesita control del cajero; un bar o café se beneficia del self-service.
**Consecuencia:** Se introducen dos flujos en el endpoint `/sessions/join`. En self-service se requieren protecciones adicionales: validación de horario de operación, rate limiting de creación de sesiones, y notificación en tiempo real al cajero. La blacklist de sesiones en Redis invalida todos los tokens de todos los participantes al cerrar.

### ADR-008: PIN de sesión + Invite Link + QR Dinámico *(v3.0)*
**Decisión:** La seguridad de acceso a la sesión se implementa con tres mecanismos complementarios: (1) PIN de 4 dígitos por sesión que el **mesero** lleva físicamente a la mesa; (2) invite link con token JWT firmado para invitar remotamente sin teclear el PIN; (3) QR dinámico generado por sesión como alternativa al QR fijo.
**Razón:** El PIN crea un vínculo de presencia física (el cliente necesita al mesero para pedir). El invite link resuelve el caso de grupos que se forman desde WhatsApp. El QR dinámico ofrece seguridad máxima para restaurantes que prefieren no imprimir QRs permanentes.
**Consecuencia:** El mesero adquiere el rol de creador de sesiones (antes era el cajero). Solo el cajero puede regenerar el PIN. Al regenerar el PIN, todos los invite links existentes se invalidan. El PIN se valida solo al confirmar el primer pedido del participante, no al entrar — esto permite navegar el menú sin fricciones.

### ADR-009: Separación explícita de roles Mesero vs Cajero
**Decisión:** El mesero es el responsable de crear sesiones y llevar el acceso (PIN o QR dinámico) a la mesa. El cajero tiene todos los permisos del mesero más: regenerar PIN, cerrar sesiones, procesar pagos, cancelar pedidos, e imprimir QR dinámico. La asignación de mesas a meseros es configurable.
**Razón:** Separar la responsabilidad operativa (mesero va a la mesa) del control de seguridad (cajero regenera PIN) crea un modelo de responsabilidades claro y auditable. Solo hay un responsable para la acción más sensible (regeneración del PIN).
**Consecuencia:** La tabla `waiter_assignments` en PostgreSQL gestiona qué mesero cubre qué mesas. Si no hay asignaciones, las notificaciones llegan a todos los meseros (fallback seguro).

### ADR-010: Red de Lealtad — Cross-tenant *(superada por ADR-023)*
> Se conserva como registro histórico. El módulo `Loyalty` se retiró al separar el producto: una red de puntos entre restaurantes no tiene sentido en un sistema de un solo local. Los puntos por cuenta de cliente siguen existiendo en `Identity`.
**Decisión:** El Programa de Lealtad Mesto es una red cross-tenant: una sola cuenta de cliente (`customer_accounts`) acumula puntos válidos en todos los restaurantes de la plataforma. Los contratos (tasa de acumulación, ítems elegibles) son negociados por Mesto y registrados en el back-office. El settlement de canjes se netea en el billing mensual del restaurante.
**Razón:** Un programa cross-tenant diferencia a Mesto de la competencia (los puntos no quedan "atrapados" en un solo restaurante), incrementa el valor percibido de la cuenta del cliente, y le da a Mesto un activo estratégico (la red de lealtad) que es más valioso cuantos más restaurantes participen.
**Consecuencia:** Se introduce el módulo `Loyalty` en el backend. Las tablas `loyalty_contracts`, `loyalty_redeemable_items`, `loyalty_transactions`, y `loyalty_redemptions` son de scope global (no filtradas por `tenantId` como el resto). El único endpoint que accede a datos cross-tenant del cliente es el módulo `Loyalty` — siempre autenticado con JWT de cliente.

### ADR-011: Roles granulares del equipo Ops — acumulables *(superada por ADR-023)*
> Histórico. No hay equipo de Ops: el módulo `Platform` y su consola se retiraron con la separación del producto.
**Decisión:** El equipo interno de Mesto tiene tres roles (`soporte`, `billing`, `admin`) que son acumulables: un agente puede tener Soporte + Billing sin ser Admin. Los permisos se verifican por rol en cada endpoint del portal (`/api/v1/platform/...`).
**Razón:** El modelo anterior (todos PlatformAdmin) crea riesgo: un agente de soporte tiene acceso a funciones de billing y configuración que no debe tocar. La granularidad previene errores accidentales y permite incorporar nuevo personal con accesos limitados.
**Consecuencia:** Se introduce la tabla `platform_user_roles` (multi-row por agente). El JWT del agente Ops incluye un claim `roles: ["soporte", "billing"]`. Los endpoints del portal verifican el claim antes de ejecutar. El cache de roles en Redis (`ops_roles:{userId}`) se invalida cuando Admin cambia roles.

### ADR-012: Módulo `Sessions` separado de `Orders` *(implementación)*
**Decisión:** Las sesiones de mesa, mesas, pisos, PIN, participantes y llamados viven en un módulo propio `Sessions`, distinto de `Orders` (que solo maneja pedidos y su routing a estaciones).
**Razón:** Los dominios tienen ciclos de vida y consistencia distintos; separarlos mantiene los límites claros y permite que Orders dependa de Sessions solo por `sessionId`. También evita un módulo Orders sobrecargado.
**Consecuencia:** `Orders` valida que la sesión exista/esté abierta vía un read-service de Sessions. La proyección de `shifts` (de Payments) se lee desde Sessions para exigir turno abierto al abrir mesas.

### ADR-013: Auto-confirmación del pedido del cliente *(implementación)*
**Decisión:** Al crear un pedido se avanza inmediatamente `PENDING → CONFIRMED` (confirmar el carrito = confirmación del pedido). No hay paso de aprobación de staff en el flujo QR.
**Razón:** El KDS solo muestra `CONFIRMED+`; sin auto-confirmación los pedidos quedaban atascados en `PENDING` e invisibles. El PRD-003 ya define "cliente confirma → aparece en el KDS en <2s".
**Consecuencia:** `PENDING` queda como estado transitorio interno; la transición manual `PENDING→CONFIRMED` por staff sigue existiendo por compatibilidad pero no se usa en el flujo normal.

### ADR-014: PIN como gate diferido + "pedir PIN al mesero" + Dynamic sin PIN *(implementación)*
**Decisión:** El PIN se valida al **confirmar el pedido** (no al entrar); si falta, el `menu` abre un modal de PIN con opción de **pedir el PIN al mesero** (evento `PIN_REQUESTED`). Las sesiones de **QR dinámico no exigen PIN**.
**Razón:** Permite navegar el menú sin fricción; el modal evita un callejón sin salida cuando el cliente no tiene el PIN; el QR dinámico (de un solo uso, mostrado por el mesero) ya autentica la mesa.
**Consecuencia:** Nuevo endpoint `POST /sessions/{id}/request-pin` y tipo de evento `PIN_REQUESTED` durable en Redis. `pin_required` se deriva de `qrMode != DYNAMIC && tenantSetting`.
**Revisión prevista (PRD-019, octubre 2026):** en `NIGHT` el menú pide el PIN en el registro inicial, junto al nombre (`POST /sessions/{id}/pin/validate`), una vez por dispositivo, y el PIN se exige en todas las mesas (también DYNAMIC y Sin seguridad). El gate de Orders al crear el pedido y el modal «pedir el PIN al mesero» se mantienen como red de seguridad; en `RESTAURANT` nada cambia (ADR-040).

### ADR-015: Mapa de mesas por pisos (layout puro) *(implementación)*
**Decisión:** El dueño diseña el local en `admin` con **pisos con nombre** (entidad `Floor`), arrastra mesas (posición/forma/sillas) y dibuja **muros** dentro de un rectángulo. El cajero ve un **mapa interactivo** por piso con estado Libre/Ocupada y abre mesas desde ahí.
**Razón:** Reflejar la distribución física real ayuda a operar (ubicar mesas, abrirlas) y a escalar a locales de varios niveles.
**Consecuencia:** Tabla `floors` (con `walls` jsonb) + columnas de layout en `tables` (`floor_id, pos_x, pos_y, seats, shape`). Endpoints `/floors`, `/floorplan`, `/tables/layout`. Es **layout puro**: no afecta sesiones/pedidos. Borrar un piso deja sus mesas "sin ubicar" (no las borra). La creación de mesas **ad-hoc** del cajero se mantiene.

### ADR-016: Cobro del cajero con turnos de caja *(implementación)*
**Decisión:** El cobro vive en `Payments` con entidad **Shift** (turno). Exigir turno abierto para abrir mesas; cada pago se estampa con el turno; al cerrar el turno se genera un reporte de caja. Split bill (igualitario/por persona) auto-cierra la sesión al completar. Recibo térmico ESC/POS por WebUSB (en el front).
**Razón:** Cuadre de caja por turno, trazabilidad de pagos y un flujo de cierre realista para el cajero.
**Consecuencia:** Tablas `shifts` y `payments.shift_id`. Pasarelas digitales (Wompi/MP) quedan diseñadas pero pendientes.

### ADR-017: Apariencia del menú por tenant — tema curado + Blob Storage *(implementación)*
**Decisión:** El branding del menú (subdoc `MenuBranding` en el catálogo MongoDB) ofrece **color de acento + preset de fondo (light/cream/dark) + fuente de una lista curada**, en vez de colores libres primario/secundario. Las imágenes (logo, hero) y las fotos de producto van a **Blob Storage** (Azure en prod, **Azurite** en dev), en contenedor con acceso público de blob; el menú aplica el tema vía variables CSS y resetea al salir para que el **login conserve la identidad Mesto**. El branding viaja embebido en el catálogo público (comparte el caché Redis).
**Razón:** Un tema curado evita combinaciones ilegibles y mantiene una UX consistente entre restaurantes; el catálogo embebido evita una llamada extra y reusa la invalidación de caché existente; Azurite da paridad dev/prod sin depender de Azure.
**Consecuencia:** Dependencia nueva de Blob Storage (servicio `azurite` en `docker-compose`, sección `BlobStorage` en `appsettings`). Reemplaza el "color secundario" del PRD-004 v3.0 por tema+fuente (PRD-004 v3.1).

---

### ADR-018: Tipo de QR/acceso por mesa (Fijo / Dinámico / Sin seguridad) *(implementación)*
**Decisión:** El tipo de QR deja de ser una config global del tenant y pasa a **cada mesa** (`tables.qr_mode` + `tables.requires_pin`, migración `AddTableQrConfig`). El administrador lo elige en `admin → Mesas` entre tres presets: **QR Fijo (con PIN)** (FIXED + pin), **QR Dinámico** (DYNAMIC, sin pin) y **Sin seguridad** (FIXED, sin pin — acceso abierto). Al abrir una sesión, el backend (`OpenSessionCommand`) **deriva** `qrMode`/`pinRequired` de la mesa e **ignora** el `qrMode` que mande el cliente para mesas existentes; cashdesk y `waiter` ya no preguntan el tipo de QR (Fijo/Sin seguridad → sesión inmediata; Dinámico → ofrece imprimir el QR de la mesa). `SetQrConfig` fuerza `requiresPin=false` cuando el modo es DYNAMIC.
**Razón:** Un mismo restaurante mezcla mesas (terraza con QR fijo impreso, barra "sin seguridad", eventos con QR dinámico); forzar un único modo por tenant no modela la realidad. Derivar de la mesa al abrir elimina una pregunta al staff y evita que el cliente fuerce un modo más laxo. "Sin seguridad" cubre el caso de bares de alta rotación donde el PIN es fricción innecesaria.
**Consecuencia:** Las columnas viven en `tables` (no en `tenants`); `GET /api/v1/tables` y el floor plan exponen `qrMode`/`requiresPin`; `POST/PATCH /api/v1/tables` los aceptan. Reemplaza el modelo per-tenant del PRD-011 v2.0 (ahora v2.1). Convive con ADR-014 (PIN como gate diferido + Dynamic sin PIN), que sigue rigiendo *cómo* se exige el PIN.

---

### ADR-019: Mesero como vista `/salon` de `staff` (app `waiter` absorbida) *(implementación)*
**Decisión:** Se retira la app `waiter` (3002) como deploy independiente y su funcionalidad se mueve a `staff` como la ruta **`/salon`** (vista del mesero, mobile-first), gateada por rol. El login de `staff` rutea por rol: Cajero/Admin→`/cashdesk`, KDS→`/board`, Mesero→`/salon`. Los estilos del salón se portan a `salon.css` **scopeados bajo `.salon-root`** para no contaminar cashdesk/board. Como cajero y mesero comparten ahora el mismo `@mesto/api-client`, store y conexión SignalR, el cashdesk rehidrata su mapa también en `SESSION_OPENED`/`SESSION_CLOSED`/`PIN_REGENERATED` (antes solo en `BILL_REQUESTED`/`WAITER_CALLED`), cerrando la asimetría que dejaba al cajero sin ver las aperturas del mesero.
**Razón:** El mesero y el cajero comparten el mismo dominio (salón/sesiones) y la lógica estaba duplicada en dos apps (cada feature de salón —p.ej. QR por mesa— había que tocarla dos veces). Consolidar elimina la duplicación, comparte la lógica para que siempre estén sincronizados, y **ahorra un servicio/puerto en el despliegue**. El backend ya restringe cobro/turnos/cierre a `Admin/Cajero`, así que el mesero es de facto "un cashdesk con menos permisos".
**Consecuencia:** `apps/waiter` deja de existir (5→4 apps frontend); el grupo SignalR `waiter-{tenantId}` y la tabla `waiter_assignments` se conservan (son conceptos de backend, no la app). El mesero usa las storage keys `mesto_staff_jwt`/`mesto_staff_info` de `staff`.

---

### ADR-020: Catálogo de planes + entitlements con enforcement bloqueante *(superada por ADR-023)*
> Histórico. Este sistema atiende a un solo local: los planes por tenant no aplican.
**Decisión:** El "plan libre por tenant" (Ops teclea monto/comisión) se reemplaza por un **catálogo** (`planCode`: `basic`/`pro`/`enterprise`) con **entitlements** (`maxFloors`, `maxKdsStations`, `maxTables`, `maxStaff`, `loyalty`, `branding`, `analytics`). Los límites se **hacen cumplir de forma bloqueante**: cada módulo dueño del recurso (Sessions para pisos/mesas, Catalog/Orders para estaciones, Identity para staff) valida contra el entitlement del tenant **antes de crear** y responde **403 `plan_limit_exceeded`** con el plan sugerido; el front muestra upsell. La fidelización está en todos los planes (adquisición). Enterprise admite override por tenant sin cambiar `planCode`.
**Razón:** Un catálogo es vendible, consistente y permite que el producto gatee funciones por valor; el enforcement real (no solo documentado) es lo que convierte el límite en palanca de upgrade. La fuente de verdad de entitlements compartida evita lógica duplicada por módulo.
**Consecuencia:** se añade `tenants.plan_code` (varchar, default `pro`) **junto a** `tenants.plan`, que pasa a representar el **modo de cobro** derivado del plan. Nuevo servicio compartido `IPlanEntitlementsReader` (impl en Identity). El enforcement vive en `CreateFloor`/`CreateTable` (Sessions), `CreateKdsStation` (Orders) e `InviteStaff` (Identity). Reemplaza el modelo de PRD-005 que dejó suscripciones/planes fuera de alcance.

### ADR-021: Módulo Ledger — contabilidad de doble partida *(superada por ADR-023)*
> Histórico. La contabilidad del negocio la lleva el cliente con su gestoría y su TPV; el módulo se retiró.
**Decisión:** Nuevo módulo **`Ledger`** (PostgreSQL, schema `ledger`, migraciones propias) que registra **asientos balanceados** (Σ débitos = Σ créditos, validado en `JournalEntry.Create` — un asiento descuadrado es imposible por construcción) sobre un **plan de cuentas mínimo** (CxC 1305, Bancos 1110, CxP 2335, IVA por pagar 2408, Ingresos 4170/4175, Descuentos 5299, Gasto fidelización 5305). Los eventos de negocio de Platform (factura emitida / pago / descuento / void) y Loyalty (settlement) invocan `ILedgerPostingService` (interfaz en `MestoBeach.Shared`, impl en Ledger), **idempotente por `(entry_type, reference)`** — re-procesar el mismo evento es un no-op. Expone estado de cuenta por tenant y balances globales; los asientos son inmutables (correcciones via reverso).
**Razón:** El negocio necesita saber, en libros, cuánto le deben los tenants (CxC) y cuánto debe Mesto (CxP: settlement de fidelización, comisiones de pasarela). Un ledger de doble partida da consistencia contable verificable (invariante D=C) y la base para reportes financieros, sin acoplar la contabilidad a la lógica de facturación.
**Consecuencia:** 10º módulo backend (`Ledger`, SDD-010); las facturas viven en Platform (`platform_invoices`), los asientos en Ledger. La interfaz en Shared evita que Platform/Loyalty dependan del módulo Ledger. Reportes financieros formales (balance/P&L) quedan para después; el ledger deja la base.

### ADR-022: Consistencia del tablero KDS — sondeo autoritativo + delta/snapshot *(implementado)*
**Decisión:** El KDS **no confía en SignalR** como fuente de verdad (es *at-most-once*, sin replay: un evento perdido escondería un plato). El tablero se construye con un **sondeo autoritativo** a `GET /api/v1/orders/active`: el socket es solo un *hint* de baja latencia que adelanta un refresco. El endpoint soporta **delta sync** — con `?since=<cursor>` devuelve solo los pedidos con `updated_at > since` más `activeIds` (el conjunto autoritativo de ids activos) y `serverTime` (próximo cursor). El cliente hace upsert de los cambios y **poda** lo que ya no está en `activeIds` (snapshot backstop). El sondeo full (sin `since`) se fuerza al montar, reconectar, volver visible y recuperar red.
**Razón:** "Que nadie se quede sin su plato": la corrección no puede depender de que llegue cada evento. El sondeo garantiza convergencia en ≤1 ciclo aunque se pierdan todos los mensajes; el delta + `activeIds` da esa garantía **sin** re-enviar todo el tablero en cada ciclo, y detecta de forma autoritativa los pedidos que salieron (entregado/cancelado).
**Consecuencia:** Nueva columna `orders.updated_at` (cursor; índice `(tenant_id, updated_at)`), bumped en cada cambio de estado. La UI muestra un banner "Sin sincronizar" cuando el sondeo falla (>25 s sin éxito) y resalta pedidos demorados. SignalR se conserva por la latencia percibida, pero ya no es crítico para la consistencia. Detalle de contrato en SDD-004. *(v2.2, diseñado — PRD-019)* El delta también considera `order_items.updated_at`: un pedido entra en el delta si su `updated_at` o el de alguno de sus platos es posterior a `since` (SDD-004).

---

### ADR-023: Mesto Beach como producto propio — separación del código de Mesto *(agosto 2026)*
**Contexto:** El sistema nació como una implantación de **Mesto**, una plataforma SaaS multi-tenant pensada para vender a muchos restaurantes. Cuatro hechos rompieron ese encaje: (1) Mesto Beach necesita un **servidor en el local** como fuente de verdad, no una nube multi-tenant; (2) **convive con el TPV**, así que planes, facturación SaaS y contabilidad sobran; (3) el mercado es **España** (euros, IVA de hostelería, Verifactu) y el núcleo estaba cableado a Colombia; y (4) el ciclo de vida por ítem (ADR-025) **reescribe** la semántica de Orders en vez de añadirse. El argumento clásico en contra —no duplicar mantenimiento— perdió fuerza al confirmarse que **Mesto no tiene clientes en producción**: no hay flota que sincronizar.
**Decisión:** Separar el proyecto a su propio repositorio (`MestoBeach`), **clonando la historia completa** en lugar de empezar de cero. Se retiran los módulos `Platform`, `Ledger` y `Loyalty`, la app `platform` (consola de Ops) y la sección de planes del admin. Se renombra todo —espacios de nombres, paquetes, contenedores, base de datos— de `Mesto` a `Mesto Beach`. Mesto queda archivado como semilla genérica.
**Consecuencias:** Menos superficie que mantener y libertad para diseñar contra un único local. Se pierde la maquinaria SaaS ya construida (planes, facturación con IVA, ledger de doble partida): si más adelante se vende el vertical a otro chiringuito, habrá que recuperarla del repositorio archivado o rehacerla. El modelo multi-tenant **se conserva** en el esquema (todo cuelga de `tenant_id`, §8) porque no estorba y abre la puerta a un segundo local sin rediseñar los datos. El alta de local ya no ocurre por API: se siembra con `scripts/bootstrap-mestobeach.sql`.

### ADR-024: Despliegue híbrido — el servidor del local es la fuente de verdad *(agosto 2026)*
**Contexto:** El servicio ocurre en una playa, con ~200 hamacas repartidas sobre la arena y una conexión a internet que no se puede dar por buena. El TPV que usan hoy (Ágora) es fiable justamente porque corre en local. Una arquitectura de nube pura implica que **un corte de internet para la cocina**, lo que en plena temporada es inaceptable. Pero el pedido por QR del comensal ocurre desde **su propio móvil, con sus propios datos**: si el sistema viviera solo en la red del local, el cliente tendría que conectarse al WiFi del chiringuito antes de pedir.
**Decisión:** Topología híbrida con **el servidor del local como fuente de verdad** de sesiones, comandas y cobros. Las apps del personal (PDA, Runner, KDS, Caja, Host) hablan contra ese servidor por la red local. La **nube publica el menú del comensal** y le traslada los pedidos al servidor del local. Ante caída del enlace, el personal sigue operando y el menú **se degrada**: la carta se ve, pero el pedido invita a llamar al camarero.
**Consecuencias:** El servicio sobrevive a cortes de internet, que es el requisito duro (OBJ-06 de PRD-018). A cambio aparecen responsabilidades nuevas: hardware en el local, copias de seguridad, actualización remota y monitorización del enlace. Las apps del personal deben **encolar y reintentar** sus escrituras ante microcortes de WiFi. El menú necesita un modo degradado explícito, no un error genérico. La cobertura WiFi hasta la orilla es un **prerrequisito de instalación**, no un detalle: el servidor local resuelve la caída de internet, no la falta de señal.

### ADR-025: Ciclo de vida por ítem — el plato reemplaza al pedido como unidad de estado *(agosto 2026)*
**Contexto:** Hoy el estado vive en el **pedido** (`orders.status`): se confirma, se prepara y se entrega entero. La operación de Mesto Beach no encaja: en una misma comanda unos platos salen "ahora" y otros esperan la instrucción del comensal (ADR-026); los platos de una comanda se reparten entre **tres estaciones** distintas; y un runner entrega **platos sueltos** de varias comandas en una misma bandeja (ADR-027). Con el estado a nivel de pedido, ninguna de esas tres cosas se puede representar.
**Decisión:** Mover el ciclo de vida a **`order_items`**: `en espera → en preparación → listo → en entrega → entregado`, más `devuelto` y `anulado`. El estado del **pedido pasa a derivarse** de sus ítems, y la comanda se **finaliza sola** cuando el último ítem queda entregado y no quedan retenidos.
**Consecuencias:** Es el refactor estructural del que dependen el coursing, los runners y la coordinación entre estaciones — por eso va **antes** que las interfaces nuevas (fase F2 del roadmap). Toca el esquema de `order_items`, los eventos de SignalR, el contrato del tablero KDS (que hoy razona por pedido) y el cliente de API. El sondeo autoritativo de ADR-022 se conserva, pero su cursor y su poda pasan a razonar por ítem. Riesgo principal: el KDS y la caja son código vivo que asume "un pedido, un estado"; hay que migrarlos a la vez que el modelo.

### ADR-026: El marchado de platos es potestad exclusiva del PDA *(agosto 2026 — parcialmente superada por ADR-032)*
**Contexto:** El comensal elige, producto a producto, si lo quiere **"ahora mismo"** o **"luego de mi instrucción"**. Queda entonces por decidir quién dispara el paso de "en espera" a "en preparación". Dejarlo en manos del comensal parece natural —es su instrucción— pero rompe la política de servicio de Mesto Beach: el PDA se acerca, confirma que la mesa terminó el plato anterior y **entonces** marcha. Sin ese filtro, la cocina recibe picos desordenados y la mesa recibe platos cuando no toca.
**Decisión:** Solo un usuario con rol **PDA** (o Administración) puede marchar un ítem retenido. El comensal puede **solicitar** su siguiente plato desde el móvil; esa solicitud llega como **aviso** al PDA del sector y nunca marcha por sí sola.
**Consecuencias:** El ritmo del servicio queda en manos de quien ve la mesa. A cambio, el sistema depende de que el PDA atienda sus avisos: si no lo hace, el comensal percibe mestobeach. La solicitud del comensal reutiliza la mecánica de llamados ya existente (`call-waiter`), no un canal nuevo. La cola "en espera" del KDS es informativa: cocina ve lo que viene, pero no puede adelantarlo.
**Revisión — ADR-032 *(agosto 2026)*:** al construirlo, marchar dejó de encender los fuegos, y con eso cayó la razón para reservarlo al PDA: **cocina también marcha** (`StaffPdaKdsOrAdmin`). Lo que sí es exclusivo de la estación es la nueva **orden de preparación**. La invariante que importaba —**el comensal no marcha nada**— sigue en pie. La solicitud del comensal existe hoy como `POST /sessions/{id}/ask-eta` (ADR-035), no como `request-next`. Ver PRD-018 D-03.
**Revisión prevista (PRD-019, octubre 2026):** con el perfil de flujo `BAR` de ADR-037, en las barras de noche desaparece el «¡a preparar!»: marchar sigue siendo `HELD → QUEUED` (el plato entra en «Pedidos», como uno pedido «ahora»), y un solo gesto de la barra, «Listo» (`POST /orders/items/{itemId}/ready`), lo lleva al pase (`QUEUED → READY`). En `FULL_PASS` (Cocina y Parrilla) nada cambia. La invariante sigue en pie: **el comensal no marcha nada**.

### ADR-027: Entrega por viajes con reclamo atómico de platos *(agosto 2026)*
**Contexto:** El problema que dio origen al proyecto: un runner sale del pase con hasta 8 platos para 3 o 4 puestos distintos, entre ~200 hamacas numeradas, y **solo su memoria** dice qué va dónde. Además hay varios runners trabajando a la vez sobre la misma cola de platos listos, con riesgo de que dos se lleven el mismo plato o de que uno quede olvidado.
**Decisión:** Modelar la entrega como un **viaje**: el runner —adscrito a **una sola estación**— reclama los ítems `listos` que carga en su bandeja; el reclamo es **atómico** (transición a `en entrega` con control de concurrencia), de modo que esos platos desaparecen de la cola de los demás runners. Durante la ruta ve el **mapa del sector** con sus paradas y el detalle por puesto. Cierra con un único botón: los ítems marcados pasan a `entregado`; **desmarcar** uno lo devuelve a su estación de origen como `devuelto`.
**Consecuencias:** Ningún plato queda en un limbo sin dueño y ninguno se entrega dos veces. El coste es un estado intermedio (`en entrega`) que hay que poder **liberar**: si un runner abandona el turno con platos reclamados, alguien debe poder soltarlos (pendiente de definir en el SDD de Orders). La adscripción a una estación es una regla de negocio del local (ADR-029), no una limitación técnica. El motivo de la devolución **no se captura en la app**: el runner lo explica en persona en cocina; el sistema solo registra el estado.
**Revisión prevista (PRD-019, octubre 2026):** ADR-038 añade al manifiesto del runner el **nombre de quien pidió** (`customerName`) y la **prioridad** de la mesa (`priority`: los reservados primero) en cada parada, y la estación del runner pasa a **leerse de BD** en `JwtValidationMiddleware` (`IStaffStationResolver`, caché `staff_station:{tenantId}:{userId}` de 60 s) en vez de ir fija en el JWT: reasignarle solo la estación ya no invalida su sesión. El viaje y el reclamo atómico quedan intactos.

### ADR-028: Convivencia con el TPV del local — no emitimos facturas *(agosto 2026)*
**Contexto:** El local ya opera con **un TPV externo**. Entrar a facturar significaría asumir **Verifactu**: registros encadenados con huella SHA-256, código QR enlazado a la AEAT, imposibilidad de alterar o borrar ventas y una **declaración responsable firmada** por el productor del software, con la responsabilidad legal que conlleva. Nada de eso aporta valor al problema que vinimos a resolver.
**Decisión:** El TPV del cliente **conserva la caja y la facturación**. Mesto Beach cubre la capa de servicio —pedir, preparar, entregar, coordinar la sala— y traslada las comandas al TPV para que la cuenta salga por él. La emisión de facturas y el cumplimiento fiscal quedan **fuera de alcance**.
**Consecuencias:** La venta al cliente es mucho más fácil ("no tocamos tu facturación") y el riesgo legal se queda donde ya estaba. A cambio, dependemos de la **capacidad de integración del TPV**: si su licencia no incluye el módulo de terceros, hay que replantear (OQ-03 de PRD-018, bloqueante). El módulo `Payments` que heredamos sigue existiendo para el cobro en caja, pero **no es la fuente fiscal**. Si algún día el comensal paga desde la app, el ticket debe seguir emitiéndolo el TPV o habrá que entrar en Verifactu por un ADR propio.

### ADR-029: Coordinación entre estaciones por visibilidad cruzada, no por platos compuestos *(agosto 2026)*
**Contexto:** Una misma mesa pide carne de **Parrilla** y guarnición de **Cocina**. Si cada estación solo ve lo suyo, una parte del pedido espera enfriándose. La primera propuesta fue modelar **platos compuestos** —un plato con componentes en varias estaciones, una estación de ensamblaje y avisos temporizados entre ellas—, pero el negocio lo descartó: en Mesto Beach **cada plato pertenece a una sola estación**; lo que falta no es partir el plato, es que las estaciones **se vean entre sí**.
**Decisión:** Cada ítem se enruta a **una** estación. Cuando una comanda mezcla estaciones, el tablero de cada una muestra además los ítems ajenos **en gris y no accionables**, con su estado en vivo. La sincronización la hace el cocinero mirando, no un temporizador.
**Consecuencias:** El modelo de catálogo se mantiene simple —una estación por ítem, sin sub-preparaciones ni ensamblaje—, y el KDS gana una vista de solo lectura por comanda. Al caer los platos compuestos desaparece también el caso de uso del **tiempo estimado de preparación**, que se retira del alcance junto con la regla de aviso a los 35 minutos. La coordinación queda sujeta al criterio humano: el sistema informa, no orquesta.
**Revisión al construirlo *(agosto 2026)*:** el gris no sobrevivió al primer servicio: volvía invisibles justo los platos que hay que mirar. Los ítems ajenos se pintan hoy **con el color de su estación** y su estado en vivo, y siguen siendo **no accionables**. El resto de la decisión —una estación por ítem, sin platos compuestos, sin ETA— queda intacto. Ver PRD-018 D-01.
**Revisión prevista (PRD-019, octubre 2026):** ADR-038 introduce una **excepción de enrutado por mesa**: cuando el plato resuelve a una estación `BAR` o a ninguna, gana la barra configurada para la mesa en `table_layouts.station_id` del modo activo. «Una estación por ítem» y la visibilidad cruzada siguen en pie; un plato de `FULL_PASS` nunca se desvía.

### ADR-030: Mapa a escala real, puestos tipo hamaca y edición en vivo de la Host *(agosto 2026)*
**Contexto:** El editor de mapa heredado sitúa mesas sobre un lienzo abstracto con muros, sin relación con las medidas del local. Para que el runner se guíe por el plano en plena playa, lo que ve en pantalla tiene que **coincidir con lo que pisa**. Además, Playa no son mesas: son ~200 hamacas numeradas, que sería absurdo dar de alta una a una. Y la distribución cambia durante el servicio.
**Decisión:** El sector calibra una **escala real** (metros ↔ unidades de lienzo) y admite una **imagen de plano de fondo**. Se añade el tipo de puesto **hamaca**, con alta en lote (prefijo, rango y disposición). La **Host puede mover y activar/desactivar** puestos en vivo, con difusión inmediata a PDA, Runners y KDS; **crear, eliminar y renumerar** queda en Administración, porque el número está **impreso en el QR físico** de cada puesto.
**Consecuencias:** El plano se convierte en herramienta de trabajo y no en adorno. Hace falta material del cliente (medidas o plano) como prerrequisito, y una imagen de fondo por sector en el almacén de imágenes. La frontera Host/Administración protege la identidad puesto↔QR: es una restricción deliberada de producto, y hay que explicarla en la formación o se percibirá como una carencia.
**Revisión prevista (PRD-019, octubre 2026):** ADR-038 reemplaza la **posición única** de `tables.pos_x/pos_y` por **una fila por modo** en `table_layouts(table_id, mode, …)`: la edición en vivo de la Host escribe la fila del **modo activo**, y `floors.visible_in` permite cerrar la Playa de noche sin tocar sus puestos. La frontera Host/Administración y la identidad puesto↔QR no cambian.

### ADR-031: Moneda en euros y localización de España *(agosto 2026)*
**Contexto:** El sistema heredado está cableado a Colombia: los campos monetarios se llaman literalmente `AmountCop`, `PriceCop`, `ItemPriceSnapshotCop`, el formateo usa `es-CO` y el vocabulario es colombiano ("mesero", "celular"). Mesto Beach factura en euros, en España, y atiende a un público internacional.
**Decisión:** Generalizar la moneda a **euros en céntimos** en modelo, API, cliente de API y las tres apps, **antes** de construir las funcionalidades nuevas encima. El vocabulario de cara al comensal pasa a español de España ("camarero", "móvil", "la cuenta"). La carta admite **traducciones**, con español e inglés al lanzamiento, sugiriendo idioma por el país que el comensal declare al identificarse.
**Consecuencias:** Es un cambio transversal y mecánico, pero toca contratos de API y por tanto el cliente manual `@mestobeach/api-client`; hacerlo tarde multiplicaría el trabajo. Los nombres de campo con sufijo `Cop` deben renombrarse, no solo reinterpretarse, o el código mentirá. El multi-idioma obliga a decidir quién provee las traducciones de la carta (OQ-07 de PRD-018) y añade un eje al modelo de catálogo.
**Revisión prevista (PRD-019, octubre 2026):** el día de negocio pasa a cortarse a las **06:00** de `Europe/Madrid` (`VenueTime.BusinessDayStartHour`), para Analytics y Payments (PR11). Supuesto pendiente de confirmar: Night cierra antes de las 06:00 (PRD-019 OQ-1909).

### ADR-032: La cola del pase — marchar y dar la orden de preparación son dos decisiones *(agosto 2026)*
**Contexto:** ADR-026 dejó el marchado en manos exclusivas del PDA: un solo gesto llevaba el plato de "en espera" a los fuegos. En el servicio real de Mesto Beach ese gesto son **dos**: alguien decide que el plato ya puede prepararse (la mesa terminó lo anterior, el plato "ahora" entró), y el **encargado de cocina** decide cuándo se encienden los fuegos —el "¡a preparar!" del pase—. Con una sola transición, la cocina no controla su propio ritmo: recibe picos que no eligió y no tiene dónde ver lo que le viene encima.
**Decisión:** Se parte la transición en dos y se añade el estado **`Queued` ("en cola")** entre `Held` e `InPreparation`. **Marchar** (`HELD → QUEUED`) mete el plato en la cola de su estación y lo pueden hacer **cocina y PDA** (el pase adelanta lo que ve venir); la **orden de preparación** (`QUEUED → IN_PREPARATION`, `POST /orders/items/{id}/start-preparation`) es **solo de la estación**. Un plato pedido "ahora" **nace en `Queued`**, no en preparación: nada entra en fuegos sin que la cocina lo lance. El tablero pasa a **cuatro columnas de izquierda a derecha** (esperando orden → en cola → en preparación → en el pase) y cada transición **sella su hora** (`queued_at`, `preparation_started_at`, junto a los ya existentes `marched_at`, `ready_at`, `delivered_at`), de modo que el KDS muestra **cuánto lleva cada plato en cada paso** —también el de la otra estación, vía la visibilidad cruzada de ADR-029—.
**Consecuencias:** Reemplaza la potestad exclusiva del PDA de ADR-026 sin quitarle su papel: el PDA sigue marchando, pero marchar ya no enciende fuegos. El coste es un estado más en la máquina del plato —que atraviesan también los caminos heredados por pedido— y tres columnas nuevas en `order_items`. A cambio, la cocina dirige su ritmo, y los sellos por estado abren la puerta a métricas reales de servicio (espera en cola, tiempo de fuegos) sin instrumentación adicional.
**Revisión prevista (PRD-019, octubre 2026):** ADR-037 añade el perfil de flujo `BAR`, que **salta `IN_PREPARATION`**: `QUEUED → READY` en un gesto, sellando `preparation_started_at = ready_at` para que los sellos sigan siendo continuos. La cola del pase de cuatro columnas sigue siendo la regla en `FULL_PASS` (Cocina y Parrilla); el cambio de modo no la toca.

### ADR-033: Diálogos propios en vez de los nativos del navegador *(agosto 2026)*
**Contexto:** Las tres apps heredaron del prototipo los diálogos del navegador —`alert()`, `confirm()` y `prompt()`, 28 usos repartidos entre `staff`, `admin` y `menu`—. Los tres **bloquean el hilo**: con el KDS colgado de la pared, un `confirm` abierto congela el sondeo autoritativo de ADR-022 y el tablero se queda mudo hasta que alguien toca la pantalla. No admiten estilo alguno: el navegador dibuja su caja gris con el dominio arriba, en pantallas que el comensal ve de reojo y que deberían leerse como Mesto Beach. Y el `confirm` nativo **no distingue lo destructivo**: "Aceptar / Cancelar" se ve idéntico para "¿guardo el cambio?" que para "¿anulo el plato?". De propina, varios navegadores ofrecen "impedir que esta página abra más diálogos", que silencia los avisos sin que el equipo se entere.
**Decisión:** Cada app monta un **`DialogProvider`** propio con un hook **`useDialog()`** que expone tres primitivas: `toast` (aviso apilable, no bloqueante), `confirm` (modal sí/no con tono `danger` para lo destructivo) y `prompt` (modal con un campo de texto). Se resuelven por promesa, de modo que el código de llamada se lee como antes (`await dialog.confirm({...})`). La **API es idéntica en las tres apps**; la estética no: cada una pinta con su propia paleta (crema y oro en staff/admin, variables del tenant en el menú). Se eliminan los 28 usos nativos.
**Consecuencias:** Los avisos dejan de bloquear el hilo —el KDS sigue sondeando con un modal abierto— y una acción destructiva se ve distinta de una inocua. El coste son **tres copias del componente**, una por app (`packages/ui` sigue vacío): si la API cambia hay que tocar las tres, por eso el contrato está fijado en la cabecera del propio archivo. Además `confirm`/`prompt` dejan de ser síncronos: quien los llame tiene que ser `async`.

### ADR-034: Modo tablero — el estado propio manda y el fullscreen nativo es un extra *(agosto 2026)*
**Contexto:** Siete de las ocho pantallas del sistema no son de escritorio: el KDS cuelga de una pared, la caja y Recepción viven en un atril, PDA y Runner van en tablets compartidas que pasan de mano en mano durante el servicio. En todas ellas el cromo —barra lateral, pie, interfaz del navegador— se come el espacio que necesita el contenido y ofrece salidas a las que nadie debería ir en mitad de un turno. El fullscreen nativo resuelve solo la mitad del problema: el navegador **puede negarlo** (exige un gesto del usuario, y falla sin avisar en iframes o con ciertos permisos) y **no se puede reactivar solo** tras una recarga.
**Decisión:** Los ocho módulos —los 7 de `staff` (Cocina, Caja, Recepción, PDA, Runner, Salón, Historial) y el layout de `admin`— ofrecen un **modo tablero** que oculta el shell de la app y, además, pide el fullscreen nativo. **El estado que manda es el nuestro** (`kiosk`): ocultar el shell siempre funciona; el fullscreen es un extra que se intenta y, si falla, no bloquea nada. Se recuerda en `localStorage` con **claves separadas por app** (`kds_kiosk` / `admin_kiosk`), porque son pantallas y hábitos distintos. Mientras está activo hay una **pastilla flotante "Salir"**: si el navegador negó el fullscreen, `Esc` no dispararía nada y el usuario quedaría encerrado. Salir del fullscreen nativo (con `Esc` o con el gesto del navegador) sale también del modo tablero: es un gesto de "devuélveme la pantalla", no medio gesto.
**Consecuencias:** La pantalla de cocina recupera el ancho que necesitan las cuatro columnas de ADR-032 y una tablet compartida no invita a navegar a otra parte. A cambio: al recargar, el tablero vuelve a pantalla completa "de app" pero **sin** el fullscreen del navegador (que exige gesto), y hace falta un clic para recuperarlo; el estado se hidrata en un efecto para que servidor y cliente pinten lo mismo; y como el modo se recuerda, quien lo dejó activo se lo encuentra al volver —la pastilla "Salir" es la salida garantizada—.

### ADR-035: "¿Cuándo sale?" — la pregunta va a la sala, y el sistema no promete tiempos *(agosto 2026)*
**Contexto:** El comensal que espera quiere saber cuánto falta. Los dos caminos evidentes están cerrados. **Mandarle la pregunta a la cocina** —que es quien lo sabe— convertiría el tablero en un buzón y distraería a quien tiene las manos en los fuegos, justo lo contrario de lo que persigue ADR-032 al darle a la estación el control de su ritmo. Y **calcular un tiempo estimado** quedó fuera de alcance en ADR-029, cuando cayeron los platos compuestos: el sistema no tiene con qué estimar, y un ETA inventado que se incumple es peor que no dar ninguno.
**Decisión:** `POST /api/v1/sessions/{sessionId}/ask-eta` (`[AllowAnonymous]`, `201`) registra un llamado con motivo **`ETA`** —junto a los ya existentes `WAITER`, `PIN` y `BILL`— y emite **`ETA_REQUESTED`** por `SessionsHub` al grupo `waiter-{tenantId}`, el de la **sala** (hoy lo atiende `/salon`; la caja está en el mismo grupo). El KDS no está suscrito a ese hub: la pregunta **nunca llega a cocina**. Un `ETA` **no pisa** un `PIN` ni un `WAITER` pendientes —el aviso persistido conserva el motivo más urgente, y la respuesta devuelve el motivo realmente guardado, no un `ETA` fijo—. En "Mis pedidos", el menú **no estima**: muestra desde cuándo lleva cada plato en su estado ("En preparación · desde hace 8 min") leyendo los sellos por transición de ADR-032.
**Consecuencias:** El comensal recibe una respuesta humana —el camarero mira el estado y contesta en persona— en lugar de una cifra automática que el local no controla. A cambio, la calidad de la respuesta depende de que la sala atienda sus avisos, la misma dependencia que ya asumió ADR-026 con los llamados; por eso el aviso suena distinto (más grave y más bajo: es una pregunta, no una urgencia) y se comporta como un llamado más, que alguien tiene que dar por atendido. Los sellos por estado dan información **verdadera** sin instrumentación nueva; si algún día hay histórico para estimar de verdad, el hueco donde ponerlo ya existe y el canal no cambia.

### ADR-036: Modo del local (`VenueMode`) y transición cerrada *(diseñado — PRD-019, octubre 2026)*
**Contexto:** Playa Demo opera **dos negocios en el mismo local**: restaurante de día y fiesta de noche («Mesto Beach Night»). El cliente quiere que Administración cambie **todo el sistema** desde el panel —carta, estaciones, distribución, prioridad, PIN y estética—, de forma manual, para todo el local, con aviso y auditoría. Hoy no existe la noción de modo: cada módulo razona contra una única configuración y nada coordina un cambio simultáneo en Catalog, Orders y Sessions. Además, el menú del comensal es **anónimo** y no puede unirse a los grupos autenticados de SignalR, así que no hay forma de empujarle el cambio.
**Decisión:** Se introduce `VenueMode = RESTAURANT | NIGHT` (enum en `MestoBeach.Shared`). La **fuente de verdad** es `tenants.venue_mode` (+ `venue_mode_changed_at`), con copia en Redis `venue:{tenantId}:mode` (TTL 10 min; la rama idempotente del POST también la reescribe) que lee un **`IVenueModeProvider` único** (Scoped) consumido por Catalog, Orders y Sessions; si la clave falta, se re-siembra desde Postgres (`SET NX`, solo si el tenant existe); si Redis no responde, se lee Postgres sin re-sembrar. El proveedor nunca se inventa un modo: si fallan Redis y Postgres, los consumidores responden `503 VENUE_MODE_UNAVAILABLE`. El cambio es `POST /api/v1/venue/mode {mode}` (Admin), **idempotente**, con un **pre-flight que exige el local vacío**: sesiones `Open`, `order_items` no finalizados **de esas sesiones abiertas** o `delivery_trips` abiertos → **`409 VENUE_NOT_IDLE`** con `extensions{openSessions, activeItems, openTrips, orphanItems, tables[]}` (`orphanItems`: platos no finalizados de mesas ya cerradas; informativo, no bloquea); **no hay `force`**; `400 INVALID_MODE` si el valor no es del enum. `GET /api/v1/venue/preflight` (Admin) permite consultarlo antes de intentar. La transición corre en una **transacción** (tenant + fila de auditoría en `venue_mode_changes`, con el pre-flight guardado en `preflight jsonb`), después `SET` en Redis y `Publish(VenueModeChangedNotification)`: Orders la **difunde por `OrdersHub`** (los handlers viven en las capas Application de Orders y Catalog) al grupo `tenant:{t}` como `OrderEvent{type:"VENUE_MODE_CHANGED"}` y Catalog invalida su caché (defensivo). El menú, anónimo, **sondea** `GET /api/v1/venue/{tenantId}` (`no-store`; `{tenantId, mode, changedAt}`); `GET /api/v1/venue` (staff o KDS, política `StaffOrDeviceJwt`) añade, desde PR3, `stations[{id,name,flowProfile,isOn}]` y `GET /api/v1/venue/history?limit=` (Admin) lista los cambios. La **cuenta atrás de 5 s** es de la interfaz del admin: el backend no espera ni programa nada.
**Alternativas consideradas:** (1) **Flag solo en Redis** — se pierde al reiniciar o vaciar la caché y el local amanecería en un modo que nadie eligió; Postgres como verdad y Redis como copia cuesta una columna. (2) **Difundirlo por `SessionsHub`** para que el menú anónimo lo recibiera — descartado: ese hub pasa a `[Authorize]` en esta misma entrega (ADR-040) y un canal anónimo de difusión por tenant invita al abuso; un sondeo `no-store` cada 30 s basta porque nadie pide durante la transición. (3) **Cierre forzado** de las mesas abiertas al cambiar (`force`) — **descartado por el cliente**: prefiere vaciar el local a mano y que el sistema se lo impida mientras quede alguien.
**Consecuencias:** Un solo interruptor coordina los tres módulos y queda rastro de quién cambió qué, cuándo y en qué estado estaba el local (`venue_mode_changes`). El coste es que **cambiar de modo exige vaciar el local**: con una mesa abierta, el 409 devuelve la lista y Administración resuelve en persona (OBJ-01 de PRD-019: cambio en menos de 60 s sin pedidos perdidos). El menú detecta el cambio con hasta ~30 s de retraso (sondeo + `visibilitychange`). Aparece una **notificación MediatR entre módulos** (Identity → Orders / Catalog) que no existía: exige un test de integración en el primer PR (publicar → caché invalidada + difusión).

### ADR-037: Perfil de flujo y encendido por modo de las estaciones *(diseñado — PRD-019, octubre 2026)*
**Contexto:** De noche **Cocina y Parrilla se vacían y se apagan**, y se encienden **dos barras** con un tablero de dos estados: la copa no pasa por fuegos, así que las cuatro columnas de ADR-032 (esperando orden → en cola → en preparación → en el pase) sobran. Hoy una estación existe o no existe —no se puede apagar sin borrarla—, el backend **no permite `QUEUED → READY`**. El límite de estaciones del `PlanCatalog` heredado (Basic = 2) no afecta: Mesto Beach se siembra en `pro` (`scripts/bootstrap-mestobeach.sql`), con estaciones ilimitadas.
**Decisión:** `kds_stations` gana `is_active bool default true`, `active_in jsonb default '["RESTAURANT","NIGHT"]'` y `flow_profile varchar(16)` con valores `FULL_PASS | BAR` (default `FULL_PASS`). **«Encendida» se calcula** —`IsOnIn(mode) = is_active && active_in contiene mode`— y **el cambio de modo no muta estaciones**. Una estación apagada **rechaza nuevos ítems** (`409 STATION_CLOSED`) y, como el pre-flight de ADR-036 exige que no quede ninguna mesa abierta, solo puede llegar al cambio con platos de mesas ya cerradas, que se terminan en su tablero aunque esté apagada. Con `flow_profile = BAR` el tablero pasa a **dos columnas** (Pedidos → Listo) y se habilita **`QUEUED → READY`** (`OrderItem.ReadyFromQueue()`, actualización atómica al estilo del reclamo de viajes (UPDATE condicional y estado derivado en la misma transacción; mecánica en SDD-004)), **sellando `preparation_started_at = ready_at`** para que los sellos de ADR-032 sigan siendo continuos; en `FULL_PASS` esa transición sigue siendo `409`. Validaciones al editar la estación: `INVALID_FLOW_PROFILE`, `ACTIVE_IN_EMPTY`. No hace falta cambiar el plan: Mesto Beach está en `pro` (`MaxKdsStations = null`) y `PlanCatalog` queda intacto. Como `PlanCatalog.For` devuelve Basic ante un código desconocido, el bootstrap/seed comprueba que `plan_code ∈ {pro, enterprise}` y falla si no.
**Alternativas consideradas:** (1) **Borrar y recrear** las estaciones al cambiar de modo — pierde histórico y categorías asignadas, y rompe la adscripción de los runners (ADR-027). (2) **Un flag por modo** (`is_night_station`) — no distingue «existe» de «está encendida» y no escala a un tercer modo; `is_active` + `active_in[]` separa las dos cosas.
**Consecuencias:** Supera **parcialmente** ADR-032 y ADR-026: en `BAR` el «¡a preparar!» desaparece: marchar no cambia (`HELD → QUEUED`, el plato entra en «Pedidos») y un solo gesto, «Listo», lo pasa de la cola al pase (`QUEUED → READY`); en `FULL_PASS` nada cambia. El KDS necesita columnas por perfil (`kdsColumnsFor(profile)`) (en `BAR`: `QUEUED`, `IN_PREPARATION` y `RETURNED` en «Pedidos» con acción Listo; `READY` en «Listo»; `HELD` fuera hasta que lo marche la PDA/salón/Administración), un estado visible «Cerrada en Night» para las estaciones apagadas y una alerta de pedido nuevo que no dependa del paso por «en cola». El esquema supone OQ-1904 = sí: Barra 1 es la barra de Cocktails y trabaja como `BAR` en los dos modos. Con un `flow_profile` único por estación, `CATEGORY_CONFLICT` para todo el local (`KdsStationCommands`) y la regla (a) de `STATION_CLOSED`, el «no» no se puede configurar: si se separa Cocktails (`FULL_PASS`, [RESTAURANT]) de Barra 1 (`BAR`, [NIGHT]), las categorías de copas solo pueden ser de una de las dos, y de noche resolverían a la estación de día apagada (409). Si Mesto Beach responde «no», **antes de la migración `AddKdsStationProfileAndModes` (PR3)** el perfil pasa a ser por modo: `flow_profile jsonb default '{"RESTAURANT":"FULL_PASS","NIGHT":"FULL_PASS"}'` con `KdsStation.ProfileIn(mode)`; `/ready`, `kdsColumnsFor` y `GET /venue`/`/kds-stations` (`flowProfile`) lo resuelven con el modo activo, y la regla (b) mira `ProfileIn(mode) = BAR`. Barra 1 quedaría `{RESTAURANT: FULL_PASS, NIGHT: BAR}`. Se prefiere a relajar `CATEGORY_CONFLICT` por solapamiento de `active_in`, porque esa vía cambia además el desempate por `created_at` del enrutado y obliga a reasignar runners en cada cambio.

### ADR-038: Distribución, prioridad y enrutado por modo *(diseñado — PRD-019, octubre 2026)*
**Contexto:** De noche **la playa cierra**, la sala se redistribuye (reservados, mesas con sillas, mesas altas), cada mesa se atiende desde **una barra concreta** y las comandas de los reservados van **primero** en barra y runner. Hoy hay **una sola posición por mesa** (`tables.pos_x/pos_y`, ADR-030), el enrutado es «una estación por ítem» según su categoría (ADR-029), el manifiesto del runner **no trae el nombre** del comensal ni prioridad alguna (ADR-027), y la estación del runner va **fija en el JWT**: reasignarlo invalida su token y le cierra la sesión en mitad del servicio.
**Decisión:** Nueva tabla **`table_layouts(table_id, mode, floor_id, pos_x, pos_y, active, station_id)`** con unicidad `(table_id, mode)`: la distribución Night es **propia y persistente** sobre el mismo plano; la migración copia `tables.pos_*` a la fila `RESTAURANT` y la clona a `NIGHT`. Toda mesa nueva nace con sus dos filas: `POST /tables` y el alta en lote la dejan activa en el modo desde el que se crea e inactiva en el otro (misma posición, sin barra); la mesa ad-hoc que crea la caja, activa en los dos y sin ubicar. `PATCH /tables/{id}` deja de aceptar posición. `floors.visible_in jsonb` oculta la Playa de noche. `tables.kind varchar(16)` con valores `TABLE | HIGH_TABLE | RESERVADO` se **snapshotea** al crear la comanda en `orders.priority int` **solo para pedidos creados en `NIGHT`** (RESERVADO → 1; en `RESTAURANT` siempre 0, porque de día manda el orden de llegada; default 0), y las colas (`/orders/active`, `/delivery-queue`) ordenan por **`priority DESC`** y después por fecha (`created_at` / `ready_at`). **Enrutado:** la barra de la mesa (`table_layouts.station_id` del modo activo) gana **solo si** el plato resuelve a una estación `BAR` o a ninguna; un plato de `FULL_PASS` sigue yendo a su estación. Abrir o unirse a una mesa con `active = false` en el modo activo → `409 TABLE_CLOSED_IN_MODE`. La estación del runner deja de salir del token: `JwtValidationMiddleware`, en la rama `type == "staff"` y tras las comprobaciones de blacklist e invalidación global, la resuelve con el puerto `IStaffStationResolver` (Identity.Application; implementación en Identity.Infrastructure), que lee Redis `staff_station:{tenantId}:{userId}` (60 s, centinela `-`) y, si no está, `user_memberships.assigned_station_id` (`status = 'ACTIVE'`). Con ese valor, y no con la claim `stationId` del JWT, rellena `ICurrentUser.AssignedStationId` y `HttpContext.Items["station_id"]` (los vacía si sale el centinela). Reasignar **solo la estación** del runner ya no invalida su token (el handler hace `DEL staff_station:{tenantId}:{userId}`); cambiar el sector (`assignedFloorId`, sigue en el JWT) o los roles sí lo sigue invalidando, como en v2.0 (SDD-001 §7). Las paradas del viaje llevan `customerName` y `priority`; los avisos de sala llevan `tableKind` y, con el local en `NIGHT`, los de reservados se listan primero (`GET /sessions/calls`).
**Alternativas consideradas:** (1) **Dos conjuntos de mesas** (duplicar `tables` por modo) — rompe la identidad mesa↔QR impreso que protege ADR-030 y duplica el alta. (2) **Reparto entre barras por cercanía** calculado desde el plano (OQ-1903, provisional) — la asignación mesa→barra configurable es más predecible para el equipo y no depende de calibrar el plano (pendiente de PRD-018). (3) **Prioridad leída de la sesión** en vez de snapshot en la comanda — una mesa reclasificada a mitad de servicio reordenaría comandas ya en cola. (4) **Claim de estación renovable** (re-login del runner) — obliga al runner a cerrar sesión y volver a entrar en mitad del servicio; leerlo de BD con caché corta cuesta una consulta cada 60 s.
**Consecuencias:** Supera **parcialmente** ADR-030 (la posición deja de ser única: la edición en vivo de la Host escribe la fila del modo activo), ADR-029 (excepción de enrutado por mesa, acotada a `BAR` o nulo) y ADR-027 (el manifiesto gana nombre y prioridad; la estación del runner deja de leerse del token). Administración edita **dos distribuciones** (segmentado Restaurante | Night, borrador por modo, aviso al cambiar con pendientes). Los QR impresos de la Playa seguirán llevando gente al menú de noche: el 409 debe traducirse a un mensaje claro («esta zona está cerrada esta noche; acércate a Recepción»). Reasignar un runner tarda hasta 60 s en reflejarse. `tables.pos_x/pos_y` quedan como legado hasta que se retiren en una migración posterior.

### ADR-039: Identidad Night y tema por tokens *(diseñado — PRD-019, octubre 2026)*
**Contexto:** El cliente quiere el **mismo branding** con el apellido «Night», estética oscura en **las tres apps** y una transición visible al cambiar («agujero negro»). `staff` y `admin` **no tienen tema oscuro**: `brand-*` y `fuego-*` son hex fijos en `tailwind.config.ts`, y hay color embebido en `ScaledFloorMap`, `lib/kds.ts`, `salon.css` y una decena de componentes. La guía de marca (`docs/marca.md` §7, principio 2) exige Gilda Display siempre a peso 400 porque la fuente solo trae ese estilo y un bold falseado la arruina; una cursiva sintética la deformaría igual, y por eso PRD-019 fija que Gilda nunca se inclina (D-12, RF-1972).
**Decisión:** La marca Night es «◆ Mesto Beach» + **«Night» en itálica serif real** (`Cormorant Garamond` 400 *italic*, variable `--font-night`) en dorado `#AA8453`; **Gilda nunca se inclina**. El tema vive en **`<html data-venue="night">`**: `globals.css` redefine los **tokens** (`--canvas`, `--paper`, `--paper-2`, `--ink`, `--muted`, `--oro`…) y `brand-*` / `fuego-*` pasan a **variables** con la escala invertida en Night (500 = `#AA8453` fijo); Tailwind `darkMode: ["selector", '[data-venue="night"]']`. Un **script inline en `<head>`** fija `dataset.venue` antes del primer pintado (`suppressHydrationWarning`, `themeColor` por modo) para evitar el destello. `BrandMark` y `VenueTransition` se **copian por app** con el contrato fijado en la cabecera del archivo. La animación va en **tres fases** —colapso 420 ms (`clip-path` a `circle(0%)`, desenfoque y oscurecimiento), singularidad de al menos 600 ms (punto dorado; debajo cambia `data-venue` y se recargan datos, con tope de 4 s) y emersión 600 ms (el tema nuevo se abre con partículas doradas)— y se reduce a un **fundido de 200 ms** bajo `prefers-reduced-motion`. SVG estático `public/brand/mestobeach-night.svg` (texto en contornos) para impresos.
**Alternativas consideradas:** (1) **Cursiva sintética** (`font-style: italic` sobre Gilda) — **descartada**: contradice D-12 y el principio de la guía de no falsear estilos que Gilda no trae (§7, principio 2) y el navegador la fabrica deformando glifos. (2) **`prefers-color-scheme` del sistema** — el modo lo decide el local, no el dispositivo; una tablet en modo claro tiene que ponerse Night igual. (3) **Mover `BrandMark` / `VenueTransition` a `packages/ui`** — sigue vacío; adoptarlo ahora mezclaría un refactor del monorepo con la entrega.
**Consecuencias:** Toda la interfaz pasa a leer color por token, lo que de paso deja listo cualquier tema futuro. El coste son **tres copias** de `BrandMark` / `VenueTransition` (deuda explícita hacia `packages/ui`, la misma que asumió ADR-033) y una migración de hex en tres prioridades (P1 mapa, KDS y salón; P2 pastillas, banners y diálogos; P3 PDA y animaciones). La **OQ-1906** (púrpura `#5B4E96` como acento secundario) queda por validar con el cliente; se le ofrece la variante **«Eclipse»** como alternativa de animación.

### ADR-040: PIN de noche y dispositivo *(diseñado — PRD-019, octubre 2026)*
**Contexto:** De noche el PIN es **obligatorio** y la mesa se identifica **una vez por dispositivo** para toda la noche (nombre + PIN único por mesa). Hoy el PIN caduca en Redis a las **4 h** (`session:{t}:{s}:pin` y `pin_validated:*`), con lo que una noche se rompería a mitad; un PIN puede repetirse entre mesas abiertas; el invite link **concede el PIN** sin teclearlo (`inviteGrantsPin`); el pedido de un anónimo no exige nombre; y `SessionsHub` acepta conexiones anónimas.
**Decisión:** El PIN es **único entre sesiones abiertas** del local: al generarlo se reserva en el hash Redis `pins_in_use:{tenantId}` (campo = PIN, valor = `sessionId`, sin TTL) con `HSETNX` y reintentos ante colisión; se libera **por sesión** al cerrar o regenerar (sin leer la clave del PIN, que caduca) y se autorrepara si el PIN colisiona con una sesión que ya no está abierta. El TTL de `session:*:pin` y `pin_validated:*` sube a **12 h en todos los modos**; **Postgres es la verdad** (`session_participants.pin_validated`) y Redis solo caché: si la clave falta, `PinValidationService` consulta Postgres y **re-siembra** con TTL fijo de 12 h. En Night **no hay atajo de invitación** (`inviteGrantsPin = false` y la respuesta del join lleva `pinValidated: false`); **`pinRequired` se fuerza a `true` al abrir la sesión** sea cual sea el preset de QR de la mesa (que no se toca; la sesión lo congela igual que `venue_mode`); el menú pide el PIN **en el registro**, junto al nombre (revisión de ADR-014); y un pedido desde el menú (sin JWT de personal) cuyo participante no tiene `displayName` recibe **`422 NAME_REQUIRED`**, porque el runner entrega preguntando por el nombre; las comandas del personal (PDA, caja) quedan exentas. El menú **recuerda la visita** en `localStorage mestobeach_visit:{tenantId}:{tableCode}` (`sessionId, participantId, displayName, identified, pinValidated, savedAt`): si `joinByTable` devuelve la misma sesión y el mismo participante, se salta la identificación; `GET /sessions/{id}?deviceId=` rehidrata. `SessionsHub` pasa a `[Authorize]`. La sesión guarda `sessions.venue_mode` para los reportes por modo.
**Alternativas consideradas:** (1) **Cookie o token de dispositivo** emitido por el servidor — duplica la identidad que ya aporta `deviceId` y el límite real sería el mismo (otro navegador = otro dispositivo). (2) **Un PIN por noche para todo el local** — pierde la protección por mesa que justifica el PIN. (3) **TTL largo solo en Night** — complica la lógica y el fallo de las 4 h también afecta a cenas largas de día; 12 h cubre cualquier servicio.
**Consecuencias:** El PIN sobrevive la noche sin depender de la expiración de Redis y un dispositivo identificado no vuelve a pasar por el modal (OBJ-04 de PRD-019). Límite documentado: **otro navegador = otro dispositivo**, y borrar los datos del sitio obliga a identificarse de nuevo. El hash `pins_in_use` reduce el espacio de PIN disponible con muchas sesiones abiertas; a cuatro dígitos sigue sobrando para el aforo de Mesto Beach. Sin atajo de invitación, el anfitrión dicta el PIN en persona; Recepción debe poder **mostrarlo** al sentar (`GET /sessions/{id}/pin` ya existe) y, en su defecto, el runner o la PDA desde el aviso «Pide el PIN».

### ADR-041: Carta única con visibilidad por modo *(diseñado — PRD-019, octubre 2026)*
**Contexto:** De noche la carta es **distinta** (sin cocina, con copas y chupitos) pero comparte productos, modificadores, imágenes y estaciones con la de día. La caché `catalog:{t}` la consume también Orders para validar pedidos (Redis L1 + Mongo L2): un filtro por modo mal situado dejaría validar lo que no se ve, o vaciaría la caché a cada cambio.
**Decisión:** **Una sola carta** con `visibleIn[]` en ítem y en categoría (`[RESTAURANT, NIGHT]` por defecto; `400 VISIBLE_IN_EMPTY` si se intenta vaciar). La caché `catalog:{t}` se guarda **sin filtrar**; el filtro se aplica **al servir** (`GET /api/v1/catalog/{t}` devuelve solo lo visible en el modo activo y añade `venueMode`) y **al validar** (`CatalogValidationService` → `409 ITEM_NOT_IN_MODE`). El branding de noche es **`branding.night`**, un conjunto de **overrides** (null = hereda del día, salvo `backgroundColor`: de noche solo se aplica el fondo libre fijado en Night; si no, manda el tema `night`) que la API pública devuelve **ya resuelto** en el servidor; tema `night` en `THEMES`; `GET/PUT /branding/night` y subida de logo y portada propios. Se **reservan** los campos `nightPriceCents` y `allowedTableIds` (precios nocturnos y productos por mesa, fuera de alcance) para no migrar dos veces.
**Alternativas consideradas:** (1) **Segundo catálogo** por modo — **descartado**: duplica modificadores, imágenes, estaciones y cachés, y obliga a editar dos veces cada cambio compartido. (2) **Caché por modo** (`catalog:{t}:{mode}`) — duplica memoria e invalidación; filtrar al servir es barato y mantiene a Orders leyendo una sola clave. (3) **Reutilizar la disponibilidad** (`isAvailable`) por franja horaria — mezcla «agotado» con «no es de noche» y rompe el stock.
**Consecuencias:** Un solo lugar para mantener la carta y un solo pipeline de imágenes; el admin marca Día / Noche con chips por ítem y categoría, con el mismo patrón que el interruptor de disponibilidad. La caché **no necesita invalidarse** al cambiar de modo (se hace de forma defensiva). Si el menú detecta un modo distinto al de la carta cargada (caso defensivo: con la transición cerrada solo ocurre si una sesión se abrió durante el cambio), recarga la carta y quita del carrito lo que dejó de ser visible con un toast; la garantía real es `409 ITEM_NOT_IN_MODE`; «Repetir lo último» solo re-añade lo visible y disponible. Los campos reservados existen sin lógica: documentarlos evita que alguien los reinterprete (OQ-1905, fuera de alcance).

---

## 15. Estado de Implementación y Features Entregadas

Qué está construido hoy en el repo (`backend/` + `frontend/`) y qué está **diseñado pero pendiente**.
Lo pendiente se especifica en **PRD-018** y en los **ADR-024…035** (§15.4) y, para la segunda fase
**Mesto Beach Night**, en **PRD-019** y los **ADR-036…041** (§15.5).

### 15.1 Backend — 7 módulos (.NET 8)
- **Identity:** login de staff (JWT RS256, claims `tenantId/role/type`), Device Token para KDS, activación y reset de staff, cuentas de comensal con puntos.
- **Catalog:** carta en MongoDB (`mestobeach_catalog`), categorías, estaciones KDS, stock por ítem, **modificadores/adiciones** y **Apariencia/branding** (subdoc `MenuBranding`: nombre, eslogan, acento, tema, fuente, logo, portada); caché en Redis (`catalog:{tenantId}`, TTL 5 min). Imágenes en **Blob Storage** (Azurite en desarrollo), con tratamiento propio para portada (hasta 2048 px) y logo (sin pérdida, con transparencia).
- **Sessions:** mesas + **pisos/mapa**, **tipo de QR por mesa** (Fijo / Dinámico / Sin seguridad — ADR-018), sesiones, PIN, participantes, QR fijo `join-by-table`, invite link, llamados (`call-waiter`/`request-bill`/`request-pin`/**`ask-eta`**), historial, exige turno abierto. **`GET /floorplan` es de lectura para cualquier staff** (`StaffJwt`) porque lo consumen, además del editor del admin y del mapa de la caja, el **KDS** (para rotular el sector de cada mesa), **Recepción**, el **PDA** y el **Runner** — roles que no pasaban `StaffAdminOrCajero`; **editar** el plano sigue en `StaffAdmin`. *(actualizado)*
- **Orders:** pedidos con auto-confirmación, validación de catálogo (Redis L1 + Mongo L2), routing a estación, **tablero KDS autoritativo** (`GET /orders/active` con delta `since` + `activeIds` — ADR-022) y **ciclo de vida por plato** (ADR-025): `HELD → QUEUED → IN_PREPARATION → READY → IN_DELIVERY → DELIVERED` (+ `RETURNED`/`CANCELLED`), con el estado del pedido **derivado** de sus ítems y auto-finalización. *(actualizado)*
- **La cola del pase (ADR-032)** *(implementado)*: estado **`Queued`** entre `Held` e `InPreparation`; **marchar** (`POST /orders/items/{id}/march`, política `StaffPdaKdsOrAdmin` — cocina **y** PDA) y la **orden de preparación** (`POST /orders/items/{id}/start-preparation`, política `StaffKdsOrAdmin` — solo la estación) son dos decisiones distintas; un plato pedido "ahora" **nace en `Queued`**. Sellos por transición en `order_items` (`queued_at`, `preparation_started_at`, `preparation_ordered_by`), migración `AddItemQueuedStateAndStamps`.
- **Observación de la comanda** *(implementado)*: `orders.observacion` (`varchar(500)`, nullable; migración `AddOrderObservacion`), en `CreateOrderRequest`/`Command` y en los DTO de lectura (`OrderDetailDto`, `OrderSummaryDto`, `KdsOrderDto`, `DeliveryQueueItemDto`, `DeliveryTripStopItemDto`).
- **"¿Cuándo sale?" (ADR-035)** *(implementado)*: `POST /api/v1/sessions/{sessionId}/ask-eta` (`[AllowAnonymous]`, `201`), motivo `ETA` persistido en Redis junto a `WAITER`/`PIN`/`BILL` (no pisa un PIN ni un WAITER pendientes) y evento `ETA_REQUESTED` al grupo de sala; a cocina no llega.
- **Entrega por viajes (ADR-027)** *(implementado)*: `delivery_trips` + `GET /delivery-queue`, `POST /delivery-trips`, `/{id}/complete`, `/{id}/release`, `GET /delivery-trips/mine` (política `StaffRunnerOrAdmin`); índice único parcial `ux_delivery_trips_runner_open` = un viaje abierto por runner.
- **Payments:** cobro en efectivo + propina % + split + **turnos de caja** (abrir/cerrar + reporte), configuración de pago. **No es la fuente fiscal** (ADR-028).
- **Notifications:** correo con MailKit (MailHog en desarrollo).
- **Analytics:** proyecciones de venta y sesiones (solo lectura; sin migraciones propias).
- **Moneda y hora del local (ADR-031):** los importes son **enteros en céntimos de euro** — `PriceCents`, `AmountCents`, `TipCents`, `ItemPriceSnapshotCents`, `OpeningFloatCents`, `RevenueCents`…, columnas `*_cents`, migración `RenameMoneyToCents`. El **día de negocio se corta a medianoche de `Europe/Madrid`**: `VenueTime` (Payments y rangos de Analytics) y `AT TIME ZONE 'Europe/Madrid'` en las consultas de Analytics; no se resta un desfase fijo porque España tiene horario de verano. *(diseñado — PR 11)*: corte a las 06:00 vía `VenueTime.BusinessDayStartHour`, para Analytics y Payments.

### 15.2 Frontend — 3 apps (Next.js 14, pnpm + Turborepo)
| App | Puerto | Estado |
|---|---|---|
| `menu` (comensal) | 3000 | Carta en cuadrícula, carrito (+ **observación de la comanda**), personalización, PIN, pedir la cuenta, landing de QR fijo. **"Mis pedidos"** muestra desde cuándo lleva cada plato en su estado ("En preparación · desde hace 8 min") y el botón **"¿cuándo sale?"**; no se promete un ETA (ADR-035). **Identidad Mesto Beach**: portada con manto de arenilla, firma centrada, Gilda Display + Barlow, sin iconos (solo tipografía) |
| `staff` | 3001 | **7 módulos**: KDS `/board`, `/cashdesk`, `/salon`, `/pda`, `/runner`, `/host` (rotulado **Recepción**) e `/history`. El **KDS** tiene cuatro columnas (Esperando orden → En cola → En preparación → En el pase) con botones "Marchar" / "¡A preparar!" / "Listo", **modo foco** (la comanda al frente, el tablero difuminado, el recorrido de cada plato con el tiempo en cada estado), **densidad automática** por carga de columna (`comfy` ≤4 / `compact` ≤8 / `dense` ≤12 / `row` a partir de 13) y, cuando la columna pasa de 8 comandas, una franja al pie con el total y la espera de la más antigua; los platos de **otra estación** salen con el color de esa estación y su estado en vivo (ADR-029) |
| `admin` | 3003 | Catálogo (+ adiciones), estaciones KDS, personal, pagos, sesiones, analítica, **editor de sala** sobre el plano a escala (arrastrar, añadir de los mismos tipos, forma/plazas, eliminar; guardado en lote), Apariencia del menú |

Iconografía **Phosphor** (peso `light` en admin, `regular` en staff); cero emojis (docs/marca.md).
Las tres apps formatean los céntimos que devuelve la API con `Intl.NumberFormat("es-ES", { currency: "EUR" })`.

**Transversal a las tres apps** *(implementado)*:
- **Diálogos propios (ADR-033):** `DialogProvider` + `useDialog()` con `toast` / `confirm` / `prompt`, misma API y estética distinta por app. **Cero** `alert`/`confirm`/`prompt` nativos (28 usos retirados).
- **Modo tablero (ADR-034):** en los 7 módulos de `staff` y en el layout de `admin`; oculta el shell, pide el fullscreen nativo como extra, se recuerda en `localStorage` (`kds_kiosk` / `admin_kiosk`) y deja una pastilla flotante "Salir".
- **Plano a escala compartido:** `ScaledFloorMap` es el mismo render en admin, Caja, Recepción, PDA y Runner (1 unidad del modelo = 1 cm; la huella de cada puesto se deriva de `shape`+`seats`), con modo edición (`onMove`) en el editor de sala del admin.
- **Orden de los sectores:** Salón → Terraza → Playa en **todos** los selectores, por `floors.sort_order` (el seed los crea en ese orden).
- **Observación de la comanda** visible en KDS, PDA, caja, salón y runner.

### 15.3 Persistencia real
- **PostgreSQL `mestobeach`** (snake_case): `tenants, users, user_memberships, staff, tables, floors, sessions, session_participants, orders` (+`updated_at`, +`observacion`), `order_items` (ciclo del plato: `status, timing, station_id, delivery_trip_id` y los sellos `marched_at, marched_by, queued_at, preparation_started_at, preparation_ordered_by, ready_at, delivered_at`), `delivery_trips, payments, shifts, kds_stations, customer_accounts, audit_log, email_templates`. Cada módulo maneja sus migraciones EF.
- **MongoDB `mestobeach_catalog`:** carta por tenant (ítems, categorías, stock, branding).
- **Redis:** PIN, sesión activa por mesa, llamados, caché de catálogo, rate limiting.
- **Blob Storage:** imágenes de producto y de marca, procesadas a WebP.

### 15.4 Diseñado y pendiente de construir *(PRD-018)*

> **Ya no está aquí** *(actualizado)*: el **ciclo de vida por ítem** (ADR-025), la **cola del pase**
> (ADR-032), las **interfaces PDA / Runner / Recepción** con sus roles y la adscripción del runner a
> una estación (ADR-027), y la **visibilidad cruzada entre estaciones** en el KDS (ADR-029) están
> construidos — ver §15.1 y §15.2.

- **Coursing "ahora / luego" en el móvil del comensal** (ADR-026): el modelo y la API ya llevan `timing` (`NOW`/`LATER`) y el **PDA** deja elegir plato a plato, pero el `menu` todavía no ofrece la elección: lo que entra por el QR va como "ahora" (y por tanto nace `Queued`, ADR-032).
- **Mapa a escala: calibración e imagen de fondo por sector** (ADR-030). El plano a escala **ya es real y compartido** (`ScaledFloorMap`, huella derivada de `shape`+`seats`) y el **editor de sala del admin** está construido; falta la escala calibrada por sector (hoy la relación cm↔píxel vive en el componente), la imagen de plano de fondo, el tipo de puesto **hamaca** de primera clase con **alta en lote** (hoy el mapa las dibuja como hamacas por el sector Playa, pero en datos son un preset del editor —cuadrada de 2 plazas— que se añade de una en una) y la **edición en vivo desde Recepción** (mover / activar puestos), que sigue siendo cosa de Administración.
- **Carta bilingüe ES/EN** con sugerencia de idioma por el país declarado (ADR-031). El resto de ADR-031 **ya está implementado** — euros en céntimos y día de negocio en `Europe/Madrid` (§15.1, §15.2); queda además unificar el vocabulario ES-ES en las apps, hoy mezclado ("mesero" / "camarero").
- **Despliegue híbrido** y modo degradado del menú (ADR-024).
- **Integración con el TPV del local** (ADR-028) — bloqueada por OQ-03.

### 15.5 Mesto Beach Night *(PRD-019)*

> **Núcleo construido.** El administrador cambia **todo el local** de «Mesto Beach» a «Mesto
> Beach Night» desde el panel. El diseño vive en **ADR-036…041**; el esquema en §5, el evento en §6 y
> las claves Redis en §9.10. Están construidos los PRs 0 a 10 y la parte principal del 12; **quedan el
> 11 (reportes por modo) y el 13 (PWA)**.

**PRs** (rama base `integration`; cada PR con su entrada en `CHANGELOG.md`):

| PR | Rama | Contenido | Depende |
|---|---|---|---|
| 0 | `docs/prd-019-night` | PRD-019, ADR-036…041, SDD-001/002/003/004/006, README | — |
| 1 | `feat/venue-mode` | enum `VenueMode` + `IVenueModeProvider` (Scoped), `VenueController` (`GET /venue` sin `stations`), política `StaffOrDeviceJwt`, pre-flight (con `orphanItems`), `venue_mode_changes`, notificación y difusión `VENUE_MODE_CHANGED` (handlers en las capas Application de Orders y Catalog), api-client | 0 |
| 2 | `feat/catalog-por-modo` | `visibleIn`, `branding.night`, filtro al servir y branding resuelto, `ITEM_NOT_IN_MODE` | 1 |
| 3 | `feat/estaciones-perfil-barra` | `is_active` / `flow_profile` / `active_in`, `isOn`, `STATION_CLOSED`, `QUEUED→READY` en `BAR`, `stations` en `GET /venue` (read model de Identity ampliado con `is_active`, `flow_profile`, `active_in`, `created_at`) | 1 |
| 4 | `feat/distribucion-por-modo` | `table_layouts`, `floors.visible_in`, `tables.kind`, floorplan y layout por modo, `TABLE_CLOSED_IN_MODE`, enrutado por mesa, URL del QR, filas de `table_layouts` en toda alta de mesa (`POST /tables` con `mode`, lote, ad-hoc), `PATCH /tables/{id}` sin posición, `GET /tables` filtrado por modo, coordenadas de cola y manifiesto desde `table_layouts` | 1, 3 |
| 5a | `feat/prioridad-y-nombre-runner` | `orders.priority` (solo en NIGHT) / `orders.venue_mode`, orden de las colas, `customerName` en las paradas, `tableKind` en los avisos | 4 |
| 5b | `feat/pin-noche` | PIN único (`pins_in_use`, hash PIN→sessionId), PIN forzado en NIGHT y pedido en el registro, TTL 12 h, fallback a Postgres, sin atajo de invitación, `NAME_REQUIRED`, `sessions.venue_mode`, `SessionsHub` `[Authorize]`, `pin_validated` persistido también en `AddParticipant` | 1 |
| 5c | `feat/estacion-runner-desde-bd` | `IStaffStationResolver` + resolución en `JwtValidationMiddleware` + caché `staff_station:{tenantId}:{userId}`; `DEL` de la clave en assignment, roles y baja de staff; quitar `InvalidateAllAsync` solo cuando cambia solo la estación (en este mismo PR; hasta entonces se mantiene); `runner/page.tsx` no envía `stationId` si no es Admin y refresca la estación desde `/identity/auth/me` | — |
| 6 | `feat/tema-night-tokens` | tokens en las 3 apps, `brand-*` / `fuego-*` a variables, `darkMode` por selector, script anti-destello, `VenueProvider` ×3 (stub `localStorage` hasta PR1), `BrandMark` + fuente, hex P1 | — |
| 7 | `feat/admin-night` | interruptor + cuenta atrás de 5 s + pre-flight + historial, distribución segmentada Restaurante / Night, Apariencia Night, catálogo Día / Noche, estaciones | 1-4, 6 |
| 8 | `feat/staff-night` | KDS `BAR` de dos columnas + alertas + estación persistida, runner con nombre + `AvisosPanel`, PDA `JoinWaiterGroup`, pastillas «Reservado» | 3, 5a, 6 |
| 9 | `feat/menu-night` | sondeo de modo, branding resuelto, `visit.ts`, `pinValidated`, «Repetir lo último», mensajes de error, `viewport`, `slideUp` | 1, 2, 5b |
| 10 | `feat/animacion-agujero-negro` | `VenueTransition` ×3, partículas, `prefers-reduced-motion` | 6 |
| 11 | `feat/reportes-por-modo` | `?mode=` (RESTAURANT / NIGHT / ALL), día de negocio desde las 06:00, bug `CANCELLED`, selector en admin (`VenueTime.BusinessDayStartHour`, compartido con Payments: `GET /api/v1/payments` también corta a las 06:00) | 5a, 5b |
| 12a-c | `fix/responsive-{runner,pda,admin}` | responsividad: tap ≥ 44 px, `safe-area`, `100dvh`, bottom-sheets, drawers con Esc y foco | — |
| 13 | `feat/pwa-instalable` | manifiestos, iconos 192/512, `metadata.manifest` (sin service worker offline en esta fase) | 6 |

Paralelismo previsto: backend `1 → (2 ‖ 3 ‖ 5c) → 4 → (5a ‖ 5b) → 11`; frontend `(12a-c ‖ 6 con stub) → 10 → 7/8/9` según lleguen 1-5.

**Hitos** (orientativos, dos personas con dedicación parcial):

| Hito | Qué se puede enseñar | Cuándo |
|---|---|---|
| **H0 — demo visual** | Tema Night + `BrandMark` + animación funcionando con el **stub** (PR6 + PR10), sin backend; sirve para la aprobación del cliente | ~2 semanas desde PR0 aprobado |
| H1 — núcleo operativo | Cambio real con pre-flight, carta por modo, estaciones y barra de dos estados, distribución Night (PR1-4, 7 y 8 parciales) | ~5-6 semanas |
| H2 — noche completa | PIN por dispositivo, reservados primero, runner con nombre y avisos, menú Night, reportes (PR5, 9, 11) | ~7-9 semanas |
| H3 — responsividad y PWA | PR12-13, en paralelo desde el inicio | continuo |

**Fuera de esta entrega** (se reservan los nombres de campo para no migrar dos veces): productos por mesa (`allowedTableIds`), precios nocturnos (`nightPriceCents`), consumo mínimo, reservas y pago móvil.

### 15.6 Retirado en la separación del producto *(ADR-023)*
Módulos `Platform`, `Ledger` y `Loyalty`; app `platform` (consola de Ops); planes, facturación SaaS y enforcement de límites. Viven en el repositorio archivado de Mesto por si el vertical se vende a otro local.

---

*Documento de arquitectura de Mesto Beach. Nació como la arquitectura de Mesto y se mantiene contra el código real.*
*Versión 4.0 — Agosto 2026 — Separación del producto (ADR-023), retirada de la maquinaria SaaS, despliegue híbrido (ADR-024) y el diseño de la operación de Mesto Beach: ciclo de vida por ítem, coursing, runners, host, mapa a escala y localización (ADR-025…031).*
*Versión 4.1 — Agosto 2026 — La operación construida: cola del pase y sellos por transición (ADR-032), diálogos propios (ADR-033), modo tablero (ADR-034) y "¿cuándo sale?" (ADR-035); esquema del plato y la entrega en §5, `ETA_REQUESTED` en §6 y §15 al día con el código. Se anotan las **revisiones** de ADR-026 (marchar deja de ser exclusivo del PDA) y ADR-029 (los platos ajenos ya no van en gris), y §3.2 pasa de tres a siete módulos de `staff`.*
*Versión 4.2 — Octubre 2026 — Diseño de **Mesto Beach Night** (PRD-019), nada de ello implementado: modo del local y transición cerrada (ADR-036), perfil de flujo y encendido por modo de las estaciones (ADR-037), distribución, prioridad y enrutado por modo (ADR-038), identidad Night y tema por tokens (ADR-039), PIN de noche y dispositivo (ADR-040) y carta única con visibilidad por modo (ADR-041); esquema previsto en §5, `VENUE_MODE_CHANGED` en §6, claves Redis en §9.10 y §15.5 con los PR e hitos. Se anotan las **revisiones previstas** de ADR-026, 027, 029, 030 y 032.*
