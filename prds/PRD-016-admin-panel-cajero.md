# PRD-016 — Admin Panel del Restaurante y Flujo del Cajero

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
| **ID** | PRD-016 |
| **Versión** | 1.4 |
| **Estado** | Aprobado — v1.4 |
| **Fecha** | Agosto 2026 |
| **Apps relacionadas** | `admin.mesto.app` (dueño/Admin), `staff.mesto.app` (cajero) |
| **Módulos de API** | Identity, Catalog, Orders, Payments, Analytics, Sessions |
| **Depende de** | PRD-003 (KDS/staff), PRD-004 (wizard y configuración), PRD-005 (pagos), PRD-013 (gestión operativa) |
| **PRDs que dependen de este** | PRD-006 (analytics), PRD-012 (lealtad — contrato lectura), PRD-014 (auth), PRD-018 (el modo beach club opera sobre estos módulos) |

---

## 1. Propósito y Objetivos

### Propósito

Definir la experiencia completa del **Admin Panel** (`admin.mesto.app`) — la herramienta del dueño para administrar su restaurante después de completar el wizard — y el **flujo de pago en caja** de la app del cajero (`staff.mesto.app`). Este PRD unifica la UX de la pantalla más usada por el dueño y el flujo más crítico del cajero.

### Objetivos

| # | Objetivo | Métrica |
|---|---|---|
| OBJ-01 | El dueño puede actualizar su menú en menos de 2 minutos desde cualquier sección | Tiempo medido en sesiones de prueba |
| OBJ-02 | El cajero puede cerrar una cuenta de 4 personas (split-bill) en menos de 3 minutos | Tiempo desde "Cobrar sesión" hasta sesión cerrada |
| OBJ-03 | El dueño tiene visibilidad del negocio del día sin necesitar informes adicionales | NPS del dashboard > 70 |
| OBJ-04 | Zero errores de pago por selección incorrecta de método o monto | Tasa de error en confirmación de pago < 0.5% |

---

## 2. Arquitectura de Navegación

### Admin Panel (admin.mesto.app)

```
admin.mesto.app/
│
├── /dashboard          → Vista principal del negocio
├── /menu               → Gestión de categorías y productos
│   └── /menu/[itemId]  → Detalle / edición de producto
├── /mesas              → Configuración de mesas
├── /staff              → Gestión del equipo
├── /reportes           → Analytics y reportes de turno
└── /configuracion      → Branding, horarios, QR, estaciones, lealtad
```

### Cajero (staff.mesto.app — vista de cajero)

```
staff.mesto.app/ (rol: Cajero)
│
├── /sesiones           → Lista de mesas activas
└── /sesiones/[id]      → Detalle de sesión + flujo de cobro
    ├── /cobrar         → Selección de método de pago
    └── /split          → División de cuenta
```

---

## 3. Admin Panel — Sidebar y Layout

### Wireframe base

```
admin.mesto.app                       Juan (Admin)  [🔔 2]  [Cerrar sesión]
═══════════════════════════════════════════════════════════════════════════

  [🏠 Dashboard  ]
  [🍽  Menú      ]
  [🪑 Mesas      ]
  [👥 Staff      ]
  [📊 Reportes   ]
  [⚙  Configuración]

  ─────────────────
  La Parrilla
  la-parrilla
  🟢 Plan activo
```

El sidebar es colapsable a solo íconos en pantallas < 1280px. En mobile el layout no está soportado en MVP (app orientada a PC/tablet).

---

## 4. Módulo 1 — Dashboard `/dashboard`

### Descripción

Pantalla de inicio del dueño al entrar al Admin Panel. Muestra el estado del negocio en tiempo real y el resumen del período. Es la pantalla más visitada; debe cargar en menos de 1 segundo.

### Wireframe

```
Dashboard — La Parrilla                         Hoy, domingo 7 Jun 2026  [🔄]
═══════════════════════════════════════════════════════════════════════════════

  AHORA MISMO
  ┌──────────────────┬──────────────────┬──────────────────┬─────────────────┐
  │  Mesas activas   │ Pedidos en curso │ GMV del día      │ Ticket promedio │
  │     3 / 12       │       8          │   $842.000       │   $17.915       │
  └──────────────────┴──────────────────┴──────────────────┴─────────────────┘

  RESUMEN DEL MES                                Período: [Jun 2026 ▼]
  ┌─────────────────────────────────────────────────────────────────────┐
  │  GMV               $14.2M    ▲ +12% vs. Mayo                       │
  │  Total pedidos      1.847    ▲ +8% vs. Mayo                        │
  │  Sesiones abiertas    203    — igual vs. Mayo                       │
  │  Ticket promedio  $17.915    ▲ +4% vs. Mayo                        │
  └─────────────────────────────────────────────────────────────────────┘

  TOP 5 PRODUCTOS HOY                  ALERTAS
  ┌──────────────────────────────┐     ┌─────────────────────────────────┐
  │ 🥇 Bandeja Paisa        18u │     │ ⚠ Cerveza Club Colombia:        │
  │ 🥈 Churrasco            14u │     │   stock bajo (2 unidades)       │
  │ 🥉 Mojito               11u │     │                                 │
  │  4. Arepa con Hogao      9u │     │ ℹ Mesa 4: sin actividad 2h     │
  │  5. Limonada Natural     8u │     │   [Notificar al cajero →]       │
  └──────────────────────────────┘     └─────────────────────────────────┘

  SESIONES EN CURSO AHORA
  ┌────────┬──────────┬────────────────┬────────────┬────────────────────┐
  │ Mesa   │ Mesero   │ Tiempo abierta │ Participan.│ Estado             │
  ├────────┼──────────┼────────────────┼────────────┼────────────────────┤
  │ Mesa 2 │ Pedro G. │ 2h 14min       │ 3          │ Pedido entregado   │
  │ Mesa 3 │ María L. │ 45min          │ 1          │ Pedido en cocina   │
  │ Mesa 7 │ Pedro G. │ 12min          │ 2          │ Revisando menú     │
  └────────┴──────────┴────────────────┴────────────┴────────────────────┘
```

### Reglas del Dashboard

- Los datos de "Ahora mismo" se actualizan por polling cada 60 segundos (sin recargar la página)
- "Pedidos en curso" = pedidos con estado `CONFIRMED` o `IN_PREPARATION`
- "Mesas activas" = sesiones con estado `OPEN`
- El comparativo del período usa el mismo período del mes anterior
- Las alertas de stock se muestran solo si el ítem tiene `stock_enabled = true` y `stock_qty ≤ low_stock_threshold`
- El wizard banner solo aparece si hay pasos incompletos:

```
  ⚠ Tu configuración está incompleta                              [Continuar wizard →]
  ████████████████████░  4/5 pasos — Falta: agregar staff
```

---

## 5. Módulo 2 — Menú `/menu`

### Descripción

Vista y edición completa del menú del restaurante. Las categorías y productos se pueden crear, editar, reordenar y deshabilitar en cualquier momento, sin afectar pedidos en curso (los pedidos ya confirmados preservan el snapshot del producto al momento del pedido).

### Wireframe — Lista de categorías y productos

```
Menú                                            [+ Nueva categoría]
═══════════════════════════════════════════════════════════════════

  🔍 Buscar en el menú...                     [⬆ Publicar cambios]

  ▼ Platos Principales                          [✏ Editar] [⋮]
  ───────────────────────────────────────────────────────────────
  [≡] [🖼] Bandeja Paisa          $28.000   ✅ Disponible   [···]
  [≡] [🖼] Churrasco 350g         $35.000   ✅ Disponible   [···]
  [≡] [🖼] Costillas BBQ          $42.000   ⛔ Agotado      [···]
  [≡] [🖼] Pollo a la Plancha     $24.000   ✅ Disponible   [···]
                                            [+ Agregar producto]

  ▶ Bebidas                                     [✏ Editar] [⋮]
  ▶ Entradas                                    [✏ Editar] [⋮]
  ▶ Postres                                     [✏ Editar] [⋮]
```

El ícono `[≡]` es el handle para drag & drop y reordenar dentro de la categoría.  
El menú `[···]` en cada producto ofrece: Editar / Duplicar / Marcar agotado / Eliminar.  
El menú `[⋮]` en cada categoría ofrece: Editar nombre / Reordenar / Eliminar categoría.

`[Publicar cambios]` solo aparece si hay cambios no guardados (optimistic UI con indicador visual).

### Wireframe — Edición de producto (modal/panel lateral)

```
Editar producto                                     [Guardar]  [Cancelar]
─────────────────────────────────────────────────────────────────────────

  Nombre *
  ┌─────────────────────────────────────────────────────────────┐
  │ Bandeja Paisa                                               │
  └─────────────────────────────────────────────────────────────┘

  Descripción
  ┌─────────────────────────────────────────────────────────────┐
  │ Frijoles, chicharrón, arroz, carne molida, chorizo,        │
  │ huevo frito, arepa y aguacate.                              │
  └─────────────────────────────────────────────────────────────┘
  (Máx. 300 caracteres)

  Precio *                    Categoría *
  ┌──────────────┐            ┌──────────────────────────────┐
  │ $28.000      │            │ Platos Principales ▼         │
  └──────────────┘            └──────────────────────────────┘

  Imágenes (hasta 3)
  ┌──────────────────────────────────────────────────────────────┐
  │  [🖼 Imagen 1]    [🖼 Imagen 2]    [+ Agregar imagen]       │
  │  (principal)       (galería)                                 │
  │                                                              │
  │  Arrastra para reordenar · Formatos: JPG/PNG · Máx 2MB c/u  │
  └──────────────────────────────────────────────────────────────┘

  Disponibilidad
  ○ Disponible     ○ Agotado (marca el producto como no disponible sin eliminarlo)

  Control de stock
  ☐ Activar control de stock para este producto
     Cantidad actual: [____]    Umbral de alerta: [____]
```

### Reglas de negocio del Menú

- Los cambios de precio aplican solo a pedidos nuevos; los pedidos confirmados usan el precio del momento de la orden
- Un producto "agotado" se muestra en el menú del cliente como no disponible (sin botón "agregar")
- Eliminar un producto es solo soft-delete (`deleted_at`); el historial de pedidos lo preserva
- El catálogo se sirve desde Redis `catalog:{tenantId}` (TTL 5 min); al guardar cambios se invalida la clave
- Máximo 3 imágenes por producto; la primera es la imagen principal en listados

---

## 6. Módulo 3 — Mesas `/mesas`

### Descripción

Configuración de las mesas del restaurante: crear, editar, activar/desactivar y descargar QRs. **Esta sección es solo configuración** — el estado en tiempo real de cada mesa (ocupada/libre) está en la app del cajero y mesero, no aquí.

> **[v1.4] Lo entregado es un editor de plano, no una tabla.** El módulo `/mesas` se construyó como
> **editor del plano canónico del local**: sobre el mismo mapa a escala que usan recepción, comandero,
> runner y caja, el administrador arrastra los puestos (las posiciones se guardan en lote), añade
> puestos de los tipos y dimensiones reales del local, cambia forma y plazas, y elimina. El wireframe
> de tabla que sigue describe el listado plano original y se conserva como referencia de las acciones
> por mesa (editar zona, activar/desactivar, descargar QRs) que **no** viven hoy en esta pantalla.
> Ver **PRD-004 US-421 / RF-411** y **PRD-018 RF-1807…RF-1809**.

### Wireframe

```
Mesas                                [+ Agregar mesa]  [⬇ Descargar todos los QRs]
═══════════════════════════════════════════════════════════════════════════════════

  ┌────────┬────────────┬──────────────────────────────────┬──────────┬──────────┐
  │ Mesa   │ Zona       │ URL del QR                       │ Estado   │ Acciones │
  ├────────┼────────────┼──────────────────────────────────┼──────────┼──────────┤
  │ Mesa 1 │ Salón      │ mesto.app/la-parrilla?t=mesa-1   │ ✅ Activa│ [···]    │
  │ Mesa 2 │ Salón      │ mesto.app/la-parrilla?t=mesa-2   │ ✅ Activa│ [···]    │
  │ Mesa 3 │ Terraza    │ mesto.app/la-parrilla?t=mesa-3   │ ✅ Activa│ [···]    │
  │ Mesa 4 │ Terraza    │ mesto.app/la-parrilla?t=mesa-4   │ ⛔ Inact.│ [···]    │
  └────────┴────────────┴──────────────────────────────────┴──────────┴──────────┘

  12 mesas  |  10 activas  |  2 inactivas
```

Desde `[···]` de cada mesa:
- Editar (nombre, zona)
- Descargar QR (PNG con logo del restaurante)
- Descargar hoja A4 con QR etiquetado (PDF)
- Desactivar / Activar

Al desactivar una mesa: si tiene una sesión activa, el sistema advierte "Esta mesa tiene una sesión abierta. Si la desactivas no se puede escanear el QR para nuevas sesiones, pero la sesión actual sigue activa hasta que el cajero la cierre."

### Agregar mesa (modal)

```
Agregar mesa
──────────────────────────────────────
  Nombre o número de mesa *
  ┌──────────────────────┐
  │ Mesa 13              │
  └──────────────────────┘

  Zona (opcional)
  ┌──────────────────────┐
  │ Salón                │ ← lista de zonas existentes + "Nueva zona..."
  └──────────────────────┘

  [Cancelar]  [Agregar mesa]
```

---

## 7. Módulo 4 — Staff `/staff`

### Descripción

Gestión del equipo del restaurante post-wizard. El dueño invita, cambia roles, desactiva y resetea contraseñas del staff. Todo el staff usa `admin.mesto.app` para gestionar su cuenta, pero el dueño/Admin es quien tiene control total.

### Wireframe

```
Staff                                                      [+ Invitar persona]
═══════════════════════════════════════════════════════════════════════════════

  ┌─────────────────┬──────────────────────┬──────────┬───────────┬──────────┬──────────┐
  │ Nombre          │ Email                │ Rol      │ Estado    │ Últ. acc.│ Acciones │
  ├─────────────────┼──────────────────────┼──────────┼───────────┼──────────┼──────────┤
  │ Juan Pérez      │ juan@laparrilla.co   │ Admin    │ ✅ Activo │ Hoy      │ [···]    │
  │ María López     │ maria@laparrilla.co  │ Cajero   │ ✅ Activo │ Hoy      │ [···]    │
  │ Pedro Gómez     │ pedro@laparrilla.co  │ Mesero   │ ✅ Activo │ Ayer     │ [···]    │
  │ Ana Ruiz        │ ana@laparrilla.co    │ Mesero   │ ⏳ Pendiente│ —       │ [···]    │
  │ Luis Martínez   │ luis@laparrilla.co   │ KDS      │ ⛔ Inactivo│ Hace 30d│ [···]    │
  └─────────────────┴──────────────────────┴──────────┴───────────┴──────────┴──────────┘

  5 personas  |  3 activas  |  1 pendiente de activación  |  1 inactiva

  ⚠ Ana Ruiz aún no activó su cuenta (invitación enviada hace 2 días)
    [Reenviar invitación →]
```

Desde `[···]` de cada persona:
- Cambiar rol (modal con selector de rol)
- **Resetear contraseña** (solo para personal **activo**): genera un **enlace de restablecimiento** (TTL 1h) que el Admin copia y comparte con el colaborador para que defina una nueva contraseña. El correo se envía como canal secundario (en dev es stub, así que el botón "Copiar enlace" es la vía principal). No aplica a cuentas PENDING (esas usan "Reenviar invitación").
- Desactivar cuenta (confirmar con texto)
- Reactivar cuenta (si está inactiva)
- Reenviar invitación (si estado es PENDING)

El Admin no puede editar su propio rol ni desactivar su propia cuenta (esas acciones están deshabilitadas en su fila).

> El reseteo reutiliza el mismo mecanismo de enlace que el "olvidé mi contraseña" self-service (ver PRD-014 §5.1); el colaborador define su propia clave en `/staff/password-reset` y, al hacerlo, se limpia cualquier cambio forzado pendiente.

### Modal — Invitar persona

El Admin elige **cómo accede** el colaborador (ver PRD-014 §4 y §4.1):

```
Invitar nueva persona al equipo
──────────────────────────────────────────────────────────────────────
  Nombre *
  ┌──────────────────────────────────────────────────────────────┐
  │ Carlos Torres                                                │
  └──────────────────────────────────────────────────────────────┘

  Email *
  ┌──────────────────────────────────────────────────────────────┐
  │ nuevo@laparrilla.co                                          │
  └──────────────────────────────────────────────────────────────┘

  Rol *
  [ Mesero ▾ ]   (Admin / Cajero / Mesero / KDS)

  ¿Cómo accede? *
  ┌───────────────────────────────┐  ┌───────────────────────────────┐
  │ ◉ Acceso directo              │  │ ○ Enlace de activación        │
  │   Genera una contraseña       │  │   Genera un enlace (48h) para │
  │   temporal. La cuenta queda   │  │   que el colaborador defina   │
  │   activa; el colaborador la   │  │   su propia contraseña.       │
  │   cambia en su primer ingreso.│  │                               │
  └───────────────────────────────┘  └───────────────────────────────┘

  [Cancelar]    [Crear con acceso directo / Enviar invitación]
```

**Resultado según el modo elegido:**

- **Acceso directo** → tarjeta (verde) con las credenciales mostradas **una sola vez**:
  ```
  Acceso directo para Carlos Torres
  Comparte estas credenciales. El colaborador deberá cambiarla en su primer ingreso.
    Correo:               nuevo@laparrilla.co
    Contraseña temporal:  SSVsmna9dis4
  [ Copiar credenciales ]
  ```
- **Enlace de activación** → banner (azul) con el enlace `…/staff/activate?token=…` (TTL 48h), botones **Copiar enlace** / **Abrir**. El email se envía como canal secundario (no requerido para el prototipo).

En la **tabla de Staff**, los colaboradores de acceso directo aparecen como `✅ Activo` directamente (no `⏳ Pendiente`); el botón "Reenviar enlace" sólo aplica a los que están en estado Pendiente (modo enlace).

---

## 8. Módulo 5 — Reportes `/reportes`

### Descripción

Dos sub-secciones: **Analytics** (métricas agregadas del negocio) y **Turnos** (reportes de turno del cajero). Son datos de lectura; el dueño no puede modificar nada aquí.

### Sub-módulo 5A — Analytics

```
Analytics                             Período: [Jun 2026 ▼]   [Exportar CSV]
═══════════════════════════════════════════════════════════════════════════

  ┌──────────────────┬──────────────────┬──────────────────┬─────────────────┐
  │ GMV total        │ Total pedidos    │ Sesiones abiertas│ Ticket promedio │
  │ $14.2M           │ 1.847            │ 203              │ $17.915         │
  │ ▲ +12% vs Mayo  │ ▲ +8% vs Mayo   │ — igual vs Mayo  │ ▲ +4% vs Mayo  │
  └──────────────────┴──────────────────┴──────────────────┴─────────────────┘

  GMV por día (Jun 2026)
  ┌─────────────────────────────────────────────────────────────────────┐
  │  [gráfica de barras: 1 Jun → 7 Jun, eje Y en COP]                 │
  └─────────────────────────────────────────────────────────────────────┘

  TOP 10 PRODUCTOS DEL PERÍODO         DISTRIBUCIÓN POR HORA
  ┌──────────────────────────────┐     ┌──────────────────────────────┐
  │ 1. Bandeja Paisa      312u  │     │ 12pm - 2pm   ████████ 38%   │
  │ 2. Churrasco 350g     204u  │     │  7pm - 9pm   ██████ 31%    │
  │ 3. Mojito             187u  │     │  2pm - 4pm   ████ 18%      │
  │ 4. Arepa con Hogao    143u  │     │  otros       ███ 13%       │
  │ ...                         │     └──────────────────────────────┘
  └──────────────────────────────┘
```

- Historial disponible desde la fecha de creación de la cuenta (indefinido en MVP)
- La tabla de analytics solo incluye sesiones con al menos un pedido
- El CSV exportable incluye: fecha, mesa, pedidos, GMV, ticket promedio, método de pago

### Sub-módulo 5B — Turnos

```
Reportes de Turno                                            [Exportar CSV]
═══════════════════════════════════════════════════════════════════════════

  Filtros: [Cajero: Todos ▼]  [Mes: Jun 2026 ▼]

  ┌────────────────┬─────────────────┬──────────────┬──────────┬──────────┐
  │ Fecha          │ Cajero          │ GMV del turno│ Pedidos  │ Acción   │
  ├────────────────┼─────────────────┼──────────────┼──────────┼──────────┤
  │ 07-Jun 09:00   │ María López     │ $842.000     │ 47       │ [Ver]    │
  │ 06-Jun 09:00   │ María López     │ $1.2M        │ 68       │ [Ver]    │
  │ 05-Jun 14:00   │ María López     │ $634.000     │ 37       │ [Ver]    │
  └────────────────┴─────────────────┴──────────────┴──────────┴──────────┘
```

### Detalle de un reporte de turno

```
Reporte de turno — María López · 07 Jun 2026 · 09:00 - 17:30
───────────────────────────────────────────────────────────────────────────

  RESUMEN
  ┌─────────────────────────────────────────────────────────────────────┐
  │  GMV total del turno:   $842.000      Sesiones cerradas:  18       │
  │  Total pedidos:         47            Ticket promedio:    $17.915   │
  │                                                                     │
  │  MÉTODOS DE PAGO                                                    │
  │  Efectivo:    $312.000 (37%)          Vuelto entregado: $28.000    │
  │  Tarjeta:     $485.000 (58%)                                        │
  │  Billetera:    $45.000 (5%)                                         │
  │                                                                     │
  │  PROPINA TOTAL:         $63.000       Propina promedio: $3.500      │
  │  CORTESÍAS:             2 sesiones    Valor total:      $56.000     │
  └─────────────────────────────────────────────────────────────────────┘

  TOP 5 PRODUCTOS DEL TURNO
  ┌────────────────────────────────┬──────────────────────────────────┐
  │ 1. Bandeja Paisa      18u     │ 2. Churrasco 350g       14u      │
  │ 3. Mojito             11u     │ 4. Arepa con Hogao       9u      │
  │ 5. Limonada Natural    8u     │                                   │
  └────────────────────────────────┴──────────────────────────────────┘

  NOTA DEL CAJERO
  "Día tranquilo. La terraza estuvo llena todo el mediodía. 
   Sistema funcionó bien, sin incidentes."

  [⬇ Exportar PDF]
```

---

## 9. Módulo 6 — Configuración `/configuracion`

### Descripción

Ajustes de configuración del restaurante post-wizard. Algunos campos estaban en el wizard; aquí se pueden modificar en cualquier momento. El modo QR y las estaciones de cocina tienen restricciones si hay sesiones activas.

### Wireframe

```
Configuración
═══════════════════════════════════════════════════════════════════

  BRANDING
  ─────────────────────────────────────────────────────────────────
  Nombre del restaurante:   [La Parrilla              ] [Guardar]
  Color primario:           [#C0392B ████              ] [Guardar]
  Logo (PNG/JPG, máx 2MB):
    [🖼 logo-parrilla.png · Subido el 15 May]   [Cambiar logo]
    Vista previa del QR con logo: [🔲 con logo centrado]

  ─────────────────────────────────────────────────────────────────
  MODO DE OPERACIÓN
  ─────────────────────────────────────────────────────────────────
  Modo QR:          ● FIXED  ○ DYNAMIC     (no editable con sesiones activas)
  PIN requerido:    ☑ Sí                   [Guardar]
  Modo de atención: ● STAFFED  ○ SELF_SERVICE

  ─────────────────────────────────────────────────────────────────
  HORARIOS DE ATENCIÓN (solo para SELF_SERVICE — QR activo fuera de horario = 404)
  ─────────────────────────────────────────────────────────────────
  Lunes:     ☑ 10:00 AM — 10:00 PM
  Martes:    ☑ 10:00 AM — 10:00 PM
  Miércoles: ☑ 10:00 AM — 10:00 PM
  Jueves:    ☑ 10:00 AM — 11:00 PM
  Viernes:   ☑ 12:00 PM — 12:00 AM
  Sábado:    ☑ 12:00 PM — 12:00 AM
  Domingo:   ☐ Cerrado
                                               [Guardar horarios]

  ─────────────────────────────────────────────────────────────────
  ESTACIONES DE COCINA
  ─────────────────────────────────────────────────────────────────
  ┌────────────────────┬────────────────────────────────────┬──────────┐
  │ Estación           │ Tipo de ítem asignado              │ Acción   │
  ├────────────────────┼────────────────────────────────────┼──────────┤
  │ Cocina caliente    │ Platos Principales, Entradas       │ [···]    │
  │ Barra              │ Bebidas, Postres                   │ [···]    │
  └────────────────────┴────────────────────────────────────┴──────────┘
                                               [+ Agregar estación]

  ─────────────────────────────────────────────────────────────────
  IMPRESORA DE CAJA (ESC/POS)
  ─────────────────────────────────────────────────────────────────
  Estado:    ⛔ Sin impresora configurada
  [Conectar impresora →]  (abre diálogo WebUSB — solo Chrome)

  ── Cuando hay impresora conectada: ──────────────────────────────
  Estado:    ✅ Epson TM-T20 · USB
  [Imprimir recibo de prueba]   [Desconectar]

  ─────────────────────────────────────────────────────────────────
  PROGRAMA DE LEALTAD MESTO (solo lectura — gestionado por Mesto)
  ─────────────────────────────────────────────────────────────────
  Estado:          ✅ Contrato activo desde 01-May-2026
  Tasa:            1 punto por cada $1.000 COP gastados
  Ítems elegibles: Café Americano (250 pts) · Agua Cristal (100 pts)
  Settlement:      Neteado en tu billing mensual

  ¿Quieres unirte al Programa de Lealtad?  Contáctanos: ops@mesto.app
```

---

## 10. Flujo del Cajero — App de Caja (staff.mesto.app)

### Descripción

La vista del cajero en `staff.mesto.app` es la pantalla principal de gestión de sesiones y cobros. El cajero tiene acceso a la lista de mesas activas, puede ver los pedidos de cada sesión, cobrar, dividir la cuenta y cerrar la sesión.

### 10.1 — Vista de Sesiones Activas

```
Caja — La Parrilla · María López                 Turno: 09:00 - activo  [⏹ Cerrar turno]
═══════════════════════════════════════════════════════════════════════════════════════════

  Sesiones activas                                          [↻ Actualizar]

  ┌────────┬────────────┬───────────┬────────────┬──────────────────┬──────────────┐
  │ Mesa   │ Tiempo     │ Partic.  │ Pedidos    │ Total actual     │ Acción       │
  ├────────┼────────────┼───────────┼────────────┼──────────────────┼──────────────┤
  │ Mesa 2 │ 2h 14min   │ 3         │ 4 pedidos  │ $112.000         │ [Cobrar]     │
  │ Mesa 3 │ 45min      │ 1         │ 1 pedido   │ $35.000          │ [Cobrar]     │
  │ Mesa 7 │ 12min      │ 2         │ 0 pedidos  │ $0               │ [Ver]        │
  └────────┴────────────┴───────────┴────────────┴──────────────────┴──────────────┘

  3 sesiones activas  |  GMV pendiente de cobro: $147.000
```

### 10.2 — Detalle de Sesión y Flujo de Cobro

Al tocar `[Cobrar]` en Mesa 2:

```
Cobro — Mesa 2                                               [← Volver a sesiones]
══════════════════════════════════════════════════════════════════════════════════════

  RESUMEN DE LA SESIÓN
  ┌─────────────────────────────────────────────────────────────────────────────┐
  │  Participantes: 3                      Tiempo abierta: 2h 14min            │
  │  Pedidos: 4 (todos entregados)         Subtotal: $112.000                  │
  └─────────────────────────────────────────────────────────────────────────────┘

  VISTA DE LA CUENTA
  ──────────────────────────────────────────────────────────────────────────────
  ○ Cuenta completa (cobrar todo junto)
  ○ Por participante (split-bill)
  ──────────────────────────────────────────────────────────────────────────────

  CUENTA COMPLETA
  ┌─────────────────────────────────────────────────────────────────────────────┐
  │  Bandeja Paisa × 2              $56.000                                    │
  │  Churrasco 350g × 1             $35.000                                    │
  │  Mojito × 2                     $21.000                                    │
  │  ─────────────────────────────────────────                                 │
  │  Subtotal                      $112.000                                    │
  │  Propina               [10%]  [15%]  [20%]  [Otra ____]   + $0            │
  │  ─────────────────────────────────────────                                 │
  │  TOTAL A COBRAR                $112.000                                    │
  └─────────────────────────────────────────────────────────────────────────────┘

  MÉTODO DE PAGO
  [ 💵 Efectivo ]  [ 💳 Tarjeta ]  [ 📱 Billetera ]  [ ⚡ Mixto ]

  ── Si [Cortesía]: marcar ANTES de confirmar el pago ──
  ☐ Marcar esta sesión como cortesía (sin costo para el cliente)
  ─────────────────────────────────────────────────────────────────────────────

  [ Confirmar pago ]
```

### 10.3 — Split-Bill (cobro por participante)

Al seleccionar "Por participante":

```
Split-bill — Mesa 2                                          [← Cuenta completa]
══════════════════════════════════════════════════════════════════════════════════════

  Asigna cada pedido a un participante (o déjalo como "compartido"):

  PEDIDO #4412 — Bandeja Paisa × 2 · $56.000
  Asignar a: ○ Persona 1  ○ Persona 2  ○ Persona 3  ● Compartido

  PEDIDO #4415 — Churrasco 350g · $35.000
  Asignar a: ○ Persona 1  ● Persona 2  ○ Persona 3  ○ Compartido

  PEDIDO #4418 — Mojito × 2 · $21.000
  Asignar a: ○ Persona 1  ○ Persona 2  ○ Persona 3  ● Compartido

  ─────────────────────────────────────────────────────────────────────────
  Lo "compartido" ($77.000) se divide en partes iguales entre los 3:  $25.667 c/u

  RESUMEN POR PERSONA:
  ┌──────────────┬────────────────────────────────────────────────────────┐
  │ Persona 1    │ $25.667 (compartido) + $0 = $25.667                   │
  │ Persona 2    │ $25.667 (compartido) + $35.000 = $60.667              │
  │ Persona 3    │ $25.667 (compartido) + $0 = $25.667                   │
  └──────────────┴────────────────────────────────────────────────────────┘
  Total: $112.001  (diferencia de $1 por redondeo → se ajusta en Persona 1)

  [← Modificar asignación]               [Confirmar distribución →]
```

Una vez confirmada la distribución, el cajero cobra a cada persona por separado:

```
Cobro — Persona 2 de 3 · Mesa 2
══════════════════════════════════════════════════════════════════════════

  ┌─────────────────────────────────────────────────────────────────────┐
  │  Churrasco 350g              $35.000                               │
  │  Compartido (1/3 de $77.000) $25.667                               │
  │  ─────────────────────────────────────                             │
  │  Subtotal                   $60.667                                │
  │  Propina    [10%]  [15%]  [20%]  [Otra ____]   + $0               │
  │  ─────────────────────────────────────                             │
  │  TOTAL A COBRAR             $60.667                                │
  └─────────────────────────────────────────────────────────────────────┘

  MÉTODO DE PAGO
  [ 💵 Efectivo ]  [ 💳 Tarjeta ]  [ 📱 Billetera ]  [ ⚡ Mixto ]

  [ Confirmar pago de Persona 2 ]

  ── Personas cobradas: 1 / 3  ────────────────────────────────────────
  ✅ Persona 1: $25.667  —  💵 Efectivo
  ⏳ Persona 2: $60.667  —  pendiente ← aquí
  ⏳ Persona 3: $25.667  —  pendiente
```

### 10.4 — Pago en Efectivo (con cálculo de vuelto)

```
Pago en efectivo — $60.667
══════════════════════════════════════════════════════════════════════════

  Total a cobrar:   $60.667

  Monto recibido:
  ┌───────────────────┐
  │ $ 80.000          │
  └───────────────────┘

  Vuelto a entregar:
  ╔═══════════════════╗
  ║   $ 19.333        ║
  ╚═══════════════════╝

  Atajos:
  [$60.000] [$70.000] [$80.000] [$100.000] [Exacto]

  [ ← Atrás ]     [ Confirmar pago ]
```

### 10.5 — Pago Mixto

```
Pago mixto — $60.667
══════════════════════════════════════════════════════════════════════════

  Agrega los métodos y montos:

  ┌──────────────────┬──────────────┬────────┐
  │ Método           │ Monto        │        │
  ├──────────────────┼──────────────┼────────┤
  │ 💳 Tarjeta       │ $40.000      │ [✕]    │
  │ 💵 Efectivo      │ $20.667      │ [✕]    │
  └──────────────────┴──────────────┴────────┘
  [+ Agregar método]

  Total asignado:  $60.667  ✅
  Pendiente:       $0

  [← Atrás]    [Confirmar pago mixto]
```

Reglas del pago mixto:
- La suma de todos los métodos debe igualar el total a cobrar
- Si hay efectivo en el mixto, el campo de "monto recibido" muestra el vuelto correspondiente solo a la porción en efectivo
- No se puede confirmar si `suma_métodos ≠ total`

### 10.6 — Cortesía

La cortesía se marca **antes** de confirmar el pago de una sesión (checkbox visible en la pantalla de cobro). Si se marca:

- El total a cobrar pasa a $0
- Los métodos de pago se deshabilitan
- El cajero confirma con botón `[Confirmar cortesía]`
- En el reporte de turno queda como: "Cortesía — Mesa 2 — $112.000"

Una sesión marcada como cortesía no genera puntos de lealtad para ningún participante.

### 10.7 — Propina

La propina es opcional e independiente por persona (en split-bill) o por cuenta (en cuenta completa):

| Botón | Cálculo |
|---|---|
| `[10%]` | `floor(subtotal × 0.10)` |
| `[15%]` | `floor(subtotal × 0.15)` |
| `[20%]` | `floor(subtotal × 0.20)` |
| `[Otra ____]` | Cajero ingresa monto libre en COP |

- La propina **no genera puntos de lealtad** (solo el subtotal de productos)
- La propina queda registrada en el reporte de turno (total + promedio)

### 10.8 — Impresión del Recibo (Impresora Térmica ESC/POS)

Al confirmar el pago (o la cortesía), aparece una pantalla de confirmación con opción de imprimir:

```
Pago confirmado ✅ — Mesa 2
══════════════════════════════════════════════════════════════════════════

  ✅ $112.000 cobrado vía Tarjeta

  ┌──────────────────────────────────────────────────────────────────┐
  │  [🖨 Imprimir recibo]     [Omitir e ir a sesiones]               │
  └──────────────────────────────────────────────────────────────────┘
```

El contenido del recibo impreso (ticket de 80mm):

```
        LA PARRILLA
      Bogotá, Colombia
   ────────────────────
   Mesa 2  |  07-Jun 17:28
   Cajero: María López
   ────────────────────
   Bandeja Paisa x2     $56.000
   Churrasco 350g x1    $35.000
   Mojito x2            $21.000
   ────────────────────
   Subtotal            $112.000
   Propina               $16.800
   ────────────────────
   TOTAL               $128.800
   Método: Tarjeta
   ════════════════════
   ¡Gracias por tu visita!
   mesto.app/la-parrilla
```

**Integración técnica — impresora térmica:**

| Propiedad | Valor |
|---|---|
| Protocolo | ESC/POS (estándar de facto en impresoras térmicas de punto de venta) |
| Interfaz | WebUSB API (Chrome 61+) para conexión directa desde el navegador; no requiere driver |
| Alternativa | QZ Tray (daemon local) si el hardware no soporta WebUSB |
| Conexión | USB o Bluetooth (modelos compatibles: Epson TM-T20, Star TSP100, Bixolon) |
| Ancho de papel | 80mm (estándar) |
| Librería | `escpos-buffer` (NPM) — generación del buffer ESC/POS en el frontend |
| Configuración | El Admin configura la impresora desde `/configuracion` → "Impresora de caja" → botón "Conectar" (abre diálogo WebUSB) |
| Sin impresora | Si no hay impresora conectada, el botón `[Imprimir recibo]` está deshabilitado con tooltip "Sin impresora configurada" |

La impresora se configura una vez en el dispositivo del cajero. No se requiere configuración por sesión.

---

### 10.9 — Cierre de Turno

Al tocar `[⏹ Cerrar turno]`:

```
Cierre de turno
══════════════════════════════════════════════════════════════════════════

  ⚠ Hay 2 sesiones activas. Si cierras el turno, estas sesiones quedarán
    abiertas hasta que el siguiente cajero las gestione.

  RESUMEN DEL TURNO (09:00 - ahora 17:28)
  GMV cobrado:       $842.000
  Pedidos:           47
  Sesiones cerradas: 18
  Cortesías:         2 ($56.000)
  Propina total:     $63.000

  Nota del turno (opcional — máx. 500 caracteres):
  ┌──────────────────────────────────────────────────────────────────┐
  │ Día tranquilo. La terraza estuvo llena todo el mediodía...      │
  └──────────────────────────────────────────────────────────────────┘

  [← Cancelar]        [Confirmar cierre de turno]
```

Al confirmar:
- El reporte de turno queda registrado y disponible para el dueño en `/reportes/turnos`
- El JWT del cajero sigue válido hasta que expire (8h); el "turno" es solo un registro
- Los turnos no tienen una hora forzada de cierre — el cajero los cierra manualmente

---

## 11. Requisitos Funcionales

### RF-1601 — Dashboard en tiempo real
- El endpoint `GET /api/v1/analytics/dashboard` devuelve métricas del día y del período seleccionado
- "Ahora mismo": consulta Redis `table:{tid}:{tableCode}:active` para mesas activas + Orders DB para pedidos en curso
- "Top 5 productos": calculado del aggregate de pedidos del día (Orders DB); TTL de caché 5 min
- Las alertas de stock se calculan al cargar el dashboard; no se recalculan en tiempo real

### RF-1602 — Gestión de menú
- Cambios de precio, disponibilidad, imágenes e imágenes aplican inmediatamente al guardar
- Al guardar cualquier cambio, invalida Redis `catalog:{tenantId}`
- La imagen principal (index 0) es la que se muestra en listados; las demás en carrusel
- Soft-delete de productos: `PUT /api/v1/catalog/{tenantId}/items/{itemId}` con `{ deleted: true }`

### RF-1603 — Gestión de mesas
- Desactivar una mesa: `PATCH /api/v1/tables/{tenantId}/{tableCode}` con `{ active: false }`
- Si la mesa tiene sesión activa, el endpoint devuelve 200 con warning en el body, no 400; la desactivación procede igual
- La descarga de QRs individuales genera el PNG en el backend con la imagen del logo (si está configurado) centrada en el QR
- **[v1.4]** El módulo `/mesas` entregado es el **editor del plano canónico** (mover con guardado en lote, alta por tipos del local, forma/plazas, borrado). **Leer** el plano está abierto a todo el personal autenticado; **editarlo** sigue siendo de Administración. Ver PRD-004 RF-411 y PRD-003 RF-317.

### RF-1604 — Gestión de staff
- Invitación: `POST /api/v1/identity/staff/invite` → genera activation token, almacena en Redis `activation:{token}` TTL 48h
- Resetear contraseña: `POST /api/v1/identity/staff/{userId}/reset-password` → envía email con reset token (TTL 1h)
- Cambiar rol: `PATCH /api/v1/identity/staff/{userId}` con `{ role: "..." }` → invalida Redis `auth_all_invalidated:{userId}`

### RF-1605 — Flujo de cobro del cajero
- Abrir cobro: `GET /api/v1/sessions/{tenantId}/{sessionId}/payment-summary` → devuelve pedidos, participantes, subtotal por participante
- Confirmar pago de cuenta completa: `POST /api/v1/payments/sessions/{sessionId}/charge` con `{ method, amount, tip, courtesy }`
- Confirmar pago split-bill: múltiples llamadas a `POST /api/v1/payments/sessions/{sessionId}/charge-participant` con `{ participantId, method, amount, tip }`
- Todos los campos de pago se validan en el backend: `sum(methods) == total` obligatorio
- La cortesía: `POST /api/v1/payments/sessions/{sessionId}/mark-courtesy` cierra la sesión sin cargo y registra en turno

### RF-1606 — Cierre de turno
- `POST /api/v1/shifts/{tenantId}/close` con `{ note }` → agrega `closed_at`, calcula agregados, persiste en DB
- El reporte de turno incluye: GMV, pedidos, sesiones, cortesías (cantidad + valor), propina, top 5 productos del turno, nota del cajero
- El turno queda visible en `GET /api/v1/shifts/{tenantId}` con filtros por cajero y rango de fechas

### RF-1607 — Observación de la comanda en la caja [v1.4]
- La cuenta de una sesión muestra, junto a cada pedido, la observación que dejó el comensal o el comandero
- Es informativa para el cajero (alergias, "todo junto", incidencias del servicio); no altera importes ni el flujo de cobro
- Origen del dato en PRD-002 RF-207

### RF-1608 — Modo tablero en caja e historial [v1.4]
- Las vistas de caja e historial ofrecen pantalla completa, igual que el resto de módulos de staff (PRD-003 RF-315)
- Pensado para el TPV o la tablet fija del mostrador: la preferencia se recuerda entre recargas
- Los avisos y confirmaciones del cobro usan diálogos propios de la app, nunca los del navegador

---

## 12. Requisitos Técnicos

### RT-1601 — Performance
- Dashboard carga en < 1 segundo (queries con índices, TTL en Redis para métricas agregadas del día)
- Lista de productos del menú carga en < 500ms (servida desde Redis con TTL 5 min)
- Las imágenes del menú son lazy-loaded en el cliente (no se cargan hasta estar en el viewport)

### RT-1602 — Imágenes de productos
- Almacenadas en Azure Blob Storage
- Redimensionadas automáticamente a 800×600px al subir (thumbnail 200×150px para listados)
- Formatos aceptados: JPG, PNG, WebP; máximo 2MB por imagen antes de compresión

### RT-1603 — Endpoints del Admin Panel (JWT `RestaurantAdmin` o `RestaurantCajero` según acción)

```
GET    /api/v1/analytics/dashboard?tenantId={id}&period={mes/semana/hoy}
GET    /api/v1/analytics/history?tenantId={id}&from={date}&to={date}
GET    /api/v1/catalog/{tenantId}/categories
POST   /api/v1/catalog/{tenantId}/categories
PATCH  /api/v1/catalog/{tenantId}/categories/{catId}
DELETE /api/v1/catalog/{tenantId}/categories/{catId}
GET    /api/v1/catalog/{tenantId}/items
POST   /api/v1/catalog/{tenantId}/items
PATCH  /api/v1/catalog/{tenantId}/items/{itemId}
DELETE /api/v1/catalog/{tenantId}/items/{itemId}
POST   /api/v1/catalog/{tenantId}/items/{itemId}/images
DELETE /api/v1/catalog/{tenantId}/items/{itemId}/images/{imageIndex}
GET    /api/v1/tables/{tenantId}
POST   /api/v1/tables/{tenantId}
PATCH  /api/v1/tables/{tenantId}/{tableCode}
GET    /api/v1/identity/staff?tenantId={id}
POST   /api/v1/identity/staff/invite
PATCH  /api/v1/identity/staff/{userId}
POST   /api/v1/identity/staff/{userId}/reset-password
POST   /api/v1/identity/staff/{userId}/resend-invitation
GET    /api/v1/shifts/{tenantId}
GET    /api/v1/shifts/{tenantId}/{shiftId}
POST   /api/v1/shifts/{tenantId}/close
GET    /api/v1/sessions/{tenantId}
GET    /api/v1/sessions/{tenantId}/{sessionId}/payment-summary
POST   /api/v1/payments/sessions/{sessionId}/charge
POST   /api/v1/payments/sessions/{sessionId}/charge-participant
POST   /api/v1/payments/sessions/{sessionId}/mark-courtesy
GET    /api/v1/config/{tenantId}
PATCH  /api/v1/config/{tenantId}
```

---

## 13. Criterios de Aceptación

- [ ] El dashboard carga el estado "Ahora mismo" con datos reales en menos de 1 segundo
- [ ] El dashboard muestra el mesero asignado a cada sesión activa en tiempo real
- [ ] El dueño puede agregar un producto con 3 imágenes y precio y verlo publicado en el menú del cliente en menos de 30 segundos
- [ ] Marcar un producto como "agotado" lo deshabilita inmediatamente en el menú del cliente (caché invalidado)
- [ ] El dueño puede invitar un nuevo cajero y el staff recibe el email de activación en menos de 1 minuto
- [ ] Solo el Admin puede resetear contraseñas y cambiar roles; un Cajero que intente no ve esas opciones (403 en API)
- [ ] El cajero puede cobrar una mesa de 3 personas con split-bill y 3 métodos de pago diferentes
- [ ] El cálculo de vuelto en efectivo es correcto al centavo
- [ ] El monto total del pago mixto debe igualar el total de la cuenta; si no, el botón de confirmar está deshabilitado
- [ ] Una sesión marcada como cortesía cierra con monto $0 y aparece separada en el reporte de turno
- [ ] La propina no genera puntos de lealtad; se calcula sobre el subtotal sin el ítem canjeado
- [ ] Al confirmar el pago aparece la opción de imprimir recibo; si no hay impresora configurada el botón está deshabilitado
- [ ] El recibo impreso incluye: nombre del restaurante, mesa, cajero, ítems, subtotal, propina y total
- [ ] El reporte de turno incluye top 5 productos, nota del cajero, cortesías y totales por método de pago
- [ ] La descarga del QR de una mesa genera un PNG con el logo del restaurante centrado en el código
- [ ] La exportación CSV de Analytics incluye todos los pedidos del rango de fechas seleccionado

---

## 14. Open Questions

| # | Pregunta | Responsable | Estado |
|---|---|---|---|
| OQ-1601 | ¿El dueño puede ver en el dashboard qué mesero está en qué mesa en este momento? | Product | **Resuelto**: Sí. La tabla "Sesiones en curso ahora" del dashboard incluye columna Mesero. Datos actualizados por polling cada 60s. |
| OQ-1602 | ¿El cajero puede emitir un recibo impreso al cliente (integración con impresora térmica)? | Product + Tech | **Resuelto**: Sí. Integración ESC/POS vía WebUSB API (Chrome). Librería `escpos-buffer`. El Admin configura la impresora una vez en `/configuracion`. Al confirmar el pago aparece opción `[Imprimir recibo]`. Ver §10.8. |
| OQ-1603 | ¿El dueño puede exportar el historial completo de pedidos a CSV desde Analytics? | Product | **Resuelto**: Sí. En `/reportes` → Analytics → `[Exportar CSV]` con filtros de fecha. El CSV incluye: fecha, mesa, ítems, subtotal, propina, método de pago, cajero. |
| OQ-1604 | ¿El cliente que redime puntos (cortesía parcial del programa de lealtad) puede también dar propina? | Product | **Resuelto**: Sí. El ítem canjeado sale a $0; la propina se calcula sobre el subtotal de los demás ítems normalmente. El cajero ve en pantalla el subtotal sin el ítem canjeado antes de calcular propina. |
| OQ-204 | ¿El cliente recibe un comprobante digital (email/WhatsApp) al pagar? | Product | **Resuelto**: Sin comprobante digital en MVP. El pago es en caja (físico). Si se desea un recibo, el cajero lo imprime con la impresora térmica (OQ-1602). Comprobante digital automático en v2 con pagos digitales. |
| OQ-401 | ¿Se ofrecen templates de menú prediseñados en el wizard? | Product | **Resuelto**: No en MVP. El menú se crea desde cero en el wizard. Templates prediseñados en v2. |


---

*PRD-016 — Admin Panel del Restaurante y Flujo del Cajero*
*Versión 1.4 — Agosto 2026 — El módulo `/mesas` entregado es el editor del plano canónico (nota en §6, RF-1603); observación de la comanda visible en la cuenta (RF-1607) y modo tablero en caja e historial (RF-1608). Refs PRD-004 v3.3, PRD-003 v3.2, PRD-002 v3.2, PRD-018 v1.1, ADR-033/ADR-034*
*Versión 1.3 — Junio 2026 — Acción "Resetear contraseña" sobre personal activo (genera enlace de restablecimiento que el Admin comparte; ver PRD-014 §5.1). Base v1.2: modal de alta con selector de modo de acceso (acceso directo con clave temporal / enlace de activación, PRD-014 §4.1). OQs cerrados: 204 (sin comprobante digital MVP), 401 (sin templates MVP), 1601 (mesero en dashboard), 1602 (impresora ESC/POS WebUSB), 1603 (exportar CSV analytics), 1604 (propina sobre subtotal sin canjeado)*
