# ══════════════════════════════════════════════════════════════════
# Seed de datos DEMO "Playa Demo" para el entorno local de Mesto
# Crea: carta (categorías + platos), 3 estaciones KDS, 3 sectores y mesas/hamacas
# Requiere: backend en :5011, tenant creado y admin activado
# Moneda: todos los importes van en CÉNTIMOS DE EURO (campo priceCents).
# ══════════════════════════════════════════════════════════════════
$ErrorActionPreference = "Stop"
$base = "http://localhost:5011"
$tenantId = "d0ebd974-8e71-4624-81b1-60384e132a95"

function PostJson($url, $body, $headers) {
  Invoke-RestMethod -Uri $url -Method Post -ContentType "application/json" -Headers $headers -Body ($body | ConvertTo-Json -Depth 6)
}

# ── 1. Login como admin del tenant ────────────────────────────────
$login = PostJson "$base/api/v1/identity/auth/login" @{ email = "admin@mestobeach.demo"; password = "MestoBeach2026!"; tenantId = $tenantId } @{}
$H = @{ Authorization = "Bearer $($login.token)" }
Write-Host "[1] Login admin OK — roles: $($login.user.roles -join ',')"

# ── 2. Categorías de la carta ─────────────────────────────────────
$cats = @{}
$catDefs = @(
  @{ Name = "Entrantes";  Order = 1 },
  @{ Name = "Del Mar";    Order = 2 },
  @{ Name = "La Parrilla"; Order = 3 },
  @{ Name = "Cócteles";   Order = 4 },
  @{ Name = "Bebidas";    Order = 5 }
)
foreach ($c in $catDefs) {
  $r = PostJson "$base/api/v1/catalog/$tenantId/categories" @{ name = $c.Name; displayOrder = $c.Order } $H
  $id = if ($r.id) { $r.id } elseif ($r.data.id) { $r.data.id } else { $r }
  $cats[$c.Name] = $id
  Write-Host "[2] Categoría '$($c.Name)' → $id"
}

# ── 3. Platos ─────────────────────────────────────────────────────
# PriceCents va en CÉNTIMOS DE EURO: 1450 = 14,50 €. Precios de chiringuito
# de playa (temporada alta, primera línea de playa).
$items = @(
  @{ Cat = "Entrantes";   Name = "Gambas al Pil Pil";        Desc = "Clásico andaluz con guindilla y ajo";              PriceCents = 1450; Order = 1 },
  @{ Cat = "Entrantes";   Name = "Ensalada Marinera";       Desc = "Naranja, bacalao y aceituna aloreña";              PriceCents = 1200; Order = 2 },
  @{ Cat = "Del Mar";     Name = "Ceviche de la Casa";         Desc = "Corvina, leche de tigre y maíz crocante";          PriceCents = 1650; Order = 1 },
  @{ Cat = "Del Mar";     Name = "Fritura Marinera";        Desc = "Boquerones, calamares y adobo";                    PriceCents = 1800; Order = 2 },
  @{ Cat = "La Parrilla"; Name = "Espeto de Sardinas";       Desc = "A la brasa de olivo, como manda la bahía";         PriceCents = 600;  Order = 1 },
  @{ Cat = "La Parrilla"; Name = "Pulpo a la Brasa";         Desc = "Con papas al vapor y pimentón de la Vera";         PriceCents = 2200; Order = 2 },
  @{ Cat = "La Parrilla"; Name = "Entrecot de Retinto";      Desc = "300 g, término al gusto";                          PriceCents = 2600; Order = 3 },
  @{ Cat = "Cócteles";    Name = "Mojito de la Casa";             Desc = "Hierbabuena fresca y ron añejo";                   PriceCents = 1100; Order = 1 },
  @{ Cat = "Cócteles";    Name = "Sangría de Cava (jarra)";  Desc = "Frutas de temporada";                              PriceCents = 2400; Order = 2 },
  @{ Cat = "Bebidas";     Name = "Tinto de Verano";          Desc = "Con limón";                                        PriceCents = 450;  Order = 1 },
  @{ Cat = "Bebidas";     Name = "Agua Mineral";             Desc = "50 cl";                                            PriceCents = 250;  Order = 2 }
)
$eur = [System.Globalization.CultureInfo]::GetCultureInfo("es-ES")
foreach ($i in $items) {
  $body = @{ categoryId = $cats[$i.Cat]; name = $i.Name; description = $i.Desc; priceCents = $i.PriceCents; available = $true; displayOrder = $i.Order }
  $r = PostJson "$base/api/v1/catalog/$tenantId/items" $body $H
  $shown = ($i.PriceCents / 100).ToString("C", $eur)
  Write-Host "[3] Plato '$($i.Name)' ($($i.Cat)) → $shown"
}

# ── 4. Estaciones KDS (routing por categoría) ─────────────────────
$stations = @(
  @{ Name = "Cocina";    Cats = @("Entrantes", "Del Mar") },
  @{ Name = "Parrilla";  Cats = @("La Parrilla") },
  @{ Name = "Cocktails"; Cats = @("Cócteles", "Bebidas") }
)
foreach ($s in $stations) {
  $catIds = @($s.Cats | ForEach-Object { "$($cats[$_])" })
  $r = PostJson "$base/api/v1/kds-stations" @{ name = $s.Name; categoryIds = $catIds } $H
  Write-Host "[4] Estación KDS '$($s.Name)' ← categorías: $($s.Cats -join ', ')"
}

# ── 5. Sectores (floors) ──────────────────────────────────────────
# El backend numera el sortOrder en orden de creación, y las pantallas ordenan
# los selectores de sección por sortOrder → este orden manda: Salón, Terraza, Playa.
$floors = @{}
foreach ($f in @("Salón", "Terraza", "Playa")) {
  $r = PostJson "$base/api/v1/floors" @{ name = $f } $H
  $id = if ($r.id) { $r.id } elseif ($r.data.id) { $r.data.id } else { $r }
  $floors[$f] = $id
  Write-Host "[5] Sector '$f' → $id"
}

# ── 6. Mesas y hamacas (sin PIN para demo fluida) ─────────────────
$tables = @()
# Salón: S01-S03
1..3 | ForEach-Object { $tables += @{ Code = "S0$_"; Zone = "Salón";   Floor = "Salón";   X = 80 + ($_ * 120); Y = 150; Seats = 4; Shape = "SQUARE" } }
# Terraza: T01-T04
1..4 | ForEach-Object { $tables += @{ Code = "T0$_"; Zone = "Terraza"; Floor = "Terraza"; X = 60 + ($_ * 110); Y = 200; Seats = 4; Shape = "SQUARE" } }
# Playa: hamacas H01-H08
1..8 | ForEach-Object { $x = 60 + ((($_ - 1) % 4) * 130); $y = 120 + ([math]::Floor(($_ - 1) / 4) * 160); $tables += @{ Code = "H0$_"; Zone = "Playa"; Floor = "Playa"; X = $x; Y = $y; Seats = 2; Shape = "ROUND" } }
foreach ($t in $tables) {
  $body = @{ tenantId = $tenantId; tableCode = $t.Code; zone = $t.Zone; active = $true; floorId = $floors[$t.Floor]; posX = [double]$t.X; posY = [double]$t.Y; seats = $t.Seats; shape = $t.Shape; requiresPin = $false }
  $r = PostJson "$base/api/v1/tables" $body $H
  Write-Host "[6] Puesto $($t.Code) en $($t.Floor)"
}

Write-Host ""
Write-Host "══════ SEED COMPLETO ══════"
Write-Host "Carta: $($catDefs.Count) categorías, $($items.Count) platos"
Write-Host "Estaciones: Cocina, Parrilla, Cocktails"
Write-Host "Sectores: Terraza (4 mesas), Salón (3 mesas), Playa (8 hamacas)"
