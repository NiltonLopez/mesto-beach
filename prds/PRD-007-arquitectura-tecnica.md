# PRD-007 — Arquitectura Técnica

> **📜 Documento heredado de Mesto.** Se escribió para la plataforma SaaS multi-tenant de la que
> Mesto Beach se separó en agosto de 2026 (**ADR-023**), y su contenido sigue vigente salvo en
> tres puntos que **manda la documentación nueva**:
> - **Moneda**: los importes de este documento están en pesos colombianos. Hoy todo entero monetario
>   son **céntimos de euro** (**ADR-031**); los nombres de campo llevan el sufijo `Cents`.
> - **Facturación**: la emite el TPV del local, no nosotros (**ADR-028**). Ignora aquí cualquier
>   referencia a pasarelas o autoridades fiscales colombianas.
> - **Ejemplos**: los platos, precios y direcciones son de la época de Mesto y no reflejan la carta
>   ni el local de Playa Demo.
>
> Lo específico de este cliente está en **PRD-018** y en los **ADR-023…035**.

## Metadata

| Campo | Valor |
|---|---|
| **ID** | PRD-007 |
| **Versión** | 2.0 |
| **Estado** | Aprobado — v2.0 |
| **Fecha** | Junio 2026 |
| **Audiencia** | Backend devs, Frontend devs, DevOps |
| **Depende de** | Todos los PRDs anteriores (este los implementa) |
| **PRDs que dependen de este** | Ninguno — es la guía de implementación |

> Este documento es el contrato técnico del proyecto. Cualquier IA o desarrollador que quiera contribuir al código de Mesto debe leer este PRD junto con `ARCHITECTURE.md`.

---

## 1. Propósito y Objetivos

### Propósito

Especificar las decisiones técnicas concretas que guían la construcción de Mesto: estructura de APIs, contratos de datos, estrategia de autenticación, manejo de errores, convenciones de código, y el pipeline de CI/CD. Este documento elimina ambigüedad técnica y permite que el equipo trabaje en paralelo con coherencia.

### Objetivos

| # | Objetivo |
|---|---|
| OBJ-01 | Que cualquier desarrollador nuevo pueda entender el sistema y contribuir en menos de 1 día |
| OBJ-02 | Que la IA (asistentes de código) pueda generar código coherente con la arquitectura leyendo este documento |
| OBJ-03 | Eliminar todas las decisiones técnicas ad-hoc — cada práctica está documentada aquí |

---

## 2. Repositorios y Estructura

### 2.1 Dos repositorios separados

```
mesto-web/    → Turborepo monorepo — todos los frontends Next.js
mesto-api/    → .NET solution — backend modular monolith
```

Los tipos TypeScript del frontend se generan automáticamente desde el OpenAPI spec del backend:
```
mesto-api → openapi.json → (openapi-typescript) → mesto-web/packages/types/generated/
```

### 2.2 `mesto-web` — Estructura Turborepo

```
mesto-web/
├── apps/
│   ├── menu/                    → PWA del cliente final
│   ├── staff/                    → KDS + cajero
│   ├── admin/                   → Dashboard del dueño
│   └── platform/                → Panel interno Mesto
├── packages/
│   ├── ui/                      → Design system compartido
│   │   ├── components/
│   │   │   ├── Button/
│   │   │   │   ├── Button.tsx
│   │   │   │   ├── Button.test.tsx
│   │   │   │   └── index.ts
│   │   │   ├── OrderCard/
│   │   │   ├── ProductCard/
│   │   │   └── ...
│   │   ├── tokens/              → Colores, tipografía, espaciado
│   │   └── package.json
│   ├── types/
│   │   ├── generated/           → AUTO-GENERADO — no editar manualmente
│   │   │   ├── orders.ts
│   │   │   ├── catalog.ts
│   │   │   ├── payments.ts
│   │   │   └── index.ts
│   │   └── package.json
│   └── utils/
│       ├── currency.ts          → formatCOP(18000) → "$18.000"
│       ├── dates.ts
│       ├── signalr.ts           → Factory de conexiones SignalR
│       └── package.json
├── turbo.json
└── package.json
```

### 2.3 `mesto-api` — Estructura .NET

```
mesto-api/
├── src/
│   ├── Mesto.API/
│   │   ├── Program.cs
│   │   ├── Middleware/
│   │   │   ├── TenantMiddleware.cs
│   │   │   ├── CorrelationIdMiddleware.cs
│   │   │   └── ErrorHandlingMiddleware.cs
│   │   ├── appsettings.json           → Solo estructura, sin secretos
│   │   └── appsettings.Development.json
│   ├── Modules/
│   │   ├── Identity/
│   │   │   ├── Domain/
│   │   │   │   ├── Entities/
│   │   │   │   │   ├── Tenant.cs
│   │   │   │   │   └── User.cs
│   │   │   │   ├── Enums/
│   │   │   │   │   └── UserRole.cs
│   │   │   │   └── Interfaces/
│   │   │   │       ├── ITenantRepository.cs
│   │   │   │       └── IUserRepository.cs
│   │   │   ├── Application/
│   │   │   │   ├── UseCases/
│   │   │   │   │   ├── LoginStaff/
│   │   │   │   │   │   ├── LoginStaffCommand.cs
│   │   │   │   │   │   └── LoginStaffHandler.cs
│   │   │   │   │   └── CreateSessionToken/
│   │   │   │   ├── DTOs/
│   │   │   │   └── Interfaces/
│   │   │   │       └── ITokenService.cs
│   │   │   ├── Infrastructure/
│   │   │   │   ├── Repositories/
│   │   │   │   ├── Services/
│   │   │   │   │   └── JwtTokenService.cs
│   │   │   │   └── IdentityModule.cs  → Registro de DI del módulo
│   │   │   └── Presentation/
│   │   │       └── Controllers/
│   │   │           └── AuthController.cs
│   │   ├── Catalog/
│   │   ├── Orders/
│   │   │   └── Infrastructure/
│   │   │       └── SignalR/
│   │   │           ├── OrdersHub.cs
│   │   │           └── HubGroups.cs
│   │   ├── Payments/
│   │   ├── Analytics/
│   │   └── Inventory/
│   └── Shared/
│       ├── Domain/
│       │   ├── Entity.cs              → Base con Id, TenantId, CreatedAt
│       │   ├── AuditableEntity.cs     → + UpdatedAt, UpdatedBy
│       │   ├── IDomainEvent.cs
│       │   └── Result.cs
│       ├── Infrastructure/
│       │   ├── Persistence/
│       │   │   ├── MestoDbContext.cs  → PostgreSQL (EF Core)
│       │   │   ├── MongoContext.cs    → MongoDB
│       │   │   └── Migrations/
│       │   ├── Cache/
│       │   │   └── RedisService.cs
│       │   └── Secrets/
│       │       └── AzureKeyVaultLoader.cs
│       └── Kernel/
│           ├── DomainEventBus.cs
│           ├── TenantContext.cs
│           └── PaginatedResult.cs
└── tests/
    ├── Mesto.UnitTests/
    └── Mesto.IntegrationTests/
```

---

## 3. Convenciones de API

### 3.1 Diseño REST

Todas las APIs siguen convenciones REST estrictas:

```
GET    /api/v1/{resource}           → Listar (con paginación)
GET    /api/v1/{resource}/{id}      → Obtener uno
POST   /api/v1/{resource}           → Crear
PUT    /api/v1/{resource}/{id}      → Reemplazar completo
PATCH  /api/v1/{resource}/{id}      → Actualización parcial
DELETE /api/v1/{resource}/{id}      → Eliminar (soft delete)
```

**Reglas:**
- Los recursos son siempre en plural: `/orders`, `/products`, `/sessions`
- Las acciones que no son CRUD usan sub-recursos: `PATCH /orders/{id}/step` no `POST /orders/change-step`
- Los filtros van como query params: `GET /orders?status=New&stationId=cocina`
- Nunca se usa GET para operaciones que modifican estado

### 3.2 Versionado

El API se versiona desde el inicio: `/api/v1/...`

Regla: **nunca hacer breaking changes en una versión existente**. Si se necesita un breaking change, se crea `v2` y se da un período de deprecación de 3 meses.

### 3.3 Estructura de respuestas

**Respuesta exitosa (200/201):**
```json
{
  "data": { ... },
  "meta": {
    "page": 1,
    "pageSize": 20,
    "total": 143
  }
}
```

**Respuesta de error:**
```json
{
  "error": {
    "code": "ORDER_NOT_FOUND",
    "message": "El pedido con ID xyz no existe",
    "correlationId": "abc-123-def"
  }
}
```

**Códigos de error HTTP usados:**
| Código | Cuándo se usa |
|---|---|
| 200 | OK — operación exitosa |
| 201 | Created — recurso creado |
| 204 | No Content — operación exitosa sin cuerpo de respuesta |
| 400 | Bad Request — datos inválidos en el request |
| 401 | Unauthorized — sin token o token inválido |
| 403 | Forbidden — token válido pero sin permisos |
| 404 | Not Found — recurso no existe (en el contexto del tenant) |
| 409 | Conflict — estado inválido (ej: sesión ya cerrada) |
| 422 | Unprocessable Entity — regla de negocio violada |
| 429 | Too Many Requests — rate limit excedido |
| 500 | Internal Server Error — error no controlado |

### 3.4 Paginación

Todos los endpoints de listado soportan paginación:

```
GET /api/v1/orders?page=1&pageSize=20&sortBy=createdAt&sortDir=desc
```

El `pageSize` máximo es 100. Por defecto es 20.

### 3.5 Endpoints del API — Referencia completa

#### Identity Module
```
POST   /api/v1/auth/staff/login              → JWT para staff
POST   /api/v1/auth/client/session           → Session token para cliente (requiere tableCode activo)
POST   /api/v1/auth/staff/refresh            → Refrescar JWT
POST   /api/v1/auth/staff/logout             → Revocar token
GET    /api/v1/tenants                       → [PlatformAdmin] Listar tenants
POST   /api/v1/tenants                       → [PlatformAdmin] Crear tenant
GET    /api/v1/tenants/{id}                  → [PlatformAdmin | RestaurantAdmin] Ver tenant
PATCH  /api/v1/tenants/{id}                  → [PlatformAdmin] Actualizar tenant
GET    /api/v1/staff                         → [RestaurantAdmin] Listar staff del tenant
POST   /api/v1/staff                         → [RestaurantAdmin] Crear usuario de staff
PATCH  /api/v1/staff/{id}                    → [RestaurantAdmin] Actualizar usuario
PATCH  /api/v1/staff/{id}/deactivate         → [RestaurantAdmin] Desactivar usuario
```

#### Catalog Module
```
GET    /api/v1/catalog/menu                  → [ClientSessionToken] Menú filtrado por horario
GET    /api/v1/catalog/branding              → [Public] Branding del restaurante (para PWA manifest dinámico)
GET    /api/v1/catalog/products              → [RestaurantAdmin] Todos los productos
POST   /api/v1/catalog/products              → [RestaurantAdmin] Crear producto
PUT    /api/v1/catalog/products/{id}         → [RestaurantAdmin] Actualizar producto completo
PATCH  /api/v1/catalog/products/{id}/availability → [RestaurantAdmin|Cashier] Cambiar disponibilidad
DELETE /api/v1/catalog/products/{id}         → [RestaurantAdmin] Soft delete
GET    /api/v1/catalog/categories            → [RestaurantAdmin] Categorías
POST   /api/v1/catalog/categories            → [RestaurantAdmin] Crear categoría
PUT    /api/v1/catalog/categories/reorder    → [RestaurantAdmin] Reordenar categorías
DELETE /api/v1/catalog/categories/{id}       → [RestaurantAdmin] Eliminar categoría
GET    /api/v1/catalog/stations              → [RestaurantAdmin] Estaciones
POST   /api/v1/catalog/stations              → [RestaurantAdmin] Crear estación
GET    /api/v1/catalog/schedules             → [RestaurantAdmin] Horarios del menú
POST   /api/v1/catalog/schedules             → [RestaurantAdmin] Crear horario
PUT    /api/v1/catalog/branding              → [RestaurantAdmin] Actualizar branding
```

#### Orders Module
```
GET    /api/v1/delivery-places               → [Cashier|RestaurantAdmin] Mesas/lugares de entrega
POST   /api/v1/delivery-places               → [RestaurantAdmin] Crear mesa
PATCH  /api/v1/delivery-places/{id}          → [RestaurantAdmin] Editar mesa
GET    /api/v1/sessions                      → [Cashier|RestaurantAdmin] Sesiones activas
POST   /api/v1/sessions                      → [Cashier] Abrir sesión de mesa
GET    /api/v1/sessions/{id}                 → [Cashier|ClientSessionToken] Ver sesión
PATCH  /api/v1/sessions/{id}/close           → [Cashier] Cerrar sesión
GET    /api/v1/sessions/{id}/orders          → [Cashier|ClientSessionToken] Pedidos de la sesión
GET    /api/v1/sessions/{id}/summary         → [Cashier|ClientSessionToken] Resumen de cuenta
POST   /api/v1/sessions/{id}/orders          → [ClientSessionToken] Nuevo pedido
GET    /api/v1/orders/station/{stationId}    → [Kitchen|Bar] Pedidos activos de la estación
PATCH  /api/v1/orders/{id}/step              → [Kitchen|Bar] Cambiar paso (Preparing, Ready)
PATCH  /api/v1/orders/{id}/status            → [Waiter] Marcar como entregado
DELETE /api/v1/orders/{id}                   → [Cashier] Cancelar pedido (solo en estado New)
POST   /api/v1/waiter-calls                  → [ClientSessionToken] Llamar al mesero
PATCH  /api/v1/waiter-calls/{id}/dismiss     → [Waiter] Descartar llamado
```

#### Payments Module
```
POST   /api/v1/payments/digital/initiate     → [ClientSessionToken] Iniciar pago digital
POST   /api/v1/payments/cash                 → [Cashier] Registrar pago en efectivo
GET    /api/v1/payments/sessions/{id}        → [Cashier|RestaurantAdmin] Pagos de una sesión
GET    /api/v1/payments                      → [RestaurantAdmin] Historial de pagos (paginado)
POST   /api/v1/payments/webhooks/wompi       → [Public — validado con firma] Webhook Wompi
POST   /api/v1/payments/webhooks/mercadopago → [Public — validado con firma] Webhook MercadoPago
```

#### Analytics Module
```
GET    /api/v1/analytics/dashboard           → [RestaurantAdmin] Métricas del dashboard principal
GET    /api/v1/analytics/products/ranking    → [RestaurantAdmin] Ranking de productos
GET    /api/v1/analytics/sales/by-category   → [RestaurantAdmin] Ventas por categoría
GET    /api/v1/analytics/sales/by-hour       → [RestaurantAdmin] Mapa de calor horario
GET    /api/v1/analytics/preparation-times   → [RestaurantAdmin] Tiempos de preparación por estación
```

#### Inventory Module
```
GET    /api/v1/inventory/stock               → [RestaurantAdmin] Stock de todos los productos
PATCH  /api/v1/inventory/stock/{productId}   → [RestaurantAdmin] Ajustar stock manualmente
PUT    /api/v1/inventory/costs/{productId}   → [RestaurantAdmin] Registrar costo de producción
GET    /api/v1/inventory/alerts              → [RestaurantAdmin] Productos con stock bajo
GET    /api/v1/inventory/stock/history       → [RestaurantAdmin] Historial de ajustes de stock
```

---

## 4. Autenticación — Implementación

### 4.1 JWT para Staff

```csharp
// Claims en el JWT del staff
{
  "sub": "user-uuid",
  "tenantId": "tenant-uuid",
  "role": "Kitchen",          // PlatformAdmin | RestaurantAdmin | Cashier | Kitchen | Bar | Waiter
  "email": "pedro@laparrilla.com",
  "exp": 1234567890,
  "jti": "unique-token-id"
}
```

- Firmado con `RS256` (clave privada en Azure Key Vault)
- Expiración: 8 horas (un turno de trabajo)
- Refresh token: 7 días, rotación en cada uso

### 4.2 Session Token para el Cliente

```csharp
// Claims en el Session Token del cliente
{
  "sub": "session-uuid",
  "tenantId": "tenant-uuid",
  "tableCode": "mesa-5",
  "customerName": "Ana",       // Nombre ingresado por el cliente
  "type": "client_session",    // Distingue del JWT del staff
  "exp": 1234567890,           // +4h desde emisión
  "jti": "unique-token-id"
}
```

- Firmado con la misma clave que el JWT del staff
- Expiración: 4 horas desde emisión
- **Invalidación inmediata:** Al cerrar la sesión, el `jti` se agrega a la blacklist en Redis

### 4.3 Middleware de Tenant

```csharp
// TenantMiddleware.cs — se ejecuta en cada request
public class TenantMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        var tenantId = context.User.Claims
            .FirstOrDefault(c => c.Type == "tenantId")?.Value;

        if (tenantId == null)
        {
            context.Response.StatusCode = 401;
            return;
        }

        // Inyectar el TenantContext en el DI scope de la request
        var tenantContext = context.RequestServices.GetService<TenantContext>();
        tenantContext.TenantId = Guid.Parse(tenantId);

        await _next(context);
    }
}
```

### 4.4 Validación de blacklist en Redis

```csharp
// En el JWT validation middleware, después de validar la firma:
var jti = principal.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
var isBlacklisted = await _redis.SetContainsAsync("jwt:blacklist", jti);
if (isBlacklisted)
{
    context.Response.StatusCode = 401;
    return;
}
```

---

## 5. Manejo de Errores

### 5.1 Result Pattern (no excepciones como control de flujo)

```csharp
// Result.cs en Shared/Domain
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(Error error) => new(false, default, error);
}

public record Error(string Code, string Message);

// Errores conocidos del dominio
public static class OrderErrors
{
    public static Error SessionClosed = new("SESSION_CLOSED", "La sesión de mesa ya fue cerrada");
    public static Error ProductNotAvailable = new("PRODUCT_NOT_AVAILABLE", "El producto no está disponible");
    public static Error OrderAlreadyPreparing = new("ORDER_ALREADY_PREPARING", "El pedido ya está en preparación y no puede cancelarse");
}
```

### 5.2 Global Error Handler

```csharp
// ErrorHandlingMiddleware.cs
// Captura todas las excepciones no controladas y las convierte en respuestas JSON
// con el formato estándar de error definido en la sección 3.3
```

### 5.3 Validaciones de request

- Se usa `FluentValidation` para validar todos los request bodies
- Las validaciones se ejecutan antes de llegar al handler del use case
- Los errores de validación retornan 400 con el detalle de qué campos fallaron

---

## 6. Tiempo Real — SignalR

### 6.1 Hub único con grupos

```csharp
// OrdersHub.cs
public class OrdersHub : Hub
{
    // Al conectar, el cliente se une al grupo de su estación/rol
    public async Task JoinGroup(string groupName)
    {
        // Validar que el JWT permite unirse a este grupo
        // (un cliente de tenantA no puede unirse a kitchen-tenantB)
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }
}

// HubGroups.cs — Constantes de nombres de grupos
public static class HubGroups
{
    public static string Kitchen(Guid tenantId, string stationId) =>
        $"kitchen-{tenantId}-{stationId}";

    public static string Bar(Guid tenantId) =>
        $"bar-{tenantId}";

    public static string Waiter(Guid tenantId) =>
        $"waiter-{tenantId}";

    public static string Client(Guid sessionId) =>
        $"client-{sessionId}";
}
```

### 6.2 Eventos que emite el servidor

```csharp
// Tipado fuerte de los eventos — tanto el backend como el frontend generan tipos desde aquí
public interface IOrdersHubClient
{
    Task OrderNew(OrderDto order);
    Task OrderStepChanged(Guid orderId, OrderStep newStep);
    Task OrderStatusChanged(Guid orderId, OrderStatus newStatus);
    Task OrderReady(Guid orderId, string tableCode, string productName);
    Task WaiterCalled(string tableCode, string reason);
    Task SessionBillUpdated(Guid sessionId, decimal newTotal);
}
```

### 6.3 Reconexión en el frontend

```typescript
// packages/utils/signalr.ts
export function createHubConnection(url: string, token: string) {
  return new HubConnectionBuilder()
    .withUrl(url, { accessTokenFactory: () => token })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000]) // backoff en ms
    .build();
}

// En cada componente que usa SignalR:
connection.onreconnected(async () => {
  // SIEMPRE re-fetch del estado completo al reconectar
  const currentState = await fetchCurrentState();
  setState(currentState);
});
```

---

## 7. Base de Datos

### 7.1 Convenciones PostgreSQL (EF Core)

- Nombres de tabla: `snake_case` en plural (`table_sessions`, `order_items`)
- PKs: UUID v4 generados en el backend (no secuenciales para no exponer volumen)
- FKs: `{tabla_referenciada_singular}_id` (ej: `session_id`, `tenant_id`)
- Timestamps: siempre en UTC, tipo `timestamptz`
- Soft deletes: columna `deleted_at timestamptz NULL` — los registros eliminados tienen este campo no nulo
- `tenant_id` es obligatorio en todas las tablas excepto `tenants`

### 7.2 Índices obligatorios

```sql
-- Todos los queries de negocio filtran por tenant_id primero
CREATE INDEX idx_orders_tenant_created ON orders(tenant_id, created_at DESC);
CREATE INDEX idx_orders_session ON orders(session_id);
CREATE INDEX idx_orders_station_active ON orders(tenant_id, station_id) WHERE status != 'Delivered' AND deleted_at IS NULL;
CREATE INDEX idx_sessions_tenant_status ON table_sessions(tenant_id, status);
CREATE INDEX idx_sessions_delivery_place ON table_sessions(delivery_place_id, status);
CREATE INDEX idx_payments_session ON payments(session_id);
CREATE INDEX idx_payments_tenant_paid ON payments(tenant_id, paid_at DESC);
CREATE INDEX idx_stock_tenant_product ON stock(tenant_id, product_id);
```

### 7.3 Migraciones

- Se usan EF Core Migrations
- Las migraciones son parte del repositorio (no se generan en producción)
- La migración se aplica al iniciar la app en desarrollo (`dotnet ef database update`)
- En producción, las migraciones se aplican en el pipeline de CI/CD antes del deploy

### 7.4 Convenciones MongoDB (Catálogo)

- Una colección por tenant: `catalog_{tenantId}`
- Documento único por colección (el catálogo completo del restaurante)
- Los productos son documentos dentro del array `products[]` del catálogo
- Índice en `products.id` para lookups rápidos de producto individual
- Nunca se almacenan datos transaccionales en MongoDB (solo catálogo y branding)

---

## 8. Frontend — Convenciones Next.js

### 8.1 App Router — Estructura de rutas

```typescript
// Cada app sigue la misma convención:
app/
├── layout.tsx           → Layout raíz (fonts, providers, metadata)
├── page.tsx             → Homepage / redirect
├── (auth)/              → Route group — páginas que requieren auth
│   ├── layout.tsx       → Verifica JWT en el layout
│   └── dashboard/
│       └── page.tsx
└── (public)/            → Route group — páginas públicas
    └── login/
        └── page.tsx
```

### 8.2 Server Components vs Client Components

- Por defecto, todos los componentes son **Server Components**
- Se añade `"use client"` solo cuando se necesita: estado local, eventos del browser, SignalR, hooks
- Los datos que no cambian en tiempo real se obtienen en Server Components (mejor performance)
- Los datos en tiempo real (SignalR) viven en Client Components

### 8.3 Manejo de estado

- **Estado global del servidor:** React Query (`@tanstack/react-query`) para cacheo y sincronización
- **Estado de tiempo real:** Zustand store (`zustand`) para datos de SignalR
- **Estado de formulario:** React Hook Form + Zod para validación
- **No se usa Redux** — es excesivo para este tipo de apps

### 8.4 Tipado end-to-end

```typescript
// Los tipos se generan desde el OpenAPI spec del backend
// packages/types/generated/orders.ts (AUTO-GENERADO)
export interface Order {
  id: string;
  sessionId: string;
  customerName: string;
  step: 'New' | 'Preparing' | 'Ready';
  status: 'Active' | 'Delivered' | 'Removed';
  items: OrderItem[];
  totalPrice: number;
  createdAt: string;
}

// Script de generación en turbo.json:
// "generate:types": "openapi-typescript https://api.mesto.app/openapi.json -o packages/types/generated/index.ts"
```

### 8.5 Convenciones de nombres

```
Componentes:     PascalCase (OrderCard.tsx, ProductList.tsx)
Hooks:           camelCase con prefijo "use" (useOrders.ts, useSignalR.ts)
Servicios/API:   camelCase con sufijo "Service" (ordersService.ts)
Constantes:      UPPER_SNAKE_CASE (HUB_GROUPS, API_BASE_URL)
CSS modules:     camelCase (styles.orderCard, styles.activeState)
```

---

## 9. Seguridad — Checklist de implementación

```
[ ] Todos los secretos en Azure Key Vault — nunca en el código ni en .env commiteado
[ ] CORS configurado con orígenes específicos (no AllowAll)
[ ] Rate limiting activado: 60 req/min por IP para endpoints públicos, 200 req/min por JWT para endpoints autenticados
[ ] JWT blacklist en Redis para invalidación inmediata de tokens del cliente
[ ] Webhooks de pasarelas validados con firma HMAC
[ ] Montos de pago calculados siempre en el servidor (nunca confiar en el cliente)
[ ] SQL injection: imposible — se usa EF Core con parámetros tipados
[ ] XSS: Next.js escapa automáticamente, customerName se sanitiza antes de almacenar
[ ] File upload: solo imágenes, con validación de MIME type real (no solo extensión), max 5MB
[ ] Logs: nunca se loguean datos sensibles (nombres de tarjeta, tokens completos)
[ ] Correlation IDs en todos los requests para trazabilidad end-to-end
```

---

## 10. CI/CD Pipeline

### 10.1 `mesto-api` — Azure Pipelines

```yaml
# Disparado en push a main y develop
stages:
  - stage: Test
    jobs:
      - job: UnitTests
        steps:
          - dotnet test Mesto.UnitTests
      - job: IntegrationTests
        steps:
          - docker-compose up -d postgres mongo redis
          - dotnet test Mesto.IntegrationTests

  - stage: Build
    dependsOn: Test
    jobs:
      - job: BuildAndPublish
        steps:
          - dotnet publish -c Release
          - docker build -t mesto-api:$(Build.BuildId)

  - stage: Deploy_Dev
    condition: branch = develop
    jobs:
      - job: DeployDev
        steps:
          - dotnet ef database update  # Aplica migraciones
          - az webapp deploy           # Azure App Service (dev)

  - stage: Deploy_Prod
    condition: branch = main
    jobs:
      - job: DeployProd
        steps:
          - dotnet ef database update
          - az webapp deploy           # Azure App Service (prod)
```

### 10.2 `mesto-web` — Vercel (automático)

Vercel detecta el monorepo Turborepo automáticamente:
- Push a `develop` → Preview deployment por app
- Push a `main` → Production deployment por app
- Cada PR → Preview URL única

### 10.3 Entornos

| Entorno | Branch | API | Apps |
|---|---|---|---|
| Local | cualquiera | `localhost:7000` | `localhost:3000-3004` |
| Development | `develop` | `api-dev.mesto.app` | `[app]-dev.mesto.app` |
| Production | `main` | `api.mesto.app` | `mesto.app`, `staff.mesto.app`, etc. |

---

## 11. Observabilidad

### 11.1 Logging estructurado (Serilog)

```csharp
// Formato JSON para todos los logs
Log.Information("Order created {@OrderId} for session {@SessionId} in tenant {@TenantId}",
    order.Id, order.SessionId, tenantContext.TenantId);
// Output:
// { "level": "Information", "message": "Order created", "orderId": "...", "sessionId": "...", "tenantId": "...", "correlationId": "...", "timestamp": "..." }
```

### 11.2 Health checks

```
GET /health        → Estado del servicio (live)
GET /health/ready  → Dependencias listas (PostgreSQL, MongoDB, Redis)
```

### 11.3 Alertas configuradas

| Condición | Umbral | Acción |
|---|---|---|
| Tasa de error 5xx | > 1% en 5 min | Alert a Slack/email |
| Latencia P95 | > 1000ms | Alert |
| Uso de memoria | > 85% | Alert |
| PostgreSQL conexiones | > 80% del pool | Alert |
| Redis latencia | > 100ms P99 | Alert |

---

## 12. Guía de inicio rápido para desarrolladores

### Prerequisitos
- .NET 8 SDK
- Node.js 20+
- Docker Desktop
- Azure CLI (para producción)

### Setup local

```bash
# 1. Levantar infraestructura local
docker-compose up -d
# Levanta: PostgreSQL:5432, MongoDB:27017, Redis:6379

# 2. Backend
cd mesto-api
dotnet restore
dotnet ef database update --project src/Mesto.API
dotnet run --project src/Mesto.API
# API disponible en http://localhost:7000
# Swagger en http://localhost:7000/swagger

# 3. Frontend
cd mesto-web
npm install
npm run dev
# menu:    http://localhost:3000
# staff: http://localhost:3001
# admin:   http://localhost:3003
# platform:http://localhost:3004

# 4. Generar tipos desde OpenAPI
npm run generate:types
```

### Variables de entorno para desarrollo

```bash
# mesto-api/appsettings.Development.json
# (Este archivo NO se commitea — está en .gitignore)
{
  "Database": {
    "PostgresConnection": "Host=localhost;Database=mesto_dev;Username=mesto;Password=mesto",
    "MongoConnection": "mongodb://localhost:27017",
    "RedisConnection": "localhost:6379"
  },
  "Jwt": {
    "PrivateKey": "[RSA private key para dev — NO usar la de producción]",
    "Issuer": "mesto-api-dev"
  }
}
```

---

## 13. Guía para contribución con IA

Si una IA está leyendo este documento para generar o modificar código de Mesto, estas son las reglas más importantes:

1. **Nunca generes código que almacene secretos en el código fuente.** Los secretos van en Azure Key Vault. En desarrollo, en `appsettings.Development.json` que está en `.gitignore`.

2. **Siempre aplica el filtro de `TenantId`** en todos los queries de base de datos. Un repository que no filtra por tenant es un bug de seguridad crítico.

3. **El Result Pattern reemplaza las excepciones como control de flujo.** Los errores de negocio se retornan como `Result.Failure(error)`, no se lanzan excepciones.

4. **SignalR nunca usa `Clients.All`.** Siempre `Clients.Group(HubGroups.Kitchen(tenantId, stationId))` u otro grupo específico.

5. **Los montos de dinero siempre se calculan en el servidor.** Si el frontend envía un monto, se recalcula en el backend antes de usarlo.

6. **Las migraciones de EF Core se generan localmente y se commitean.** Nunca se ejecuta `dotnet ef migrations add` en un pipeline de CI.

7. **Al agregar un nuevo endpoint**, verificar que tenga: atributo `[Authorize]`, filtro de tenant aplicado en el repositorio, validación de request con FluentValidation, y que esté documentado en la sección 3.5 de este PRD.

8. **Los tipos TypeScript del frontend son auto-generados.** No edites los archivos en `packages/types/generated/` manualmente — regenera desde el OpenAPI spec.

---

## 14. Decisiones pendientes por resolver

| # | Decisión | Opciones | Urgencia |
|---|---|---|---|
| DEC-01 | ¿Wompi o MercadoPago para integrar primero? | Wompi (más local), MercadoPago (más documentación) | Alta — bloquea PRD-005 |
| DEC-02 | ¿Azure App Service o Azure Container Apps para el backend? | App Service (más simple), Container Apps (más escalable) | Media |
| DEC-03 | ¿EF Core o Dapper para el módulo de Analytics? | EF Core (consistente), Dapper (queries SQL raw más rápidas para agregaciones) | Baja — se decide al implementar Analytics |
| DEC-04 | ¿Qué proveedor de email para notificaciones? | SendGrid, Azure Communication Services, Resend | Media |

---

*PRD-007 — Arquitectura Técnica*
*Versión 2.0 — Junio 2026*
