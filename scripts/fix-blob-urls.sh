#!/usr/bin/env bash
# ══════════════════════════════════════════════════════════════════
# Repara las URLs de imagen guardadas en el catálogo (MongoDB).
#
# La URL pública se guarda ENTERA en el documento en el momento de subir la
# imagen, no se calcula al leer. Por eso, si cambia la dirección con la que se
# publica el blob —el host (localhost ⇄ IP del equipo) o el contenedor— las
# imágenes ya subidas se quedan apuntando a la dirección vieja.
#
# Este script las reescribe a la dirección actual:
#
#     http://<MESTOBEACH_HOST>:10000/devstoreaccount1/catalog-images/<blob>
#
#   ./scripts/fix-blob-urls.sh                → usa MESTOBEACH_HOST de .env
#   ./scripts/fix-blob-urls.sh 192.168.1.50   → lo fijas tú
#
# Es idempotente: reconoce las URLs ya correctas y las deja igual.
# ══════════════════════════════════════════════════════════════════
set -euo pipefail

raiz="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [ $# -ge 1 ]; then
  host="$1"
elif [ -f "$raiz/.env" ] && grep -q '^MESTOBEACH_HOST=' "$raiz/.env"; then
  host="$(grep '^MESTOBEACH_HOST=' "$raiz/.env" | tail -1 | cut -d= -f2-)"
else
  host="localhost"
fi

base="http://${host}:10000/devstoreaccount1/catalog-images"
echo "──▶ Reescribiendo las URLs de imagen a: $base"

docker exec -i mestobeach_mongodb mongosh \
  "mongodb://mestobeach:mestobeach_dev@localhost:27017/mestobeach_catalog?authSource=admin" \
  --quiet --eval "
const BASE = '${base}';
const MARCA = '/devstoreaccount1/';

// Normaliza una URL de blob: se queda con el nombre real del blob y le antepone
// la base actual. Tolera tanto la URL rota (sin el segmento del contenedor)
// como la ya correcta, así que puede ejecutarse las veces que haga falta.
function arreglar(url) {
  if (typeof url !== 'string' || url.indexOf(MARCA) < 0) return url;
  let blob = url.substring(url.indexOf(MARCA) + MARCA.length);
  // Si ya trae el contenedor delante del nombre del blob, se lo quitamos para
  // no duplicarlo al volver a componer.
  if (blob.startsWith('catalog-images/catalog-images/') || blob.startsWith('catalog-images/branding/')) {
    blob = blob.substring('catalog-images/'.length);
  }
  return BASE + '/' + blob;
}

const d = db.getSiblingDB('mestobeach_catalog');
let docs = 0, campos = 0;

d.catalogs.find({}).forEach(doc => {
  let tocado = false;

  if (doc.branding) {
    ['logoUrl', 'heroUrl'].forEach(k => {
      const nuevo = arreglar(doc.branding[k]);
      if (nuevo && nuevo !== doc.branding[k]) { doc.branding[k] = nuevo; campos++; tocado = true; }
    });
  }

  (doc.items || []).forEach(item => {
    if (Array.isArray(item.images)) {
      item.images = item.images.map(u => {
        const nuevo = arreglar(u);
        if (nuevo !== u) { campos++; tocado = true; }
        return nuevo;
      });
    }
  });

  if (tocado) { d.catalogs.replaceOne({ _id: doc._id }, doc); docs++; }
});

print('   documentos actualizados: ' + docs);
print('   urls reescritas:         ' + campos);
"

echo "──▶ Listo."
