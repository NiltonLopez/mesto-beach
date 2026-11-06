# CLAUDE.md

Guía para Claude Code (claude.ai/code) al trabajar en este repositorio.

## Qué es Mesto Beach

Sistema de pedidos y servicio **a medida de un chiringuito de playa** en España (`Playa Demo` en esta
versión pública). El comensal escanea el QR de su mesa o hamaca, pide desde su móvil sin instalar nada, y el
pedido llega en tiempo real a la estación que corresponde (Cocina, Parrilla o Cocktails), al personal
de sala y a la caja. Idioma del producto: **español de España**.

> **Origen.** Nació como implantación de **Mesto**, una plataforma SaaS multi-tenant, y se separó a
> este repositorio en agosto de 2026 (**ADR-023**). De ahí que el esquema conserve `tenant_id`: hay
> **un solo local**, sembrado al instalar con `scripts/bootstrap-mestobeach.sql`.

Producto: `docs/GUIA.md` · Arquitectura y decisiones: `ARCHITECTURE.md` · Estado operativo real:
`AGENTS.md` · Cómo encender el entorno: `docs/DESARROLLO-LOCAL.md`.

## Proceso de desarrollo (importante)

El flujo es **PRD → Arquitectura (ADRs) → SDD → Código** (ver `PROCESS.md`). Regla fundamental: no se
escribe código hasta tener el SDD del módulo. Antes de cambiar contratos o comportamiento, lee el PRD
y el SDD correspondientes: `prds/PRD-*.md` y `sdd/SDD-*.md`.

Lo específico de este cliente está en **PRD-018** (mapa a escala, coursing, PDA, runners, host), en
**PRD-019** (modo fiesta, «Night») y en los **ADR-023…041** de `ARCHITECTURE.md` §14. Los SDDs marcados *v2.0* ya **no** son todos diseño
pendiente: buena parte está en código. No los tomes ni como implementados ni como pendientes —
**compruébalo en el código antes de afirmarlo**.

## Forma de trabajar

Casi todas las reglas de este fichero salieron de algo que se rompió o se malinterpretó. Al trabajar
aquí:

- **Comprueba antes de afirmar.** Un documento puede ir por detrás del código: manda el código.
- **Un cambio no está hecho hasta verificarlo.** Di qué se probó y cómo, y también qué no se probó.
- **Si tocas un contrato,** actualiza el SDD, `@mestobeach/api-client` y `CHANGELOG.md` en el mismo PR.
- **Nunca versiones secretos** ni condiciones comerciales del cliente.

## Ramas y pull requests

**Nada se commitea directamente en una rama compartida.** Diagrama para explicarlo:
`docs/branching.svg`.

| Rama | Para qué |
|---|---|
| `main` | **Solo versiones estables.** No recibe PR de ramas de trabajo. |
| `integration` | Donde llega todo. Punto de partida y de destino del día a día. |
| `feat/…` · `fix/…` · `docs/…` | Una por cambio. **Sale de `integration`** y vuelve a `integration` por PR. |

1. Antes de tocar nada, comprueba en qué rama estás. Si es `main` o `integration`, **crea una nueva
   desde `integration`**.
2. El cambio entra en `integration` por **PR**, nunca por push directo.
3. `main` se actualiza **solo desde `integration`**, con su propio PR, cuando hay versión estable.
4. **Cada PR actualiza `CHANGELOG.md`**: qué cambió, **por qué** y **qué se decidió**. Se conservan
   al menos las tres últimas entradas, la más reciente arriba — es el sitio donde ponerse al día sin
   reconstruirlo del historial de git.

## Arquitectura

**Backend** (`backend/`): monolito modular .NET 8. `src/MestoBeach.API/Program.cs` es el entry point y
registra cada módulo vía `Add<Modulo>Module(...)`. Los **7 módulos** viven en `src/Modules/`:
Identity, Catalog, Sessions, Orders, Payments, Analytics, Notifications. Cada uno sigue Clean
Architecture en 3 proyectos — `Domain → Application → Infrastructure` — y **maneja sus propias
migraciones EF**. `src/Shared/MestoBeach.Shared` es el kernel compartido.

> Se retiraron `Platform`, `Ledger` y `Loyalty` (ADR-023): eran maquinaria de SaaS multi-tenant. Sus
> SDDs quedan archivados en `sdd/retirados/`. **No los reintroduzcas.**

**Persistencia políglota**: PostgreSQL `mestobeach` (local, personal, sesiones, pedidos, pagos),
MongoDB `mestobeach_catalog` (la carta — documental), Redis (PIN, estado en tiempo real, caché).

**Tiempo real**: SignalR. Hubs en `Program.cs`: `/hubs/sessions`, `/hubs/orders`, `/hubs/realtime`.
La auth del navegador va por query string (`?access_token=`) — es intencional.

**Frontend** (`frontend/`): monorepo pnpm + Turborepo, Next.js 14 App Router. **Tres apps**:

| App | Puerto | Persona |
|---|---|---|
| `menu` | 3000 | Comensal (PWA mobile-first) |
| `staff` | 3001 | Siete módulos, uno por rol: `/board` Cocina · `/cashdesk` Caja · `/host` **Recepción** · `/pda` PDA · `/salon` Salón · `/runner` Runner · `/history` Historial |
| `admin` | 3003 | Administración del local |

Paquetes compartidos: `packages/api-client` (**manual, no generado**) y `packages/tsconfig`.

## Reglas del dominio que se rompen fácil

- **El dinero son céntimos de euro.** Todo entero monetario (`*Cents`) son céntimos: 1450 = 14,50 €.
  Al mostrarlo se divide entre 100 con `formatEur` (`Intl.NumberFormat("es-ES", … currency: "EUR")`).
  Nunca imprimas un importe crudo. Ver **ADR-031**.
- **El recibo térmico no es una pantalla.** `frontend/apps/staff/src/lib/escpos.ts` manda bytes por
  WebUSB a una impresora de un byte por carácter: nada de símbolos fuera de ASCII (el `€` sale como
  basura). Ahí se imprime `14,50 EUR`.
- **El día de negocio se corta a medianoche de `Europe/Madrid`**, no en UTC. Usa
  `MestoBeach.Shared.Time.VenueTime`; en SQL, `created_at AT TIME ZONE 'Europe/Madrid'`. España tiene
  horario de verano: restar un número fijo de horas está mal media parte del año.
- **El TPV del local factura, nosotros no** (ADR-028). No añadas emisión de facturas ni
  lógica fiscal sin un ADR nuevo: eso arrastra Verifactu y responsabilidad legal.
- **Marchar no enciende los fuegos** (ADR-032). Son dos decisiones: *marchar* (`HELD → QUEUED`,
  `/orders/items/{id}/march`) encola el plato y lo pueden hacer cocina **y** PDA; la *orden de
  preparación* (`QUEUED → IN_PREPARATION`, `/orders/items/{id}/start-preparation`) es **solo de la
  estación**. Un plato pedido "ahora" nace en `Queued`.
- **El modo del local (`RESTAURANT` | `NIGHT`) manda sobre carta, estaciones y mesas** (ADR-036…041).
  Se lee con `IVenueModeProvider`, nunca directo de Redis o Postgres. Solo se cambia con el local
  vacío (`409 VENUE_NOT_IDLE`, sin `force`), y si no se puede saber el modo la API responde 503 en
  vez de suponerlo.
- **El orden de los sectores es Salón → Terraza → Playa** en todos los selectores: sale de
  `floors.sort_order`, no lo alfabetices.

## Entorno y comandos

**Arranque — todo en Docker** (cualquier sistema, no hace falta .NET ni pnpm):

```bash
docker compose --profile demo up -d --build
```

El perfil `demo` añade el contenedor efímero `init`, que aplica migraciones, siembra el local y
crea la carta. Es **idempotente**: repetirlo no duplica nada. Sin el perfil, `docker compose up -d`
levanta lo mismo **sin tocar los datos**. Para desarrollo nativo con recarga en caliente (solo
Windows) está `.\scripts\dev-up.ps1`. Detalle de ambos en `docs/DESARROLLO-LOCAL.md`.

- Contenedores: `mestobeach_postgres`, `mestobeach_mongodb`, `mestobeach_redis`, `mestobeach_mailhog`,
  `mestobeach_azurite`, más `mestobeach_api`, `mestobeach_menu`, `mestobeach_staff`, `mestobeach_admin` y el
  efímero `mestobeach_init`. Base, usuario y clave: `mestobeach` / `mestobeach` / `mestobeach_dev`.
- El backend **se cae al arrancar si Redis no está**: Docker primero, siempre.
- Backend local en `http://localhost:5011` (ver `Properties/launchSettings.json`).
- **En desarrollo nativo**, define `NEXT_PUBLIC_API_URL=http://localhost:5011` en cada app
  (`.env.local`, fuera de git). **En Docker esos ficheros no se usan**: las `NEXT_PUBLIC_*` entran
  como *build args* desde `docker-compose.yml` y **se hornean en la imagen al construir**, así que
  cambiarlas exige `--build`.
- Acceso de desarrollo: `admin@mestobeach.demo` / `MestoBeach2026!`. Sirve para Admin (`:3003`) y para
  Staff (`:3001`). El menú del comensal **no funciona hasta que el staff abre una mesa**.
- `dotnet-ef` puede no estar en PATH: `export PATH="$HOME/.dotnet/tools:$PATH"`.

**Scripts del repo** (todos idempotentes salvo donde se diga):

| Script | Para qué |
|---|---|
| `scripts/docker-init.sh` | Lo ejecuta el contenedor `init` del perfil `demo`: migraciones → bootstrap → seed. No suele lanzarse a mano. |
| `scripts/set-host.sh` | Fija la dirección con la que se anuncia el sistema. Sin argumento detecta la IP LAN; acepta una IP, o `localhost` para volver al modo local. Escribe `MESTOBEACH_HOST` en `.env`. **Exige reconstruir después.** |
| `scripts/fix-blob-urls.sh` | Reescribe a la dirección actual las URLs de imagen ya guardadas en Mongo. Necesario tras cambiar `MESTOBEACH_HOST`. |
| `scripts/seed-demo-mestobeach.sh` | Carta de demostración por API (macOS/Linux; el `.ps1` es el equivalente en Windows). **No es idempotente**: repetirlo duplica la carta. Dentro del perfil `demo` no hace falta — `docker-init.sh` ya lo llama con guardarraíl. |
| `scripts/bootstrap-mestobeach.sql` | Alta del local y de la cuenta de administración. Idempotente (`ON CONFLICT DO NOTHING`). |

**Probar desde el móvil (por IP LAN, en HTTP)**: por defecto todo se anuncia como `localhost`, que
en el móvil es *el propio teléfono*. `MESTOBEACH_HOST` gobierna de una vez las **cinco** cosas que
tienen que coincidir —si falta una, no funciona—: `NEXT_PUBLIC_API_URL` de las tres apps,
`NEXT_PUBLIC_MENU_URL` de `staff` (base del **QR fijo**), `Frontend:BaseUrl` de la API (base del
**QR dinámico**), `Cors:AllowedOrigins` (el origen pasa a ser `http://<IP>:3000`) y
`BlobStorage:BaseUrl` (las **imágenes**).

```bash
./scripts/set-host.sh && docker compose --profile demo up -d --build && ./scripts/fix-blob-urls.sh
```

**Backend** (desde `backend/`):
- Run: `dotnet run --project src/MestoBeach.API`
- Build: `dotnet build src/MestoBeach.API`
- Tests: `dotnet test MestoBeach.sln`
- Migraciones: usa **siempre** `--project`, `--startup-project src/MestoBeach.API`, `--context` y
  `--connection` (este último **solo** lo acepta `database update`; `migrations add` no). Además:
  **para la API de `:5011` antes de compilar** — con el proceso vivo los DLL están bloqueados
  (`MSB3021`/`MSB3027`) — y **nunca uses `--no-build` en `database update`**: usa el ensamblado
  viejo y responde *"already up to date"* sin aplicar nada. Verifica contra Postgres, no contra la
  salida del comando.

**Frontend** (desde `frontend/`):
- Todas: `pnpm dev` · Una: `pnpm --filter @mestobeach/menu dev`
- Comprobación: `pnpm type-check` (o `--filter` para una sola app)

**Reset del entorno de pruebas**: usa la skill `reset-pruebas`
(`bash .claude/skills/reset-pruebas/reset-pruebas.sh`) cuando haya que volver a probar el flujo desde
cero. **No limpies Postgres a mano**: el estado "mesa ocupada" vive en Redis
(`table:{tenantId}:{tableCode}:active`), y borrar `sessions` sin limpiarlo deja mesas fantasma
ocupadas para siempre. La skill vacía lo transaccional en Postgres **y** el estado operativo en
Redis, y conserva configuración y login.

## Convenciones frontend

- **Auth client-side; no hay `middleware.ts`.** Las apps hidratan Zustand desde storage en `useEffect`.
- Claves de almacenamiento: `mestobeach_staff_jwt`, `mestobeach_staff_info`, `mestobeach_admin_jwt`,
  `mestobeach_device_id`; en `menu`, JWT y sesión en `sessionStorage`. El modo tablero se recuerda en
  `kds_kiosk` (staff) y `admin_kiosk` (admin).
- **El `content` de `tailwind.config.ts` incluye `./src/lib/**` — no lo quites.** Tailwind solo
  genera las clases escritas literalmente en lo que rastrea, y en `src/lib` viven mapas de clases
  compuestas (`apps/staff/src/lib/kds.ts`). Sin esa ruta el CSS no se genera y **el elemento sale
  sin color, sin ningún error**. Si creas una app nueva, cópiala.
- **Nada de `alert()` / `confirm()` / `prompt()` nativos**: usa el `DialogProvider` + `useDialog()`
  de cada app (`src/components/Dialog.tsx`). No queda ninguno nativo en el repo.
- `next.config` en `.mjs` con `typedRoutes` activo: los arrays de navegación con `href` usan `as const`.
- **`images.remotePatterns` lleva un patrón para Azurite** además del de Azure. Está acotado por
  puerto (`10000`) y ruta (`/devstoreaccount1/**`) en vez de por host, para que valga igual con
  `localhost` que con la IP LAN. Sin él, `next/image` responde **400 `"url" parameter is not
  allowed`** y no se ve ninguna imagen. Si creas una app nueva, cópialo.
- TypeScript con `exactOptionalPropertyTypes`: para params opcionales usa **spread condicional**, no
  mandes `undefined`.
- `@mestobeach/api-client` es **manual**: si cambias contratos del backend, actualízalo a mano en el
  mismo PR.
- **`crypto.randomUUID` no existe fuera de contexto seguro** (solo HTTPS o `localhost`), así que por
  IP en HTTP es `undefined`. Si necesitas un UUID en cliente usa `safeUuid()`
  (`apps/menu/src/lib/api.ts`), que cae a `getRandomValues`.
- **Iconos Phosphor, nunca emojis** (peso `light` en admin, `regular` en staff; el menú del comensal
  va sin iconos). Reglas de marca en `docs/marca.md`.

## Convenciones backend

- **Ninguna clave se versiona.** En Development, `DevJwtKeys` genera una clave JWT local
  (`.dev-keys/`, fuera de git); en cualquier otro entorno la API no arranca sin `Jwt__PrivateKeyPem`
  y `Jwt__PublicKeyPem`. Las contraseñas de `docker-compose.yml` y la cuenta de administración son
  solo para la demo local.
- **El correo son dos caminos y solo uno llega a MailHog.** `NotificationsModule` registra
  `MailKitEmailSender` (`IEmailSender`) y ese sí sale por SMTP a MailHog (`1025`, UI `8025`). Pero
  `IdentityModule` registra `EmailNotificationService` (`IEmailNotificationService`), hoy un **stub
  que solo escribe en el registro**: la activación de personal y el reset de contraseña **no
  aparecen en MailHog**. El enlace se saca con `docker compose logs api | grep EMAIL`.
- **`BlobStorage:BaseUrl` debe incluir el contenedor** (`…/devstoreaccount1/catalog-images`, no
  `…/devstoreaccount1`): el servicio compone `{BaseUrl}/{nombreDelBlob}` y los nombres ya empiezan
  por `branding/…` o `catalog-images/…`. Si falta el segmento, Azurite devuelve **403** y las
  imágenes no cargan aunque el panel diga "actualizado" — porque la subida sí funcionó. Y como la
  URL **se guarda entera en Mongo al subir**, arreglar la config no repara lo ya subido: pasa
  `scripts/fix-blob-urls.sh`.
- **`backend/Dockerfile`: el servicio `api` necesita `target: runtime`.** La última etapa del
  fichero es `initializer`, y `docker build` sin `--target` construye la última: la API acabaría
  siendo el sembrador y moriría con `exec: "/scripts/docker-init.sh": no such file or directory`.
- **Los `.sh` van siempre con LF** (`.gitattributes`). El contenedor `init` los ejecuta en Linux: con
  CRLF muere con `bash\r: No such file or directory`, la API arranca sin tablas y el login da 500.
- SignalR por query string en `/hubs/*` es intencional.

## Verificación realista

- **Backend:** `dotnet test MestoBeach.sln` corre 155 pruebas xUnit de dominio y seguridad (Orders,
  Identity y Sessions). Las de Orders recorren la máquina de estados del plato par a par. Catalog y
  Payments aún no tienen pruebas.
- **Frontend:** no hay tests; la comprobación es `pnpm type-check`.
- Si tocas un flujo, añade la **prueba manual o por API**: los tests no cubren la integración.
