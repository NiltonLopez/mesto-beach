using MestoBeach.Orders.Domain.Enums;

namespace MestoBeach.Orders.Domain.Entities;

/// <summary>
/// Un viaje de entrega (ADR-027): la bandeja que un runner arma con platos "listos" de su
/// estación y reparte por varios puestos. Es el dueño de los ítems mientras están en ruta,
/// y lo que permite recuperar el manifiesto si la app se cierra o se cae la red (ADR-024).
/// </summary>
public sealed class DeliveryTrip
{
    private DeliveryTrip() { } // EF Core

    public DeliveryTrip(Guid id, Guid tenantId, Guid runnerId, Guid stationId)
    {
        Id = id;
        TenantId = tenantId;
        RunnerId = runnerId;
        StationId = stationId;
        Status = DeliveryTripStatus.Open;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Staff con rol Runner que carga la bandeja. Solo puede tener un viaje abierto.</summary>
    public Guid RunnerId { get; private set; }

    /// <summary>Estación a la que está adscrito el runner; todos los platos del viaje salen de ella.</summary>
    public Guid StationId { get; private set; }

    public DeliveryTripStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Momento en que el viaje dejó de estar abierto, se cerrara o se liberara.</summary>
    public DateTime? CompletedAt { get; private set; }

    public bool IsOpen => Status == DeliveryTripStatus.Open;

    /// <summary>
    /// Cierra la ruta: lo que seguía marcado quedó entregado y lo desmarcado, devuelto.
    /// El viaje pasa a ser histórico; no se reabre.
    /// </summary>
    public void Complete()
    {
        EnsureOpen("cerrar");
        Status = DeliveryTripStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Libera un viaje abandonado: sus platos vuelven a "listo" para que otro runner los lleve.
    /// Cubre al runner que termina el turno —o pierde el móvil— con la bandeja reclamada.
    /// </summary>
    public void Release()
    {
        EnsureOpen("liberar");
        Status = DeliveryTripStatus.Released;
        CompletedAt = DateTime.UtcNow;
    }

    private void EnsureOpen(string accion)
    {
        if (Status != DeliveryTripStatus.Open)
            throw new InvalidOperationException(
                $"No se puede {accion} el viaje {Id}: ya está " +
                $"{(Status == DeliveryTripStatus.Completed ? "cerrado" : "liberado")}.");
    }
}
