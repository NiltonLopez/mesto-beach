-- ══════════════════════════════════════════════════════════════════
-- Bootstrap del local: crea el restaurante y su cuenta de administración.
--
-- Mesto Beach es un sistema de un solo local: no hay alta de tenants por
-- API (eso vivía en el módulo Platform, retirado). El local se siembra una vez
-- al instalar y a partir de ahí todo se gestiona desde la app de administración.
--
--   docker cp scripts/bootstrap-mestobeach.sql mestobeach_postgres:/tmp/bootstrap.sql
--   docker exec mestobeach_postgres psql -U mestobeach -d mestobeach -f /tmp/bootstrap.sql
--
-- Idempotente: se puede ejecutar varias veces sin duplicar.
-- ══════════════════════════════════════════════════════════════════
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ── El local ──────────────────────────────────────────────────────
INSERT INTO tenants (
  id, slug, name, plan, status, qr_mode, operation_mode, pin_required,
  timezone, city, owner_email, nit, nit_verified, plan_code, created_at
)
VALUES (
  'd0ebd974-8e71-4624-81b1-60384e132a95',
  'mestobeach',
  'Playa Demo',
  'none',
  'active',
  'FIXED',
  'STAFFED',
  false,                       -- las hamacas se abren sin PIN
  'Europe/Madrid',
  'Costa Demo',
  'admin@mestobeach.demo',
  'B-00000000',
  false,
  'pro',
  now()
)
ON CONFLICT (id) DO NOTHING;

-- ── Cuenta de administración ──────────────────────────────────────
-- Cambia la contraseña tras el primer acceso.
INSERT INTO users (id, name, email, password_hash, status, must_change_password, created_at)
VALUES (
  '22222222-2222-2222-2222-222222222222',
  'Administración',
  'admin@mestobeach.demo',
  crypt('MestoBeach2026!', gen_salt('bf', 11)),
  'ACTIVE',
  false,
  now()
)
ON CONFLICT (email) DO NOTHING;

INSERT INTO user_memberships (id, user_id, tenant_id, roles, status, created_at)
SELECT
  gen_random_uuid(),
  '22222222-2222-2222-2222-222222222222',
  'd0ebd974-8e71-4624-81b1-60384e132a95',
  'Admin',
  'ACTIVE',
  now()
WHERE NOT EXISTS (
  SELECT 1 FROM user_memberships
  WHERE user_id = '22222222-2222-2222-2222-222222222222'
    AND tenant_id = 'd0ebd974-8e71-4624-81b1-60384e132a95'
);

SELECT t.name AS local, t.timezone, u.email AS admin, m.roles
FROM tenants t
JOIN user_memberships m ON m.tenant_id = t.id
JOIN users u ON u.id = m.user_id;
