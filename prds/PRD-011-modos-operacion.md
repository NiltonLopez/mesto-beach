# PRD-011 — Modos de Operación

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
| **ID** | PRD-011 |
| **Versión** | 2.1 |
| **Estado** | Aprobado — v2.1 |
| **Fecha** | Junio 2026 |
| **Apps relacionadas** | `customer`, `staff` (cajero + mesero `/salon`), `admin` |
| **Módulos de API** | Sessions, Identity |
| **Depende de** | PRD-002 (flujo cliente), PRD-003 (cajero/mesero), PRD-004 (configuración) |
| **PRDs que dependen de este** | PRD-008 (notificaciones de sesión), PRD-010 (seguridad PIN e invite link), PRD-013 (operaciones) |

---

## 1. Propósito y Objetivos

### Propósito

Definir en detalle los modos de operación de Mesto: tipo de QR (FIJO o DINÁMICO), modo de sesión (STAFFED o SELF_SERVICE) y requerimiento de PIN. Este PRD es la fuente de verdad para cualquier decisión de diseño o implementación relacionada con el acceso a sesiones de mesa.

### Objetivos

| # | Objetivo | Métrica |
|---|---|---|
| OBJ-01 | El mesero puede crear una sesión y llevar el acceso a la mesa en menos de 30 segundos | Medido en pruebas |
| OBJ-02 | El cliente puede navegar el menú completo sin fricciones antes de necesitar el PIN | 0 bloqueos antes de llegar al carrito |
| OBJ-03 | El invite link funciona para unir a personas remotas sin teclear el PIN | Probado en grupos de WhatsApp |
| OBJ-04 | El cajero es el único que puede regenerar el PIN | Verificado en tests de autorización |

---

## 2. Las Tres Configuraciones del Sistema QR

El comportamiento de acceso a la sesión se determina así:

| Campo | Opciones | Default | Dónde vive |
|---|---|---|---|
| `qrMode` | `FIXED` / `DYNAMIC` | `FIXED` | **Por mesa** (`tables.qr_mode`). FIXED: QR permanente impreso en la mesa. DYNAMIC: QR generado por sesión, mostrado/impreso por el staff. |
| `requiresPin` | `true` / `false` | `true` | **Por mesa** (`tables.requires_pin`). Si es true, el PIN de 4 dígitos se exige al confirmar el primer pedido. N/A para DYNAMIC. |
| `operationMode` | `STAFFED` / `SELF_SERVICE` | `STAFFED` | Tenant. STAFFED: el staff abre la sesión. SELF_SERVICE: el cliente la abre al escanear el QR fijo. |

### Tipo de QR / acceso por mesa [v2.1]

El **administrador define el tipo de QR por cada mesa** en `admin → Mesas` (no es ya una config global del tenant). Tres opciones que mapean a `(qrMode, requiresPin)`:

| Opción (admin) | qrMode | requiresPin | Acceso del cliente |
|---|---|---|---|
| **QR Fijo (con PIN)** | FIXED | true | QR impreso reusable; el cliente pide el PIN al confirmar. |
| **QR Dinámico** | DYNAMIC | false | Un QR nuevo por sesión (impreso/mostrado por el staff); sin PIN. |
| **Sin seguridad** | FIXED | false | QR fijo **abierto**: ni PIN ni nada; el cliente escanea y ordena directo. |

**Reglas de compatibilidad:**
- `DYNAMIC` siempre implica `STAFFED` y nunca exige PIN (el QR de un solo uso ya autentica la mesa).
- `SELF_SERVICE` solo está disponible con `FIXED`.
- Al abrir una sesión, **el staff ya no elige el tipo de QR**: el backend lo deriva de la mesa. Fijo y "Sin seguridad" → la sesión se crea de inmediato; Dinámico → tras crearla se ofrece imprimir/mostrar el QR de la mesa.

---

## 3. Matriz de Flujos

| qrMode | operationMode | pinRequired | ¿Quién crea la sesión? | Cómo entra y ordena el cliente |
|---|---|---|---|---|
| FIXED | STAFFED | true | Mesero | QR fijo → navega menú → PIN al confirmar |
| FIXED | STAFFED | false | Mesero | QR fijo → navega y ordena directo |
| FIXED | SELF_SERVICE | true | Cliente (al escanear) | QR fijo → navega → mesero viene → PIN al confirmar |
| FIXED | SELF_SERVICE | false | Cliente (al escanear) | QR fijo → navega y ordena directo |
| DYNAMIC | STAFFED | true | Mesero | QR dinámico del mesero → navega → PIN al confirmar |
| DYNAMIC | STAFFED | false | Mesero | QR dinámico del mesero → navega y ordena directo |

---

## 4. Definición de Modos

### FIXED QR

El QR está impreso permanentemente en la mesa. Nunca cambia. La URL es siempre `mesto.app/[slug]?t=[tableCode]`.

La seguridad no proviene del QR (que es público) sino del PIN de 4 dígitos que el mesero lleva a la mesa, o del invite link que solo pueden generar los participantes ya adentro.

**Ideal para:** cualquier tipo de restaurante que quiera imprimir QRs una sola vez.

### DYNAMIC QR

No hay QR permanente en la mesa. El mesero crea la sesión desde su app y se genera un QR único para esa sesión. El mesero lo muestra en su pantalla al cliente, o el cajero lo imprime desde su panel.

El QR dinámico expira cuando la sesión se cierra. Una foto del QR de una visita anterior no sirve para nada.

**Ideal para:** restaurantes con alta rotación que quieren seguridad máxima sin depender del PIN.

### STAFFED

El mesero (o el cajero) crea la sesión explícitamente desde su app. Ningún cliente puede iniciar una sesión por su cuenta.

### SELF_SERVICE

El primer cliente que escanea el QR fijo de una mesa sin sesión activa crea la sesión automáticamente. El mesero recibe una notificación y va a la mesa a entregar el PIN (si está configurado).

---

## 5. Flujo del PIN

### ¿Cuándo se pide el PIN?

El PIN **no** se pide al entrar a la sesión. Se pide al **confirmar el primer pedido** del participante.

```
Cliente escanea QR → ingresa nombre → ve menú completo → agrega productos al carrito
→ toca "Confirmar pedido" → pantalla: "Ingresa el PIN que te dió el mesero"
→ ingresa PIN correcto → pedido enviado ✓
→ pedidos posteriores del mismo participante: sin PIN (ya validado)
```

Esta decisión de diseño permite que el cliente explore el menú y decida qué pedir sin presión, mientras el mesero llega a la mesa con el PIN.

### ¿Cómo llega el PIN al cliente?

```
Mesero crea sesión → PIN de 4 dígitos aparece en su app:

┌─────────────────────────┐
│   Mesa 5                │
│                         │
│    4  8  2  1           │  ← grande, legible
│                         │
│   [Mostrar / Ocultar]   │
└─────────────────────────┘

Mesero va físicamente a la mesa → muestra su pantalla al cliente
Cliente ingresa el PIN en su celular al confirmar el pedido
```

### Regeneración del PIN (solo cajero)

Si el cajero sospecha que el PIN fue comprometido, puede regenerarlo desde su panel:
- Se genera un nuevo PIN de 4 dígitos
- El PIN anterior queda inválido inmediatamente
- Todos los invite links existentes de esa sesión quedan inválidos
- Los participantes ya adentro **no son afectados** — solo los que intenten confirmar un nuevo primer pedido necesitarán el nuevo PIN

---

## 6. Flujo del Invite Link

```
Participante dentro de la sesión toca "Compartir invitación"
→ Se genera un link: mesto.app/join?i={TOKEN_FIRMADO}
→ Se comparte por WhatsApp, SMS, o se copia al portapapeles

Destinatario toca el link
→ Solo ingresa su nombre
→ PIN no es requerido (el token ya lo valida internamente)
→ Entra a la sesión
→ Notificación a todos: "María se unió (por invitación)"
```

**Propiedades del link:**
- El token es un JWT firmado — imposible de adivinar
- Un solo token por sesión, reutilizable para múltiples personas
- Expira cuando la sesión se cierra o cuando el cajero regenera el PIN
- No expone el PIN en la URL

---

## 7. Roles del Staff en la Sesión

| Acción | Mesero | Cajero |
|---|---|---|
| Crear sesión (STAFFED) | ✅ sus mesas asignadas | ✅ cualquier mesa |
| Ver PIN activo | ✅ sus mesas | ✅ todas |
| Mostrar QR dinámico en app | ✅ | ✅ |
| Imprimir QR dinámico | ❌ | ✅ |
| **Regenerar PIN** | ❌ | ✅ único responsable |
| Cerrar sesión | ❌ | ✅ |
| Procesar pagos | ❌ | ✅ |
| Ver pedidos de cualquier mesa | ✅ | ✅ |
| Cancelar pedidos en estado Nuevo | ❌ | ✅ |
| Forzar ingreso de participante | ❌ | ✅ |
| Vista global de todas las mesas | ❌ (solo asignadas) | ✅ |

---

## 8. Historias de Usuario

### Épica 1: Mesero crea sesión y entrega PIN

```
US-1101
Como mesero,
quiero crear la sesión de una mesa desde mi app y ver el PIN generado,
para ir a la mesa y dárselo al cliente sin necesidad de pasar por la caja.

Criterio de aceptación:
- En mi app hay una lista de las mesas que tengo asignadas
- Al tocar una mesa libre puedo tocar "Abrir mesa"
- Inmediatamente aparece el PIN en pantalla grande (legible a distancia)
- El PIN se mantiene visible mientras la mesa esté abierta
- Puedo ocultar/mostrar el PIN con un botón (para privacidad al mostrarlo)

US-1102
Como mesero en un restaurante con QR dinámico,
quiero que al abrir la sesión aparezca el QR en mi pantalla para mostrárselo al cliente,
para que el cliente no necesite buscar ningún QR impreso.

Criterio de aceptación:
- Al abrir la mesa en modo DYNAMIC, mi app muestra un QR grande en pantalla
- El QR es escaneable desde el celular del cliente a 30 cm de distancia
- El cajero puede imprimir el mismo QR desde su panel si se necesita
- Al cerrar la sesión, el QR ya no funciona

US-1103
Como mesero,
quiero recibir una notificación cuando un cliente crea una sesión automáticamente
en modo SELF_SERVICE en una de mis mesas,
para ir a entregarle el PIN lo antes posible.

Criterio de aceptación:
- La notificación llega a mi app en menos de 2 segundos
- Muestra: número de mesa y hora de creación
- Al tocarla me lleva directo al detalle de esa mesa con el PIN
- Si no tengo mesas asignadas, la notificación llega a todos los meseros disponibles
```

### Épica 2: PIN al confirmar pedido (cliente)

```
US-1104
Como cliente en una mesa con PIN requerido,
quiero poder navegar el menú completo y armar mi carrito sin necesitar el PIN,
para no ser bloqueado antes de saber qué quiero pedir.

Criterio de aceptación:
- Puedo ver el menú completo, los detalles de productos y las categorías sin PIN
- Puedo agregar productos al carrito sin PIN
- Solo al tocar "Confirmar pedido" se me pide el PIN
- El mensaje es claro: "Ingresa el PIN que te dio el mesero"
- Si el mesero aún no ha llegado, puedo volver al carrito y esperar

US-1105
Como cliente,
quiero que después de ingresar el PIN una vez, mis pedidos siguientes no lo requieran,
para no tener que ingresarlo cada vez que quiero pedir algo más.

Criterio de aceptación:
- El PIN solo se pide al confirmar el PRIMER pedido de mi participantId
- Los pedidos siguientes del mismo participante se confirman directamente
- Si me uno a una sesión por invite link, tampoco se me pide PIN en ningún pedido
```

### Épica 3: SELF_SERVICE

```
US-1106
Como cliente en un bar con modo Self-Service,
quiero poder escanear el QR y empezar a navegar el menú sin esperar a nadie,
para tener autonomía desde el momento en que me siento.

Criterio de aceptación:
- Al escanear el QR de una mesa sin sesión activa, la sesión se crea automáticamente
- Puedo ingresar mi nombre y navegar el menú de inmediato
- Si hay PIN requerido, el mesero llega a traerlo mientras yo navego el menú
- Si no hay PIN requerido, puedo confirmar mi primer pedido directamente

US-1107
Como cliente en modo Self-Service fuera del horario de atención,
quiero ver un mensaje claro de cuándo abre el restaurante,
para no quedarme esperando sin entender qué pasa.

Criterio de aceptación:
- La pantalla muestra: "Restaurante cerrado. Abrimos el [día] a las [hora]"
- Se calcula el próximo horario de apertura automáticamente
- No hay posibilidad de crear una sesión fuera del horario
```

### Épica 4: Invite Link

```
US-1108
Como participante dentro de una sesión activa,
quiero poder invitar a alguien que está llegando tarde enviándole un link por WhatsApp,
para que pueda unirse sin necesitar el PIN ni al mesero.

Criterio de aceptación:
- En la pantalla de la sesión hay un botón "Invitar al grupo"
- Al tocarlo se abre el share sheet nativo del celular (WhatsApp, SMS, copiar)
- La persona invitada solo necesita tocar el link e ingresar su nombre
- Al unirse, aparece una notificación a todos: "X se unió (por invitación)"
- El link funciona para múltiples personas, no es de un solo uso

US-1109
Como cajero,
quiero que al regenerar el PIN los links de invitación anteriores queden inválidos,
para que si el PIN fue comprometido, el acceso por link también sea revocado.

Criterio de aceptación:
- Al regenerar el PIN, todos los invite links activos de esa sesión quedan inválidos
- Si alguien toca un link viejo recibe: "Esta invitación ya no es válida. Pide una nueva al grupo."
- Los participantes ya adentro no son afectados
```

### Épica 5: Cajero — Control total

```
US-1110
Como cajero,
quiero poder regenerar el PIN de cualquier mesa activa,
para responder si sospecho que el PIN fue comprometido o compartido indebidamente.

Criterio de aceptación:
- En el detalle de cualquier mesa activa hay un botón "Regenerar PIN" (solo visible para cajero)
- Al regenerar, el nuevo PIN aparece en pantalla del cajero Y en la app del mesero asignado
- La acción se registra en el log de auditoría con timestamp y usuario
- Se muestra una confirmación: "¿Seguro? Los links de invitación anteriores quedarán inválidos"

US-1111
Como cajero,
quiero ver el PIN activo de cualquier mesa en mi panel,
para poder dárselo a un cliente si el mesero no está disponible.

Criterio de aceptación:
- En el detalle de cada mesa activa se muestra el PIN actual
- Hay un botón "Mostrar/Ocultar" para privacidad
- El PIN se actualiza en tiempo real si el mesero de la mesa lo regeneró
```

---

## 9. Protecciones de Seguridad

### PIN — Brute force

- Máximo 5 intentos de PIN incorrectos por dispositivo por sesión de 10 minutos
- Al superar el límite: `429 Too Many Requests` con `Retry-After: 600` (10 minutos)
- El contador se almacena en Redis: `pin_attempts:{tenantId}:{tableCode}:{deviceId}`
- El `deviceId` es un fingerprint generado en el frontend (no requiere autenticación)

### SELF_SERVICE — Rate limiting

- Máximo 5 sesiones nuevas por mesa por hora
- Al superar: `429 Too Many Requests`
- El cajero ve en su panel si una mesa está siendo abusada (alerta si supera 3 sesiones en 10 minutos)

### QR Dinámico — Expiración

- El QR dinámico tiene exactamente el mismo TTL que la sesión activa en Redis
- Al cerrar la sesión: `DEL qr:{tenantId}:{sessionId}:token`
- Un cliente que intenta usar un QR dinámico expirado recibe `410 Gone`

---

## 10. Criterios de Aceptación

- [ ] El mesero puede crear una sesión y ver el PIN en su app en menos de 3 segundos
- [ ] El cliente puede navegar el menú completo sin ingresar el PIN
- [ ] El PIN se pide exactamente al confirmar el primer pedido del participante
- [ ] Una vez ingresado el PIN correctamente, los pedidos siguientes no lo requieren
- [ ] Al unirse por invite link, nunca se requiere el PIN
- [ ] El cajero es el único usuario que puede regenerar el PIN (Mesero recibe 403)
- [ ] Al regenerar el PIN, los invite links anteriores retornan 410 Gone
- [ ] En SELF_SERVICE, el cliente puede crear sesión dentro del horario configurado
- [ ] Fuera del horario, el mensaje muestra la próxima hora de apertura
- [ ] 5 intentos fallidos de PIN bloquean el dispositivo por 10 minutos

---

## 11. Open Questions

| # | Pregunta | Responsable | Estado |
|---|---|---|---|
| OQ-1101 | ¿El PIN se configura también por zona, o siempre es a nivel de todo el restaurante? (v2) | Product | **Resuelto**: El PIN es **siempre por sesión** (mesa individual). No habrá PIN por zona en ninguna versión. Concepto de zona no aplica para Mesto. |
| OQ-1102 | ¿Los invite links pueden tener un límite de usos (ej: máx 10 personas)? | Product | **Resuelto**: El invite link **no tiene límite** de participantes. Cualquier número de personas puede unirse a la sesión mientras esté abierta. |
| OQ-1103 | ¿El mesero recibe el nuevo PIN automáticamente cuando el cajero regenera, o tiene que buscarlo? | Product + UX | **Resuelto**: El mesero recibe una notificación push en tiempo real vía SignalR (grupo `staff:{userId}`) con el nuevo PIN. No necesita buscarlo manualmente. |

---

*PRD-011 — Modos de Operación*
*Versión 2.0 — Junio 2026 — PIN de sesión, QR dinámico, invite link, roles mesero/cajero*
