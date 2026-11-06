# PRD-018 — Mesto Beach: Modo Beach Club

## Metadata

| Campo | Valor |
|---|---|
| **ID** | PRD-018 |
| **Versión** | 1.1 |
| **Estado** | Borrador — v1.1 |
| **Fecha** | Agosto 2026 |
| **Apps relacionadas** | `menu` (comensal), `staff` (nuevas vistas PDA, Runner, Host + KDS existente), `admin` (mapa a escala, carta, asignaciones) |
| **Módulos de API** | Sessions (sectores, mapa, ocupación), Orders (estados por ítem, coursing, viajes de entrega), Catalog (estación por plato), Identity (roles PDA/Runner/Host), Payments (EUR, convivencia con TPV) |
| **Depende de** | PRD-002 (flujo del cliente), PRD-003 (KDS y staff), PRD-004 (configuración del restaurante y editor de plano), PRD-011 (modos de operación), PRD-013 (gestión operativa), PRD-016 (panel de administración y caja) |
| **PRDs que dependen de este** | PRD-019 — **Extendido por PRD-019 (modo fiesta)**: Mesto Beach Night añade el modo del local sobre lo aquí definido, sin sustituirlo |

---

## 1. Propósito y Objetivos

### Propósito

Definir el modelo operativo de **Mesto Beach**, la implantación de Mesto para **Playa Demo** (chiringuito en la costa, España). El local sirve en **tres sectores** (Salón, Terraza y Playa, este último con ~200 hamacas), prepara en **tres estaciones** (Cocina, Parrilla y Cocktails) y divide al personal en **Host**, **PDA** (toma de comandas y atención) y **Runners** (entrega, adscritos a una sola estación).

El producto se entrega **con la marca del cliente**, pero se construye como **capacidades del núcleo de Mesto** activables por tenant — no como una bifurcación del código. Lo aquí definido debe poder reutilizarse en el siguiente beach club sin reescribirlo.

> **Qué NO es esto.** No es un reemplazo del TPV. El local opera hoy con **un TPV externo** y la facturación
> se queda ahí (ver §4 Out of Scope y OQ-03). Este PRD cubre la **capa de servicio**: pedir, preparar,
> entregar y coordinar la sala.

### Objetivos

| # | Objetivo | Métrica de éxito |
|---|---|---|
| OBJ-01 | Ningún runner depende de su memoria para saber qué plato va a qué puesto | 100% de los platos en tránsito tienen destino visible en el dispositivo del runner |
| OBJ-02 | Reducir los errores de entrega (plato equivocado / puesto equivocado) | Entregas devueltas por error < 1% de los platos servidos |
| OBJ-03 | El comensal pide sin esperar a que pase alguien | ≥ 60% de las comandas de Playa iniciadas desde el QR del propio comensal |
| OBJ-04 | Las tres estaciones sacan los platos de una misma mesa de forma coordinada | Diferencia entre el primer y el último plato de una comanda < 5 min |
| OBJ-05 | La Host mantiene el mapa y la ocupación al día sin avisar por radio | Cambios de mapa y de ocupación visibles en PDA/Runner/KDS en < 2 s |
| OBJ-06 | El servicio no se detiene si se cae la conexión a internet | Toma de comandas, KDS, entrega y cobro operativos con internet caído |

---

## 2. Contexto y Problema que Resuelve

El local sirve a cientos de comensales repartidos en tres ambientes muy distintos: mesas de salón, mesas de terraza y ~200 hamacas sobre la arena. Con el TPV actual (Ágora), la operación arrastra cuatro fricciones:

1. **La memoria del runner.** Un runner sale del pase con una bandeja de hasta 8 platos para 3 o 4 puestos distintos, entre 200 hamacas numeradas. No existe herramienta que le recuerde qué va dónde: la información vive en su cabeza.
2. **Estaciones descoordinadas.** Cocina, Parrilla y Cocktails no ven el avance de las demás. Cuando una comanda mezcla estaciones, una parte del pedido espera enfriándose mientras sale la otra.
3. **Pedir cuesta.** El comensal de hamaca tiene que localizar a alguien para pedir, repetir y esperar. Cada fricción es consumo que no ocurre.
4. **Sin datos de sala.** No hay una vista fiable y en vivo de cuánta gente hay por sector, ni del tiempo real que tarda un plato desde que sale del pase hasta que llega al puesto.

---

## 3. Usuarios y Roles

| Rol | Qué hace en el sistema | Dispositivo |
|---|---|---|
| **Comensal** | Escanea el QR de su mesa o hamaca, consulta la carta, pide, elige el ritmo de sus platos, llama al camarero, pide la cuenta | Su propio móvil (sin instalar nada) |
| **Host** | Asigna mesas y registra cuántas personas son; mantiene el mapa; ve la ocupación por sector. **[v1.1]** Su vista se presenta como **"Recepción"** | Tablet o móvil |
| **PDA** | Atiende **un sector**: comanda por cualquier mesa de su sector, marcha los platos en espera, atiende llamadas | Comandero / móvil |
| **Runner** | Adscrito a **una estación**: toma platos listos, arma su bandeja, los reparte guiado por el mapa, cierra la ruta y gestiona devoluciones | Móvil, en exterior y a pleno sol |
| **Estación (KDS)** | Cocina, Parrilla o Cocktails: ve sus platos, **[v1.1]** da la orden de preparación y marca listo, y ve los platos de las otras estaciones de la misma comanda con el color de cada una | Pantalla fija o tablet |
| **Cajero** | Cobra, gestiona el turno de caja (rol existente, PRD-016) | TPV / tablet |
| **Administración** | Construye el mapa a escala, mantiene la carta y las estaciones, gestiona personal y asignaciones | Escritorio |

---

## 4. Alcance

### In Scope

- **Tres sectores** (Salón, Terraza, Playa) modelados sobre pisos, con **plano a escala real** y muros.
- **Puestos de tipo hamaca**, con alta en lote y numeración propia.
- **QR de pedido en los tres sectores**, incluidas las hamacas.
- **Ciclo de vida por plato** (no por comanda) como base de todo lo demás.
- **Coursing**: el comensal marca cada producto como "ahora mismo" o "luego de mi instrucción". **[v1.1]** Marchar lo pueden el PDA y la propia cocina; **la orden de preparación es solo de la estación** (ADR-032).
- **[v1.1] Observación libre de la comanda**: una nota del comensal o del PDA que viaja con el pedido hasta cocina, caja, salón y runner.
- **[v1.1] "¿Cuándo sale?"**: el comensal consulta cómo va su pedido y puede pedir que alguien de sala se lo cuente.
- **Interfaz PDA**: mapa del sector, comanda con buscador y secciones de carta, marcha de platos.
- **Interfaz Runner**: cola de su estación, armado de bandeja, mapa-manifiesto de entrega, cierre de ruta, devoluciones.
- **Interfaz Host**: ocupación en vivo por sector, asignación con número de comensales, ajuste del mapa.
- **KDS por estación** con **[v1.1] cuatro columnas** (esperando orden / en cola / en preparación / en el pase) y **visibilidad cruzada con el color de cada estación** (ADR-029).
- **[v1.1] Modo tablero** (pantalla completa) en las siete vistas de `staff` y en `admin`, para pantallas colgadas y atriles.
- **Identificación opcional** del comensal (nombre y nacionalidad) para idioma y estadística.
- **Marca del cliente** en las apps de cara al local (`menu`, `staff`, `admin`).
- **Localización España**: euros, vocabulario ES-ES, carta bilingüe ES/EN.
- **Despliegue híbrido** local/nube y **convivencia con el TPV existente**.

### Out of Scope

- **Facturación y cumplimiento fiscal (Verifactu).** Las facturas las sigue emitiendo el TPV del cliente. Si en el futuro Mesto emite, entra por un PRD propio.
- **Platos compuestos multi-estación.** Cada plato pertenece a **una** estación; la coordinación se resuelve con visibilidad cruzada, no partiendo el plato (ver §6, E4).
- **Tiempo estimado de preparación por plato y aviso de demora al comensal.** Descartado: sin platos compuestos ni promesa de ETA, el campo no tiene caso de uso activo.
- **Descuentos a empleados y asociados.** Reconocido como necesidad futura; sin definir.
- **Pasarela de pago en la app.** Objetivo declarado del cliente, pero depende de la decisión fiscal (OQ-03).
- **Multi-local.** El local opera un solo local; la consola de Ops (`platform`) no se entrega ni se marca con la marca del cliente.

---

## 5. Flujo

```
  COMENSAL                    PDA / HOST              ESTACIONES            RUNNER
     │                            │                       │                   │
     │  la Host asigna puesto ────┤                       │                   │
     │  (registra nº de personas) │                       │                   │
     │                            │                       │                   │
  escanea QR                      │                       │                   │
     │  (nombre + país, opcional) │                       │                   │
     │                            │                       │                   │
  arma su pedido                  │                       │                   │
   cada plato:                    │                       │                   │
   "ahora" ──────────────────────────────────────►  EN COLA                   │
   "luego" ──────────────────────────────────►  EN ESPERA                     │
     │                            │                       │                   │
     │   ¿siguiente plato?        │                       │                   │
     │◄── el PDA confirma ───────►│── marcha ────────►  EN COLA                │
     │                            │                       │                   │
     │  ¿cuándo sale? ──────────► aviso ETA          da la ORDEN               │
     │  (nunca a cocina)          │              de preparación ──► EN PREPARACIÓN
     │                            │                       │                   │
     │                            │                  marca LISTO ──────────►  cola de
     │                            │                       │              SU estación
     │                            │                       │                   │
     │                            │                       │            arma bandeja
     │                            │                       │         (varios puestos)
     │                            │                       │                   │
     │                            │                       │       mapa-manifiesto:
     │                            │                       │        qué va a qué puesto
     │                            │                       │                   │
     │◄──────────── entrega ──────────────────────────────────────────── ENTREGADO
     │                            │                       │                   │
     │                            │                       │◄── devuelto ──── (si falla)
     │                            │                       │
     │  al entregarse el último plato → COMANDA FINALIZADA
     │
  pide la cuenta ───────────► cobro en caja (TPV del local)
```

**Vista del runner durante la ruta** (mapa del sector con sus paradas):

```
 ┌──────────────────────────── PLAYA ─────────────────────────────┐
 │                                                                │
 │   H01   H02  ((H03))  H04     H05   H06   H07  ((H08))         │
 │                 ▲                                   ▲          │
 │                 │                                   │          │
 │   H09   H10   H11    H12      H13   H14   H15   H16            │
 │                                                                │
 └────────────────────────────────────────────────────────────────┘
   Llevas 5 platos · 2 paradas
   ((H03))  2× Espeto de sardinas · 1× Ensalada marinera
   ((H08))  2× Pulpo a la brasa
                                     [ ENTREGA FINALIZADA ]
```

---

## 6. Historias de Usuario

### E1 — El mapa a escala

- **Como** Administración, **quiero** construir el plano de cada sector con medidas reales y sus muros, **para que** lo que el personal ve en pantalla coincida con lo que pisa.
  - El sector se calibra a escala (metros ↔ píxeles) y admite una imagen de plano de fondo.
  - Las mesas y hamacas tienen posición, tamaño, forma y número propio.
  - Las ~200 hamacas de Playa se dan de alta en lote, no una a una.
- **Como** Host, **quiero** mover o desactivar puestos sobre el plano, **para que** el mapa refleje la realidad del día.
  - Los cambios llegan a PDA, Runners y KDS al instante.
  - **La Host no crea, elimina ni renumera puestos**: eso vive en Administración, porque el número está impreso en el QR físico.
- **[v1.1] Como** Administración, **quiero** organizar la sala **sobre el mismo plano a escala que ve el equipo**, **para que** lo que dibujo y lo que ellos usan sean la misma cosa.
  - Arrastro las mesas a su sitio; las posiciones se guardan en lote cuando termino.
  - Añado más puestos de los **mismos tipos y dimensiones del local** (Redonda 2/4/6, Cuadrada 4/6/8, Hamaca de playa), sin inventar medidas.
  - Cambio forma y plazas de un puesto, o lo elimino.
- **[v1.1] Como** equipo, **quiero** que los sectores salgan siempre en el mismo orden —**Salón → Terraza → Playa**— en todos los selectores, **para no** dudar en qué pestaña estoy.

### E2 — Pedir desde el puesto

- **Como** comensal, **quiero** escanear el QR de mi mesa o hamaca y pedir desde mi móvil, **para no** tener que buscar a alguien.
  - Disponible en los tres sectores.
  - Puedo identificarme con nombre y país, o **saltarlo** y seguir como invitado.
- **Como** comensal, **quiero** decidir el ritmo, **para que** los platos lleguen cuando quiero.
  - Cada producto se marca al añadirlo como **"ahora mismo"** o **"luego de mi instrucción"**.
  - **[v1.1]** Lo inmediato entra **en la cola** de su estación —no directamente en los fuegos—; lo demás queda en espera, visible para cocina pero sin prepararse.
- **[v1.1] Como** comensal, **quiero** dejar una nota con mi pedido ("sin cebolla", "la niña es celíaca", "todo junto"), **para** no tener que explicarlo a quien pase.
  - La escribo en el carrito antes de confirmar; es una sola nota por comanda.
  - La ve cocina, caja, sala y el runner que me lo trae.
- **[v1.1] Como** comensal, **quiero** saber cómo va mi pedido sin levantarme, **para** no tener que preguntar.
  - En "Mis pedidos" veo el estado de cada uno y **desde cuándo** lleva en él ("En preparación · desde hace 8 min").
  - No se me promete una hora de salida: el sistema no la calcula y prefiere callar antes que mentir (D-04).
  - Si aun así quiero saberlo, hay un botón para preguntar y alguien de sala se acerca a contármelo.

### E3 — El PDA

- **Como** PDA, **quiero** ver el mapa de mi sector y comandar tocando una mesa, **para** trabajar sobre la distribución real.
  - La comanda ofrece buscador de platos, secciones de carta, personalizaciones y la elección ahora/luego.
- **Como** PDA, **quiero** marchar los platos en espera, **para que** nada salga a destiempo.
  - Confirmo con el comensal en persona antes de marchar (política de servicio del local).
  - **[v1.1]** Marchar mete el plato **en la cola** de su estación; no enciende los fuegos. Quien decide cuándo se empieza es la estación (ADR-032), así que marchar de más ya no descoloca a cocina.
  - El comensal puede **solicitar** su siguiente plato desde el móvil; esa solicitud me llega como aviso, no marcha sola.
- **[v1.1] Como** PDA, **quiero** leer la observación que dejó el comensal y poder escribir la mía al comandar, **para** que la cocina se entere de lo que se dijo en la mesa.
- **[v1.1] Como** PDA o Runner, **quiero** ver el plano de mi sector sin pedir permisos, **para** trabajar desde el primer día.
  - El plano es de lectura para cualquiera del equipo; diseñarlo sigue siendo cosa de Administración.

### E4 — Las estaciones

- **[v1.1] Como** estación, **quiero** mi tablero en **cuatro columnas**, de izquierda a derecha, **para** leer el servicio como se lee un texto.
  - **Esperando orden** → **En cola** → **En preparación** → **En el pase**.
  - Tres botones y ningún menú: **"Marchar"**, **"¡A preparar!"** y **"Listo"**.
  - **Nada entra en los fuegos sin que yo lo lance**: el paso "en cola" → "en preparación" es mío y de nadie más.
- **[v1.1] Como** estación, **quiero** ver los platos de las otras estaciones **con el color de esa estación** y su estado en vivo, **para** sacar la mesa completa y a la vez.
  - Antes se mostraban en gris; el gris los volvía invisibles justo cuando había que mirarlos (ADR-029).
  - Siguen siendo **no accionables**: los veo, no los toco.
- **[v1.1] Como** encargado de cocina, **quiero** abrir una comanda al frente y ver el recorrido de cada plato con el tiempo que lleva en cada estado, **para** entender de un vistazo por qué esa mesa va lenta.
  - El tablero de atrás se difumina; se cierra igual de rápido que se abrió.
  - El recorrido se muestra tanto de mis platos como de los ajenos.
- **[v1.1] Como** estación en hora punta, **quiero** que la cola llena quepa en la pantalla, **para no** tener que hacer scroll con las manos ocupadas.
  - La tarjeta se comprime sola según cuántas comandas haya en esa columna; el detalle completo sigue a un toque, en el modo foco.
  - Cuando la columna se llena, aparece al pie el total de comandas y cuánto lleva esperando la más antigua.
- **Como** estación, **quiero** recibir de vuelta los platos que el runner no pudo entregar, **para** rehacerlos o corregirlos.
  - El plato reaparece en mi cola marcado como **devuelto**; el motivo lo explica el runner en persona.

### E5 — Los runners

- **Como** runner, **quiero** ver únicamente los platos listos de **mi** estación, **para no** mezclarme con las demás.
- **Como** runner, **quiero** marcar los platos que cargo en la bandeja, **para que** el sistema me guarde la ruta.
  - Al reclamarlos, esos platos dejan de estar disponibles para otro runner y pasan a **en entrega**.
- **Como** runner, **quiero** el mapa con mis paradas y qué lleva cada una, **para no** depender de mi memoria entre 200 hamacas.
- **Como** runner, **quiero** cerrar la ruta con un botón grande, **para** confirmar lo entregado sin ir plato por plato.
  - Los platos van marcados por defecto; **desmarcar** uno lo devuelve a su estación.
  - La comanda pasa a **finalizada** sola cuando se entrega su último plato y no quedan platos en espera.

### E6 — La Host

- **Como** Host, **quiero** asignar cada mesa registrando cuántas personas son, **para que** la ocupación del local sea un dato y no una impresión.
- **Como** Host, **quiero** ver la ocupación total y por sector en vivo, **para** decidir dónde sentar.
- **[v1.1] Como** Host, **quiero** que mi vista se llame **Recepción**, **para que** el nombre diga lo que hago: recibir y sentar. "Sala" se confundía con el sector Salón.
- **[v1.1] Como** Salón, **quiero** que la pregunta "¿cuándo sale?" de una mesa me llegue a mí y no a cocina, **para** ir yo a contestarla sin interrumpir el pase.
  - El aviso convive con los demás (camarero, PIN, cuenta) y **nunca los pisa**: si la mesa ya reclamaba algo más urgente, ese sigue siendo el motivo.

### E7 — Marca y localización

- **Como** Mesto Beach, **quiero** que el software se vea y se llame como mi casa, **para** que el cliente perciba un producto propio.
  - Marca, colores, tipografías, logo y portada por tenant, en `menu`, `staff` y `admin`.
- **Como** comensal internacional, **quiero** la carta en mi idioma, **para** entender qué pido.
  - Español e inglés al lanzamiento; el idioma se sugiere por el país declarado al identificarse.
- Importes en **euros**, guardados como enteros en céntimos; vocabulario de España (camarero, móvil, cuenta).

### E8 — Que el servicio no se detenga

- **Como** local, **quiero** que un corte de internet no pare el servicio, **para no** depender de la cobertura.
  - PDA, KDS, runners y caja siguen operando contra el servidor del local.
  - Lo que se degrada es el pedido por QR del comensal: la carta invita a llamar al camarero.
- **Como** local, **quiero** seguir facturando con mi TPV actual, **para no** asumir riesgo fiscal ni cambiar de sistema.

### E9 — La pantalla como mueble del local **[v1.1]**

- **Como** cocina, caja, recepción, PDA, runner, salón o administración, **quiero** poner mi vista **a pantalla completa**, **para que** la pantalla colgada o el atril muestren trabajo y no cromo de navegador.
  - Desaparece la barra lateral de la app y, si el navegador deja, también la suya.
  - Se recuerda entre recargas: la pantalla de cocina vuelve sola a modo tablero.
  - Siempre hay una salida visible, aunque el navegador haya negado la pantalla completa; nadie se queda encerrado.
- **Como** Mesto Beach, **quiero** que ningún aviso se vea como una ventana de Windows, **para que** el software siga pareciendo mío hasta en el error.
  - Los avisos, las confirmaciones y las preguntas se muestran con la estética de cada app.

### Mapa y sectores

| ID | Requisito |
|---|---|
| RF-1801 | El sistema soporta N sectores por tenant; Mesto Beach arranca con Salón, Terraza y Playa (ese es también su orden de presentación, RF-1809). |
| RF-1802 | Cada sector define una escala real (metros por unidad de lienzo) y admite una imagen de plano de fondo. |
| RF-1803 | Un puesto tiene tipo (`mesa` \| `hamaca`), código único, posición, tamaño, forma, capacidad y modo de QR. |
| RF-1804 | Administración puede dar de alta puestos en lote indicando prefijo, rango y disposición en cuadrícula. |
| RF-1805 | La Host puede mover y activar/desactivar puestos; no puede crear, eliminar ni renumerar. |
| RF-1806 | Todo cambio del mapa se difunde en vivo a PDA, Runner, Host y KDS. |
| RF-1807 **[v1.1]** | **Leer** el plano está abierto a cualquier miembro del personal autenticado (Recepción, PDA, Runner, Salón, Caja, Administración). **Editarlo** —crear, mover, cambiar o eliminar puestos y sectores— sigue reservado a Administración. |
| RF-1808 **[v1.1]** | Administración organiza la sala sobre el mismo plano a escala que consume el equipo: arrastra puestos, añade puestos de los tipos y dimensiones del local (Redonda 2/4/6, Cuadrada 4/6/8, Hamaca), cambia forma y plazas, y elimina. Las posiciones se guardan en lote. |
| RF-1809 **[v1.1]** | Los sectores se presentan en un orden explícito y estable —Salón, Terraza, Playa— en todos los selectores de todas las apps. |

### Comanda y coursing

| ID | Requisito |
|---|---|
| RF-1810 **[v1.1]** | Cada ítem de una comanda tiene su propio ciclo de vida: `en espera` → **`en cola`** → `en preparación` → `listo` → `en entrega` → `entregado`, más `devuelto` y `anulado`. El estado `en cola` se intercala entre `en espera` y `en preparación` (ADR-032). |
| RF-1811 **[v1.1]** | Al añadir un producto, el comensal elige `ahora` o `luego`; **`ahora` nace `en cola`** —no en preparación— y `luego` queda `en espera`. Ningún plato entra en preparación sin que la estación lo lance. |
| RF-1812 **[v1.1]** | **Marchar** (`en espera` → `en cola`) lo pueden hacer el rol PDA, las estaciones (KDS) y Administración. |
| RF-1813 | El comensal puede solicitar su siguiente plato; genera un aviso al PDA del sector, nunca marcha el plato. |
| RF-1814 | Una comanda pasa a `finalizada` automáticamente cuando todos sus ítems están `entregados` y ninguno queda `en espera`. |
| RF-1815 | Cada ítem se enruta a la estación que corresponde a su categoría de carta. |
| RF-1816 **[v1.1]** | Una comanda admite una **observación libre** de hasta 500 caracteres, opcional. La captura el comensal en el carrito o el PDA al comandar, y se muestra en KDS, PDA, caja, salón y runner. |
| RF-1817 **[v1.1]** | El comensal puede preguntar **"¿cuándo sale?"** por su pedido. Genera un aviso de mesa que llega a Salón; **nunca llega a cocina**. |
| RF-1818 **[v1.1]** | El aviso de "¿cuándo sale?" es el **menos urgente** de los motivos de mesa: no sustituye a un aviso de PIN ni de camarero ya pendientes, aunque sí se crea si no había ninguno. |
| RF-1819 **[v1.1]** | En "Mis pedidos", el comensal ve el estado de cada pedido y **desde cuándo** lleva en él. El sistema **no muestra un tiempo estimado de salida**, porque no lo calcula (D-04). |

### Estaciones (KDS)

| ID | Requisito |
|---|---|
| RF-1820 **[v1.1]** | El tablero de una estación muestra **cuatro columnas**, de izquierda a derecha: **Esperando orden**, **En cola**, **En preparación** y **En el pase**. Las acciones son tres: **"Marchar"**, **"¡A preparar!"** y **"Listo"**. |
| RF-1821 **[v1.1]** | Si una comanda incluye ítems de otras estaciones, el tablero los muestra **con el color de esa estación** y su estado en vivo, y **no accionables**. Sustituye al gris de la v1.0 (ADR-029). |
| RF-1822 | Los ítems devueltos por un runner reaparecen en la cola de su estación de origen, señalados como devueltos. |
| RF-1823 **[v1.1]** | La **orden de preparación** (`en cola` → `en preparación`) es potestad exclusiva de la estación; ni el PDA ni el comensal pueden encender los fuegos. |
| RF-1824 **[v1.1]** | Al tocar una comanda se abre en **modo foco**, al frente y con el tablero difuminado, mostrando por cada plato —propio o ajeno— su **recorrido** y el **tiempo que lleva en cada estado**. |
| RF-1825 **[v1.1]** | La tarjeta de comanda **ajusta su densidad automáticamente** según cuántas comandas haya en su columna, de modo que una cola llena quepa sin scroll. A partir de cierto número de comandas, la columna añade al pie el total y la espera de la más antigua. |
| RF-1826 **[v1.1]** | Cada transición de un plato deja su **sello de tiempo**; son los sellos, y no una estimación, la base de todos los tiempos que se muestran. |

### Runners

| ID | Requisito |
|---|---|
| RF-1830 | Un runner solo ve los ítems `listos` de la estación a la que está adscrito. |
| RF-1831 | Un runner reclama uno o varios ítems para su bandeja; quedan `en entrega` y dejan de ser visibles para los demás runners. |
| RF-1832 | La vista de ruta muestra el mapa del sector con los puestos de destino resaltados y el detalle de qué ítems van a cada uno. |
| RF-1833 | El cierre de ruta ("entrega finalizada") marca como `entregados` los ítems que siguen marcados. |
| RF-1834 | Desmarcar un ítem antes de cerrar la ruta lo devuelve a su estación de origen con estado `devuelto`. |

### Recepción y ocupación

| ID | Requisito |
|---|---|
| RF-1840 | Al asignar un puesto, la Host registra el número de comensales; ese dato alimenta la ocupación. |
| RF-1841 | El sistema expone ocupación total y por sector, en vivo. |
| RF-1842 | La identificación del comensal (nombre y país) es **opcional** y no afecta al conteo de ocupación. |
| RF-1843 **[v1.1]** | La vista del rol Host se presenta al usuario como **"Recepción"**. Es un cambio de etiqueta: el rol y la ruta no cambian. |

### Personal

| ID | Requisito |
|---|---|
| RF-1850 | Existen los roles `Host`, `PDA` y `Runner`, además de los actuales. |
| RF-1851 | Un runner tiene una estación asignada y un PDA un sector asignado; son fijos por persona y editables por Administración. |
| RF-1852 | El acceso a cada vista está gateado por rol; Administración las ve todas. |

### Marca y localización

| ID | Requisito |
|---|---|
| RF-1860 | Las apps de cara al local toman marca, colores, tipografías, logo y portada de la configuración del tenant. |
| RF-1861 | Los importes se almacenan como enteros en **céntimos de euro** (`1450` = 14,50 €) y se muestran al usuario formateados en euros. |
| RF-1862 | La carta admite traducciones; el comensal ve español o inglés. |
| RF-1863 **[v1.1]** | Los avisos, confirmaciones y preguntas al usuario se muestran con la estética de cada app; no se usan diálogos nativos del navegador en ninguna superficie (ADR-033). |

### Pantalla y puesto de trabajo **[v1.1]**

| ID | Requisito |
|---|---|
| RF-1870 | Las siete vistas de `staff` (Cocina, Caja, Recepción, PDA, Runner, Salón e Historial) y el panel de `admin` ofrecen **modo tablero**: la vista ocupa toda la pantalla, sin el armazón de la app (ADR-034). |
| RF-1871 | El modo tablero se recuerda por app entre recargas, para que una pantalla fija vuelva sola a su estado de trabajo. |
| RF-1872 | Mientras el modo tablero está activo hay siempre una salida visible, aunque el navegador haya denegado la pantalla completa nativa. |

| ID | Requisito |
|---|---|
| RT-1801 | El ciclo de vida por ítem sustituye al estado por pedido como fuente de verdad de la cocina; el estado del pedido pasa a derivarse de sus ítems. |
| RT-1802 | El reclamo de ítems por un runner debe ser atómico: dos runners no pueden llevarse el mismo plato (control de concurrencia en la transición a `en entrega`). |
| RT-1803 | Los cambios de mapa, cola de estación y ocupación se difunden por SignalR; el sondeo autoritativo existente (ADR-022) sigue siendo la verdad ante desincronización. |
| RT-1804 | **Despliegue híbrido**: el servidor del local es la fuente de verdad de sesiones, comandas y cobros; la nube publica el menú del comensal y le traslada sus pedidos. |
| RT-1805 | Ante caída del enlace, las apps de personal siguen operando contra el servidor local y el menú del comensal degrada a "pide a tu camarero". Requiere ADR propio en Fase 2. |
| RT-1806 | Las apps de personal encolan y reintentan sus escrituras ante cortes breves de red. |
| RT-1807 | **Convivencia con el TPV**: las comandas deben poder trasladarse al TPV del local para que la cuenta y la factura salgan por él (ver OQ-03). |
| RT-1808 | La generalización a euros —enteros en céntimos, sin decimales— afecta a modelo, API, cliente de API y las cuatro apps; se hace antes de construir sobre ella. |
| RT-1809 | Las vistas de PDA y Runner se diseñan para uso a pleno sol y con una sola mano: alto contraste, tipografía de cuerpo alto y objetivos táctiles grandes. |
| RT-1810 | El plano de fondo del sector se almacena como imagen del tenant en el almacenamiento de blobs existente. |
| RT-1811 | Todo lo anterior se implementa como capacidad del núcleo activable por tenant; no se bifurca el repositorio. |
| RT-1812 **[v1.1]** | Cada transición del plato persiste su propio sello de tiempo junto a quién dio la orden de preparación. Los tiempos que ven cocina y comensal se derivan de esos sellos; no se guarda ninguna duración calculada. |
| RT-1813 **[v1.1]** | El estado `en cola` se añade al final del enumerado de estados del plato para no desplazar los valores ya persistidos. |
| RT-1814 **[v1.1]** | En el modo tablero manda el estado propio de la aplicación; la pantalla completa nativa es un añadido que se intenta y, si el navegador la niega, no bloquea nada. La preferencia se guarda con una clave distinta por app. |
| RT-1815 **[v1.1]** | La observación de la comanda viaja en la creación del pedido y en todos los DTO de lectura que consumen KDS, PDA, caja, salón y runner; ninguna superficie la deriva ni la reconstruye. |

---

## 9. Criterios de Aceptación

- [ ] Los tres sectores existen con su plano a escala y sus muros; Playa tiene sus hamacas numeradas y con QR.
- [ ] Un comensal de hamaca escanea, pide dos platos ("ahora" y "luego") y ambos aparecen en la columna correcta de su estación: el de "ahora" **en cola**, el de "luego" **esperando orden**.
- [ ] **[v1.1]** Ningún plato aparece en "En preparación" sin que alguien de la estación haya pulsado "¡A preparar!".
- [ ] **[v1.1]** El PDA marcha un plato en espera y este cae **en la cola** de su estación, no en los fuegos.
- [ ] **[v1.1]** En una comanda que mezcla estaciones, cada tablero ve los platos ajenos **con el color de su estación** y su estado real, sin poder accionarlos.
- [ ] **[v1.1]** Al tocar una comanda, el tablero se difumina y la comanda muestra el recorrido de cada plato con el tiempo en cada estado.
- [ ] **[v1.1]** Con la columna llena, las comandas siguen cabiendo sin scroll y el pie indica cuántas hay y cuánto lleva la más antigua.
- [ ] **[v1.1]** Una observación escrita en el carrito del comensal se lee tal cual en KDS, PDA, caja, salón y runner.
- [ ] **[v1.1]** "¿Cuándo sale?" deja aviso en Salón y no aparece en ninguna pantalla de cocina.
- [ ] **[v1.1]** Si la mesa ya tenía pendiente un aviso de PIN o de camarero, preguntar "¿cuándo sale?" no lo sustituye.
- [ ] **[v1.1]** "Mis pedidos" indica desde cuándo está el pedido en su estado y en ningún momento promete una hora de salida.
- [ ] **[v1.1]** Recepción, PDA, Runner y Salón cargan el plano del local sin recibir un error de permisos.
- [ ] **[v1.1]** Administración mueve una mesa sobre el plano a escala, guarda, y el equipo la ve en su nueva posición.
- [ ] **[v1.1]** Los sectores aparecen como Salón, Terraza y Playa, en ese orden, en todas las apps.
- [ ] **[v1.1]** Las siete vistas de `staff` y el panel de `admin` entran y salen del modo tablero, y la salida sigue estando visible aunque el navegador deniegue la pantalla completa.
- [ ] **[v1.1]** No aparece ningún diálogo nativo del navegador en ninguna de las tres apps.
- [ ] Un runner de Parrilla no ve platos de Cocina ni de Cocktails.
- [ ] Dos runners no pueden reclamar el mismo plato.
- [ ] La vista de ruta muestra el mapa con los puestos de destino y qué lleva cada uno.
- [ ] Al cerrar la ruta, los platos quedan entregados; el desmarcado vuelve a su estación como devuelto.
- [ ] La comanda se finaliza sola al entregarse su último plato.
- [ ] La Host mueve un puesto y el cambio se ve en el PDA en menos de 2 segundos.
- [ ] La ocupación por sector refleja las personas registradas al asignar.
- [ ] Con el enlace a internet caído: el PDA comanda, el KDS recibe, el runner entrega y la caja cobra.
- [ ] Los importes se muestran en euros en las cuatro superficies.
- [ ] La carta se puede consultar en español e inglés.

---

## 10. Open Questions

| ID | Pregunta | Responsable | Estado |
|---|---|---|---|
| OQ-01 | ¿Cuántas mesas tienen Terraza y Salón, y cuál es la unidad pedible en Playa (hamaca individual o pareja con mesita)? | Mesto Beach | Abierto |
| OQ-02 | ¿Existe plano a escala del local (CAD/PDF) o hay que levantarlo con medidas? | Mesto Beach | Abierto |
| OQ-03 | ¿La licencia del TPV del cliente incluye el módulo de integración con terceros? Es lo que decide si la convivencia es viable. | Nilton | Abierto |
| OQ-04 | ¿Quién emite la factura cuando el comensal pague desde la app? Condiciona si Mesto entra en el ámbito de Verifactu. | Mesto Beach + gestoría | Abierto |
| OQ-05 | ¿Cuál es la cobertura real de WiFi/4G en la zona de hamacas? Determina el alcance del modo degradado. | Mesto Beach | Abierto |
| OQ-06 | ¿El personal usa dispositivos propios o del local? ¿Las pantallas de estación serán tablets o televisores? | Mesto Beach | Abierto |
| OQ-07 | ¿Quién provee las traducciones de la carta al inglés? | Mesto Beach | Abierto |
| OQ-08 | ¿La cuenta se cobra por persona, por mesa, o ambas? | Mesto Beach | Abierto |
| OQ-09 | ¿El rol se llamará "PDA" también en la interfaz, o "Camarero"? | Nilton | Abierto |
| OQ-10 | ¿Fecha objetivo de demo y de puesta en producción? | Nilton | Abierto |

---

## Decisiones cerradas que originaron este PRD

| # | Decisión | Fecha |
|---|---|---|
| D-01 | **No existen platos compuestos.** Cada plato pertenece a una estación; la coordinación entre estaciones se resuelve mostrando en gris los platos ajenos de la misma comanda. | Ago 2026 |
| D-02 | La entrega se registra **por plato**; la comanda se finaliza sola al entregarse el último. | Ago 2026 |
| D-03 | **Solo el PDA marcha** los platos en espera, previa confirmación en persona con el comensal. | Ago 2026 |
| D-04 | **Descartada** la regla de tiempo estimado / aviso de demora a los 35 minutos. | Ago 2026 |
| D-05 | La Host **mueve** puestos pero no los renumera; el número vive impreso en el QR. | Ago 2026 |
| D-06 | El **pax lo registra la Host** al asignar; la identificación del comensal es opcional. | Ago 2026 |
| D-07 | Hay **QR de pedido en los tres sectores**, hamacas incluidas. | Ago 2026 |
| D-08 | Las devoluciones vuelven a la estación de origen; el **motivo se explica en persona**. | Ago 2026 |
| D-09 | Runner↔estación y PDA↔sector son **fijos por persona**, editables por Administración. | Ago 2026 |
| D-10 | **Despliegue híbrido**: personal contra servidor local (fuente de verdad), comensal desde la nube. | Ago 2026 |
| D-11 | Se entrega **con la marca del cliente** pero **sin bifurcar** el repositorio; la consola de Ops queda fuera. | Ago 2026 |

**Revisiones posteriores [v1.1]** — la tabla anterior se conserva como registro de lo que originó el PRD; tres de sus decisiones se revisaron al construirlas:

| # | Qué cambió | Dónde se decidió |
|---|---|---|
| D-01 | La visibilidad cruzada **ya no es en gris**: los platos ajenos llevan el color de su estación y su estado en vivo. El resto de la decisión (no hay platos compuestos) sigue en pie. | ADR-029 |
| D-03 | **Marchar deja de ser exclusivo del PDA**: también marcha la cocina. A cambio nace la **orden de preparación**, que sí es exclusiva de la estación. La confirmación en persona antes de marchar sigue siendo política del local. | ADR-032 |
| D-04 | **Se confirma**: no hay tiempo estimado. Lo que se muestra al comensal es desde cuándo lleva su pedido en el estado en el que está, nunca una previsión; para preguntar, el aviso va a la sala. | ADR-035 |
