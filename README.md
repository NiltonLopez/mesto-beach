# Mesto Beach

Pedidos por QR y coordinación del servicio en tiempo real para un beach club: el comensal pide desde el móvil sin instalar nada y cada plato llega a su estación, al camarero y al runner. Un interruptor pasa todo el local de restaurante a modo fiesta.

**Stack:** .NET 8 (monolito modular, Clean Architecture) · Next.js 14 y TypeScript · PostgreSQL, MongoDB y Redis · SignalR · Docker.

**Ejecutar** (solo hace falta Docker):

    docker compose --profile demo up -d --build

Panel en `http://localhost:3003` y consola del personal en `http://localhost:3001`, con la cuenta de demo local `admin@mestobeach.demo` / `MestoBeach2026!`.

**Desarrollo con IA:** hecho por Nilton López y Brayan López con Claude Code. [`CLAUDE.md`](CLAUDE.md) recoge las reglas con las que trabaja el agente y [`PROCESS.md`](PROCESS.md), el flujo PRD → ADR → SDD → código.

Es la versión pública y anónima de un proyecto real: los datos del cliente están cambiados y se publica en un solo commit. Detalle en [`docs/GUIA.md`](docs/GUIA.md) y [`ARCHITECTURE.md`](ARCHITECTURE.md). Código solo para consulta: ver [`LICENSE`](LICENSE).
