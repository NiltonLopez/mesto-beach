using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;

namespace MestoBeach.Orders.Tests;

/// <summary>
/// Constructores de conveniencia para las pruebas del ciclo de vida por ítem (ADR-025 / ADR-026 / ADR-032).
/// <para>
/// Son pruebas de <b>dominio puro</b>: no hay EF, ni repositorios, ni SignalR — solo
/// <see cref="Order"/> y <see cref="OrderItem"/>. Un estado de partida se alcanza siempre
/// recorriendo transiciones válidas con la misma API pública que se prueba, de modo que ninguna
/// prueba arranca desde un estado que la máquina real no podría producir.
/// </para>
/// </summary>
internal static class Comanda
{
    public static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Estacion = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Cocina = Guid.Parse("66666666-6666-6666-6666-666666666666");

    /// <summary>Un plato recién añadido a la comanda, con el timing que eligió el comensal (RF-1811).</summary>
    public static OrderItem Plato(
        ItemTiming timing = ItemTiming.Now,
        string nombre = "Ensaladilla",
        Guid? stationId = null)
        => new(
            id: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            tenantId: Tenant,
            catalogItemId: "cat-ensaladilla",
            itemNameSnapshot: nombre,
            itemPriceSnapshotCents: 1_450,
            quantity: 1,
            notes: null,
            loyaltyRedeemed: false,
            selectedModifiers: null,
            timing: timing,
            stationId: stationId ?? Estacion);

    /// <summary>Un plato colocado en <paramref name="estado"/> recorriendo solo transiciones válidas.</summary>
    public static OrderItem PlatoEn(OrderItemStatus estado, string nombre = "Ensaladilla")
    {
        var plato = Plato(estado == OrderItemStatus.Held ? ItemTiming.Later : ItemTiming.Now, nombre);

        switch (estado)
        {
            case OrderItemStatus.Held:
            case OrderItemStatus.Queued:
                break;
            case OrderItemStatus.InPreparation:
                plato.StartPreparation(Cocina);
                break;
            case OrderItemStatus.Ready:
                plato.StartPreparation(Cocina);
                plato.MarkReady();
                break;
            case OrderItemStatus.InDelivery:
                plato.StartPreparation(Cocina);
                plato.MarkReady();
                plato.ClaimForDelivery(Guid.NewGuid());
                break;
            case OrderItemStatus.Delivered:
                plato.StartPreparation(Cocina);
                plato.MarkReady();
                plato.ClaimForDelivery(Guid.NewGuid());
                plato.MarkDelivered();
                break;
            case OrderItemStatus.Returned:
                plato.StartPreparation(Cocina);
                plato.MarkReady();
                plato.ClaimForDelivery(Guid.NewGuid());
                plato.Return();
                break;
            case OrderItemStatus.Cancelled:
                plato.CancelItem();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(estado), estado, "Estado de ítem no contemplado por el fixture.");
        }

        // Si el fixture deriva del dominio, la prueba estaría midiendo otra cosa: mejor reventar aquí.
        if (plato.Status != estado)
            throw new InvalidOperationException(
                $"El fixture dejó el plato en {plato.Status} y se había pedido {estado}.");

        return plato;
    }

    /// <summary>Una comanda recién creada (PENDING) con esos platos.</summary>
    public static Order Pendiente(params OrderItem[] platos)
    {
        var pedido = new Order(
            id: Guid.NewGuid(),
            sessionId: Guid.NewGuid(),
            tenantId: Tenant,
            participantId: Guid.NewGuid(),
            stationId: null);
        pedido.Items.AddRange(platos);
        return pedido;
    }

    /// <summary>
    /// Una comanda ya confirmada con esos platos. El estado derivado no se calcula mientras el
    /// pedido está <c>PENDING</c>, así que las pruebas de la tabla de prioridades parten de aquí.
    /// </summary>
    public static Order Confirmada(params OrderItem[] platos)
    {
        var pedido = Pendiente(platos);
        pedido.Advance(OrderStatus.Confirmed);
        return pedido;
    }

    /// <summary>Comanda confirmada cuyos platos arrancan en los estados dados.</summary>
    public static Order ConfirmadaCon(params OrderItemStatus[] estados)
        => Confirmada(estados.Select((e, i) => PlatoEn(e, $"Plato {i + 1}")).ToArray());
}
