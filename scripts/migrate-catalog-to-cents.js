// ══════════════════════════════════════════════════════════════════
// Renombrado de los campos monetarios del catálogo (MongoDB)
//
//   <base>.catalogs
//     items[].priceCop                         → items[].priceCents
//     modifierGroups[].options[].priceDeltaCop → …[].priceDeltaCents
//
// CONTEXTO: el catálogo vive en Mongo, sin migraciones automáticas. El código
// del backend pasa a leer `priceCents` / `priceDeltaCents` (son los nombres de
// los `[BsonElement]` en `CatalogRepository.ItemDoc` y `OptionDoc`); los
// documentos ya escritos hay que renombrarlos a mano — esto es ese "a mano".
//
// SEMÁNTICA: no se convierte ningún importe. El entero conserva su valor y
// cambia de significado: ahora son CÉNTIMOS DE EURO (1450 = 14,50 €). Para el
// catálogo de demostración lo sano es borrarlo y volver a sembrarlo con
// `scripts/seed-demo-mestobeach.ps1`, que ya trae precios de chiringuito.
//
// ⚠️ POR QUÉ SE MIRAN DOS BASES
//   La base "buena" es `mestobeach_catalog`, la que nombran `.env.example` y
//   `appsettings.Development.json`. Pero el backend NO la usa hoy: elige la
//   base con `CatalogOptions.MongoDatabaseName`, que se enlaza a la sección de
//   configuración `Catalog` — sección que no existe en ningún `appsettings`.
//   Al no existir, gana el valor por defecto del código, que sigue siendo el
//   heredado `mesto_catalog` (mismo caso en `OrdersCatalogOptions`). El nombre
//   de base del connection string no manda: el repositorio llama a
//   `GetDatabase("mesto_catalog")` de forma explícita.
//   Resultado: los datos reales de tu máquina están casi seguro en
//   `mesto_catalog`. Migrar solo `mestobeach_catalog` diría "0 documentos, OK"
//   dejando la carta de verdad sin tocar, así que se recorren las dos y se
//   omite en silencio la que no exista.
//
// IDEMPOTENTE: en cada base solo se seleccionan los documentos que aún tengan
// algún campo `*Cop`, y dentro de cada uno solo se renombran los campos que
// existan; si el destino ya existe, se conserva el destino y se descarta el
// origen. Segunda ejecución: 0 documentos seleccionados, 0 escrituras.
//
// USO:
//   docker cp scripts/migrate-catalog-to-cents.js mestobeach_mongodb:/tmp/cents.js
//   docker exec mestobeach_mongodb mongosh \
//     "mongodb://mestobeach:mestobeach_dev@localhost:27017/?authSource=admin" \
//     --quiet --file /tmp/cents.js
// ══════════════════════════════════════════════════════════════════

// La primera es la base oficial; la segunda, la heredada que el backend usa de
// hecho mientras falte la sección `Catalog` en la configuración.
const DB_NAMES = ["mestobeach_catalog", "mesto_catalog"];
const COLLECTION_NAME = "catalogs";

/**
 * Renombra UNA clave de un objeto plano. Devuelve 1 si tocó algo, 0 si no.
 * Si el destino ya existe (migración a medias), gana el destino y el origen
 * simplemente desaparece: nunca se pisa un valor ya migrado.
 */
function renameKey(obj, oldKey, newKey) {
  if (obj === null || typeof obj !== "object") return 0;
  if (!Object.prototype.hasOwnProperty.call(obj, oldKey)) return 0;
  if (!Object.prototype.hasOwnProperty.call(obj, newKey)) {
    obj[newKey] = obj[oldKey];
  }
  delete obj[oldKey];
  return 1;
}

// Documentos que todavía tienen algo que migrar.
const PENDING = {
  $or: [
    { "items.priceCop": { $exists: true } },
    { "modifierGroups.options.priceDeltaCop": { $exists: true } },
  ],
};

const MIGRATED = {
  $or: [
    { "items.priceCents": { $exists: true } },
    { "modifierGroups.options.priceDeltaCents": { $exists: true } },
  ],
};

/** Migra una base. Devuelve null si esa base no tiene catálogo. */
function migrateDatabase(dbName) {
  const target = db.getSiblingDB(dbName);

  // getCollectionNames() sobre una base inexistente devuelve [] sin error, así
  // que sirve de comprobación de existencia sin pedir permisos de admin.
  if (target.getCollectionNames().indexOf(COLLECTION_NAME) === -1) {
    print("· " + dbName + ": sin colección `" + COLLECTION_NAME + "`, se omite");
    return null;
  }

  const catalogs = target.getCollection(COLLECTION_NAME);

  // Se materializa la lista antes de escribir: son pocos documentos (uno por
  // local) y así no se reescribe la colección mientras se recorre su cursor.
  const pendingDocs = catalogs.find(PENDING).toArray();
  print("· " + dbName + ": " + pendingDocs.length + " documento(s) pendiente(s)");

  const stats = { docs: 0, items: 0, options: 0 };

  pendingDocs.forEach(function (doc) {
    let changes = 0;

    (doc.items || []).forEach(function (item) {
      const n = renameKey(item, "priceCop", "priceCents");
      stats.items += n;
      changes += n;
    });

    (doc.modifierGroups || []).forEach(function (group) {
      (group.options || []).forEach(function (option) {
        const n = renameKey(option, "priceDeltaCop", "priceDeltaCents");
        stats.options += n;
        changes += n;
      });
    });

    if (changes > 0) {
      catalogs.replaceOne({ _id: doc._id }, doc);
      stats.docs += 1;
      print("    catálogo " + doc._id + " (tenant " + doc.tenantId + "): " + changes + " campo(s)");
    }
  });

  // ── Verificación de esta base ──
  stats.leftovers = catalogs.countDocuments(PENDING);
  stats.migrated = catalogs.countDocuments(MIGRATED);
  return stats;
}

const totals = { docs: 0, items: 0, options: 0, leftovers: 0, migrated: 0, bases: 0 };

DB_NAMES.forEach(function (name) {
  const s = migrateDatabase(name);
  if (s === null) return;
  totals.bases += 1;
  totals.docs += s.docs;
  totals.items += s.items;
  totals.options += s.options;
  totals.leftovers += s.leftovers;
  totals.migrated += s.migrated;
});

print("──────────────────────────────────────────────");
if (totals.bases === 0) {
  print("No se encontró ninguna base de catálogo (" + DB_NAMES.join(", ") + ").");
  print("¿Seguro que apuntas al Mongo correcto y el backend ya ha escrito el catálogo?");
} else {
  print("bases con catálogo                    : " + totals.bases);
  print("documentos actualizados               : " + totals.docs);
  print("items priceCop→priceCents             : " + totals.items);
  print("opciones priceDeltaCop→priceDeltaCents: " + totals.options);
  print("documentos ya en céntimos             : " + totals.migrated);
  print(
    "documentos con campos *Cop pendientes : " +
      totals.leftovers +
      (totals.leftovers === 0 ? "  OK" : "  <- revisar")
  );
}
