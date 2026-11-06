# Layouts creativos de sala (demo)

Genera y aplica los 3 planos: Terraza (14 mesas mixtas, cara al mar), Salón (12,
interior con muros + puerta) y Playa (96 hamacas en rejilla 8×12, 6+6 con pasillo central).

    node scripts/gen-floor-layouts.cjs        # -> %TEMP%/seed-layouts.sql
    cat "$TEMP/seed-layouts.sql" | docker exec -i mestobeach_postgres psql -U mestobeach -d mestobeach
    docker exec mestobeach_redis sh -c "redis-cli -a mestobeach_dev KEYS 'table:*' | xargs -r -I{} redis-cli -a mestobeach_dev DEL {}"

Nota: los IDs de piso están fijados a la BD de demo; si cambian, ajústalos en el script.
