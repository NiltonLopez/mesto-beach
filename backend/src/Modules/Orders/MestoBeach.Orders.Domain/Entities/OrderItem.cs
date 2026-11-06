using MestoBeach.Orders.Domain.Enums;

namespace MestoBeach.Orders.Domain.Entities;

/// <summary>
/// Una línea de pedido, con el snapshot de nombre/precio del catálogo y —desde ADR-025—
/// su <b>propio ciclo de vida</b>: el plato es la unidad de estado, no la comanda.
/// </summary>
public sealed class OrderItem
{
    private OrderItem() { } // EF Core

    public OrderItem(
        Guid id,
        Guid orderId,
        Guid tenantId,
        string catalogItemId,
        string itemNameSnapshot,
        int itemPriceSnapshotCents,
        int quantity,
        string? notes,
        bool loyaltyRedeemed,
        string? selectedModifiers = null,
        ItemTiming timing = ItemTiming.Now,
        Guid? stationId = null)
    {
        Id = id;
        OrderId = orderId;
        TenantId = tenantId;
        CatalogItemId = catalogItemId;
        ItemNameSnapshot = itemNameSnapshot;
        ItemPriceSnapshotCents = itemPriceSnapshotCents;
        Quantity = quantity;
        Notes = notes;
        LoyaltyRedeemed = loyaltyRedeemed;
        SelectedModifiers = selectedModifiers;
        Timing = timing;
        StationId = stationId;
        // El plato "luego" queda retenido esperando a que lo marchen; el "ahora" entra directo en
        // la COLA de su estación —no en preparación—, porque quien enciende los fuegos es el
        // encargado de cocina con la orden de preparación.
        Status = timing == ItemTiming.Later ? OrderItemStatus.Held : OrderItemStatus.Queued;
        UpdatedAt = DateTime.UtcNow;
        // Sello del primer estado: los tiempos por paso se miden entre sellos consecutivos.
        if (Status == OrderItemStatus.Queued) QueuedAt = UpdatedAt;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }

    /// <summary>
    /// Denormalizado del pedido. Existe para que el KDS y la cola de runners puedan filtrar
    /// por tenant + estación + estado sin unir con <c>orders</c>
    /// (índices <c>(tenant_id, status)</c> y <c>(tenant_id, station_id, status)</c>).
    /// </summary>
    public Guid TenantId { get; private set; }

    public string CatalogItemId { get; private set; } = string.Empty;
    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public int ItemPriceSnapshotCents { get; private set; }
    public int Quantity { get; private set; }
    public string? Notes { get; private set; }
    public bool LoyaltyRedeemed { get; private set; }
    /// <summary>Resumen legible de los modificadores elegidos, p. ej. "3/4 · sin Cebolla · + Michelada".</summary>
    public string? SelectedModifiers { get; private set; }

    // ── Ciclo de vida por ítem (ADR-025) ──────────────────────────────────────

    public OrderItemStatus Status { get; private set; }

    /// <summary>Lo pidió "ahora" o "luego" (ADR-026).</summary>
    public ItemTiming Timing { get; private set; }

    /// <summary>Estación que prepara este plato. Cada ítem va a una sola estación (ADR-029).</summary>
    public Guid? StationId { get; private set; }

    /// <summary>Cuándo se marchó el plato retenido.</summary>
    public DateTime? MarchedAt { get; private set; }
    /// <summary>Quién lo marchó (trazabilidad: cocina o PDA/Administración).</summary>
    public Guid? MarchedBy { get; private set; }

    // ── Sellos por estado ─────────────────────────────────────────────────────
    // El KDS muestra cuánto lleva el plato en CADA paso (suyo y el de la otra estación), así que
    // cada entrada de estado deja su marca. El tramo de un paso es la diferencia con el siguiente
    // sello; el del paso actual se calcula en vivo contra "ahora".

    /// <summary>Cuándo entró en la cola de su estación (marchado, esperando la orden).</summary>
    public DateTime? QueuedAt { get; private set; }

    /// <summary>Cuándo el encargado de cocina dio la orden de preparación.</summary>
    public DateTime? PreparationStartedAt { get; private set; }
    /// <summary>Quién dio la orden de preparación (trazabilidad).</summary>
    public Guid? PreparationOrderedBy { get; private set; }

    /// <summary>Cuándo la estación lo dio por listo.</summary>
    public DateTime? ReadyAt { get; private set; }

    /// <summary>Viaje de entrega que lo lleva ahora mismo; null si no está reclamado.</summary>
    public Guid? DeliveryTripId { get; private set; }

    /// <summary>Cuándo se entregó en el puesto.</summary>
    public DateTime? DeliveredAt { get; private set; }

    /// <summary>
    /// Cursor del delta del KDS <b>a nivel de ítem</b> (ADR-022 llevado al plato): se sella en
    /// cada cambio de estado para que el sondeo autoritativo devuelva solo lo que cambió.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Está vivo en el tablero: aún hay algo que hacer con él.</summary>
    public bool IsActive => Status is OrderItemStatus.Held
        or OrderItemStatus.Queued
        or OrderItemStatus.InPreparation
        or OrderItemStatus.Ready
        or OrderItemStatus.InDelivery
        or OrderItemStatus.Returned;

    // ── Máquina de estados ────────────────────────────────────────────────────
    //
    //   Held ──march──► Queued ──orden de preparación──► InPreparation ──ready──► Ready
    //                                                          ▲                    │
    //                                    ──claim──► InDelivery ─┴──release──────────┤
    //                                                    │                          │
    //                                                    └──deliver──► Delivered    │
    //                                       Returned ◄──return──┘                   │
    //                                          └──────────ready─────────────────────┘
    //
    //   Un plato "ahora" nace ya en Queued; uno "luego" nace en Held y marchar lo mete en la cola.
    //   Cancelled se alcanza desde Held, Queued, InPreparation, Ready y Returned.
    //   Delivered y Cancelled son terminales. Un plato en manos del runner (InDelivery) no se
    //   anula: primero se cierra o se libera el viaje.

    private static readonly Dictionary<OrderItemStatus, OrderItemStatus[]> AllowedTransitions = new()
    {
        [OrderItemStatus.Held]          = [OrderItemStatus.Queued, OrderItemStatus.Cancelled],
        [OrderItemStatus.Queued]        = [OrderItemStatus.InPreparation, OrderItemStatus.Cancelled],
        [OrderItemStatus.InPreparation] = [OrderItemStatus.Ready, OrderItemStatus.Cancelled],
        [OrderItemStatus.Ready]         = [OrderItemStatus.InDelivery, OrderItemStatus.Cancelled],
        [OrderItemStatus.InDelivery]    = [OrderItemStatus.Delivered, OrderItemStatus.Returned, OrderItemStatus.Ready],
        [OrderItemStatus.Returned]      = [OrderItemStatus.Ready, OrderItemStatus.Cancelled],
        [OrderItemStatus.Delivered]     = [],
        [OrderItemStatus.Cancelled]     = []
    };

    public bool CanTransitionTo(OrderItemStatus target) =>
        AllowedTransitions.TryGetValue(Status, out var allowed) && allowed.Contains(target);

    /// <summary>Etiqueta en español del estado, para los mensajes de error.</summary>
    public static string Describe(OrderItemStatus status) => status switch
    {
        OrderItemStatus.Held          => "en espera",
        OrderItemStatus.Queued        => "en cola",
        OrderItemStatus.InPreparation => "en preparación",
        OrderItemStatus.Ready         => "listo",
        OrderItemStatus.InDelivery    => "en entrega",
        OrderItemStatus.Delivered     => "entregado",
        OrderItemStatus.Returned      => "devuelto",
        OrderItemStatus.Cancelled     => "anulado",
        _                             => status.ToString()
    };

    private void Transition(OrderItemStatus target, string accion)
    {
        if (!CanTransitionTo(target))
            throw new InvalidOperationException(
                $"No se puede {accion} el plato «{ItemNameSnapshot}»: está {Describe(Status)} " +
                $"y esa transición no es válida ({Describe(Status)} → {Describe(target)}).");

        Status = target;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marcha un plato retenido: lo saca de "en espera" y lo mete en la <b>cola</b> de su
    /// estación, donde espera la orden de preparación. Lo pueden hacer la cocina (que ve el
    /// plato venir) y el PDA / Administración; el rol lo comprueba la capa de aplicación, aquí
    /// solo se valida la transición.
    /// </summary>
    public void March(Guid staffId)
    {
        Transition(OrderItemStatus.Queued, "marchar");
        MarchedAt = UpdatedAt;
        MarchedBy = staffId;
        QueuedAt = UpdatedAt;
    }

    /// <summary>
    /// El encargado de cocina da la <b>orden de preparación</b>: el plato sale de la cola y
    /// entra en preparación. Es el "¡a preparar!" del pase — la estación decide cuándo se
    /// encienden los fuegos, no el reloj del comensal.
    /// </summary>
    public void StartPreparation(Guid staffId)
    {
        Transition(OrderItemStatus.InPreparation, "dar la orden de preparación");
        PreparationStartedAt = UpdatedAt;
        PreparationOrderedBy = staffId;
    }

    /// <summary>
    /// La estación lo da por listo: entra en la cola de runners. También es el camino de vuelta
    /// de un plato devuelto, que la estación vuelve a poner en el pase.
    /// </summary>
    public void MarkReady()
    {
        // La arista InDelivery -> Ready existe, pero es de ReleaseFromDelivery: sirve para soltar
        // un viaje abandonado, no para que la estación "vuelva a dar por listo" un plato que ya va
        // en la bandeja de un runner. Sin esta guarda, hacerlo lo sacaría del viaje en silencio y
        // el runner llegaría a la mesa con un plato que el sistema cree que sigue en el pase.
        if (Status is OrderItemStatus.InDelivery)
            throw new InvalidOperationException(
                $"El plato «{ItemNameSnapshot}» ya lo lleva un runner: no se puede dar por listo. " +
                "Cierra o libera su ruta primero.");

        Transition(OrderItemStatus.Ready, "dar por listo");
        ReadyAt = UpdatedAt;
    }

    /// <summary>
    /// Perfil <c>BAR</c> (ADR-037): la barra da por listo un plato que sigue en la cola, sin pasar
    /// por los fuegos. Guarda propia —no añade la arista Queued → Ready a AllowedTransitions—, así
    /// la transición solo existe a través de este método. Sella <c>preparation_started_at = ready_at</c>
    /// para que los sellos de ADR-032 sigan continuos (tiempo de fuegos cero, no nulo). En
    /// persistencia la regla la aplica el UPDATE condicional del repositorio, que la replica en SQL.
    /// </summary>
    public void ReadyFromQueue(Guid staffId, DateTime now)
    {
        if (Status != OrderItemStatus.Queued)
            throw new InvalidOperationException(
                $"No se puede dar por listo desde la cola el plato «{ItemNameSnapshot}»: está {Describe(Status)}.");

        Status = OrderItemStatus.Ready;
        UpdatedAt = now;
        ReadyAt = now;
        PreparationStartedAt ??= now;
        PreparationOrderedBy ??= staffId;
    }

    /// <summary>
    /// Un runner lo reclama para su bandeja. El reclamo es atómico a nivel de repositorio
    /// (ADR-027): aquí se valida que el plato siga estando "listo".
    /// </summary>
    public void ClaimForDelivery(Guid tripId)
    {
        Transition(OrderItemStatus.InDelivery, "reclamar para entrega");
        DeliveryTripId = tripId;
    }

    /// <summary>Entregado en el puesto del comensal. Cierra el ciclo del plato.</summary>
    public void MarkDelivered()
    {
        Transition(OrderItemStatus.Delivered, "marcar como entregado");
        DeliveredAt = UpdatedAt;
    }

    /// <summary>
    /// El runner no pudo entregarlo: vuelve a la cola de su estación de origen señalado como
    /// devuelto (RF-1822). El motivo no se captura: lo explica el runner en persona (ADR-027).
    /// </summary>
    public void Return()
    {
        Transition(OrderItemStatus.Returned, "devolver");
        DeliveryTripId = null;
    }

    /// <summary>
    /// Libera un plato de un viaje abandonado: vuelve a "listo" para que lo reclame otro runner
    /// (<c>POST /delivery-trips/{tripId}/release</c>). No es lo mismo que devolverlo: el plato
    /// nunca salió del pase, solo cambió de dueño.
    /// </summary>
    public void ReleaseFromDelivery()
    {
        Transition(OrderItemStatus.Ready, "liberar");
        DeliveryTripId = null;
        ReadyAt ??= UpdatedAt;
    }

    /// <summary>Anula la línea. Se ignora al derivar el estado del pedido.</summary>
    public void CancelItem()
    {
        Transition(OrderItemStatus.Cancelled, "anular");
    }

    /// <summary>
    /// Enruta el plato a una estación (ADR-029). Se usa al crear el pedido y al recolocar
    /// platos cuando cambia la configuración de estaciones.
    /// </summary>
    public void AssignStation(Guid? stationId)
    {
        if (StationId == stationId) return;
        StationId = stationId;
        UpdatedAt = DateTime.UtcNow;
    }
}
