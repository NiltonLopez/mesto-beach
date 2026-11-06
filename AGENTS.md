# Mesto Beach

Estado operativo real del repositorio. Si algo aquí contradice a `docs/GUIA.md`, gana esto; si algo
contradice al código, **gana el código**.

## Antes de cambiar algo
- `docs/GUIA.md` explica el producto; `PROCESS.md`, el flujo PRD → arquitectura (ADRs) → SDD → código.
- Si cambias contratos o comportamiento, lee antes el PRD y el SDD del módulo (`prds/`, `sdd/`).
- Lo propio de este cliente: **PRD-018**, **PRD-019** y **ADR-023…041** en `ARCHITECTURE.md` §14.
- Ojo con los SDDs marcados **v2.0**: ya **no** son todos diseño pendiente. El ciclo de vida por
  plato, el coursing, los viajes de runner, los roles Host/PDA/Runner y el mapa a escala **están en
  código**. Antes de dar algo por pendiente, compruébalo en el código: manda el código.

## Ramas y pull requests

**Nada se commitea directamente en una rama compartida.** El diagrama para explicárselo a alguien
que llega está en [`docs/branching.svg`](docs/branching.svg).

| Rama | Para qué |
|---|---|
| `main` | **Solo versiones estables.** No recibe PR de ramas de trabajo. |
| `integration` | Donde llega todo. Punto de partida y de destino del día a día. |
| `feat/…` · `fix/…` · `docs/…` | Una por cambio. **Sale de `integration`** y vuelve a `integration` por PR. |

1. Antes de tocar nada, comprueba en qué rama estás. Si es `main` o `integration`, **crea una nueva
   desde `integration`**:
   ```bash
   git checkout integration && git pull && git checkout -b feat/lo-que-sea
   ```
2. El cambio entra en `integration` por **PR**, nunca por push directo.
3. `main` se actualiza **solo desde `integration`**, con su propio PR, cuando hay versión estable.
4. **Cada PR actualiza `CHANGELOG.md`**: qué cambió, **por qué** y **qué se decidió**. Se conservan
   al menos las tres últimas entradas, la más reciente arriba — es el sitio donde ponerse al día sin
   reconstruirlo del historial de git. Las razones y las decisiones son justo lo que el `git log` no
   conserva bien.

## Layout real del repo
- `backend/`: monolito modular .NET 8. `src/MestoBeach.API/Program.cs` registra los módulos.
- **7 módulos**: Identity, Catalog, Sessions, Orders, Payments, Analytics, Notifications. Cada uno
  sigue `Domain -> Application -> Infrastructure` y maneja sus propias migraciones EF.
- Se **retiraron** `Platform`, `Ledger` y `Loyalty` al separar el producto de Mesto (ADR-023). Sus
  SDDs están en `sdd/retirados/`. No los reintroduzcas.
- `frontend/`: monorepo pnpm + Turborepo con **tres apps**: `menu` (3000), `staff` (3001),
  `admin` (3003). El personal de sala vive dentro de `staff` como la ruta `/salon` (ADR-019).
- `staff` son **siete módulos**, uno por ruta y cada uno con su rol (`components/StaffNav.tsx`):
  `/board` Cocina (KDS) · `/cashdesk` Caja · `/host` **Recepción** · `/pda` PDA · `/salon` Salón ·
  `/runner` Runner · `/history` Historial. **La vista del Host se llama "Recepción"**, no "Sala":
  cambió la etiqueta, no la ruta `/host` ni el rol `Host`.
- Compartidos: `packages/api-client` (manual, no generado) y `packages/tsconfig`.

## Entorno y gotchas
- **Todo en Docker (cualquier sistema)**: `docker compose --profile demo up -d --build`. Levanta
  infraestructura, API y las tres apps, y el perfil `demo` añade el contenedor `init`, que aplica
  migraciones, siembra el local y crea la carta. Es **idempotente**: repetirlo no duplica nada.
  Sin el perfil, `docker compose up -d` levanta lo mismo **sin tocar los datos**.
- **Desarrollo nativo con recarga en caliente** (solo Windows): `.\scripts\dev-up.ps1`, que deja la
  infraestructura en Docker y el código en la máquina. Detalle de ambos en `docs/DESARROLLO-LOCAL.md`.
- Contenedores `mestobeach_*`: `postgres`, `mongodb`, `redis`, `mailhog`, `azurite`, más `api`,
  `menu`, `staff`, `admin` y el efímero `init`. Base, usuario y clave: `mestobeach` / `mestobeach` /
  `mestobeach_dev`. Mongo es `mestobeach_catalog`.
- **El backend se cae al arrancar si Redis no responde** (`ConnectionMultiplexer.Connect` en
  `Program.cs`). Docker primero, siempre.
- El backend local corre en `http://localhost:5011`
  (`backend/src/MestoBeach.API/Properties/launchSettings.json`).
- **En desarrollo nativo**, define `NEXT_PUBLIC_API_URL=http://localhost:5011` en el `.env.local` de
  cada app; esos archivos no están en git y hay que crearlos en cada máquina. **En Docker no se usan**:
  esas variables entran como *build args* desde `docker-compose.yml` y se hornean en la imagen.
- `backend/src/MestoBeach.API/appsettings.Development.json` tampoco está en git: lleva Mongo con
  autenticación, Blob apuntando a Azurite y la clave TOTP.
- `dotnet-ef` puede no estar en PATH: `export PATH="$HOME/.dotnet/tools:$PATH"`.

## Trampas que ya nos costaron horas

Ninguna da un error claro: todas fallan en silencio o mienten. Están aquí para que no las repitas.

### Tailwind no ve `src/lib` si no se lo dices
El `content` de los tres `tailwind.config.ts` incluye **`./src/lib/**`** además de `app` y
`components`. Tailwind solo genera las clases que encuentra **escritas literalmente** en los
archivos que rastrea, y en `src/lib` viven mapas de clases compuestas en objetos (los colores de
botón y la urgencia del reloj del KDS están en `apps/staff/src/lib/kds.ts`). Si quitas esa ruta —o
creas una app nueva sin ella— las reglas no llegan al CSS y **el elemento sale sin color**, sin
ningún aviso: así fue como el botón "Listo" del KDS acabó transparente, blanco sobre crema.

### Migraciones EF: tres formas de creerte que aplicaste algo
- **Para la API de `:5011` antes de compilar.** Con el proceso vivo, los DLL están bloqueados y el
  build revienta con `MSB3021` / `MSB3027`.
- **Nunca `--no-build` en `dotnet ef database update`.** Usa el ensamblado viejo, no ve la migración
  nueva y responde tan tranquilo *"already up to date"* sin tocar la base. **Verifica siempre contra
  Postgres** (que la columna exista), no contra lo que imprime el comando.
- **`migrations add` no acepta `--connection`** (solo lo acepta `database update`). Si se lo pasas,
  falla el parseo del comando.

### Probar desde el móvil (por IP LAN, en HTTP) pide cinco cosas a la vez
Cinco, y si falta una no funciona. En Docker las gobierna **una sola variable**, `MESTOBEACH_HOST`:

```bash
./scripts/set-host.sh          # detecta la IP LAN y la deja en .env
docker compose --profile demo up -d --build
```

El `--build` **no es opcional**: las `NEXT_PUBLIC_*` de Next **se hornean en la imagen al
construir**, no se leen al arrancar el contenedor. Lo que esa variable ajusta:

1. `NEXT_PUBLIC_API_URL` de las tres apps — si no, el móvil llama a *su propio* `localhost:5011`.
2. `NEXT_PUBLIC_MENU_URL` de `staff` — es la base del **QR fijo** de cada mesa.
3. `Frontend:BaseUrl` de la API — es la base del **QR dinámico**, que lo arma el backend.
4. `Cors:AllowedOrigins` — en `appsettings.json` solo están los `http://localhost:300x`; el origen
   del móvil es `http://<IP>:3000` y sin añadirlo el backend lo rechaza.
5. `BlobStorage:BaseUrl` — es la base de las **imágenes** de la carta y del branding.

Además, API y fronts han de escuchar en `0.0.0.0` (en Docker ya lo hacen) y los puertos `3000`,
`3001`, `3003`, `5011` y `10000` han de estar abiertos en el firewall.
Además, **`crypto.randomUUID` no existe fuera de contexto seguro** — solo en HTTPS o `localhost`—,
así que por IP en HTTP es `undefined`. Por eso el menú genera el `deviceId` con `safeUuid()`
(`apps/menu/src/lib/api.ts`), que cae a `getRandomValues`. Si escribes código nuevo que necesite un
UUID en el cliente, **usa ese helper**, no `crypto.randomUUID` a pelo.

### La URL de una imagen se guarda entera al subirla, y la base debe llevar el contenedor
Dos trampas encadenadas, y las dos acaban en imágenes que no cargan mientras el panel dice
"actualizado" tan contento —porque la **subida sí funciona**, lo que está mal es la URL—:

- **`BlobStorage:BaseUrl` tiene que incluir el contenedor**: `…/devstoreaccount1/catalog-images`,
  no `…/devstoreaccount1`. El servicio compone `{BaseUrl}/{nombreDelBlob}`, y los nombres ya
  empiezan por `branding/…` o `catalog-images/…`. Si falta el segmento, Azurite interpreta
  `branding` como contenedor, no lo encuentra y responde **403 `AuthorizationFailure`** (o 404).
- **`next/image` rechaza el host de Azurite** si no está en `remotePatterns`: devuelve
  **400 `"url" parameter is not allowed`**. El patrón vive en los tres `next.config.mjs` y está
  acotado por puerto (`10000`) y ruta (`/devstoreaccount1/**`) en vez de por host, para que valga
  igual con `localhost` que con la IP LAN.

Y lo que convierte esto en trampa: **la URL pública se guarda entera en Mongo en el momento de
subir**, no se calcula al leer. Arreglar la configuración **no repara lo ya subido**. Para
reescribir lo existente a la dirección actual (también al cambiar de IP):

```bash
./scripts/fix-blob-urls.sh
```

### El Dockerfile del backend: sin `target`, Docker construye la última etapa
`backend/Dockerfile` tiene tres etapas y la última es `initializer` (el sembrador del perfil
`demo`), no `runtime`. Como `docker build` sin `--target` construye **la última**, el servicio
`api` de `docker-compose.yml` declara **`target: runtime`** de forma explícita. Si lo quitas —o
añades etapas debajo sin mirar—, la API se construye como el inicializador y el contenedor muere
con `exec: "/scripts/docker-init.sh": no such file or directory`, que no dice nada del problema
real. El síntoma delator: el error habla de la API, no del `init`.

### La persistencia es políglota: limpiar Postgres a mano deja mesas fantasma
El estado "mesa ocupada" **no vive en Postgres**, vive en Redis
(`table:{tenantId}:{tableCode}:active`, en `SessionCacheService`). Si borras filas de `sessions` sin
limpiar Redis, las mesas siguen apareciendo ocupadas para siempre y no se puede abrir sesión en
ellas. Lo mismo con PINs y llamados.

**No lo limpies a mano: usa la skill `reset-pruebas`.**

```bash
bash .claude/skills/reset-pruebas/reset-pruebas.sh
```

Vacía lo **transaccional** en Postgres (`sessions`, `orders`, `order_items`, `payments`,
`delivery_trips`, `shifts`, `device_tokens`, `session_participants`, `customer_accounts`,
`audit_log`) **y** el estado operativo en Redis (`table:*`, `session:*`, `pin_validated:*`,
`waiter_call:*`), y **conserva** la configuración (tenant, staff, mesas, planos, estaciones, carta
en Mongo) y tu login (`refresh:*`, `user_sessions:*`). Úsala para volver a probar el flujo completo
desde cero; **no** es un wipe de fábrica. Necesita los contenedores levantados. Detalle en
`.claude/skills/reset-pruebas/SKILL.md`.

## Reglas del dominio que se rompen fácil
- **Dinero = céntimos de euro.** Los campos `*Cents` son céntimos (1450 = 14,50 €); al mostrarlos se
  dividen entre 100 con `formatEur`. Nunca imprimas un importe crudo (ADR-031).
- **El recibo térmico solo admite ASCII**: `frontend/apps/staff/src/lib/escpos.ts` imprime
  `14,50 EUR`, no `€` — los bytes van directos a la impresora.
- **El día de negocio se corta en `Europe/Madrid`**, no en UTC: usa `MestoBeach.Shared.Time.VenueTime`
  o `AT TIME ZONE 'Europe/Madrid'` en SQL. Nada de restar horas fijas (hay horario de verano).
- **No emitimos facturas**: eso es del TPV del local (ADR-028). Meterse ahí implica Verifactu.
- **Marchar no enciende los fuegos** (ADR-032). Son dos decisiones distintas: *marchar*
  (`HELD → QUEUED`, `POST /orders/items/{id}/march`) mete el plato en la **cola** de su estación y lo
  pueden hacer cocina **y** PDA; la **orden de preparación** (`QUEUED → IN_PREPARATION`,
  `POST /orders/items/{id}/start-preparation`) es **solo de la estación**. Un plato pedido "ahora"
  **nace en `Queued`**, no en preparación.
- **El aviso "¿cuándo sale?" no va a cocina.** `POST /sessions/{id}/ask-eta` crea un llamado con
  motivo `ETA` y emite `ETA_REQUESTED` al grupo de sala; hoy **solo `/salon` lo escucha** (el PDA no
  se suscribe a `SessionsHub`), y **no pisa** un `PIN` ni un `WAITER`
  pendientes. Tampoco calculamos un ETA estimado: el menú del comensal solo muestra *desde cuándo*
  está el plato en su estado, con los sellos por transición.
- **El orden de los sectores es Salón → Terraza → Playa** en todos los selectores. No lo alfabetices
  ni lo ordenes por fecha: sale de `floors.sort_order` y el seed los crea en ese orden.
- **Iconos Phosphor, cero emojis** (`docs/marca.md` §7).

## Comandos útiles
- Entorno entero en Docker: `docker compose --profile demo up -d --build` (con datos) ·
  `docker compose up -d` (sin tocar datos) · `docker compose --profile demo logs -f init` ·
  `docker compose stop` (conserva datos) · `docker compose --profile demo down -v` (borra todo).
- Backend (desde `backend/`): `dotnet run --project src/MestoBeach.API` · `dotnet build src/MestoBeach.API`
  · `dotnet test MestoBeach.sln`
- Frontend (desde `frontend/`): `pnpm dev` · `pnpm --filter @mestobeach/staff dev` · `pnpm type-check`
- `turbo.json` hace que `lint` y `type-check` dependan de builds upstream: usa `--filter` cuando
  toques una sola app.
- Migraciones EF: siempre con `--project`, `--startup-project src/MestoBeach.API`, `--context` y
  `--connection` (este último **solo** en `database update`). Antes de tocarlas, lee las trampas de
  arriba: para la API, y nada de `--no-build`.
- Reset del entorno de pruebas: `bash .claude/skills/reset-pruebas/reset-pruebas.sh`.
- Dirección con la que se anuncia el sistema: `./scripts/set-host.sh` (IP LAN) ·
  `./scripts/set-host.sh localhost` (vuelve al modo local). Reconstruye después.
- Reparar las URLs de imagen ya guardadas: `./scripts/fix-blob-urls.sh`.

## Convenciones que sí importan
- Auth client-side; no hay `middleware.ts` de Next. Las apps hidratan Zustand desde storage en
  `useEffect`.
- Claves de almacenamiento: `mestobeach_staff_jwt`, `mestobeach_staff_info`, `mestobeach_admin_jwt`,
  `mestobeach_device_id`; en `menu`, JWT y sesión en `sessionStorage`. El modo tablero recuerda su
  estado en `kds_kiosk` (staff) y `admin_kiosk` (admin) — claves separadas a propósito.
- **Nada de `alert()`, `confirm()` ni `prompt()` nativos.** Las tres apps tienen su propio
  `DialogProvider` + `useDialog()` (`src/components/Dialog.tsx`), con toast, confirm y prompt en la
  estética de cada una. Ya no queda ninguno nativo en el repo; no reintroduzcas uno.
- **Pantalla completa (modo tablero)** en `staff` y `admin`: `useKioskMode` / `useKioskToggle`
  (`src/lib/useKioskMode.ts`) + `<FullscreenButton />`. El estado propio manda y el fullscreen nativo
  es un extra: si el navegador lo niega, la pastilla flotante "Salir" es la única salida (Esc no
  dispararía).
- `typedRoutes` activo: los arrays de navegación con `href` usan `as const`.
- `exactOptionalPropertyTypes`: usa spread condicional en vez de mandar `undefined`.
- `@mestobeach/api-client` es manual: actualízalo a mano en el mismo PR que el contrato del backend.
- SignalR con `?access_token=` en `/hubs/*` es intencional.
- **El correo son dos caminos distintos, y solo uno llega a MailHog.** `Notifications` registra
  `MailKitEmailSender` (`IEmailSender`) y ese sí sale por SMTP a MailHog (`1025`, UI `8025`). Pero
  `Identity` registra `EmailNotificationService` (`IEmailNotificationService`), que hoy es un
  **stub que solo escribe en el registro**: la activación de personal y el restablecimiento de
  contraseña **no aparecen en MailHog**. El enlace se saca de los logs de la API:
  ```bash
  docker compose logs api | grep EMAIL
  ```

## Verificación realista
- Backend: `dotnet test MestoBeach.sln`, 155 pruebas xUnit (Orders, Identity y Sessions). Catalog y
  Payments aún no tienen pruebas.
- Frontend: sin tests. Al tocar un flujo: **type-check focalizado y prueba manual o por API**.
- Credenciales de desarrollo: `admin@mestobeach.demo` / `MestoBeach2026!`.
