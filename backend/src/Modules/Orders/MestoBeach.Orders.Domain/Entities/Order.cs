using MestoBeach.Orders.Domain.Enums;

namespace MestoBeach.Orders.Domain.Entities;

/// <summary>An order placed by a session participant.</summary>
public sealed class Order
{
    private Order() { } // EF Core

    public Order(
        Guid id,
        Guid sessionId,
        Guid tenantId,
        Guid participantId,
        Guid? stationId)
    {
        Id = id;
        SessionId = sessionId;
        TenantId = tenantId;
        ParticipantId = participantId;
#pragma warning disable CS0618 // se sigue rellenando mientras el tablero KDS razone por pedido
        StationId = stationId;
#pragma warning restore CS0618
        Status = OrderStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Attribute the order to a customer account / name (denormalized from the participant).</summary>
    public void SetCustomer(Guid? customerAccountId, string? customerName)
    {
        CustomerAccountId = customerAccountId;
        CustomerName = string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Observación libre para toda la comanda (se muestra en KDS, PDA, caja y reparto).</summary>
    public void SetObservacion(string? observacion)
    {
        Observacion = string.IsNullOrWhiteSpace(observacion) ? null : observacion.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// PRD-019 / ADR-038: sella prioridad y modo del local al crear la comanda. No se recalculan
    /// si la mesa cambia de clase o el local de modo, y no mueven <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetVenueSnapshot(string venueMode, int priority)
    {
        VenueMode = venueMode;
        Priority = priority;
    }

    public Guid Id { get; private set; }
    /// <summary>Snapshot al crear: 1 si nace en NIGHT desde una mesa RESERVADO; 0 en otro caso (ADR-038).</summary>
    public int Priority { get; private set; }
    /// <summary>Modo del local (RESTAURANT | NIGHT) al crear la comanda. No se recalcula.</summary>
    public string VenueMode { get; private set; } = "RESTAURANT";
    public Guid SessionId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ParticipantId { get; private set; }
    /// <summary>Denormalized from the participant at creation: the Mesto Beach account that ordered (nullable = guest).</summary>
    public Guid? CustomerAccountId { get; private set; }
    /// <summary>Denormalized name shown on the KDS / waiter ("who ordered").</summary>
    public string? CustomerName { get; private set; }
    /// <summary>Observación libre de la comanda entera (alergias, "todo para llevar"…). Null si vacía.</summary>
    public string? Observacion { get; private set; }
    public OrderStatus Status { get; private set; }

    /// <summary>
    /// Estación del pedido entero. <b>Obsoleto (ADR-025/ADR-029)</b>: desde el ciclo de vida por
    /// ítem cada plato se enruta a su propia estación (<c>OrderItem.StationId</c>) y una comanda
    /// puede repartirse entre varias. Se conserva —columna incluida— porque el tablero KDS, la
    /// consulta de estaciones activas y el cliente de API vigentes todavía razonan por pedido;
    /// se retira cuando la UI migre al contrato por ítem.
    /// </summary>
    [Obsolete("Usa OrderItem.StationId: cada plato tiene su estación (ADR-025/ADR-029). " +
              "Order.StationId se mantiene solo para el tablero KDS y el cliente de API vigentes.")]
    public Guid? StationId { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    /// <summary>Bumped on every state change — the cursor for KDS delta sync (P2).</summary>
    public DateTime UpdatedAt { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? ReadyAt { get; private set; }

    // Navigation
    public List<OrderItem> Items { get; private set; } = [];

    public bool IsActive => Status is OrderStatus.Pending
        or OrderStatus.Confirmed
        or OrderStatus.InPreparation
        or OrderStatus.Ready;

    // ── State machine ─────────────────────────────────────────────────────────

    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending]        = [OrderStatus.Confirmed],
        [OrderStatus.Confirmed]      = [OrderStatus.InPreparation],
        [OrderStatus.InPreparation]  = [OrderStatus.Ready],
        [OrderStatus.Ready]          = [OrderStatus.Delivered],
        [OrderStatus.Delivered]      = [],
        [OrderStatus.Cancelled]      = [],
        [OrderStatus.Finalized]      = []
    };

    public bool CanTransitionTo(OrderStatus target) =>
        AllowedTransitions.TryGetValue(Status, out var allowed) && allowed.Contains(target);

    public void Advance(OrderStatus target)
    {
        if (!CanTransitionTo(target))
            throw new InvalidOperationException(
                $"Transición inválida: {Status} → {target}.");

        Status = target;
        UpdatedAt = DateTime.UtcNow;
        if (target == OrderStatus.Confirmed)
            ConfirmedAt = DateTime.UtcNow;
        if (target == OrderStatus.Ready)
            ReadyAt = DateTime.UtcNow;
    }

    public void Cancel(string reason, Guid cancelledBy)
    {
        if (Status is OrderStatus.Ready or OrderStatus.Delivered or OrderStatus.Finalized)
            throw new InvalidOperationException("No se puede cancelar una orden en estado READY, DELIVERED o FINALIZED.");
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("La orden ya está cancelada.");

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        CancelledBy = cancelledBy;
        CancelledAt = DateTime.UtcNow;
        UpdatedAt = CancelledAt.Value;
    }

    // ── Estado derivado de los ítems (ADR-025) ────────────────────────────────

    /// <summary>
    /// Deriva el estado del pedido del estado de sus platos. Se llama después de cada acción
    /// sobre un ítem (marchar, dar por listo, reclamar, entregar, devolver, anular).
    /// <para>
    /// Tabla de prioridades, evaluada en orden y <b>ignorando los ítems anulados</b>:
    /// <list type="number">
    /// <item>no queda ninguna línea viva (todas anuladas) → <c>CANCELLED</c>;</item>
    /// <item>todas entregadas y ninguna retenida → <c>FINALIZED</c> (auto-finalización, RF-1814);</item>
    /// <item>alguna en preparación o devuelta → <c>IN_PREPARATION</c> (hay trabajo en la estación);</item>
    /// <item>alguna lista o en entrega → <c>READY</c> (nada en cocina, hay platos en el pase o en ruta);</item>
    /// <item>en otro caso (solo quedan retenidas, con o sin entregadas) → <c>CONFIRMED</c>.</item>
    /// </list>
    /// La regla 4 va antes que la 5 a propósito: un plato listo pesa más en el tablero que uno
    /// retenido. Lo que nunca se pierde es la regla 2 — un pedido con platos <c>HELD</c> no se
    /// finaliza aunque lo servido ya esté entregado.
    /// </para>
    /// </summary>
    /// <returns><c>true</c> si el estado del pedido cambió (y por tanto se movió el cursor).</returns>
    public bool RecalculateStatusFromItems()
    {
        // Un pedido cancelado en bloque es terminal: no se resucita desde sus platos.
        if (Status == OrderStatus.Cancelled) return false;
        // Antes de confirmar no se deriva nada: el pedido se confirma en el mismo acto de crearlo.
        if (Status == OrderStatus.Pending) return false;
        // Sin ítems cargados no hay nada que derivar (protege las lecturas sin Include).
        if (Items.Count == 0) return false;

        var vivos = Items.Where(i => i.Status != OrderItemStatus.Cancelled).ToList();

        OrderStatus derivado;
        if (vivos.Count == 0)
            derivado = OrderStatus.Cancelled;
        else if (vivos.All(i => i.Status == OrderItemStatus.Delivered)
                 && !vivos.Any(i => i.Status is OrderItemStatus.Held or OrderItemStatus.Queued))
            derivado = OrderStatus.Finalized;
        else if (vivos.Any(i => i.Status is OrderItemStatus.InPreparation or OrderItemStatus.Returned))
            derivado = OrderStatus.InPreparation;
        else if (vivos.Any(i => i.Status is OrderItemStatus.Ready or OrderItemStatus.InDelivery))
            derivado = OrderStatus.Ready;
        else
            derivado = OrderStatus.Confirmed;

        if (derivado == Status) return false;

        Status = derivado;
        UpdatedAt = DateTime.UtcNow;

        // Los sellos de tiempo solo se ponen la primera vez: el pedido puede ir y volver
        // (un plato devuelto lo baja de READY a IN_PREPARATION) y no queremos reescribirlos.
        if (derivado == OrderStatus.Confirmed) ConfirmedAt ??= UpdatedAt;
        if (derivado == OrderStatus.Ready) ReadyAt ??= UpdatedAt;
        if (derivado == OrderStatus.Cancelled)
        {
            CancelledAt ??= UpdatedAt;
            CancellationReason ??= "Todas las líneas del pedido fueron anuladas.";
        }

        return true;
    }
}
