# PRD-019 — Mesto Beach Night: modo fiesta

## Metadata

| Campo | Valor |
|---|---|
| **ID** | PRD-019 |
| **Versión** | 1.0 |
| **Estado** | Aprobado — v1.0; núcleo implementado (faltan los reportes por modo y la PWA) |
| **Fecha** | Octubre 2026 |
| **Apps relacionadas** | `menu` (comensal de noche, registro una vez por dispositivo, carta Night), `staff` (tablero de barra de 2 estados, runner con nombre y avisos, Recepción con PIN), `admin` (interruptor de modo con pre-flight y cuenta atrás, distribución Night, Apariencia Night, carta Día/Noche, estaciones, reportes por modo) |
| **Módulos de API** | Identity (modo del local, auditoría del cambio, estación del runner desde BD), Catalog (visibilidad por modo, branding Night), Sessions (distribución por modo, tipo de mesa, PIN de noche), Orders (perfil de flujo de estación, prioridad, enrutado por mesa, evento de cambio de modo), Analytics (reportes por modo) |
| **Depende de** | PRD-018 (modo beach club: sectores, estaciones, runners, viajes), PRD-003 (KDS y staff), PRD-002 (flujo del cliente), PRD-004 (configuración del restaurante y editor de plano), PRD-011 (modos de operación y PIN), PRD-016 (panel de administración y caja), ADR-028 (frontera fiscal: la cuenta se liquida en el TPV) |
| **PRDs que dependen de este** | — |

> **Qué es esto.** Un **modo** del mismo local, no un segundo tenant ni una bifurcación. Playa Demo es
> de día un restaurante (PRD-018) y de noche una fiesta; este PRD define cómo el administrador cambia el
> sistema entero de **«Mesto Beach»** a **«Mesto Beach Night»** y qué cambia en cada app cuando lo hace.
> La frontera fiscal **no se mueve**: la cuenta sigue abierta en Mesto y se liquida en el **TPV** (ADR-028).
>
> **Estado del documento.** El núcleo descrito aquí **ya está en código**; quedan los reportes por modo y
> la instalación como PWA. Ante cualquier duda, manda el código.

---

## 1. Propósito y Objetivos

### Propósito

Definir el **modo Night** de Mesto Beach: el estado del sistema en el que el local deja de ser restaurante y pasa a ser fiesta. Con un solo gesto de Administración —aviso, cuenta atrás de 5 segundos y cambio en **todo el local**— la carta pasa a ser la de noche, **Cocina y Parrilla se vacían y se apagan**, se encienden **dos barras** con un tablero de **dos estados**, la **distribución de mesas cambia** (la Playa cierra y las mesas se reparten entre barras), las comandas de los **reservados van primero**, el **runner entrega en mesa preguntando por el nombre** de quien pidió, el **PIN es obligatorio y se recuerda por dispositivo toda la noche**, y las tres apps pasan a una **estética oscura** con una transición tipo «agujero negro».

Todo se construye como **capacidades del núcleo** activables por tenant (ADR-023): el siguiente local con dos negocios en una sala no tendría que reescribir nada.

### Objetivos

| # | Objetivo | Métrica de éxito |
|---|---|---|
| OBJ-01 | El cambio de modo es rápido y no pierde trabajo | Cambio completo (API + las tres apps re-temadas) en **< 60 s** desde que Administración confirma, con **cero pedidos perdidos** o duplicados en la transición |
| OBJ-02 | Ninguna comanda llega a la mesa equivocada por culpa del sistema | **0** entregas a mesa equivocada atribuibles al sistema (mesa inexistente en el modo, barra incorrecta, parada sin destino) por noche |
| OBJ-03 | El runner siempre sabe a quién pregunta | El **100 %** de los ítems pedidos por el comensal desde el QR muestran su nombre en la parada; las comandas tomadas por la PDA o la caja se identifican como «Comanda de sala · {mesa}» |
| OBJ-04 | El PIN no se rompe a media noche | El PIN validado por un dispositivo sigue siendo válido **toda la noche** (≥ 12 h) sin volver a pedirse; **0** `PIN_REQUIRED` espurios tras una caída o limpieza de caché |

---

## 2. Contexto y Problema que Resuelve

Playa Demo opera **dos negocios en el mismo local**: restaurante de día y fiesta de noche. Hoy el sistema solo conoce el primero (PRD-018). A partir de cierta hora el local cambia de piel —otra carta, otras estaciones, otra distribución, otro ritmo— y el equipo lo resuelve a mano y de memoria. El cliente quiere que ese cambio sea **un gesto del administrador desde el panel**, que lo vean las tres apps a la vez y que nada de lo que pasa de noche contamine los datos del día.

### Lo que cambia de noche

| Ámbito | Día (PRD-018) | Noche (este PRD) |
|---|---|---|
| Carta | Platos de restaurante por estación | Carta de noche (bebidas, shots, botellas); lo del día desaparece |
| Estaciones | Cocina, Parrilla, Cocktails con tablero de 4 columnas | Cocina y Parrilla **apagadas**; **dos barras** con tablero de **2 estados** |
| Distribución | Salón, Terraza y Playa | **La Playa cierra**; distribución Night propia sobre el mismo plano; cada mesa adscrita a una barra |
| Unidad de servicio | Mesa o hamaca | **Mesas**: reservados, mesas con sillas y mesas altas |
| Prioridad | Orden de llegada | **Reservados primero** en barra y runner |
| Entrega | Runner por estación, mapa-manifiesto | Runner por barra; **pregunta por el nombre** de quien pidió |
| Identificación | Opcional (nombre y país) | **Obligatoria**: nombre + PIN, **una vez por dispositivo** y noche |
| Estética | Crema y oro | **Oscura** en las tres apps; mismo branding + «Night» |
| Cuenta | Abierta, se liquida en el TPV | **Igual** (ADR-028 intacto) |

### Hallazgos de la exploración que condicionan el diseño

Al leer el código actual aparecieron siete límites que el modo Night tiene que resolver, y que explican buena parte de los requisitos técnicos de §8:

1. **El PIN caduca a las 4 horas.** `PinCommands.cs` siembra `pin_validated` en Redis con `TimeSpan.FromHours(4)`: una noche entera se rompería a mitad de servicio y el comensal volvería a ver `PIN_REQUIRED`.
2. **El manifiesto del runner no trae el nombre.** `DeliveryTripStopItemDto` conoce la mesa y el plato, no quién lo pidió; de noche, con varias personas por mesa, el runner no sabría por quién preguntar.
3. **La PDA conecta al hub pero no se une al grupo.** Falta el `invoke("JoinWaiterGroup")` en `pda/page.tsx`, así que los avisos de mesa le llegan solo por el sondeo de 6 s: sin el evento en vivo ni el pitido, y hasta 6 s tarde.
4. **La estación del runner viaja fija en el JWT.** Reasignar un runner a otra barra invalida su token y le cierra la sesión en mitad del turno.
5. **Hay una sola posición por mesa.** `tables.pos_x/pos_y` no admiten una distribución de día y otra de noche.
6. **El backend no permite `QUEUED → READY`.** La máquina del plato (ADR-032) exige pasar por `IN_PREPARATION`; una barra que sirve en un gesto no puede saltárselo.
7. **`staff` y `admin` no tienen tema oscuro.** Las paletas `brand-*` y `fuego-*` viven en hex fijo; no hay tokens que redefinir.

> El plan no es un límite: Mesto Beach está en `pro` (`scripts/bootstrap-mestobeach.sql`), que no limita estaciones (`MaxKdsStations = null`); las cuatro estaciones de Night caben sin tocar el plan.

---

## 3. Usuarios y Roles

Los roles son los de PRD-018; lo que cambia es **qué hace cada uno de noche**.

| Rol | Qué hace en Night | Diferencia respecto a PRD-018 | Dispositivo |
|---|---|---|---|
| **Comensal de noche** | Se sienta en una mesa que la Recepción le abrió, se registra **una sola vez** con su nombre y el PIN de la mesa, pide de la carta de noche, repite lo último con un toque, llama al camarero, pide la cuenta | Identificación **obligatoria** (nombre + PIN) y recordada **por dispositivo** toda la noche; no existe el atajo de invitación; solo ve la carta de noche | Su propio móvil |
| **Barra** (estación `BAR`) | Ve los pedidos de su barra en **dos columnas** —Pedidos y Listo—, sirve y marca «Listo» en un gesto; los reservados le aparecen primero | Tablero de **2 estados** en vez de 4; nunca pasa por «En preparación»; recibe solo las mesas que tiene adscritas | Pantalla o tablet en la barra |
| **Runner de noche** | Adscrito a **una barra**; arma la bandeja, va a la mesa y **pregunta por el nombre** de quien pidió cada cosa; atiende los avisos de mesa (camarero, PIN, cuenta, «¿cuándo sale?») de las mesas de su barra | Ve el **nombre** del comensal en cada parada y la pastilla **Reservado**; recibe los **avisos de mesa** que de día recibe la sala; su estación se lee de BD y puede cambiar sin cerrar sesión | Móvil o tablet del local |
| **Recepción** (Host) | Sienta la mesa, registra el pax y **lee el PIN** al grupo; ve la distribución Night | Ve el **PIN** al sentar; el plano que ve es el de noche (sin Playa) | Tablet o móvil |
| **PDA / Salón** | Igual que de día: comanda a la mesa y atiende los avisos de camarero del comensal (en tiempo real cuando se corrija la suscripción, RF-1954) | Sin cambios de flujo. En los reservados el servicio es presencial y la app del comensal es solo el respaldo (D-04) | Comandero / móvil |
| **Administración** | **Cambia el modo** desde el panel (pre-flight, cuenta atrás, historial); diseña la **distribución Night**; marca qué ítems y categorías se ven de noche; define la **Apariencia Night**; configura perfil y modos de cada estación; lee los **reportes por modo** | Gana el interruptor de modo y una segunda distribución, una segunda apariencia y un filtro por modo en todo lo que ya tenía | Escritorio y móvil |
| **Cajero** | Cobra en el TPV; en Mesto ve y cierra la cuenta como de día | Sin cambios (ADR-028) | TPV / tablet |

---

## 4. Alcance

### In Scope — «Núcleo Night» (primera entrega)

- **Modo del local** `RESTAURANT | NIGHT`, único para todo el local, cambiado **manualmente por Administración** desde el panel, con **pre-flight**, **cuenta atrás de 5 s** y **auditoría** de cada cambio.
- **Transición cerrada**: el cambio se **bloquea mientras haya mesas abiertas** (con sus platos sin finalizar) o viajes abiertos (409 con la lista); los platos que quedaran colgados de mesas ya cerradas no bloquean y se informan como `orphanItems`; no existe `force`.
- **Difusión del cambio** en tiempo real a `staff` y `admin`; el menú del comensal lo detecta por sondeo.
- **Una carta con visibilidad por modo**: cada ítem y cada categoría declara si se ve de día, de noche o en ambos; el menú público solo sirve lo del modo activo y el backend rechaza lo que no corresponde.
- **Branding Night** como capa de overrides sobre el branding del día (lo no definido se hereda); la API pública entrega el branding ya resuelto.
- **Estaciones con perfil de flujo** (`FULL_PASS` | `BAR`) y **encendido por modo**; una estación `BAR` sirve en dos estados (`QUEUED → READY`); los pedidos no se enrutan a estaciones apagadas.
- **Distribución por modo** sobre el **mismo plano a escala** (ADR-030): posición, «en servicio» y **barra adscrita** por mesa y modo; pisos visibles por modo (la **Playa cierra** de noche); toda mesa nueva nace con su fila en los dos modos (RF-1940).
- **Tipo de mesa** `TABLE | HIGH_TABLE | RESERVADO` y **prioridad de reservados** en barra y en la cola del runner.
- **Reparto mesa → barra configurable** desde la distribución Night (OQ-1903).
- **PIN de noche**: obligatorio **en todas las mesas** (sea cual sea su preset de QR) y pedido en el registro junto al nombre, **único entre sesiones abiertas**, válido **toda la noche** (12 h) con Postgres como verdad; **nombre obligatorio** para pedir desde el menú (las comandas del personal quedan exentas); sin atajo de invitación.
- **Una vez por dispositivo**: el comensal se registra una sola vez por mesa y noche en su móvil; al reabrir el menú no vuelve a identificarse.
- **Runner con nombre**: el manifiesto agrupa los ítems de cada parada por comensal («Pregunta por: Nombre»), con pastilla **Reservado**; el runner recibe los **avisos de mesa** en Night; si la persona no está, se entrega igual en la mesa (RF-1957).
- **Estación del runner desde BD**: reasignar un runner a otra barra no le cierra la sesión.
- **«Repetir lo último»** en el menú del comensal.
- **Reportes separados por modo** y opción conjunta; día de negocio que empieza a las 06:00.
- **Identidad Night**: mismo branding + «Night» en itálica serif dorada; **estética oscura en las tres apps** por tokens; **animación «agujero negro»** en el cambio de modo, con variante de movimiento reducido.
- **Responsividad** de las vistas de personal y del panel (workstream paralelo) y **PWA instalable** en las tres apps.

### Out of Scope (primera entrega)

Quedan fuera, **con los nombres de campo ya reservados** para que nadie elija otros cuando entren:

| Qué | Por qué queda fuera | Nombre reservado |
|---|---|---|
| **Productos por mesa** (una carta distinta según el reservado) | Alcance Núcleo (D-09). Caso del cliente: habilitar algunos productos solo para algunas mesas, configurable desde el panel de administración | `allowedTableIds` en el ítem de carta |
| **Precios nocturnos** (el mismo ítem a otro precio de noche) | OQ-1905; depende de cómo se cargue la carta de noche | `nightPriceCents` en el ítem de carta |
| **Snacks y modificadores de noche** | OQ-1905, pendiente de preguntar al cliente (§14); si entra, será en una fase posterior | — (usa `visibleIn` cuando entren) |
| **Consumo mínimo** por mesa o reservado | Requiere definir qué pasa al cerrar la cuenta en el TPV | — |
| **Reservas** (anticipadas, con seña) | Producto aparte | — |
| **Pago móvil** desde la app | Frontera fiscal (ADR-028); futuro | — |
| **Facturación propia** (que el sistema emita el ticket, sin el TPV) | Hoy rige ADR-028 y la cuenta se liquida en el TPV. Para entrar hace falta Verifactu y un PRD y un ADR propios (ver PRD-018 §Fuera de alcance y OQ-04) | — |
| **Pedidos sin mesa o en la barra cara a cara** | Night es solo para quien tiene mesa (D-01); el cliente: «cuando se acerquen a la barra se pide normalmente». Se piden y cobran en la barra como hoy (TPV/Ágora), sin pasar por Mesto; ni el tablero de barra ni la caja dan de alta esos pedidos | — |
| **Control de menores** / verificación de edad | No pedido; política del local en puerta | — |
| **Hora límite** de pedido o cierre automático del modo | El cambio es **manual** por decisión del cliente (D-05) | — |
| **Service worker / modo offline** de la PWA | La PWA de esta fase es instalable, no offline | — |
| **Límite por pedido** (cantidad o importe) | OQ-1907, pendiente de preguntar al cliente (§14); fuera de la primera entrega | — |

---

## 5. Flujo

### (a) Cambio de modo con pre-flight y cuenta atrás

```
  ADMINISTRACIÓN (panel)                       API                              STAFF / MENÚ
     │                                          │                                   │
  pulsa «Night» en el pie ─────────────────►  GET /venue/preflight                  │
     │                                          │                                   │
     │◄── {ready:false, tables:[T4, R2]} ───────┤                                   │
  «Hay 2 mesas abiertas: T4, R2»                │                                   │
  (no se puede cambiar; no hay force)           │                                   │
     │                                          │                                   │
  ... la caja cierra T4 y R2 ...                │                                   │
     │                                          │                                   │
  pulsa «Night» ─────────────────────────────►  GET /venue/preflight                │
     │◄── {ready:true} ─────────────────────────┤                                   │
  aviso + cuenta atrás  5 · 4 · 3 · 2 · 1       │                                   │
  «Cambiar» se habilita                         │                                   │
     │                                          │                                   │
  confirma ──────────────────────────────────►  POST /venue/mode {mode:"NIGHT"}     │
     │                                          │  pre-flight otra vez (idempotente)│
     │                                          │  tenants.venue_mode = NIGHT       │
     │                                          │  venue_mode_changes (+1 fila)     │
     │                                          │  Redis venue:{t}:mode             │
     │                                          │  ─── VENUE_MODE_CHANGED ────────► staff/admin:
     │◄── 200 {mode:"NIGHT", changed:true} ─────┤                                   │ animación
  animación «agujero negro»                     │                                   │ y re-tema
  panel en oscuro, «Mesto Beach Night»       │               menú: sondeo GET /venue/{t} ≤ 30 s
     │                                          │                                   │ → re-tema
  historial: GET /venue/history                 │                                   │
```

### (b) Llegada a mesa: la Recepción sienta → PIN → el comensal se registra una vez por dispositivo

```
  RECEPCIÓN                       COMENSAL (su móvil)                       API
     │                                 │                                     │
  sienta la mesa R2 (pax 6) ──────────────────────────────────────────►  POST /sessions
     │                                 │                                     │  PIN único entre
     │◄── PIN 4821 (visible al sentar) ◄─────────────────────────────────────┤  sesiones abiertas
  lo dice al grupo                     │                                     │  sessions.venue_mode=NIGHT
     │                                 │                                     │
     │                            escanea el QR de R2                        │
     │                                 │ ──── join-by-table ───────────────► │
     │                                 │◄─── misma sesión, participante nuevo┤
     │                            «¿Cómo te llamas?»  (obligatorio)          │
     │                            «PIN de la mesa»    (obligatorio)          │
     │                                 │─ POST /sessions/{id}/pin/validate ► │  pin_validated
     │                                 │                                     │  TTL 12 h + Postgres
     │                            guarda la visita en el dispositivo         │
     │                            mestobeach_visit:{t}:{R2}                    │
     │                                 │                                     │
     │                       ... dos horas después, cierra y reabre el menú  │
     │                                 │ ──── join-by-table (mismo device) ► │
     │                                 │◄─── misma sesión + participante ────┤
     │                            salta el registro: directo a la carta      │
     │                                 │                                     │
     │                       otro móvil del mismo grupo → se registra él     │
     │                       (otro navegador = otro dispositivo)             │
```

### (c) Pedido → barra de 2 estados → runner con nombre → entrega en mesa

```
  COMENSAL (Ana, R2)        BARRA 2 (tablero 2 estados)          RUNNER (Barra 2)
     │                              │                                  │
  pide 2× Gin tonic ──────────►  PEDIDOS                              │
  (Shot si quiere;             ┌─────────────┐                        │
   «Chuletón» → no está)       │ R2 Reservado│ ◄── va primero          │
     │                         │  Ana · 2×GT │                         │
     │                         │ H1          │                         │
     │                         │  Luis · 1×GT│                         │
     │                         └─────────────┘                         │
     │                              │ sirve y pulsa «Listo»            │
     │                              │ (QUEUED → READY, sin pasar       │
     │                              │  por «En preparación»)           │
     │                              ▼                                  │
     │                           LISTO ──────────────────────────► cola de Barra 2
     │                                                               (R2 antes que H1)
     │                                                                  │
     │                                                             arma la bandeja
     │                                                                  │
     │                                                        manifiesto por parada:
     │                                                         ((R2)) Reservado
     │                                                           Pregunta por: Ana
     │                                                             2× Gin tonic
     │                                                         ((H1))
     │                                                           Pregunta por: Luis
     │                                                             1× Gin tonic
     │                                                                  │
     │◄──────────── «¿Ana?» · entrega en mesa ──────────────────── ENTREGADO
     │        (si Ana no está → se entrega igual en R2, sin destapar; queda a cargo de la cuenta de la mesa; no se devuelve a la barra)
     │                                                                  │
  «Repetir lo último» → 2× Gin tonic otra vez ─────────────────────►  PEDIDOS
```

### (d) Cierre de cuenta en el TPV (sin cambios respecto al día)

```
  COMENSAL                 RUNNER / SALÓN                CAJA (Mesto)              ÁGORA (TPV)
     │                           │                           │                         │
  pide la cuenta ─────────► aviso BILL ───────────────►  abre la cuenta de R2         │
     │                           │                      (lo pedido por cada nombre)    │
     │                           │                           │ ── traslada / cobra ──► │ ticket
     │                           │                           │◄──────────────────────── │ y factura
     │                           │                      cierra la sesión en Mesto       │
     │                           │                      (R2 vuelve a «libre»;           │
     │                           │                       el PIN deja de estar en uso)   │
```

---

## 6. Historias de Usuario

### Épica 1: Cambiar el local de modo

```
US-1901
Como Administración,
quiero cambiar todo el local de «Mesto Beach» a «Mesto Beach Night» desde el panel con un aviso y una cuenta atrás,
para que la noche empiece con un gesto y nadie se entere por sorpresa.

Criterio de aceptación:
- El interruptor vive en el pie de la barra lateral y abre un diálogo propio (ADR-033), nunca uno del navegador
- El diálogo muestra el resultado del pre-flight antes de dejarme confirmar
- «Cambiar» está deshabilitado hasta que termina una cuenta atrás visible de 5 segundos
- Al confirmar, el panel ejecuta la transición «agujero negro» y queda en oscuro con la marca «Mesto Beach Night»
- El cambio afecta a todo el local a la vez: no hay modo por sector ni por estación
- El historial del panel muestra quién cambió, cuándo y de qué modo a cuál

US-1902
Como Administración,
quiero que el sistema me impida cambiar de modo mientras haya mesas abiertas, platos sin terminar o viajes en curso,
para no dejar a nadie a medias ni perder pedidos en la transición.

Criterio de aceptación:
- Si hay sesiones abiertas (con sus ítems no finalizados) o viajes abiertos, el cambio responde 409 VENUE_NOT_IDLE; los ítems colgados de mesas ya cerradas se muestran como aviso, pero no bloquean
- El diálogo lista las mesas afectadas por su código para que la caja sepa qué cerrar
- No existe ninguna opción de forzar el cambio
- Pedir el modo que ya está activo no hace nada y no deja rastro en el historial
```

### Épica 2: El comensal de noche

```
US-1903
Como comensal de noche,
quiero registrarme con mi nombre y el PIN de la mesa una sola vez en mi móvil,
para no volver a identificarme cada vez que abro el menú durante la noche.

Criterio de aceptación:
- La primera vez que entro, la misma pantalla me pide nombre y PIN (el PIN se valida con `POST /sessions/{id}/pin/validate`); el nombre es obligatorio para continuar
- Si aún no tengo el PIN, desde esa pantalla puedo «Pedir el PIN» (`POST /sessions/{id}/request-pin`) y «Llamar al camarero» (`POST /sessions/{id}/call-waiter`) y ver la carta; no puedo enviar un pedido hasta validarlo (el servidor responde `PIN_REQUIRED` y se abre el modal de siempre)
- Si cierro el navegador y vuelvo a escanear el QR de la misma mesa, entro directo a la carta
- El PIN sigue siendo válido toda la noche: nunca me lo vuelven a pedir por haber pasado el tiempo
- Si entro desde otro móvil (u otro navegador), ese dispositivo se registra por su cuenta
- Un enlace de invitación no me ahorra el PIN en Night

US-1904
Como comensal de noche,
quiero ver solo la carta de noche y que el sistema me avise si mi zona o mi producto no están disponibles,
para no pedir algo que no existe esta noche.

Criterio de aceptación:
- La carta no muestra los platos del día; sí los productos marcados para la noche
- Si escaneo el QR de una mesa que no está en servicio esta noche (p. ej. Playa), veo «Esta zona está cerrada esta noche; acércate a Recepción» (sin la indicación «Pide al camarero que abra tu mesa»)
- Si intento pedir algo que no corresponde al modo, el pedido se rechaza con un mensaje claro

US-1912
Como comensal de noche,
quiero repetir mi último pedido con un toque,
para pedir otra ronda sin volver a buscar cada producto.

Criterio de aceptación:
- En la carta hay un botón «Repetir lo último» cuando ya hice al menos un pedido en esta sesión
- Re-añade al carrito los ítems de mi último pedido que siguen visibles y disponibles
- Lo que ya no está (agotado o fuera del modo) no se añade y un toast me dice qué quedó fuera
- El pedido no se confirma solo: lo reviso y lo envío como siempre
```

### Épica 3: La barra

```
US-1905
Como barra,
quiero un tablero de dos columnas —Pedidos y Listo— con un solo botón,
para servir al ritmo de una barra y no al de una cocina.

Criterio de aceptación:
- Una estación con perfil BAR muestra dos columnas: «Pedidos» (con la acción «Listo») y «Listo»
- Pulsar «Listo» lleva el ítem de la cola directamente al pase, sin pasar por «En preparación»
- Una estación con perfil FULL_PASS conserva sus cuatro columnas de día (PRD-018 RF-1820)
- Un pedido nuevo se destaca con un borde dorado durante 3 segundos y un contador de «sin atender»; nada parpadea a más de 3 Hz
- La estación que elijo en el KDS se recuerda en este dispositivo; por defecto es la que tengo asignada

US-1906
Como barra,
quiero que las comandas de los reservados me aparezcan antes que las demás,
para servir primero a quien el local prioriza.

Criterio de aceptación:
- Las comandas de mesas RESERVADO se ordenan antes que las de HIGH_TABLE y TABLE, aunque hayan llegado después
- A igual prioridad, manda el orden de llegada
- La tarjeta lleva una pastilla «Reservado» (hairline dorado, versalitas)
- La cola del runner de mi barra respeta el mismo orden
```

### Épica 4: El runner de noche

```
US-1907
Como runner de noche,
quiero ver en cada parada el nombre de quien pidió cada cosa,
para preguntar por esa persona al llegar a la mesa y no repartir a ciegas.

Criterio de aceptación:
- Cada parada del manifiesto agrupa los ítems por comensal bajo «Pregunta por: Nombre»
- Las paradas de mesas RESERVADO se muestran primero y con la pastilla «Reservado»
- Si una mesa tiene varios comensales con pedidos en la misma bandeja, cada grupo sale con su nombre
- En Night el comensal no puede pedir sin nombre desde el menú; una comanda tomada por la PDA o la caja sale como «Comanda de sala · {mesa}»
- Si la persona nombrada no está, entrego en la mesa igualmente y queda a cargo de su cuenta; no lo devuelvo a la barra por ese motivo. Todo viaja sin destapar y se destapa en la mesa

US-1913
Como runner de noche,
quiero recibir los avisos de mesa —camarero, PIN y cuenta— de las mesas de mi barra en mi vista,
para atenderlos yo cuando de noche no hay PDA pasando por cada mesa.

Criterio de aceptación:
- En Night, la vista del runner muestra el panel de avisos de mesa que de día tiene la PDA
- Solo veo los avisos de las mesas de mi barra, más los de las mesas sin barra asignada
- Cada aviso indica la mesa, el motivo y si la mesa es un reservado (pastilla «Reservado»)
- Los avisos de mesas RESERVADO salen primero, por delante de los de las demás mesas aunque estos lleven más tiempo esperando; dentro de cada grupo, el más antiguo primero
- Si un comensal pide el PIN, el aviso trae el botón «Ver PIN» y se lo dicto en la mesa, igual que hoy la PDA
- Al atenderlo, desaparece para todos los que lo veían
- La PDA sigue recibiendo los mismos avisos y además los recibe en vivo (hoy solo le llegan por el sondeo de 6 s, sin pitido, por un fallo de suscripción que se corrige)

US-1908
Como runner,
quiero que me cambien de barra sin que se me cierre la sesión,
para pasar de Barra 1 a Barra 2 en mitad del turno sin perder la sesión ni la bandeja que estoy armando.

Criterio de aceptación:
- Administración cambia mi estación asignada y mi app empieza a ver la cola de la nueva barra sin volver a iniciar sesión
- El cambio tarda como mucho un minuto en reflejarse
- Cambiar solo la estación no invalida mi token; cambiar mi rol sí sigue cerrando la sesión
```

### Épica 5: Recepción y distribución

```
US-1909
Como Recepción,
quiero sentar una mesa de noche y tener su PIN a la vista,
para decírselo al grupo en el momento sin buscarlo.

Criterio de aceptación:
- Al sentar la mesa, el PIN aparece en el panel de asiento (si no estaba visible, se muestra)
- El PIN de esa mesa no coincide con el de ninguna otra mesa abierta en el local
- El plano que veo es el de noche: sin la Playa, con las mesas en su posición Night

US-1910
Como Administración,
quiero diseñar una distribución de noche propia sobre el mismo plano, con la Playa cerrada y cada mesa adscrita a una barra,
para que de noche el local se vea y se sirva como realmente se trabaja.

Criterio de aceptación:
- En «Mesas» hay un control segmentado «Distribución: Restaurante | Night», independiente del modo activo, con una pastilla «activo ahora»
- Cada mesa tiene por modo: posición y «En servicio» (en las dos pestañas, Restaurante y Night), y en la pestaña Night además «Barra» (solo estaciones BAR encendidas en Night)
- Si añado una mesa en la pestaña Night, aparece en el plano de noche y no en el de día hasta que la ponga En servicio en Restaurante (y al revés)
- Cada piso tiene un interruptor «visible de noche»; la Playa queda apagada en Night
- Cada mesa tiene un tipo: mesa, mesa alta o reservado
- Los dos borradores se guardan por separado; si cambio de pestaña con cambios pendientes, me avisa
- Los QR físicos no cambian: una mesa cerrada en Night responde con el mensaje de zona cerrada
```

### Épica 6: Reportes

```
US-1911
Como Administración,
quiero ver los reportes del restaurante y de la fiesta por separado, y también juntos,
para saber qué se pide en cada negocio desde las mesas sin mezclar los números.

Criterio de aceptación:
- El panel de reportes tiene un segmentado «Restaurante | Night | Todo»
- Cada pedido y cada sesión guardan el modo en que nacieron; el filtro se basa en eso, no en la hora
- El día de negocio empieza a las 06:00 (Europe/Madrid): lo vendido a las 3 de la madrugada cuenta en el día que empezó la noche (supuesto: Night cierra antes de las 06:00; OQ-1909)
- Night + Restaurante suman exactamente Todo
- Los pedidos cancelados no suman en ningún modo
- Los reportes cubren solo lo pedido a través de Mesto (QR, PDA, caja); lo vendido en la barra cara a cara vive en el TPV y no aparece
```

---

## 7. Requisitos Funcionales

### Modo y transición

| ID | Requisito |
|---|---|
| RF-1901 | El local tiene un **modo** con dos valores, `RESTAURANT` y `NIGHT`; es **único para todo el local** y las tres apps lo conocen. |
| RF-1902 | Solo **Administración** cambia el modo, **manualmente** y desde el panel; no hay cambio por horario ni por sector. |
| RF-1903 | Antes de cambiar, el panel muestra un **pre-flight** con sesiones abiertas, platos no finalizados de esas sesiones abiertas, viajes abiertos y la lista de mesas afectadas; y, como aviso que no bloquea, los platos no finalizados de mesas ya cerradas (`orphanItems`). |
| RF-1904 | El cambio **se bloquea** mientras exista alguna sesión abierta, plato no finalizado de una sesión abierta o viaje abierto: responde `409 VENUE_NOT_IDLE` con esa lista. **No existe `force`.** Los platos de mesas ya cerradas no bloquean: los `IN_PREPARATION`/`READY` se terminan en su tablero y los `HELD`/`QUEUED` ya no pueden avanzar. |
| RF-1905 | «Cambiar» se habilita solo tras una **cuenta atrás de 5 s** visible, dentro de un diálogo propio de la app (ADR-033). |
| RF-1906 | El cambio es **idempotente**: pedir el modo ya activo responde 200 con `changed=false`, no toca datos y no genera fila de historial. |
| RF-1907 | Cada cambio queda **auditado** (quién, cuándo, de qué modo a cuál y el resultado del pre-flight) y es consultable como **historial** desde el panel. |
| RF-1908 | `staff` y `admin` reciben el cambio **en tiempo real**; el menú del comensal lo detecta por **sondeo ≤ 30 s** y al volver a primer plano. Ante un `503` o un error de red del sondeo, el menú conserva el último modo conocido y **no** dispara la transición; solo un `200` con un `mode` distinto cambia el tema. Lo mismo vale para `staff` y `admin` al releer `GET /venue`. |
| RF-1909 | Un modo distinto de los dos válidos se rechaza con `400 INVALID_MODE`. |

### Carta y branding

| ID | Requisito |
|---|---|
| RF-1911 | Hay **una sola carta**; cada ítem y cada categoría declara en qué modos es visible (`visibleIn`); por defecto, en ambos. |
| RF-1912 | Un ítem o categoría **no puede quedar sin ningún modo**: se rechaza con `400 VISIBLE_IN_EMPTY`. |
| RF-1913 | El menú público solo entrega lo visible en el modo activo e informa del modo (`venueMode`) para que la app se tematice. |
| RF-1914 | Pedir un ítem no visible en el modo activo se rechaza con `409 ITEM_NOT_IN_MODE`, venga del comensal, de la PDA o de la caja. |
| RF-1915 | *(Defensivo)* Si el menú detecta un modo distinto al de la carta cargada (por el sondeo de `GET /venue/{tenantId}` o por el `venueMode` del catálogo), recarga la carta, quita del carrito lo que ya no es visible y lo avisa con un toast. Con RF-1904 esto solo ocurre si una sesión se abrió durante la transición. La garantía real es `409 ITEM_NOT_IN_MODE` (RF-1914). Los carritos de sesiones anteriores ya se descartan hoy (`hydrateCart` ata el carrito a su `sessionId`). |
| RF-1916 | El **branding Night** es una capa de overrides (`branding.night`) sobre el del día: cada campo no definido **hereda**, salvo el fondo libre `backgroundColor`, que de noche solo se aplica si se fija en Night (si no, manda el tema `night`); la API pública entrega el branding **resuelto** para el modo activo. |
| RF-1917 | Administración edita la Apariencia en una pestaña **«Día | Night»** reutilizando la vista previa, con **«Heredar del día»** por campo (excepto el fondo libre, cuyo vacío significa «Usar el tema»); existe el tema `night`, la fuente `mestobeach` y el dorado `#AA8453` entre los colores predefinidos. |
| RF-1918 | El catálogo del panel muestra chips **«Día / Noche»** por ítem y por categoría, con el mismo patrón que el interruptor de disponibilidad, y un filtro **«Ver: Todo | Día | Noche»**. |

### Estaciones y barra

| ID | Requisito |
|---|---|
| RF-1921 | Cada estación declara si está **activa**, su **perfil de flujo** (`FULL_PASS` \| `BAR`) y en **qué modos se enciende** (`activeIn`). «Encendida» se **calcula**: activa y el modo actual está en `activeIn`. |
| RF-1922 | Configuración de estaciones de Mesto Beach, **condicionada a OQ-1904**. Recomendada (OQ-1904 = sí): **Cocina y Parrilla** `FULL_PASS` solo en `RESTAURANT`; **Barra 1** es la estación Cocktails actual, renombrada, con sus categorías de copas, `BAR` y `activeIn` [RESTAURANT, NIGHT]; **Barra 2** `BAR` solo en `NIGHT`, sin categorías propias (recibe por el reparto mesa → barra). Si OQ-1904 = no: Barra 1 (la Cocktails actual) pasa a tener **perfil por modo** `{RESTAURANT: FULL_PASS, NIGHT: BAR}`, lo que exige el cambio de esquema de ADR-037 antes de la migración de PR3; siguen siendo cuatro estaciones. De noche, Cocina y Parrilla quedan apagadas y su tablero muestra «Cerrada en Night». |
| RF-1923 | Una estación `BAR` muestra **dos columnas** —«Pedidos» (con la acción «Listo») y «Listo»— y permite `QUEUED → READY` en un gesto. Una estación `FULL_PASS` conserva las cuatro columnas de PRD-018 (RF-1820) y **no** permite ese salto. Un plato pedido «luego» sigue el flujo de marchar de PRD-018 y entra en «Pedidos» al marcharse. Correspondencia estado → columna en `BAR`: `QUEUED`, `IN_PREPARATION` y `RETURNED` (este con la pastilla «Devuelto — rehacer») van a «Pedidos» con la acción «Listo»; `READY` va a «Listo»; `HELD` queda fuera de las columnas hasta que lo marche la PDA, el salón o Administración (la barra no tiene columna de espera). |
| RF-1924 | Ningún ítem se enruta a una estación **apagada**; intentarlo responde `409 STATION_CLOSED`. |
| RF-1925 | El tablero de barra **destaca el pedido nuevo** (borde dorado 3 s y contador «sin atender») **sin parpadear por encima de 3 Hz**. |
| RF-1926 | La estación elegida en el KDS **se recuerda por dispositivo**; por defecto es la asignada al usuario. |
| RF-1927 | Un perfil de flujo desconocido se rechaza con `400 INVALID_FLOW_PROFILE`; una lista de modos vacía, con `400 ACTIVE_IN_EMPTY`. |
| RF-1928 | El tenant de Mesto Beach sigue en plan **`pro`** (estaciones ilimitadas, `MaxKdsStations = null`); no se cambia el plan ni `PlanCatalog` y no hay endpoint de cambio de plan (Platform retirado, ADR-023). El bootstrap/seed falla de forma explícita si `plan_code` no es `pro` ni `enterprise`. |

### Distribución y reservados

| ID | Requisito |
|---|---|
| RF-1931 | Cada mesa tiene **una distribución por modo**: posición, «en servicio» y barra adscrita, sobre el **mismo plano a escala** (ADR-030). Existe exactamente una fila por mesa y modo; la garantizan la migración y todas las altas de mesas (RF-1940). |
| RF-1932 | Cada piso declara en qué modos es **visible**; la **Playa** es visible solo en `RESTAURANT`. |
| RF-1933 | Cada mesa tiene un **tipo**: `TABLE`, `HIGH_TABLE` o `RESERVADO`. |
| RF-1934 | Abrir o unirse a una mesa que **no está en servicio** en el modo activo responde `409 TABLE_CLOSED_IN_MODE`; el menú lo traduce a «Esta zona está cerrada esta noche; acércate a Recepción». |
| RF-1935 | El **reparto mesa → barra es configurable** en la distribución Night: cuando el plato es de perfil `BAR` o no tiene estación, se enruta a la barra adscrita a la mesa. |
| RF-1936 | Las comandas de mesas `RESERVADO` **creadas en modo `NIGHT`** van **primero** en el tablero de barra y en la cola del runner; la prioridad se **congela en el pedido** al crearlo; en `RESTAURANT` el pedido nace con prioridad 0 aunque la mesa sea `RESERVADO` (de día manda el orden de llegada); a igual prioridad manda la llegada. |
| RF-1937 | La pastilla **«Reservado»** aparece en la tarjeta del KDS, en la parada del runner y en los avisos de mesa (`tableKind`). |
| RF-1938 | Administración edita la distribución con un segmentado **«Restaurante \| Night»** independiente del modo activo (pastilla «activo ahora»), **borrador por modo** con aviso al cambiar con pendientes, campos «En servicio» en las dos pestañas y «Barra» en la pestaña Night (solo estaciones `BAR` encendidas en Night), interruptor «visible de noche» por piso y tipo de mesa. El guardado es en lote e idempotente. |
| RF-1939 | La URL del **QR** que genera el backend apunta al menú público real (`MENU_PUBLIC_URL` + `/mesa?tenant=&table=`); los QR impresos no cambian entre modos. |
| RF-1940 | Una mesa creada desde el editor nace con su fila en los dos modos: en el modo de la pestaña desde la que se crea, **activa** y con la posición indicada; en el otro modo, **inactiva**, con la misma posición y sin barra. Así no altera la distribución ya ajustada del otro modo, y el admin la enciende allí si la quiere. El alta en lote de puestos sigue la misma regla (modo por defecto `RESTAURANT`). La mesa ad-hoc que crea la caja al abrir sesión nace activa en los dos modos y sin ubicar. La posición se edita solo por modo: `PATCH /tables/{id}` deja de aceptarla. |

### PIN y dispositivo

| ID | Requisito |
|---|---|
| RF-1941 | En `NIGHT` el **PIN es obligatorio en todas las mesas**, sea cual sea su preset (QR Fijo, QR Dinámico o Sin seguridad), y el **nombre también**: un pedido desde el menú (sin JWT de personal) cuyo participante no tiene nombre se rechaza con `422 NAME_REQUIRED`; las comandas del personal (PDA y caja) quedan exentas. |
| RF-1942 | El PIN es **único entre las sesiones abiertas** del local: dos mesas abiertas nunca comparten PIN. |
| RF-1943 | Un PIN validado vale **toda la noche** (12 h) y **sobrevive** a una caída o limpieza de la caché: la verdad está en Postgres y la caché se re-siembra sola. |
| RF-1944 | **Una vez por dispositivo**: en `NIGHT` el comensal se registra en una misma pantalla al entrar, con nombre y PIN (`POST /sessions/{id}/pin/validate`), una sola vez por mesa y noche en su dispositivo. Al volver a escanear, si la sesión y el participante son los mismos, entra directo a la carta. Sin PIN puede ver la carta, «Pedir el PIN» (`POST /sessions/{id}/request-pin`) y «Llamar al camarero» (`POST /sessions/{id}/call-waiter`), pero no enviar un pedido (el servidor responde `PIN_REQUIRED` y se abre el modal existente). **Otro navegador u otro móvil es otro dispositivo.** |
| RF-1945 | En `NIGHT` **no existe el atajo de invitación**: un enlace de invitación no concede el PIN. |
| RF-1946 | Recepción ve el **PIN** de la mesa al sentarla. En su defecto, el camarero (PDA/Salón, sin cambios) o el runner de noche lo da desde el aviso «Pide el PIN», con el botón «Ver PIN» (`GET /sessions/{id}/pin`, `StaffJwt`, ya existe; sin cambios de backend). |
| RF-1947 | En una mesa `RESERVADO` el servicio es presencial y la app del comensal es el **respaldo**, por si no encuentra a nadie. El botón de camarero es el mismo de la carta de día y su aviso llega al runner de la barra de esa mesa (RF-1953) y a la PDA (RF-1954). Lo que se pida desde la app va primero (RF-1936) y sus avisos se listan los primeros (RF-1956). |

### Runner y avisos

| ID | Requisito |
|---|---|
| RF-1951 | El manifiesto del runner agrupa los ítems de cada parada **por comensal** bajo **«Pregunta por: Nombre»**. |
| RF-1952 | La **estación del runner se lee de la base de datos**, no del token: reasignarlo a otra barra se refleja en ≤ 60 s **sin cerrar su sesión**. Cambiar su rol sí la invalida. |
| RF-1953 | En `NIGHT` la vista del runner muestra el **panel de avisos de mesa** (camarero, **PIN**, cuenta, «¿cuándo sale?») con la mesa, el motivo y si es reservado. Es el mismo panel de la PDA: el aviso de PIN trae el botón «Ver PIN». El runner ve los avisos de las mesas cuya barra (`table_layouts.station_id` de `NIGHT`) es su estación, más los de las mesas sin barra o fuera del plano: solo se descarta lo que se sabe que pertenece a otra barra. Un usuario sin estación asignada (Administración) ve todos. El orden es el de RF-1956. |
| RF-1954 | La PDA **se une al grupo de avisos** al conectar (corrige la suscripción que hoy falta). |
| RF-1955 | Las paradas de mesas `RESERVADO` se muestran **primero** en el manifiesto y en la cola de la barra del runner. |
| RF-1956 | Con el local en `NIGHT`, los avisos de mesa (camarero, cuenta, PIN, «¿cuándo sale?») de las mesas `RESERVADO` se listan **primero** en el panel de avisos del Runner, la PDA y el Salón, sea cual sea su antigüedad; dentro de cada grupo (reservados / resto) se ordenan del más antiguo al más reciente. En `RESTAURANT` el orden es el de llegada, como hoy. El orden sale de `tableKind` y del modo activo, y es el mismo en las tres pantallas. |
| RF-1957 | En `NIGHT`, si al llegar a la mesa el runner no encuentra a la persona por la que pregunta, **entrega igualmente en esa mesa**. Lo pedido queda **a cargo de la cuenta de la mesa** (ya lo está: el pedido pertenece a la sesión de la mesa), sin devolverlo a la barra ni anularlo **por ese motivo**. Los runners llevan **todo sin destapar** y se destapa en la mesa. Desmarcar un ítem al cerrar la ruta (`RETURNED`) sigue disponible de noche, pero solo para incidencias del producto (equivocado, derramado), no porque falte el comensal. No cambia ningún contrato: es una regla de operación y un texto de la vista del runner. |

### Reportes

| ID | Requisito |
|---|---|
| RF-1961 | Los reportes se filtran por modo con un segmentado **«Restaurante \| Night \| Todo»** (solo pedidos registrados en Mesto; la venta de barra cara a cara queda fuera, ver §4). |
| RF-1962 | El **día de negocio empieza a las 06:00** (Europe/Madrid): la madrugada pertenece al día que empezó la noche. Supuesto pendiente de confirmar: Night cierra antes de las 06:00 (OQ-1909). El corte vale también para la página de Pagos del admin (`GET /api/v1/payments`), que usa el mismo `VenueTime`. |
| RF-1963 | Los pedidos **cancelados** quedan fuera de todas las métricas, en cualquier modo. |
| RF-1964 | Cada **pedido** y cada **sesión** guardan el modo en que nacieron; el filtro de reportes se basa en ese dato, no en la hora. |

### Identidad y animación

| ID | Requisito |
|---|---|
| RF-1971 | En `NIGHT` las **tres apps** pasan a una estética **oscura** manteniendo el branding de Mesto Beach; la marca se lee **«Mesto Beach Night»**. |
| RF-1972 | La palabra **«Night»** va en **itálica serif real** (Cormorant Garamond Italic) en dorado; **Gilda Display nunca se inclina**. La marca se renderiza desde un único componente por app. |
| RF-1973 | El cambio de modo se presenta con la animación **«agujero negro»** en tres fases (colapso → singularidad → emersión) en las tres apps; con `prefers-reduced-motion` se sustituye por un **fundido de 200 ms**. |
| RF-1974 | Al recargar en `NIGHT` **no hay destello** del tema claro; el color de la barra del navegador acompaña al modo. |
| RF-1975 | Se ofrece al cliente una **variante «Eclipse»** (disco que cruza en diagonal, corona dorada en la totalidad, descubre Night al salir); la elegida se fija en H0. |
| RF-1976 | Existe un **SVG estático** de la marca Night (texto en contornos) para impresos. |
| RF-1977 | Las pantallas de día **no cambian** de aspecto (tema, tokens, marca) por la existencia del modo Night. Excepción: si OQ-1904 = sí, el tablero de Cocktails/Barra 1 pasa también de día a dos columnas por decisión expresa del cliente. |

### Responsividad y dispositivos

| ID | Requisito |
|---|---|
| RF-1981 | Las vistas de personal y el panel cumplen: objetivos táctiles **≥ 44 px**, respeto a `safe-area-inset-*`, altura `100dvh`, tablas con desplazamiento horizontal, modales con `max-h-[90dvh]`, cajones cerrables con `Esc` y con foco gestionado. |
| RF-1982 | El panel de `admin` es **usable en móvil**: barra lateral oculta bajo `md` con hamburguesa y cajón. |
| RF-1983 | El plano a escala admite **pinch-zoom y arrastre** táctil, leyenda plegable y detalle por toque, en `staff` y `admin`. |
| RF-1984 | Las tres apps son **PWA instalables** (`display: standalone`, iconos 192/512); **sin** service worker offline en esta fase. |
| RF-1985 | Un runner, en su móvil o en una tablet del local, puede cambiar de barra sin volver a iniciar sesión (RF-1952), y el modo tablero (ADR-034) sigue disponible en Night. Si una tablet del local pasa de un runner a otro, el saliente cierra sesión y el entrante entra con su propia cuenta (flujo actual). Las cuentas no se comparten, porque cada viaje es de un único runner (ADR-027: `TRIP_ALREADY_OPEN`, `RUNNER_HAS_OPEN_TRIP`). No hay un «cambiar de usuario» rápido en esta fase. |
| RF-1986 | La respuesta a «¿y el móvil personal?» es: dispositivos del local (anexo de equipos, OQ-1908) + PWA instalable + modo tablero. |

---

## 8. Requisitos Técnicos

| ID | Requisito |
|---|---|
| RT-1901 | **Enumerado compartido.** `VenueMode = RESTAURANT \| NIGHT` vive en `MestoBeach.Shared`. La fuente de verdad es `tenants.venue_mode` (+ `venue_mode_changed_at`), con caché en Redis **`venue:{tenantId}:mode`** (TTL 10 min); un único `IVenueModeProvider` (registrado Scoped) lo sirve a Catalog, Orders y Sessions. Con Redis caído lee Postgres sin re-sembrar; nunca se inventa un modo: solo si fallan Redis y Postgres lanza `VenueModeUnavailableException`, que los consumidores traducen a `503 VENUE_MODE_UNAVAILABLE`. |
| RT-1902 | **Evento.** El cambio se difunde por `OrdersHub` al grupo `tenant:{t}` como `OrderEvent{type:"VENUE_MODE_CHANGED"}`. El menú (anónimo) **sondea** `GET /api/v1/venue/{tenantId}` (`Cache-Control: no-store`) cada 30 s y en `visibilitychange`. |
| RT-1903 | **Endpoints** del controlador `VenueController` (Identity): `GET /api/v1/venue/{tenantId}` (anónimo, ruta `{tenantId:guid}`: un id que no es UUID da 404) → `{tenantId, mode, changedAt}` (`changedAt` se omite si nunca cambió); `GET /api/v1/venue` (staff o KDS, política `StaffOrDeviceJwt`) → igual + `stations[{id,name,flowProfile,isOn}]` (a partir de PR3; hasta entonces el endpoint de staff devuelve solo modo y `changedAt`); `GET /api/v1/venue/preflight` (Admin) → `{ready, openSessions, activeItems, openTrips, orphanItems, tables[]}`; `POST /api/v1/venue/mode {mode}` (Admin; `mode` llega como string) → 200 `{mode, previous, changed, changedAt}` · `409 VENUE_NOT_IDLE` con `openSessions, activeItems, openTrips, orphanItems, tables[]` en la raíz del ProblemDetails · `400 INVALID_MODE`; `GET /api/v1/venue/history?limit=` (Admin). Los GET responden `503 VENUE_MODE_UNAVAILABLE` si Redis y Postgres fallan a la vez. |
| RT-1904 | **Handler** `ChangeVenueModeHandler` (Identity): idempotencia → pre-flight (`sessions.status = Open`, `order_items` no finalizados **de sesiones abiertas** —join `orders` → `sessions`—, `delivery_trips` abiertos, vía read models `ExcludeFromMigrations`; `orphanItems` = platos no finalizados de sesiones no abiertas, informativo, no entra en `ready`) → transacción (tenant + fila de auditoría) → `SET venue:{t}:mode EX 600` → `Publish(VenueModeChangedNotification)` (handlers en las capas Application de Orders —difunde por hub— y de Catalog —invalida `catalog:{t}` de forma defensiva—) → log de auditoría. |
| RT-1905 | **Migraciones** (API parada; una por módulo): Identity `AddTenantVenueModeAndChanges` (`tenants.venue_mode varchar(16) default 'RESTAURANT'`, `venue_mode_changed_at timestamptz null`; tabla `venue_mode_changes(id uuid, tenant_id, from_mode, to_mode, changed_by uuid, changed_at timestamptz, preflight jsonb)`); Orders `AddKdsStationProfileAndModes` (`kds_stations.is_active bool default true`, `flow_profile varchar(16) default 'FULL_PASS'`, `active_in jsonb default '["RESTAURANT","NIGHT"]'`); Orders `AddOrderVenueModeAndPriority` (`orders.priority int default 0`, `orders.venue_mode varchar(16) default 'RESTAURANT'`); Sessions `AddTableLayoutsPerModeAndKind` (`table_layouts(table_id, mode, floor_id, pos_x double precision, pos_y double precision, active bool, station_id uuid null)` con único `(table_id, mode)`, `floors.visible_in jsonb`, `tables.kind varchar(16) default 'TABLE'`; **datos**: copia de `tables.pos_*` a la fila `RESTAURANT` y clon a `NIGHT`); Sessions `AddSessionVenueMode` (`sessions.venue_mode varchar(16) default 'RESTAURANT'`). |
| RT-1906 | **Carta.** `ItemDoc`/`CategoryDoc` ganan `visibleIn[]` (y en dominio `CatalogItem`/`CatalogCategory` ganan `VisibleIn` con `[JsonInclude]`, porque la caché reconstruye el dominio con System.Text.Json) y `BrandingDoc` gana `Night` (overrides, `null` = hereda, salvo `backgroundColor`). La caché `catalog:{t}` se guarda **sin filtrar**; el filtro por modo se aplica **al servir** (`GetPublicCatalogQuery`) y **al validar** (`CatalogValidationService`, cuyos read models L1 y L2 ganan `visibleIn` de ítems y categorías → `ITEM_NOT_IN_MODE`). Endpoints: `POST`/`PATCH items/categories` con `visibleIn` opcional, normalizado a mayúsculas y sin duplicados (→ `400 VISIBLE_IN_EMPTY` / `400 INVALID_MODE`), `GET/PUT /branding/night`, `POST/DELETE /branding/night/{logo\|hero}/image`. Los campos **`nightPriceCents`** y **`allowedTableIds`** quedan **reservados** y no se usan en esta entrega. |
| RT-1907 | **Estaciones.** `KdsStation` gana `IsActive`, `FlowProfile`, `ActiveIn` y `IsOnIn(mode)`. `OrderItem.ReadyFromQueue()` solo procede si la estación es `BAR`; sella `PreparationStartedAt = ReadyAt` para no romper los tiempos por estado (ADR-032); la actualización es **atómica**, al estilo del reclamo de viajes (UPDATE condicional y estado derivado en la misma transacción; mecánica en SDD-004). Validación en `KdsStationCommands`: `INVALID_FLOW_PROFILE`, `ACTIVE_IN_EMPTY`. |
| RT-1908 | **Enrutado y orden.** `OrderCommands` resuelve cada plato contra todas las estaciones y rechaza la comanda si la estación resuelta está apagada (`409 STATION_CLOSED`); cuando el plato es `BAR` encendido o sin estación, aplica el **override por mesa** desde `table_layouts.station_id` del modo activo; **congela** `priority` (`RESERVADO` → 1 solo si el modo leído es `NIGHT`; 0 en `RESTAURANT`) y `venue_mode` en el pedido; en Night, un pedido desde el menú (sin JWT de personal, `CallerTenantId` vacío) cuyo participante no tiene `DisplayName` recibe `422 NAME_REQUIRED`; las comandas del personal (PDA, caja) quedan exentas. Las colas ordenan `priority DESC, created_at` (activos) y `priority DESC, ready_at` (entrega). Los DTO de parada ganan `customerName` y `priority`; las coordenadas de cola y manifiesto salen de `table_layouts` del modo activo. |
| RT-1909 | **PIN de noche.** TTL de `session:*:pin` y `pin_validated` pasa a **12 h** (constante propia, separada de `SessionTtl`; `table:*:active` sigue en 4 h); Postgres es la verdad y `PinValidationService` hace **fallback** a `session_participants.pin_validated` y **re-siembra** Redis con TTL fijo de 12 h. Unicidad entre sesiones abiertas con el hash Redis **`pins_in_use:{t}`** (campo = PIN, valor = `sessionId`; `HSETNX` con reintentos; liberación por `sessionId` al cerrar o regenerar). En `NIGHT`, `pinRequired = true` al abrir cualquier sesión (`venueMode == NIGHT \|\| (FIXED && RequiresPin)`), `inviteGrantsPin = false` y la respuesta del join lleva `pinValidated: false`. `GET /sessions/by-table` devuelve `session.PinRequired`. `sessions.venue_mode` se fija al abrir. `SessionsHub` pasa a `[Authorize]` (verificar `accessTokenFactory` en cashdesk/salon/pda/runner). |
| RT-1910 | **Estación del runner desde BD.** En cada petición de staff, la estación del llamante deja de ser la claim `stationId` del token: `JwtValidationMiddleware`, en la rama `type == "staff"` y tras la blacklist y la invalidación global, la **sustituye** por `user_memberships.assigned_station_id` mediante el puerto `IStaffStationResolver` (caché Redis **`staff_station:{tenantId}:{userId}`** de 60 s, centinela `-`) y la escribe en `ICurrentUser.AssignedStationId` y `HttpContext.Items["station_id"]`, que es lo que lee el código. No se añade ninguna claim ni se usa `IClaimsTransformation`. Los Device Tokens del KDS no se tocan. `StaffAssignmentCommands` deja de invalidar el token cuando solo cambia la estación (en el mismo PR). La app del runner (`runner/page.tsx`) no envía `stationId` si el usuario no es Admin y refresca su estación desde `GET /api/v1/identity/auth/me`. Diseño en SDD-001 §7. |
| RT-1911 | **Distribución y mesas.** `PATCH /tables/layout` acepta `mode`, `active` y `stationId`; `GET /floorplan?mode&includeHidden`; `PATCH /floors/{id}` acepta `visibleIn`; `PATCH /tables/{id}` acepta `kind`; `SessionCommands` responde `409 TABLE_CLOSED_IN_MODE` en open/join; `/sessions/calls` añade `tableKind`. `TableLayoutReadModel` (Orders) lee `table_layouts` del modo activo. La URL del QR usa `MENU_PUBLIC_URL` + `/mesa?tenant=&table=`. `POST /tables` (con `mode` opcional), `POST /tables/batch` y el auto-alta de `POST /sessions` crean las dos filas de `table_layouts` (RF-1940); `PATCH /tables/{id}` deja de aceptar posición; `GET /tables` filtra por el modo activo; `GET /floorplan` gana `tables[].activeGlobal` y `includeInactive` (Host y Admin). |
| RT-1912 | **Tema por tokens.** `<html data-venue="night">` redefine los tokens (`--canvas 22 22 22`, `--paper 30 28 26`, `--paper-2 38 35 32`, `--ink 244 236 226`, `--muted 181 168 150`, `--oro 170 132 83`…); `brand-*` y `fuego-*` pasan a variables con escala invertida en Night (500 = `#AA8453` fijo); Tailwind `darkMode: ["selector", '[data-venue="night"]']`; un **script inline en `<head>`** fija `dataset.venue` antes del primer pintado (`suppressHydrationWarning` en `<html>`); `viewport` de Next con `themeColor` por modo (el menú retira `maximum-scale=1`). Migración de hex por prioridad P1 → P3 según el plan. |
| RT-1913 | **Proveedor de modo.** `src/lib/venue.tsx` → `VenueProvider { mode, setMode, transitioning }` en las tres apps: `staff` y `admin` con conexión propia a `OrdersHub` (`JoinTenantGroup`, escucha `VENUE_MODE_CHANGED`); `menu` con sondeo. Persistencia `localStorage mestobeach_venue_mode` (menú: `:{tenantId}`), patrón de `useKioskMode`. Hasta que exista el backend, el proveedor funciona con un **stub de `localStorage`** (H0). Ante `503` o error de red, el proveedor conserva el último modo (`localStorage`) y no dispara la transición; solo un `200` con un `mode` distinto la dispara. |
| RT-1914 | **Marca.** `src/components/BrandMark.tsx` (`{night?, size?, className?}`): «◆ Mesto Beach» + `<em class="font-night text-oro">Night</em>`; `Cormorant_Garamond({weight:"400", style:"italic", variable:"--font-night"})` en los tres `app/layout.tsx`; sustituye todas las marcas inline. SVG estático `public/brand/mestobeach-night.svg`. Tres copias (`packages/ui` sigue vacío): contrato fijado en cabecera; deuda registrada en ADR-039. |
| RT-1915 | **Animación.** `src/components/VenueTransition.tsx` (una copia por app): portal a `document.body`, `#venue-root` envuelve la app, overlay `fixed inset-0 z-[3000]`. **Colapso** 420 ms `cubic-bezier(.7,0,.84,0)` (`clip-path: circle(150%) → circle(0%)`, `blur 0→6px`, `brightness 1→.4`, `scale 1→.92 rotate -2deg`; overlay en los últimos 150 ms) → **Singularidad** mín. 600 ms, máx. hasta datos (timeout 4 s; punto dorado 6 px con halo; debajo cambia `data-venue`, refetch, re-tema) → **Emersión** 600 ms `cubic-bezier(.16,1,.3,1)` (`circle(0%) → circle(150%)`, 16 partículas doradas, overlay se funde; al acabar se limpian `clip-path/filter/transform`). `prefers-reduced-motion`: fundido 200 ms. Disparo: admin al 200 del POST; staff al evento; menú al detectar el cambio. |
| RT-1916 | **Visita por dispositivo.** `menu/src/lib/visit.ts`: `localStorage mestobeach_visit:{tenantId}:{tableCode}` = `{sessionId, participantId, displayName, identified, pinValidated, savedAt}`; `storeSession/getSession/isIdentified/markIdentified` pasan a esa capa. En `/mesa`, si `joinByTable` devuelve la misma sesión y participante → se salta el registro, usando `pinValidated` del join. En `NIGHT` el registro pide nombre y PIN en la misma pantalla (`POST /sessions/{id}/pin/validate`) y ofrece, sin PIN, «Pedir el PIN» y «Llamar al camarero»; `GET /sessions/{id}?deviceId=` rehidrata. Límite documentado: otro navegador = otro dispositivo. |
| RT-1917 | **Cliente de API** (`@mestobeach/api-client`, manual): `VenueMode`, `createVenueApi`, `KdsStationDto{isActive, flowProfile, activeIn, isOn}`, `FloorPlanTable{kind, active, activeGlobal, stationId}`, `FloorPlanFloor{visibleIn}`, `PublicCatalogResponse{venueMode}`, `DeliveryTripStopItemDto{customerName, priority}`, `visibleIn` en ítem y categoría (y `visibleIn?` también en los tipos de creación `createItem`/`createCategory`), branding Night; `changedAt?` y `changedByName?` como opcionales (no `string \| null` obligatorio). |
| RT-1918 | **KDS por perfil.** `lib/kds.ts`: `kdsColumnsFor(profile)` (`FULL_PASS` = 4 columnas; `BAR` = `[Pedidos (acción READY), Listo]`), `columnOf(status, profile)` (en `BAR`: `QUEUED`, `IN_PREPARATION` y `RETURNED` → «Pedidos» con acción Listo; `READY` → «Listo»; `HELD`, `IN_DELIVERY`, `DELIVERED`, `CANCELLED` → fuera), `buildRecorrido` de 2 pasos; rejilla `repeat(cols.length, minmax(0,1fr))`; estación persistida en `localStorage kds_station:{tenantId}`, por defecto `assignedStationId`. Runner: `AvisosPanel` extraído de la PDA a `components/AvisosPanel.tsx` y montado en Night (conexión a `SESSIONS_HUB_URL` + `JoinWaiterGroup`); PDA añade el `invoke("JoinWaiterGroup")` que falta. `AvisosPanel` deja de reordenar solo por antigüedad: en `NIGHT` ordena reservados primero y después el más antiguo (RF-1956). El filtro por barra vive en `runner/page.tsx` (como `avisosDelSector` en la PDA, no dentro de `AvisosPanel`): avisos de `GET /sessions/calls` + `SessionEvent` cuyo `tableCode` no tenga en `GET /floorplan?mode=NIGHT` un `stationId` distinto de la estación efectiva del runner; el texto vacío del panel pasa a «Sin avisos en tu barra» en el runner. |
| RT-1919 | **Reportes.** `AnalyticsRepository`: `?mode=RESTAURANT\|NIGHT\|ALL`, día de negocio desde las **06:00** Europe/Madrid, y corrección de `UPPER(status) <> 'CANCELLED'` en todas las consultas afectadas. El corte vive en `VenueTime.BusinessDayStartHour` (método compartido `VenueTime.ToUtcRange`): `GET /api/v1/payments` también pasa a cortar a las 06:00. |
| RT-1920 | **PWA.** `public/manifest.webmanifest` por app, `display: standalone`, iconos 192/512, `metadata.manifest`; **sin service worker** offline en esta fase. |
| RT-1921 | **Responsividad.** Criterios de RF-1981 aplicados por prioridad: P1 `StaffShell` (`h-[100dvh]`, cajón con `Esc`/foco/cierre 44 px, safe-area), `runner` (barra inferior `sticky` en la columna de contenido, safe-area, select 44 px), `pda` + `ComandaDrawer` (bottom-sheet bajo `lg`, botones 44 px, pie ≤ 120 px), `admin` layout (aside oculto bajo `md` + hamburguesa + cajón); P2 `ScaledFloorMap` ×2 (pinch-zoom/pan con pointer events y `touch-action:none`), `mesas`/`catalog`; P3 `analytics`, `payments`, `Dialog`. |
| RT-1922 | **Plan.** Sin cambios: Mesto Beach en `pro` (estaciones ilimitadas); `PlanCatalog` intacto; el seed verifica `plan_code ∈ {pro, enterprise}`. |
| RT-1923 | **Verificación.** La notificación MediatR entre módulos (publicar → caché invalidada + difusión) se cubre con un test de integración en la primera PR de backend; el flujo completo se verifica con el script idempotente `scripts/e2e-night.mjs` contra la API y un tenant de prueba (§9). |

---

## 9. Criterios de Aceptación

Verificación end-to-end (automatizable en `scripts/e2e-night.mjs`, tenant de prueba, idempotente):

- [ ] **Preparación.** Con login de Administración, el tenant está en plan `pro` (sin límite de estaciones) y existen cuatro estaciones: Cocina y Parrilla (`FULL_PASS`, `[RESTAURANT]`), Barra 1 y Barra 2 (`BAR`, Barra 2 solo `[NIGHT]`). Barra 1: si OQ-1904 = sí, `BAR` en ambos modos; si = no, perfil por modo `{RESTAURANT: FULL_PASS, NIGHT: BAR}` (ADR-037).
- [ ] **Distribución.** El piso Playa tiene `visibleIn: [RESTAURANT]`; existen las mesas T1 (`TABLE`), H1 (`HIGH_TABLE`) y R1 (`RESERVADO`); la distribución Night adscribe T1 → Barra 1 y H1/R1 → Barra 2.
- [ ] **Carta.** «Chuletón» es visible solo en `[RESTAURANT]`, «Gin tonic» en ambos y «Shot» solo en `[NIGHT]`.
- [ ] **Bloqueo.** Con una sesión abierta en T1 con pedido, `POST /venue/mode NIGHT` responde **409 `VENUE_NOT_IDLE`** con `tables: ["T1"]`; al cerrarla, responde 200; `GET /venue/history` devuelve exactamente una fila.
- [ ] **Carta por modo.** En Night, `GET /catalog/{t}` no trae «Chuletón», sí «Shot», informa `venueMode: "NIGHT"` y `branding.theme: "night"`.
- [ ] **Pedido de noche.** Tras unirse a R1 y H1: un pedido de un comensal sin nombre (sin token de personal) responde **422 `NAME_REQUIRED`**, y una comanda de la PDA sin nombre responde 201 (los pedidos del personal no lo exigen); pedir «Chuletón» responde **409 `ITEM_NOT_IN_MODE`**; «Gin tonic» cae en Barra 2; forzar `stationId` de Parrilla responde **409 `STATION_CLOSED`**.
- [ ] **Dos estados.** `POST /items/{id}/ready` desde `QUEUED` en Barra 2 responde 200; en una estación `FULL_PASS` responde 409.
- [ ] **Reservados primero.** `/orders/active` y `/delivery-queue?stationId=Barra2` listan R1 antes que H1 aunque H1 pidió antes; con el local en `RESTAURANT`, R1 en servicio pide → `priority` 0, sin pastilla «Reservado», y Cocina mantiene el orden de llegada.
- [ ] **Nombre al runner.** Al crear un viaje, `/delivery-trips/mine` trae `customerName` en cada parada; una comanda de la PDA sin nombre sale como «Comanda de sala · {mesa}».
- [ ] **PIN toda la noche.** Tras `DEL pin_validated:*` en Redis, el siguiente pedido **no** devuelve `PIN_REQUIRED`; dos sesiones abiertas no comparten PIN.
- [ ] **Zona cerrada.** Un join en una mesa de Playa durante Night responde **409 `TABLE_CLOSED_IN_MODE`**; el menú muestra «Esta zona está cerrada esta noche; acércate a Recepción» y no la indicación del camarero.
- [ ] **PIN en todas las mesas.** Con el local en NIGHT, abrir una mesa Sin seguridad o DYNAMIC → 201 con `pin` no nulo y `pinRequired: true`; un pedido sin validar el PIN → 403 `PIN_REQUIRED`; `GET /sessions/{id}/pin` lo devuelve.
- [ ] **Mesa nueva.** Crear una mesa con `mode=NIGHT` → aparece en `GET /floorplan?mode=NIGHT` y no en el de RESTAURANT sin `includeHidden`.
- [ ] **Evento.** Un cliente SignalR en el grupo del tenant recibe `VENUE_MODE_CHANGED` al volver a `RESTAURANT`.
- [ ] **Reportes.** `analytics?mode=NIGHT` + `?mode=RESTAURANT` = `?mode=ALL`.

Verificación manual:

- [ ] Tras recargar en Night, `document.documentElement.dataset.venue === "night"` **sin destello** del tema claro en las tres apps.
- [ ] La animación «agujero negro» se ve en las tres apps al cambiar de modo; con movimiento reducido se ve un fundido de 200 ms.
- [ ] El comensal cierra el navegador, vuelve a escanear el QR de su mesa y entra directo a la carta sin nombre ni PIN; desde otro navegador se le pide registrarse.
- [ ] «Repetir lo último» re-añade el último pedido y avisa de lo que quedó fuera.
- [ ] El runner ve «Pregunta por: Nombre» en cada parada y la pastilla «Reservado»; en Night recibe los avisos de mesa.
- [ ] Reasignar un runner de Barra 1 a Barra 2 cambia su cola en ≤ 60 s sin cerrarle la sesión.
- [ ] Cocina y Parrilla muestran «Cerrada en Night»; Barra 2 muestra dos columnas; el pedido nuevo se destaca sin parpadeo.
- [ ] En Admin → Mesas, el segmentado «Restaurante | Night» guarda dos distribuciones distintas y avisa al cambiar con pendientes.
- [ ] `pnpm --filter … type-check` pasa en los cuatro paquetes; `dotnet build` pasa; las migraciones corren con la API parada y un `SELECT` confirma las columnas nuevas.
- [ ] Capturas día/noche de KDS, Salón, Admin y Menú para la aprobación del cliente (H0).

---

## 10. Open Questions

| ID | Pregunta | Responsable | Estado |
|---|---|---|---|
| OQ-1901 | ¿Qué pasa con las mesas abiertas al cambiar de modo? | Nilton | **Resuelta** — se bloquea hasta vaciar (`409 VENUE_NOT_IDLE` con la lista); sin `force` (D-10) |
| OQ-1902 | ¿Cuál es la unidad de pedido de noche y cómo se registra el comensal? | Mesto Beach | **Resuelta** — mesas (reservado, con sillas, altas); nombre + PIN único por mesa y noche; una vez por dispositivo (D-01) |
| OQ-1903 | ¿Cómo se reparten las mesas entre las dos barras: por cercanía fija o configurable? | Nilton + cliente | **Provisional** — mesa → barra configurable en la distribución Night (D-11); reparto inicial (¿por cercanía?) a confirmar con Mesto Beach (§14); se revisa tras las primeras noches reales |
| OQ-1904 | ¿La barra de día (Cocktails) pasa también al tablero de 2 estados? | Mesto Beach | **Abierta** — no bloquea PR1/PR2; **cerrar antes de PR3**: un «no» cambia el esquema de estaciones (perfil por modo, ADR-037), no solo su configuración; recomendación del equipo: **sí** (Barra 1 sería `BAR` en ambos modos) |
| OQ-1905 | ¿Precios nocturnos, snacks y modificadores de noche? | Mesto Beach | **Abierta** (pregunta al cliente, §14); fuera de la primera entrega; nombres reservados `nightPriceCents` / `allowedTableIds` |
| OQ-1906 | ¿El púrpura `#5B4E96` de la web es acento real de eventos/noche o ruido de plugin? ¿Entra como acento secundario de Night? | Mesto Beach | **Validar** con el cliente en H0 |
| OQ-1907 | ¿Habrá un límite por pedido (cantidad o importe) de noche? | Mesto Beach | **Abierta** (pregunta al cliente, §14); fuera de la primera entrega |
| OQ-1908 | ¿Qué dispositivos hay de noche (pantallas de barra, tablets de runner, móvil de Recepción)? | Mesto Beach | **Operativa** — alimenta el anexo de equipos; no bloquea el diseño |
| OQ-1909 | ¿A qué hora cierra Night como máximo? Si puede pasar de las 06:00, el corte del día de negocio debe moverse (p. ej. a las 07:00) | Mesto Beach | **Abierta** — supuesto actual: cierra antes de las 06:00; cerrar antes de PR11 |

---

## 11. Decisiones cerradas que originaron este PRD

Con el cliente (dirección de Playa Demo, octubre 2026):

| # | Decisión | Quién | Cuándo |
|---|---|---|---|
| D-01 | **Unidad: mesas** (reservado, con sillas, altas). **Nombre + PIN único por mesa y noche**, registrado **una vez por dispositivo**. | Mesto Beach | Oct 2026 |
| D-02 | **Entrega en mesa** por runners adscritos a barra; el runner ve **el nombre** de quien pidió. | Mesto Beach | Oct 2026 |
| D-03 | **Pago**: cuenta abierta, se liquida en el TPV (ADR-028 intacto). El pago móvil es futuro. Quien se acerca a la barra pide como hoy, fuera de Mesto. Lo que el comensal nombrado no recoge **se convierte en responsabilidad de la mesa** y se entrega igualmente; los runners llevan todo **sin destapar** y se abre en la mesa. | Mesto Beach | Oct 2026 |
| D-04 | **Reservados**: servicio presencial; la app es respaldo con prioridad inmediata: sus comandas y sus avisos van **primero** en barra y runner; el botón de camarero es el mismo de día. | Mesto Beach | Oct 2026 |
| D-05 | **Cambio de modo**: lo hace Administración desde el panel, **manual**, con aviso y **cuenta atrás de 5 s**, para **todo el local**, y queda **auditado**. | Mesto Beach | Oct 2026 |
| D-06 | **Identidad**: mismo branding + «Night»; **oscuro en las tres apps**; animación «agujero negro». | Mesto Beach | Oct 2026 |
| D-07 | **Distribución**: la Playa cierra; distribución Night **propia y persistente** sobre el **mismo plano**. | Mesto Beach | Oct 2026 |
| D-08 | **Reportes** separados por modo, con opción conjunta. | Mesto Beach | Oct 2026 |

Con Nilton (13 de octubre de 2026):

| # | Decisión | Quién | Cuándo |
|---|---|---|---|
| D-09 | **Alcance de la primera entrega: «Núcleo Night».** Fuera: productos por mesa, precios nocturnos, consumo mínimo, reservas, pago móvil; se **reservan los nombres de campo**. | Nilton | 13 oct 2026 |
| D-10 | **Mesas abiertas al cambiar: bloquear hasta vaciar** (409 con la lista); **sin `force`**. | Nilton | 13 oct 2026 |
| D-11 | **Reparto entre barras: mesa → barra configurable** en la distribución Night. | Nilton | 13 oct 2026 |
| D-12 | **La palabra «Night»** va en **itálica serif real** (Cormorant Garamond Italic) en dorado; **Gilda nunca inclinada**. | Nilton | 13 oct 2026 |

---

## 12. Relación con ADRs

ADRs **nuevos** que este PRD origina (se redactan en `ARCHITECTURE.md` §14 como *diseñados — pendientes de implementación, PRD-019*):

| ADR | Título | Qué fija |
|---|---|---|
| ADR-036 | Modo del local y transición cerrada | `VenueMode` como estado único del tenant; fuente de verdad + caché; cambio manual, idempotente, auditado y **bloqueado** mientras el local no esté vacío; difusión por hub y sondeo del menú |
| ADR-037 | Perfil de flujo y encendido por modo | `flow_profile (FULL_PASS \| BAR)`, `is_active`, `active_in[]`; «encendida» se calcula; `BAR` permite `QUEUED → READY` sellando `PreparationStartedAt = ReadyAt` |
| ADR-038 | Distribución, prioridad y enrutado por modo | `table_layouts` por (mesa, modo), `floors.visible_in`, `tables.kind` → `orders.priority`; override de estación por mesa |
| ADR-039 | Identidad Night y tema por tokens | `data-venue` + tokens CSS, `brand-*`/`fuego-*` a variables, `BrandMark`, animación por fases, anti-destello; deuda de las tres copias (`packages/ui`) |
| ADR-040 | PIN de noche y dispositivo | PIN obligatorio y único entre sesiones abiertas, TTL 12 h con Postgres como verdad, sin atajo de invitación, `NAME_REQUIRED`; visita recordada por dispositivo |
| ADR-041 | Carta única con visibilidad por modo | `visibleIn[]` en ítem y categoría, caché sin filtrar, filtro al servir y al validar; `branding.night` como overrides resueltos por la API |

ADRs existentes que se **superan parcialmente** (se anota la revisión en cada uno, sin reescribirlos):

| ADR | Qué se mantiene | Qué cambia con PRD-019 |
|---|---|---|
| ADR-030 (mapa a escala, Host en vivo) | Plano a escala, hamacas, frontera Host/Administración | La **posición pasa a ser por modo** (`table_layouts`), no una sola por mesa; la Playa puede no ser visible en un modo |
| ADR-032 / ADR-026 (cola del pase) | Marchar y dar la orden son dos decisiones; nada entra en fuegos sin la estación | Una estación **`BAR` salta `IN_PREPARATION`** (`QUEUED → READY`); la excepción vive en el perfil, no en la máquina general |
| ADR-029 (una estación por ítem) | Un ítem, una estación; sin platos compuestos | **Excepción de enrutado por mesa**: cuando el plato es `BAR` o sin estación, manda la barra adscrita a la mesa en el modo activo |
| ADR-027 (entrega por viajes) | Reclamo atómico, un viaje abierto por runner, cierre con un botón | El manifiesto gana **nombre del comensal** y **prioridad** por parada; la estación del runner se lee de BD, no del token |
| ADR-014 (PIN como gate diferido) | El gate de Orders al crear el pedido (`PIN_REQUIRED`), el modal con «pedir el PIN al mesero» (`PIN_REQUESTED`), el QR dinámico sin PIN y todo el modo `RESTAURANT` | En `NIGHT` el menú pide el PIN en el registro inicial, junto al nombre (`POST /sessions/{id}/pin/validate`), una vez por dispositivo; el gate de Orders queda como red de seguridad; y en `NIGHT` el PIN se exige también en mesas DYNAMIC y Sin seguridad |
| ADR-031 (moneda y hora del local) | Euros en céntimos, `Europe/Madrid` | El día de negocio se corta a las **06:00** en vez de a medianoche (`VenueTime.BusinessDayStartHour`), para Analytics y Payments |

ADRs que **se aplican tal cual**: ADR-028 (la cuenta se liquida en el TPV; nada de este PRD toca la facturación), ADR-033 (el diálogo de cambio de modo es propio), ADR-034 (modo tablero disponible en Night), ADR-022 (el sondeo autoritativo sigue siendo la verdad del KDS tras el cambio), ADR-023 (todo como capacidad del núcleo, sin bifurcar).

---

## 13. Hitos

Orientativo, con dos personas a dedicación parcial (Nilton backend y arquitectura; Brayan frontend y animación). El detalle de PRs vive en el plan de trabajo; aquí lo que se puede **enseñar** en cada hito.

| Hito | Qué se puede enseñar | Cuándo |
|---|---|---|
| **H0 — demo visual** | Tema Night + `BrandMark` + animación funcionando con el **stub** de `localStorage`, sin backend. Sirve para la **aprobación del cliente** (variante «agujero negro» vs «Eclipse», OQ-1906) | ~2 semanas desde la aprobación de este PRD |
| **H1 — núcleo operativo** | Cambio real con pre-flight, carta por modo, estaciones con perfil y barra de 2 estados, distribución Night | ~5–6 semanas |
| **H2 — noche completa** | PIN por dispositivo, reservados primero, runner con nombre y avisos, menú Night, reportes por modo | ~7–9 semanas |
| **H3 — responsividad y PWA** | Vistas de personal y panel usables en móvil; apps instalables (van en paralelo desde el inicio) | continuo |


---

## 14. Preguntas al negocio pendientes

Para llevar al cliente en la próxima reunión. Ninguna bloquea el arranque del backend (PR1/PR2); la primera condiciona la configuración de estaciones (PR3) y la segunda la demo visual (H0).

| OQ | Pregunta tal como se plantea al cliente | Qué necesitamos de vuelta | Cuándo hace falta |
|---|---|---|---|
| OQ-1904 | «De día, la barra de Cocktails ¿quiere trabajar como de noche —solo "Pedidos" y "Listo"— o prefiere seguir con las cuatro columnas de cocina?» Nuestra recomendación es **sí**: así Barra 1 es la misma barra de día y de noche. | Sí / No | Antes de PR3: un «no» cambia el esquema de estaciones (perfil por modo, ADR-037), no solo su configuración |
| OQ-1906 | «El púrpura `#5B4E96` que aparece en vuestra web, ¿es un color vuestro de eventos/noche o viene de un plugin? ¿Lo queréis como segundo acento del modo Night?» Lo enseñaremos sobre la demo visual. | Sí / No, y sobre qué elementos | En la demo visual (H0) |
| OQ-1905 | «¿La carta de noche tendrá precios distintos para el mismo producto, snacks o modificadores propios?» Hoy queda fuera; necesitamos saber si entra en una fase posterior para no cerrar puertas. | Lista de casos reales, si los hay | Antes de cerrar la fase 2 |
| OQ-1907 | «¿Queréis un límite por pedido (número de copias o importe) desde el móvil?» | Regla, si existe | Fase posterior |
| OQ-1908 | «¿Qué dispositivos hay de noche: pantallas en cada barra, tablets de los runners, móvil de Recepción? ¿Son los mismos del día?» | Inventario para el anexo de equipos | Antes de H1 |
| OQ-1903 | «De noche, ¿cada barra atiende las mesas que tiene más cerca? Decidnos qué mesas y reservados sirve cada barra; el reparto se puede cambiar desde el panel.» | Mesas que atiende cada barra (reparto inicial) | Antes de H1 (al cargar la distribución Night) |
| OQ-1909 | «¿A qué hora cierra la noche como muy tarde? Si alguna noche pasa de las 6 de la mañana, movemos el corte del día de los informes.» | Hora máxima de cierre de Night | Antes de PR11 (reportes por modo) |
