# ══════════════════════════════════════════════════════════════════
# Enciende el entorno local completo de Mesto Beach.
#   .\scripts\dev-up.ps1            → infraestructura + backend + las 3 apps
#   .\scripts\dev-up.ps1 -SoloDocker → solo los contenedores
# Cada servicio se abre en su propia ventana; ciérrala para apagarlo.
# ══════════════════════════════════════════════════════════════════
param([switch]$SoloDocker)

$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent

function Escuchando($puerto) {
  return [bool](netstat -ano | Select-String ":$puerto\s.*LISTENING")
}

# En PowerShell 5.1, lo que un .exe escribe en la salida de error aborta el
# script si $ErrorActionPreference = "Stop". Aislamos la comprobación.
function DockerVivo {
  $ErrorActionPreference = "Continue"
  docker info --format "{{.ServerVersion}}" 2>&1 | Out-Null
  return ($LASTEXITCODE -eq 0)
}

function Abrir($titulo, $carpeta, $comando) {
  Start-Process powershell -ArgumentList @(
    "-NoExit", "-Command",
    "`$host.UI.RawUI.WindowTitle = '$titulo'; Set-Location '$carpeta'; $comando"
  )
}

# ── 1. Docker ──────────────────────────────────────────────────────
if (-not (DockerVivo)) {
  Write-Host "Docker no responde. Arrancando Docker Desktop..." -ForegroundColor Yellow
  $exe = "$env:LOCALAPPDATA\Programs\DockerDesktop\Docker Desktop.exe"
  if (-not (Test-Path $exe)) { $exe = "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe" }
  if (-not (Test-Path $exe)) { throw "No encuentro Docker Desktop. Ábrelo a mano y reintenta." }
  Start-Process $exe

  # Un arranque en frío puede tardar varios minutos (Docker levanta su VM de WSL).
  $listo = $false
  foreach ($i in 1..75) {
    Start-Sleep -Seconds 5
    if (DockerVivo) { $listo = $true; break }
    if ($i % 6 -eq 0) { Write-Host "   ...esperando a Docker ($($i * 5)s)" -ForegroundColor DarkGray }
  }
  if (-not $listo) {
    throw "Docker sigue sin responder. Abre Docker Desktop desde el menú de inicio, espera a que la ballena deje de moverse y vuelve a ejecutar este script."
  }
  Write-Host "Docker listo." -ForegroundColor Green
}

Write-Host "→ Levantando contenedores (postgres, mongo, redis, mailhog, azurite)..." -ForegroundColor Cyan
Set-Location $raiz
docker compose up -d | Out-Null
docker ps --format "table {{.Names}}\t{{.Status}}"

# Postgres y Redis deben estar sanos antes del backend: si Redis no está,
# el backend se cae al arrancar (ConnectionMultiplexer.Connect en Program.cs).
Write-Host "→ Esperando a que la base de datos y Redis estén sanos..." -ForegroundColor Cyan
foreach ($i in 1..30) {
  $sanos = (docker ps --filter "name=mestobeach_postgres" --filter "health=healthy" --format "{{.Names}}") -and
           (docker ps --filter "name=mestobeach_redis" --filter "health=healthy" --format "{{.Names}}")
  if ($sanos) { break }
  Start-Sleep -Seconds 2
}

if ($SoloDocker) { Write-Host "`nContenedores listos." -ForegroundColor Green; return }

# ── 2. Backend + apps ──────────────────────────────────────────────
$servicios = @(
  @{ Nombre = "API";   Puerto = 5011; Carpeta = "$raiz\backend";  Comando = "dotnet run --project src/MestoBeach.API --launch-profile http" },
  @{ Nombre = "menu";  Puerto = 3000; Carpeta = "$raiz\frontend"; Comando = "pnpm --filter @mestobeach/menu dev" },
  @{ Nombre = "staff"; Puerto = 3001; Carpeta = "$raiz\frontend"; Comando = "pnpm --filter @mestobeach/staff dev" },
  @{ Nombre = "admin"; Puerto = 3003; Carpeta = "$raiz\frontend"; Comando = "pnpm --filter @mestobeach/admin dev" }
)

foreach ($s in $servicios) {
  if (Escuchando $s.Puerto) {
    Write-Host "→ $($s.Nombre) ya estaba corriendo en :$($s.Puerto)" -ForegroundColor DarkGray
  } else {
    Write-Host "→ Arrancando $($s.Nombre) en :$($s.Puerto)" -ForegroundColor Cyan
    Abrir "Mesto Beach · $($s.Nombre)" $s.Carpeta $s.Comando
    if ($s.Nombre -eq "API") { Start-Sleep -Seconds 12 }  # el backend primero
  }
}

Write-Host ""
Write-Host "Listo. Tarda ~1 min en compilar la primera vez." -ForegroundColor Green
Write-Host "  Menu (comensal) http://localhost:3000/mesa?tenant=<tenantId>&table=H01"
Write-Host "  Staff           http://localhost:3001"
Write-Host "  Admin           http://localhost:3003"
Write-Host "  API / Swagger   http://localhost:5011/swagger"
Write-Host "  MailHog         http://localhost:8025"
