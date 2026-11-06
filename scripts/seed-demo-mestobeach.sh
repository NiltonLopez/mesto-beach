#!/usr/bin/env bash
# ══════════════════════════════════════════════════════════════════
# Seed de datos DEMO "Playa Demo" para el entorno local (equivalente
# bash de seed-demo-mestobeach.ps1, para macOS/Linux).
# Crea: carta (categorías + platos), 3 estaciones KDS, 3 sectores y mesas/hamacas
# Requiere: backend en :5011, tenant creado y admin activado, curl + jq.
# Moneda: todos los importes van en CÉNTIMOS DE EURO (campo priceCents).
#
# NO es idempotente: si lo ejecutas dos veces, duplica la carta.
# ══════════════════════════════════════════════════════════════════
set -euo pipefail

# Desde el host apuntan a localhost; el contenedor de init los sobreescribe
# para hablar con el servicio `api` por la red de Compose.
BASE="${MESTOBEACH_API_URL:-http://localhost:5011}"
TENANT_ID="${MESTOBEACH_TENANT_ID:-d0ebd974-8e71-4624-81b1-60384e132a95}"

if ! command -v jq >/dev/null 2>&1; then
  echo "Falta jq. Instálalo con: brew install jq" >&2
  exit 1
fi

post_json() {
  local url="$1" body="$2"
  shift 2
  curl -sS -X POST "$url" -H "Content-Type: application/json" "$@" -d "$body"
}

extract_id() {
  # Igual que el fallback de la versión PowerShell: .id, si no .data.id, si no la respuesta cruda.
  jq -r 'if type == "object" then (.id // .data.id) else . end'
}

eur() {
  local cents=$1
  printf "%d,%02d €" $((cents / 100)) $((cents % 100))
}

# ── 1. Login como admin del tenant ────────────────────────────────
login=$(post_json "$BASE/api/v1/identity/auth/login" \
  "$(jq -n --arg email "admin@mestobeach.demo" --arg password "MestoBeach2026!" --arg tenantId "$TENANT_ID" \
    '{email: $email, password: $password, tenantId: $tenantId}')")

token=$(echo "$login" | jq -r '.token' 2>/dev/null || true)
if [ -z "$token" ] || [ "$token" = "null" ]; then
  echo "Login fallido. Respuesta de $BASE:" >&2
  echo "$login" >&2
  echo "" >&2
  echo "Comprueba que las migraciones y bootstrap-mestobeach.sql ya se aplicaron." >&2
  exit 1
fi
roles=$(echo "$login" | jq -r '.staff.roles | join(",")')
echo "[1] Login admin OK — roles: $roles"

AUTH=(-H "Authorization: Bearer $token")

# ── 2. Categorías de la carta ─────────────────────────────────────
# El bash de macOS (3.2) no tiene arrays asociativos: usamos dos arrays
# paralelos (nombre/id) y una función de búsqueda lineal.
cat_names=("Entrantes" "Del Mar" "La Parrilla" "Cócteles" "Bebidas")
cat_orders=(1 2 3 4 5)
cat_ids=()

cat_id() {
  local wanted="$1"
  local i
  for i in "${!cat_names[@]}"; do
    if [ "${cat_names[$i]}" = "$wanted" ]; then
      echo "${cat_ids[$i]}"
      return
    fi
  done
}

for i in "${!cat_names[@]}"; do
  name="${cat_names[$i]}"
  order="${cat_orders[$i]}"
  r=$(post_json "$BASE/api/v1/catalog/$TENANT_ID/categories" \
    "$(jq -n --arg name "$name" --argjson order "$order" '{name: $name, displayOrder: $order}')" "${AUTH[@]}")
  id=$(echo "$r" | extract_id)
  cat_ids[$i]="$id"
  echo "[2] Categoría '$name' → $id"
done

# ── 3. Platos ─────────────────────────────────────────────────────
# PriceCents va en CÉNTIMOS DE EURO: 1450 = 14,50 €. Precios de chiringuito
# de playa (temporada alta, primera línea de playa).
# columnas: categoría | nombre | descripción | precio en céntimos | orden
items=(
  "Entrantes|Gambas al Pil Pil|Clásico andaluz con guindilla y ajo|1450|1"
  "Entrantes|Ensalada Marinera|Naranja, bacalao y aceituna aloreña|1200|2"
  "Del Mar|Ceviche de la Casa|Corvina, leche de tigre y maíz crocante|1650|1"
  "Del Mar|Fritura Marinera|Boquerones, calamares y adobo|1800|2"
  "La Parrilla|Espeto de Sardinas|A la brasa de olivo, como manda la bahía|600|1"
  "La Parrilla|Pulpo a la Brasa|Con papas al vapor y pimentón de la Vera|2200|2"
  "La Parrilla|Entrecot de Retinto|300 g, término al gusto|2600|3"
  "Cócteles|Mojito de la Casa|Hierbabuena fresca y ron añejo|1100|1"
  "Cócteles|Sangría de Cava (jarra)|Frutas de temporada|2400|2"
  "Bebidas|Tinto de Verano|Con limón|450|1"
  "Bebidas|Agua Mineral|50 cl|250|2"
)

IFS=$'\n'
for row in "${items[@]}"; do
  IFS='|' read -r cat name desc price order <<<"$row"
  catId=$(cat_id "$cat")
  body=$(jq -n --arg categoryId "$catId" --arg name "$name" --arg desc "$desc" \
    --argjson price "$price" --argjson order "$order" \
    '{categoryId: $categoryId, name: $name, description: $desc, priceCents: $price, available: true, displayOrder: $order}')
  post_json "$BASE/api/v1/catalog/$TENANT_ID/items" "$body" "${AUTH[@]}" >/dev/null
  echo "[3] Plato '$name' ($cat) → $(eur "$price")"
done
unset IFS

# ── 4. Estaciones KDS (routing por categoría) ─────────────────────
create_station() {
  local station="$1"
  shift
  local catIdsJson="[]"
  local c
  for c in "$@"; do
    catIdsJson=$(echo "$catIdsJson" | jq --arg id "$(cat_id "$c")" '. + [$id]')
  done
  local body
  body=$(jq -n --arg name "$station" --argjson categoryIds "$catIdsJson" '{name: $name, categoryIds: $categoryIds}')
  post_json "$BASE/api/v1/kds-stations" "$body" "${AUTH[@]}" >/dev/null
  echo "[4] Estación KDS '$station' ← categorías: $*"
}

create_station "Cocina" "Entrantes" "Del Mar"
create_station "Parrilla" "La Parrilla"
create_station "Cocktails" "Cócteles" "Bebidas"

# ── 5. Sectores (floors) ──────────────────────────────────────────
# El backend numera el sortOrder en orden de creación, y las pantallas ordenan
# los selectores de sección por sortOrder → este orden manda: Salón, Terraza, Playa.
floor_names=("Salón" "Terraza" "Playa")
floor_ids=()

floor_id() {
  local wanted="$1"
  local i
  for i in "${!floor_names[@]}"; do
    if [ "${floor_names[$i]}" = "$wanted" ]; then
      echo "${floor_ids[$i]}"
      return
    fi
  done
}

for i in "${!floor_names[@]}"; do
  f="${floor_names[$i]}"
  r=$(post_json "$BASE/api/v1/floors" "$(jq -n --arg name "$f" '{name: $name}')" "${AUTH[@]}")
  id=$(echo "$r" | extract_id)
  floor_ids[$i]="$id"
  echo "[5] Sector '$f' → $id"
done

# ── 6. Mesas y hamacas (sin PIN para demo fluida) ─────────────────
create_table() {
  local code="$1" zone="$2" floor="$3" x="$4" y="$5" seats="$6" shape="$7"
  local body
  body=$(jq -n \
    --arg tenantId "$TENANT_ID" --arg tableCode "$code" --arg zone "$zone" \
    --arg floorId "$(floor_id "$floor")" --argjson posX "$x" --argjson posY "$y" \
    --argjson seats "$seats" --arg shape "$shape" \
    '{tenantId: $tenantId, tableCode: $tableCode, zone: $zone, active: true, floorId: $floorId, posX: $posX, posY: $posY, seats: $seats, shape: $shape, requiresPin: false}')
  post_json "$BASE/api/v1/tables" "$body" "${AUTH[@]}" >/dev/null
  echo "[6] Puesto $code en $floor"
}

# Salón: S01-S03
for n in 1 2 3; do
  create_table "S0$n" "Salón" "Salón" $((80 + n * 120)) 150 4 "SQUARE"
done
# Terraza: T01-T04
for n in 1 2 3 4; do
  create_table "T0$n" "Terraza" "Terraza" $((60 + n * 110)) 200 4 "SQUARE"
done
# Playa: hamacas H01-H08
for n in 1 2 3 4 5 6 7 8; do
  idx=$((n - 1))
  x=$((60 + (idx % 4) * 130))
  y=$((120 + (idx / 4) * 160))
  create_table "H0$n" "Playa" "Playa" "$x" "$y" 2 "ROUND"
done

echo ""
echo "══════ SEED COMPLETO ══════"
echo "Carta: ${#cat_names[@]} categorías, ${#items[@]} platos"
echo "Estaciones: Cocina, Parrilla, Cocktails"
echo "Sectores: Terraza (4 mesas), Salón (3 mesas), Playa (8 hamacas)"
