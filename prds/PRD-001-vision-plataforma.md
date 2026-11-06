# PRD-001 — Visión y Plataforma Mesto

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
| **ID** | PRD-001 |
| **Versión** | 2.1 |
| **Estado** | Aprobado — v2.1 |
| **Fecha** | Junio 2026 |
| **Módulos relacionados** | Plataforma completa |
| **Depende de** | Ninguno (es la base) |
| **PRDs que dependen de este** | PRD-002, PRD-003, PRD-004, PRD-005, PRD-006, PRD-007 |

---

## 1. Propósito y Objetivos

### Propósito

Definir la visión, el modelo de negocio, las personas, los objetivos de producto y las métricas de éxito de Mesto. Este documento es la fuente de verdad para cualquier decisión de producto que se tome en el proyecto.

### Objetivos del Producto

| # | Objetivo | Métrica de éxito |
|---|---|---|
| OBJ-01 | Ser la plataforma de pedidos preferida por restaurantes y bares en Colombia | 100 restaurantes activos en el primer año |
| OBJ-02 | Que cualquier restaurante pueda lanzar en menos de 10 minutos sin soporte técnico | Tiempo de onboarding ≤ 10 min medido en pruebas con usuarios |
| OBJ-03 | Reducir errores de pedido a cero mediante digitalización del flujo | Tasa de pedidos con error < 0.5% |
| OBJ-04 | Generar ingresos sostenibles via modelo de comisión y/o suscripción | MRR positivo al mes 6 de operación |
| OBJ-05 | Experiencia del cliente que supere a las soluciones actuales | NPS del cliente final > 70 |

---

## 2. Contexto y Problema que Resuelve

### Contexto

Los restaurantes y bares en Colombia operan mayoritariamente con procesos manuales para la toma de pedidos. Las soluciones digitales existentes son:

- **Cartas en papel / PDFs con QR:** Informativos, no procesan pedidos.
- **Sistemas POS enterprise (Toast, Square, Oracle Micros):** Costosos (USD 500-2000/mes), requieren hardware específico, difíciles de configurar.
- **Apps propias por restaurante:** Requieren inversión de desarrollo y mantenimiento continuo.
- **Plataformas de delivery (Rappi, iFood):** Orientadas a domicilios, no a servicio en mesa.

Ninguna solución existente resuelve el flujo completo de mesa (pedido → cocina/barra → entrega → pago) a un costo accesible para el mercado colombiano.

### El problema en 3 capas

```
CAPA 1 — El cliente en la mesa:
  - Espera al mesero para pedir (5-15 min en horas pico)
  - No sabe el estado de su pedido
  - El pago y la división de cuenta son lentos y manuales

CAPA 2 — El staff:
  - La cocina y la barra reciben pedidos en papel o de voz
  - Errores de transcripción frecuentes
  - No hay visibilidad del estado de cada pedido
  - El mesero hace viajes innecesarios para verificar

CAPA 3 — El dueño:
  - No tiene datos de ventas en tiempo real
  - No sabe qué productos son más rentables
  - No tiene control del inventario
  - Las decisiones se toman de memoria
```

---

## 3. Usuarios y Personas

> Ver `README.md` para el perfil completo de cada persona. Aquí se resumen los atributos clave para el producto.

### P1 — Cliente Final (Ana, 28 años)
- **Contexto:** Está en un restaurante con amigos, quiere pedir sin esperar
- **Necesidad principal:** Pedir rápido, saber cuándo llega su pedido, pagar sin complicaciones
- **Frustración actual:** Esperar al mesero, no saber si el pedido llegó, dividir la cuenta en efectivo
- **Dispositivo:** Su propio celular (iOS o Android)
- **Adopción tecnológica:** Alta — usa Rappi, Nequi, Instagram

### P2 — Dueño del Restaurante (Carlos, 45 años)
- **Contexto:** Dueño de un restaurante mediano, no es técnico pero entiende de negocio
- **Necesidad principal:** Que su operación sea eficiente y tener datos para decidir
- **Frustración actual:** No sabe cuánto vende hasta cuadrar la caja al cierre
- **Dispositivo:** PC y celular
- **Adopción tecnológica:** Media — usa WhatsApp Business y Excel

### P3 — Cocinero / Bartender (Pedro, 30 años)
- **Contexto:** Trabaja en la cocina o barra, necesita saber qué preparar y en qué orden
- **Necesidad principal:** Ver los pedidos claramente, sin ambigüedad, actualizarlos fácil
- **Frustración actual:** Comandas en papel que se mojan o se pierden, pedidos de voz que no escucha
- **Dispositivo:** Tablet fija en la cocina o TV con browser
- **Adopción tecnológica:** Baja — solo usa WhatsApp

### P4 — Mesero (Laura, 22 años)
- **Contexto:** Atiende múltiples mesas simultáneamente
- **Necesidad principal:** Saber cuándo un pedido está listo, responder llamados de mesa, no hacer viajes innecesarios
- **Frustración actual:** Tiene que preguntar en cocina si el pedido está listo, los clientes la llaman sin que ella sepa
- **Dispositivo:** Su propio celular
- **Adopción tecnológica:** Alta

### P5 — Cajero (Valentina, 25 años)
- **Contexto:** Gestiona el flujo de mesas y los pagos
- **Necesidad principal:** Ver qué mesas están activas, cerrar cuentas rápido, generar QRs
- **Frustración actual:** Tiene que recordar qué mesas están ocupadas, el cierre de cuenta es lento
- **Dispositivo:** Tablet o PC en caja
- **Adopción tecnológica:** Media-Alta

### P6 — Mesto Ops (Equipo interno)
- **Contexto:** Equipo de Mesto que onboardea y soporta restaurantes
- **Necesidad principal:** Configurar nuevos restaurantes rápido, monitorear la plataforma
- **Dispositivo:** PC
- **Adopción tecnológica:** Alta (son el equipo técnico)

---

## 4. Alcance

### In Scope — MVP

- Plataforma multi-tenant: cada restaurante es un tenant aislado
- Onboarding de restaurantes via panel de administración (Platform Admin)
- Platform Admin debe exponer un listado de tenants con CTA visible para crear un tenant manualmente y entrar a su ficha inmediatamente después de crearlo
- App del cliente: menú digital, carrito, pedido, tracking, cuenta, pago
- KDS de cocina: pedidos en tiempo real, actualización de estado
- KDS de barra: pedidos de bebidas en tiempo real
- App de mesero: alertas de pedidos listos y llamados de mesa
- App del cajero: gestión de mesas, apertura/cierre de sesiones, QRs, resumen de cuenta
- Dashboard del dueño: menú configurable, mesas, branding, staff
- Analytics básicos: ventas del día, productos top, ticket promedio
- Inventario básico: control de stock y alertas
- Pagos: digital (Wompi / MercadoPago) y en caja (efectivo / datáfono)
- Split bill y propina configurable
- QR fijo por mesa con seguridad de sesión temporal
- Modelo de autenticación completo: staff por JWT, cliente por session token

### Out of Scope — MVP (planificado para v2)

- Facturación electrónica DIAN
- Integración contable (Siigo, Alegra)
- Programa de fidelización / puntos para clientes
- Reservas de mesa online
- Dark kitchens / pedidos para llevar via QR de mostrador
- App nativa (iOS / Android)
- Integración con sistemas de delivery (Rappi, iFood)
- Soporte multi-idioma completo (el español es el único idioma en MVP)
- Expansión a otros países de Latam

### Out of Scope — Nunca (o muy largo plazo)

- Gestión de recursos humanos (nómina, turnos)
- Contabilidad general del negocio
- Gestión de proveedores y compras
- Sistema de reservas con sala virtual 3D

---

## 5. Historias de Usuario

### Épica 1: Onboarding de un restaurante nuevo

```
US-001
Como Mesto Ops,
quiero crear un nuevo restaurante en la plataforma con un formulario simple,
para que el dueño pueda empezar a configurar su cuenta inmediatamente.

Criterio de aceptación:
- Puedo ingresar: nombre del restaurante, slug URL, email del dueño, plan de cobro
- Puedo ingresar: nombre del restaurante, slug URL, ciudad, email del dueño y NIT/RUT
- Desde el listado de tenants en Platform Admin existe un CTA visible "Crear tenant" que abre el formulario manual de alta
- El sistema crea el tenant con configuración por defecto (5 mesas, branding vacío)
- Al crear el tenant, también se crea automáticamente un usuario staff `Admin` para ese restaurante usando el `ownerEmail`
- El admin del restaurante NO recibe contraseña temporal; debe definir su contraseña desde un enlace de activación de un solo uso
- El tenant se crea con plan inicial `none`; la configuración del modelo de cobro ocurre después desde billing de Platform Admin
- El dueño recibe un email con su link de acceso al Admin Panel
- Ops puede ver en Platform el `tenantId` y el estado del admin creado (`PENDING` o `ACTIVE`) para poder asistir en pruebas y soporte
- El proceso toma menos de 2 minutos

US-002
Como Dueño del restaurante,
quiero completar un wizard de onboarding paso a paso,
para configurar mi restaurante sin necesitar soporte técnico.

Criterio de aceptación:
- El wizard tiene máximo 5 pasos: branding, menú inicial, mesas, estaciones, staff
- Puedo subir mi logo y elegir mis colores de marca
- Puedo crear al menos un producto en el menú durante el wizard
- Al finalizar, tengo QRs descargables para mis mesas
- El tiempo total no supera los 10 minutos
```

### Épica 2: Gestión del negocio por el dueño

```
US-003
Como Dueño del restaurante,
quiero ver un dashboard con las ventas del día al abrir el Admin Panel,
para saber cómo va el negocio en cualquier momento.

Criterio de aceptación:
- Veo: total vendido hoy, número de pedidos, ticket promedio, mesa con más consumo
- Los datos se actualizan cada 5 minutos o al recargar
- Puedo cambiar el rango de fecha (hoy, esta semana, este mes)

US-004
Como Dueño del restaurante,
quiero gestionar mi menú completo desde el panel,
para actualizar precios, disponibilidad e imágenes sin depender de nadie.

Criterio de aceptación:
- Puedo crear, editar y eliminar productos
- Puedo marcar un producto como agotado con un switch (aparece en el menú del cliente como no disponible)
- Puedo reordenar categorías con drag-and-drop
- Los cambios se reflejan en la app del cliente en menos de 5 minutos

US-005
Como Dueño del restaurante,
quiero gestionar el staff (usuarios) de mi restaurante,
para controlar quién tiene acceso a qué.

Criterio de aceptación:
- Puedo crear usuarios con los roles: Cajero, Cocina, Barra, Mesero
- Puedo desactivar un usuario sin eliminarlo
- Los usuarios de cocina solo pueden ver el KDS de cocina, no la configuración del menú
```

### Épica 3: Modelo de cobro flexible

```
US-006
Como Mesto Ops,
quiero configurar el modelo de cobro de cada restaurante desde el Platform Admin,
para adaptar el precio a lo que cada cliente prefiere.

Criterio de aceptación:
- Puedo seleccionar para cada tenant: comisión por transacción, suscripción mensual, o híbrido
- Puedo configurar el porcentaje de comisión o el valor mensual
- El sistema aplica el cobro automáticamente según el modelo configurado

US-007
Como Dueño del restaurante,
quiero poder activar o desactivar los pagos digitales para mi restaurante,
para decidir si quiero ofrecer pago digital o solo pago en caja.

Criterio de aceptación:
- En la configuración del restaurante hay un switch "Pagos digitales"
- Si está desactivado, la app del cliente no muestra la opción de pago digital
- Si está activado, debo haber configurado mi pasarela de pagos primero
```

---

## 6. Requisitos Funcionales

### RF-001 — Multi-tenancy
- Cada tenant tiene su propio slug URL (ej: `mesto.app/la-parrilla`)
- Los datos de cada tenant están completamente aislados — ningún usuario puede ver datos de otro tenant
- El tenant puede ser activado o desactivado desde Platform Admin

### RF-002 — Gestión de usuarios y roles
- El sistema soporta los roles: PlatformAdmin, RestaurantAdmin, Cashier, Kitchen, Bar, Waiter
- Cada rol tiene acceso únicamente a las funciones que le corresponden
- Un usuario solo puede pertenecer a un tenant (excepto PlatformAdmin)
- Todo tenant creado manualmente desde Platform Admin debe nacer con un staff `RestaurantAdmin` bootstrap asociado al `ownerEmail`
- El `RestaurantAdmin` bootstrap queda en estado `PENDING` hasta completar activación; no existe contraseña inicial en texto plano
- Un tenant en estado `PENDING` debe permitir acceso únicamente al `RestaurantAdmin` bootstrap para completar onboarding inicial; otros roles del restaurante permanecen bloqueados

### RF-003 — Configuración del restaurante
- El dueño puede configurar: branding, menú, mesas, zonas, estaciones, staff, horarios, modelo de pago
- Los cambios en el menú se propagan a la app del cliente en menos de 5 minutos (TTL de caché)

### RF-004 — QR de mesa
- Cada mesa tiene un QR permanente que apunta a una URL fija
- El QR solo es funcional cuando el cajero ha abierto una sesión para esa mesa
- Al cerrar la sesión, todos los tokens del cliente asociados se invalidan inmediatamente

### RF-005 — Tiempo real
- Los pedidos nuevos aparecen en el KDS en menos de 2 segundos después de ser confirmados
- El cliente ve el cambio de estado de su pedido en menos de 3 segundos
- El mesero recibe la alerta de pedido listo en menos de 3 segundos

### RF-006 — Modelo de ingresos
- La plataforma soporta tres modelos de cobro por tenant: comisión, suscripción, híbrido
- El modelo se configura desde Platform Admin y se aplica automáticamente

---

## 7. Requisitos Técnicos

### RT-001 — Disponibilidad
- El sistema debe tener disponibilidad ≥ 99.5% (downtime máximo 3.65 horas/mes)
- Las horas pico de un restaurante son 12pm-3pm y 7pm-10pm — estos períodos son críticos

### RT-002 — Latencia
- Las APIs de pedidos deben responder en menos de 500ms en el percentil 95
- Los mensajes de SignalR deben llegar en menos de 2 segundos en condiciones normales

### RT-003 — Seguridad
- Todos los secretos (credenciales de BD, API keys) deben estar en Azure Key Vault — nunca en el código
- Todos los endpoints deben requerir autenticación (JWT o Session Token)
- CORS configurado por origen específico, no wildcard
- Rate limiting activo en todos los endpoints públicos

### RT-004 — Escalabilidad
- La arquitectura debe soportar al menos 500 restaurantes activos sin cambios estructurales
- El módulo de Analytics puede ser el primero en extraerse a un servicio separado si las queries afectan el tiempo de respuesta del API principal

### RT-005 — Observabilidad
- Todos los requests deben tener un Correlation ID para trazabilidad
- Logs estructurados (JSON) con nivel de severidad
- Health check endpoint: `GET /health`
- Alertas configuradas para: tiempo de respuesta > 1s, tasa de error > 1%, uso de memoria > 80%

---

## 8. Criterios de Aceptación del MVP

El MVP se considera completo cuando:

- [ ] Un restaurante nuevo puede hacer el onboarding completo en menos de 10 minutos
- [ ] Un cliente puede escanear un QR, ver el menú, hacer un pedido y ver su estado en tiempo real
- [ ] El pedido aparece en el KDS correspondiente (cocina o barra) en menos de 3 segundos
- [ ] El cajero puede cerrar una cuenta y registrar el pago (digital o en caja)
- [ ] El dueño puede ver el resumen de ventas del día en el admin panel
- [ ] El sistema opera correctamente con al menos 10 restaurantes simultáneos en prueba de carga

---

## 9. Dependencias con otros PRDs

| PRD | Tipo de dependencia |
|---|---|
| PRD-002 | Define el flujo del cliente que este PRD promete |
| PRD-003 | Define las apps de staff (KDS, mesero, cajero) |
| PRD-004 | Define la configuración del restaurante (onboarding) |
| PRD-005 | Define los pagos, que son el core del modelo de negocio |
| PRD-006 | Define analytics e inventario (diferenciadores del producto) |
| PRD-007 | Define cómo se construye técnicamente todo lo anterior |

---

## 10. Métricas de Éxito

### Métricas de negocio

| Métrica | Meta MVP | Meta año 1 |
|---|---|---|
| Restaurantes activos | 10 (beta) | 100 |
| MRR | Positivo al mes 6 | COP 20M |
| Churn mensual de restaurantes | < 5% | < 3% |
| Tiempo de onboarding promedio | < 10 min | < 7 min |

### Métricas de producto

| Métrica | Meta |
|---|---|
| NPS del cliente final | > 70 |
| Tasa de error en pedidos | < 0.5% |
| Uptime del sistema | > 99.5% |
| Latencia P95 del API | < 500ms |
| Pedidos procesados sin intervención humana | > 90% |

### Métricas de adopción

| Métrica | Meta |
|---|---|
| % de mesas que usan el QR por turno | > 60% en restaurantes activos |
| % de cuentas con pago digital | > 30% |
| % de pedidos con cambios de estado actualizados por staff | > 80% |

---

## 11. Open Questions

| # | Pregunta | Responsable | Fecha límite |
|---|---|---|---|
| OQ-01 | ¿Cuál es la comisión exacta a cobrar por transacción digital? (Wompi cobra ~2.99%) | Founders | Antes de activar pagos |
| OQ-02 | ¿El modelo freemium (gratis con límite de pedidos) tiene sentido para adquisición? | Product | Mes 2 |
| OQ-03 | ¿Se puede hacer onboarding self-service sin intervención de Mesto Ops? | Product + Tech | Definir en PRD-004 |
| OQ-04 | ¿Cómo se maneja el soporte al restaurante? ¿Chat en la plataforma, WhatsApp, email? | Operations | Mes 3 |
| OQ-05 | ¿El slug del restaurante es permanente o el dueño puede cambiarlo? | Product | Antes de lanzar |

---

*PRD-001 — Visión y Plataforma Mesto*
*Versión 2.1 — Junio 2026*
