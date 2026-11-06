# Encender el entorno local

Guía para levantar Mesto Beach en tu PC. La primera vez tarda unos minutos;
después, encender todo son dos pasos.

---

## Todo en Docker (multiplataforma)

El repositorio trae imágenes para el backend y las tres apps, así que no hace falta
tener .NET ni pnpm instalados: basta Docker.

```bash
docker compose --profile demo up -d --build
```

El perfil `demo` añade un contenedor de un solo uso, `init`, que **prepara los datos
por ti**: aplica las migraciones de los cinco módulos, siembra el local y la cuenta de
administración, y crea la carta de demostración (5 categorías, 11 platos, 3 estaciones
KDS, 3 sectores y 15 puestos). Al terminar se apaga solo.

Es **idempotente**: puedes repetir el comando tantas veces como quieras — las migraciones
ya aplicadas se saltan y, si la carta ya tiene categorías, no la vuelve a sembrar.

Sin el perfil, `docker compose up -d` levanta lo mismo pero **sin tocar los datos**: útil
para el día a día, cuando la base ya está preparada.

Sigue el progreso con:

```bash
docker compose --profile demo logs -f init
```

| Servicio | URL |
|---|---|
| Menú del comensal | `http://localhost:3000/mesa?tenant=<tenantId>&table=H01` |
| Staff | http://localhost:3001 |
| Admin | http://localhost:3003 |
| API + Swagger | http://localhost:5011/swagger |
| MailHog | http://localhost:8025 |

Acceso de desarrollo: `admin@mestobeach.demo` / `MestoBeach2026!`.

> Para volver a empezar de cero: `docker compose --profile demo down -v` borra los
> volúmenes, y el siguiente `up --profile demo` reconstruye los datos desde nada.

---

## El camino corto (Windows, sin Docker para el código)

1. **Abre Docker Desktop** (menú de inicio) y espera a que el icono de la ballena deje de moverse.
2. En PowerShell, desde la raíz del repositorio:

```powershell
.\scripts\dev-up.ps1
```

El script levanta los contenedores, espera a que la base de datos y Redis estén sanos,
y abre el backend y las tres apps, cada uno en su propia ventana. Cerrar una ventana
apaga ese servicio.

Variante: `.\scripts\dev-up.ps1 -SoloDocker` levanta solo la infraestructura.

> El script intenta arrancar Docker Desktop si no lo encuentra encendido, pero lanzar una
> aplicación de escritorio desde un script no siempre funciona: si se queda esperando,
> ábrelo a mano y vuelve a ejecutarlo.

---

## El camino manual (lo que hace el script)

### 1. Infraestructura

```powershell
docker compose up -d
```

Levanta PostgreSQL, MongoDB, Redis, MailHog y Azurite. Compruébalo con `docker ps`:
las cinco deben decir `Up`, y postgres/mongo/redis además `(healthy)`.

**Importante:** el backend se cae al arrancar si Redis no está listo, así que espera
a que estén sanos antes del paso siguiente.

### 2. Backend (ventana propia)

```powershell
cd backend
dotnet run --project src/MestoBeach.API --launch-profile http
```

Queda escuchando en `http://localhost:5011`. Compruébalo abriendo `http://localhost:5011/health`
— debe responder `Healthy`.

### 3. Apps (una ventana por app)

```powershell
cd frontend
pnpm --filter @mestobeach/menu dev
```

```powershell
cd frontend
pnpm --filter @mestobeach/staff dev
```

```powershell
cd frontend
pnpm --filter @mestobeach/admin dev
```

`pnpm dev` a secas levanta todas a la vez, pero mezcla los registros de todas en
una sola ventana; con `--filter` es más fácil ver qué falla.

---

## Direcciones

| Servicio | URL |
|---|---|
| Menú del comensal | `http://localhost:3000/mesa?tenant=<tenantId>&table=<mesa>` |
| Staff (caja, cocina, salón) | http://localhost:3001 |
| Admin (dueño) | http://localhost:3003 |
| API + Swagger | http://localhost:5011/swagger |
| MailHog (correos de prueba) | http://localhost:8025 |

---

## Apagar

- **Los servicios**: cierra sus ventanas, o `Ctrl+C` dentro de cada una.
- **Los contenedores**: `docker compose stop` (conserva los datos).

> ⚠️ `docker compose down -v` **borra los volúmenes**: perderías la base de datos, el
> catálogo y las imágenes subidas. Úsalo solo si quieres empezar de cero — y entonces
> tendrás que aplicar las migraciones y volver a sembrar los datos.

---

## Primera vez en una máquina nueva

Requisitos: Docker Desktop (con WSL 2), .NET SDK 8+, Node 20+, `pnpm`, y `dotnet-ef`.

```powershell
npm install -g pnpm@9.15.0
dotnet tool install -g dotnet-ef
```

Después, una vez con los contenedores arriba:

```powershell
cd frontend
pnpm install
```

Crear el esquema de la base de datos — una migración por módulo:

```powershell
cd backend
$env:ConnectionStrings__Postgres = "Host=localhost;Port=5432;Database=mestobeach;Username=mestobeach;Password=mestobeach_dev"
"Identity","Sessions","Payments","Notifications","Orders" | ForEach-Object {
  dotnet ef database update --project "src/Modules/$_/MestoBeach.$_.Infrastructure" --startup-project src/MestoBeach.API --context "$($_)DbContext" --connection $env:ConnectionStrings__Postgres
}
```

Configuración local (no va a git, hay que crearla en cada máquina):

- `backend/src/MestoBeach.API/appsettings.Development.json` — conexión a Mongo con autenticación,
  Blob Storage apuntando a Azurite, clave TOTP y `PlatformAuth:BypassMfa`.
- `frontend/apps/{menu,staff,admin}/.env.local` con `NEXT_PUBLIC_API_URL=http://localhost:5011`.

El local y su cuenta de administración (una sola vez) y, después, los datos de demostración (carta, estaciones, sectores y mesas):

```powershell
docker cp scripts/bootstrap-mestobeach.sql mestobeach_postgres:/tmp/bootstrap.sql
docker exec mestobeach_postgres psql -U mestobeach -d mestobeach -f /tmp/bootstrap.sql
.\scripts\seed-demo-mestobeach.ps1
```

En macOS o Linux, el equivalente del seed es `scripts/seed-demo-mestobeach.sh` (necesita
`jq`: `brew install jq`). Está escrito para el bash 3.2 que trae macOS, así que no hace
falta instalar uno más nuevo:

```bash
docker cp scripts/bootstrap-mestobeach.sql mestobeach_postgres:/tmp/bootstrap.sql
docker exec mestobeach_postgres psql -U mestobeach -d mestobeach -f /tmp/bootstrap.sql
bash scripts/seed-demo-mestobeach.sh
```

> Ninguno de los dos seeds es idempotente: si lo ejecutas dos veces, duplica la carta.
> El perfil `demo` de Docker sí lo es — comprueba antes si la carta ya tiene categorías.

La carta de demostración trae precios de chiringuito de playa **en céntimos de euro**
(el espeto a `600` son 6,00 €; el entrecot a `2600`, 26,00 €).

---

## Bases de antes: pasar el dinero a céntimos

El sistema heredado guardaba los importes en pesos colombianos, con campos y columnas
terminados en `Cop` / `_cop`. Ahora el entero significa **céntimos de euro** (`1450` = 14,50 €)
y los nombres llevan sufijo `Cents` / `_cents`.

Si creas la base desde cero no tienes que hacer nada: las migraciones de EF ya la crean con
los nombres nuevos, y el seed siembra euros. Esto es solo para una base que **ya existía**:

```powershell
# PostgreSQL — renombra las columnas *_cop a *_cents
docker cp scripts/migrate-money-to-cents.sql mestobeach_postgres:/tmp/cents.sql
docker exec mestobeach_postgres psql -U mestobeach -d mestobeach -f /tmp/cents.sql

# MongoDB — renombra priceCop/priceDeltaCop dentro de los documentos del catálogo
docker cp scripts/migrate-catalog-to-cents.js mestobeach_mongodb:/tmp/cents.js
docker exec mestobeach_mongodb mongosh "mongodb://mestobeach:mestobeach_dev@localhost:27017/?authSource=admin" --quiet --file /tmp/cents.js
```

Los dos son idempotentes: comprueban antes de tocar, y una segunda pasada no cambia nada.

> **Dónde vive el catálogo.** El script de Mongo recorre `mestobeach_catalog` *y* `mesto_catalog`,
> y omite la que no exista, porque en bases antiguas la carta quedó en la heredada. En una
> instalación actual es **`mestobeach_catalog`**: el backend elige la base con
> `CatalogOptions.MongoDatabaseName`, cuyo valor por defecto en el código ya es ese, y el nombre
> que lleve el connection string no influye. Si vienes de una base vieja, comprueba en cuál de
> las dos están tus documentos antes de dar por buena una migración.

> **Ninguno convierte importes**: el número se queda como está y solo cambia de significado.
> Los datos de desarrollo son de mentira, así que lo limpio es borrar la carta y volver a
> sembrarla con `seed-demo-mestobeach.ps1` en vez de arrastrar cifras que fueron pesos.

> **No mezcles caminos en la misma base**: si aplicas el `.sql` a mano y después dejas correr
> la migración de EF que hace el mismo renombrado, EF fallará al no encontrar las columnas
> `*_cop`. O EF, o el script.
