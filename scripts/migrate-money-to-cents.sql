-- ══════════════════════════════════════════════════════════════════
-- Renombrado de las columnas monetarias: *_cop  →  *_cents
--
-- CONTEXTO — ¿por qué existe este script si hay EF Core?
--   El renombrado "oficial" viaja en una migración de EF: cada módulo tiene la
--   suya, `RenameMoneyToCents` (Identity, Orders y Payments). Ese es el camino
--   normal: base nueva, o base al día con `dotnet ef database update`.
--
--   Este script es para las bases que ya existen FUERA del flujo de EF:
--   la de desarrollo que lleva meses viva, una copia restaurada de un dump,
--   o cualquier entorno donde aplicar la migración de EF no sea viable.
--
--   ⚠️ Si ejecutas este script Y DESPUÉS la migración de EF sobre la misma
--   base, EF intentará renombrar columnas que ya no se llaman `*_cop` y
--   fallará. Elige un camino: o EF, o este script y después dar las migraciones
--   `*_RenameMoneyToCents` por aplicadas insertando su fila en la tabla
--   `"__EFMigrationsHistory"` (una por módulo: Identity, Orders y Payments).
--
-- SEMÁNTICA — este script NO convierte importes.
--   El entero no cambia de valor, cambia de significado: lo que antes eran
--   pesos colombianos ahora son céntimos de euro (1450 = 14,50 €). No hay
--   tipo de cambio que aplicar porque no hay datos reales de producción que
--   preservar: los datos de desarrollo se vuelven a sembrar con
--   `scripts/seed-demo-mestobeach.ps1`, ya con precios en céntimos.
--
-- IDEMPOTENTE: comprueba cada columna antes de tocarla. Ejecutarlo dos veces
--   deja la base igual y solo imprime «ya migrada».
--
-- USO:
--   docker cp scripts/migrate-money-to-cents.sql mestobeach_postgres:/tmp/cents.sql
--   docker exec mestobeach_postgres psql -U mestobeach -d mestobeach -f /tmp/cents.sql
-- ══════════════════════════════════════════════════════════════════

\set ON_ERROR_STOP on

DO $$
DECLARE
  r           record;
  has_old     boolean;
  has_new     boolean;
  renamed_n   int := 0;
  skipped_n   int := 0;
  missing_n   int := 0;
BEGIN
  -- Lista EXPLÍCITA de columnas monetarias. Deliberadamente no se recorre
  -- information_schema buscando '%_cop': solo se renombra lo que se sabe que
  -- es dinero, para no arrastrar por error columnas ajenas.
  FOR r IN
    SELECT * FROM (VALUES
      ('payments',    'amount_cop',              'amount_cents'),
      ('payments',    'tip_cop',                 'tip_cents'),
      ('payments',    'total_cop',               'total_cents'),
      ('payments',    'commission_cop',          'commission_cents'),
      ('shifts',      'opening_float_cop',       'opening_float_cents'),
      ('order_items', 'item_price_snapshot_cop', 'item_price_snapshot_cents'),
      ('tenants',     'subscription_amount_cop', 'subscription_amount_cents')
    ) AS t(tbl, old_col, new_col)
  LOOP
    SELECT EXISTS (
      SELECT 1 FROM information_schema.columns
      WHERE table_schema = current_schema()
        AND table_name   = r.tbl
        AND column_name  = r.old_col
    ) INTO has_old;

    SELECT EXISTS (
      SELECT 1 FROM information_schema.columns
      WHERE table_schema = current_schema()
        AND table_name   = r.tbl
        AND column_name  = r.new_col
    ) INTO has_new;

    IF has_old AND NOT has_new THEN
      EXECUTE format('ALTER TABLE %I RENAME COLUMN %I TO %I', r.tbl, r.old_col, r.new_col);
      renamed_n := renamed_n + 1;
      RAISE NOTICE 'renombrada  %.% → %', r.tbl, r.old_col, r.new_col;

    ELSIF has_old AND has_new THEN
      -- Estado raro: conviven las dos. No se decide por el usuario.
      RAISE WARNING 'CONFLICTO en %: existen % y %. Revísalo a mano (no se toca nada).',
        r.tbl, r.old_col, r.new_col;
      skipped_n := skipped_n + 1;

    ELSIF has_new THEN
      RAISE NOTICE 'ya migrada  %.%', r.tbl, r.new_col;
      skipped_n := skipped_n + 1;

    ELSE
      -- La tabla no existe en esta base (módulo sin migrar) o la columna se
      -- eliminó. No es un error: este script corre sobre bases heterogéneas.
      RAISE NOTICE 'ausente     %.% (tabla o columna no encontrada, se omite)', r.tbl, r.old_col;
      missing_n := missing_n + 1;
    END IF;
  END LOOP;

  RAISE NOTICE '──────────────────────────────────────────────';
  RAISE NOTICE 'renombradas: %  ·  ya migradas/conflictos: %  ·  ausentes: %',
    renamed_n, skipped_n, missing_n;
END $$;

-- ── Verificación ──────────────────────────────────────────────────
-- No debe quedar ninguna columna monetaria con sufijo _cop.
SELECT table_name, column_name, 'PENDIENTE' AS estado
FROM information_schema.columns
WHERE table_schema = current_schema()
  AND column_name LIKE '%\_cop'
UNION ALL
SELECT table_name, column_name, 'OK'
FROM information_schema.columns
WHERE table_schema = current_schema()
  AND column_name LIKE '%\_cents'
ORDER BY estado, table_name, column_name;
