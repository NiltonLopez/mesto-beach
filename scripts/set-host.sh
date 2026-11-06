#!/usr/bin/env bash
# ══════════════════════════════════════════════════════════════════
# Fija la dirección con la que el sistema se anuncia a sí mismo.
#
#   ./scripts/set-host.sh              → detecta la IP de esta máquina en la LAN
#   ./scripts/set-host.sh 192.168.1.50 → la fijas tú
#   ./scripts/set-host.sh localhost    → vuelve al modo solo-este-ordenador
#
# Escribe MESTOBEACH_HOST en el fichero .env de la raíz, que Docker Compose lee
# solo. Después hay que RECONSTRUIR, porque las variables NEXT_PUBLIC_* de Next
# se hornean en la imagen durante el build:
#
#   docker compose --profile demo up -d --build
# ══════════════════════════════════════════════════════════════════
set -euo pipefail

raiz="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
envfile="$raiz/.env"

detectar_ip() {
  # macOS: la interfaz activa suele ser en0 (Wi-Fi) o en1.
  if command -v ipconfig >/dev/null 2>&1; then
    for iface in en0 en1 en2; do
      ip=$(ipconfig getifaddr "$iface" 2>/dev/null || true)
      [ -n "$ip" ] && { echo "$ip"; return 0; }
    done
  fi
  # Linux
  if command -v hostname >/dev/null 2>&1; then
    ip=$(hostname -I 2>/dev/null | awk '{print $1}')
    [ -n "$ip" ] && { echo "$ip"; return 0; }
  fi
  return 1
}

if [ $# -ge 1 ]; then
  host="$1"
else
  host=$(detectar_ip) || {
    echo "No pude detectar la IP de esta máquina." >&2
    echo "Pásala a mano:  ./scripts/set-host.sh 192.168.1.50" >&2
    exit 1
  }
fi

# Reescribe la línea si ya existía; si no, la añade.
if [ -f "$envfile" ] && grep -q '^MESTOBEACH_HOST=' "$envfile"; then
  tmp="$(mktemp)"
  grep -v '^MESTOBEACH_HOST=' "$envfile" > "$tmp"
  printf 'MESTOBEACH_HOST=%s\n' "$host" >> "$tmp"
  mv "$tmp" "$envfile"
else
  printf 'MESTOBEACH_HOST=%s\n' "$host" >> "$envfile"
fi

echo "MESTOBEACH_HOST=$host  →  $envfile"
echo
if [ "$host" = "localhost" ]; then
  echo "Modo local. Solo accesible desde este ordenador."
else
  echo "El sistema se anunciará en http://$host — accesible desde el móvil"
  echo "si está en la misma red Wi-Fi."
fi
echo
echo "Ahora reconstruye para que las apps se horneen con esa dirección:"
echo "  docker compose --profile demo up -d --build"
