# Imágenes mock del menú (demo)

Genera los tiles por plato + copia el logo, y apunta el catálogo a ellos.

1. Tiles (crea apps/menu/public/mock/*.svg):
   node scripts/gen-menu-mock-tiles.cjs

2. Apuntar el catálogo (Mongo) a los tiles + logo:
   docker exec mestobeach_mongodb mongosh 'mongodb://mestobeach:mestobeach_dev@localhost:27017/mestobeach_catalog?authSource=admin' --quiet --eval '
     const slug=s=>s.normalize("NFD").replace(/[̀-ͯ]/g,"").toLowerCase().replace(/[^a-z0-9]+/g,"-").replace(/^-|-$/g,"");
     const c=db.catalogs.findOne();
     c.items.forEach(it=>{ it.images=["/mock/"+slug(it.name)+".svg"]; });
     db.catalogs.replaceOne({_id:c._id}, c);'

3. Invalidar la caché del catálogo:
   docker exec mestobeach_redis sh -c "redis-cli -a mestobeach_dev KEYS '*catalog*' | xargs -r -I{} redis-cli -a mestobeach_dev DEL {}"
