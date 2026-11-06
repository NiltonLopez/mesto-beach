using FluentAssertions;
using MestoBeach.Orders.Domain.Enums;

namespace MestoBeach.Orders.Tests;

/// <summary>
/// El estado derivado de la comanda (<c>Order.RecalculateStatusFromItems</c>, ADR-025) y la
/// <b>auto-finalización</b> del pedido (RF-1814).
/// <para>
/// La comanda ya no tiene estado propio: lo deduce de sus platos tras cada acción. El riesgo real
/// no es la ruta feliz, sino el orden de la tabla de prioridades y —sobre todo— la regla que
/// protege el coursing: <b>una comanda con platos en espera no se finaliza aunque lo servido ya
/// esté entregado</b>. Si esa regla se rompe, la mesa se queda sin su segundo plato y nadie se
/// entera, porque el pedido habrá desaparecido del tablero.
/// </para>
/// </summary>
public class OrderDerivedStatusTests
{
    private static readonly Guid Pda = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ── Tabla de prioridades del SDD-004 v2.0 ─────────────────────────────────
    //
    //   (1) ninguna línea viva                      → CANCELLED
    //   (2) todas entregadas y ninguna retenida     → FINALIZED
    //   (3) alguna en preparación o devuelta        → IN_PREPARATION
    //   (4) alguna lista o en entrega               → READY
    //   (5) resto (retenidas o en cola ± entregadas) → CONFIRMED
    //
    // El orden importa: (3) gana a (4) porque aún hay trabajo en la estación, y (4) gana a (5)
    // porque un plato en el pase pesa más en el tablero que uno esperando al PDA.

    [Theory]
    // (1) todas las líneas anuladas: no queda comanda
    [InlineData(OrderItemStatus.Cancelled, OrderItemStatus.Cancelled, OrderStatus.Cancelled)]
    // (2) auto-finalización; las anuladas se ignoran al derivar
    [InlineData(OrderItemStatus.Delivered, OrderItemStatus.Delivered, OrderStatus.Finalized)]
    [InlineData(OrderItemStatus.Delivered, OrderItemStatus.Cancelled, OrderStatus.Finalized)]
    // (3) hay trabajo en la estación, gane lo que gane el resto
    [InlineData(OrderItemStatus.InPreparation, OrderItemStatus.Ready, OrderStatus.InPreparation)]
    [InlineData(OrderItemStatus.InPreparation, OrderItemStatus.Held, OrderStatus.InPreparation)]
    [InlineData(OrderItemStatus.InPreparation, OrderItemStatus.Delivered, OrderStatus.InPreparation)]
    [InlineData(OrderItemStatus.Returned, OrderItemStatus.Delivered, OrderStatus.InPreparation)]
    [InlineData(OrderItemStatus.Returned, OrderItemStatus.InDelivery, OrderStatus.InPreparation)]
    // (4) nada en cocina, hay platos en el pase o en ruta — pesa más que un retenido
    [InlineData(OrderItemStatus.Ready, OrderItemStatus.Held, OrderStatus.Ready)]
    [InlineData(OrderItemStatus.InDelivery, OrderItemStatus.Held, OrderStatus.Ready)]
    [InlineData(OrderItemStatus.InDelivery, OrderItemStatus.Delivered, OrderStatus.Ready)]
    [InlineData(OrderItemStatus.Ready, OrderItemStatus.Cancelled, OrderStatus.Ready)]
    // (5) solo quedan retenidas o en cola, con o sin entregadas
    [InlineData(OrderItemStatus.Held, OrderItemStatus.Held, OrderStatus.Confirmed)]
    [InlineData(OrderItemStatus.Held, OrderItemStatus.Delivered, OrderStatus.Confirmed)]
    [InlineData(OrderItemStatus.Queued, OrderItemStatus.Queued, OrderStatus.Confirmed)]
    [InlineData(OrderItemStatus.Queued, OrderItemStatus.Delivered, OrderStatus.Confirmed)]
    public void ElEstadoDeLaComanda_SigueLaTablaDePrioridades(
        OrderItemStatus primero, OrderItemStatus segundo, OrderStatus esperado)
    {
        var pedido = Comanda.ConfirmadaCon(primero, segundo);

        pedido.RecalculateStatusFromItems();

        pedido.Status.Should().Be(esperado);
    }

    /// <summary>
    /// La tabla no depende del orden en que estén los platos dentro de la comanda: derivar es
    /// una función del conjunto, no de la posición.
    /// </summary>
    [Theory]
    [InlineData(OrderItemStatus.Held, OrderItemStatus.Delivered, OrderStatus.Confirmed)]
    [InlineData(OrderItemStatus.Ready, OrderItemStatus.InPreparation, OrderStatus.InPreparation)]
    [InlineData(OrderItemStatus.Cancelled, OrderItemStatus.Ready, OrderStatus.Ready)]
    public void ElEstadoDerivado_NoDependeDelOrdenDeLosPlatos(
        OrderItemStatus primero, OrderItemStatus segundo, OrderStatus esperado)
    {
        var directo = Comanda.ConfirmadaCon(primero, segundo);
        var alReves = Comanda.ConfirmadaCon(segundo, primero);

        directo.RecalculateStatusFromItems();
        alReves.RecalculateStatusFromItems();

        directo.Status.Should().Be(esperado);
        alReves.Status.Should().Be(esperado);
    }

    // ── Auto-finalización (RF-1814) ───────────────────────────────────────────

    /// <summary>
    /// SDD-004: "una comanda con platos <c>HELD</c> no se finaliza aunque lo servido esté
    /// entregado". Es el caso que sostiene todo el coursing.
    /// </summary>
    [Fact]
    public void UnaComandaConUnPlatoEntregadoYOtroEnEspera_NoSeFinaliza()
    {
        var servido = Comanda.PlatoEn(OrderItemStatus.Delivered, "Ensaladilla");
        var retenido = Comanda.PlatoEn(OrderItemStatus.Held, "Chuletón");
        var pedido = Comanda.Confirmada(servido, retenido);

        pedido.RecalculateStatusFromItems();

        pedido.Status.Should().NotBe(OrderStatus.Finalized,
            "el Chuletón sigue esperando al PDA: la comanda no ha terminado");
        pedido.Status.Should().Be(OrderStatus.Confirmed);
    }

    /// <summary>Ni siquiera con varios platos entregados y uno solo retenido.</summary>
    [Fact]
    public void UnSoloPlatoEnEsperaBastaParaImpedirLaFinalizacion()
    {
        var pedido = Comanda.ConfirmadaCon(
            OrderItemStatus.Delivered,
            OrderItemStatus.Delivered,
            OrderItemStatus.Delivered,
            OrderItemStatus.Held);

        pedido.RecalculateStatusFromItems();

        pedido.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public void LaComandaSeFinalizaSola_CuandoSeEntregaSuUltimoPlato()
    {
        var servido = Comanda.PlatoEn(OrderItemStatus.Delivered, "Ensaladilla");
        var enRuta = Comanda.PlatoEn(OrderItemStatus.InDelivery, "Chuletón");
        var pedido = Comanda.Confirmada(servido, enRuta);
        pedido.RecalculateStatusFromItems();
        pedido.Status.Should().Be(OrderStatus.Ready);

        enRuta.MarkDelivered();
        var cambio = pedido.RecalculateStatusFromItems();

        cambio.Should().BeTrue("el pedido salió del tablero y el cliente de API debe enterarse");
        pedido.Status.Should().Be(OrderStatus.Finalized);
    }

    /// <summary>Un plato anulado no cuenta como pendiente: la comanda se finaliza igual.</summary>
    [Fact]
    public void UnPlatoAnulado_NoImpideLaFinalizacionDelResto()
    {
        var entregado = Comanda.PlatoEn(OrderItemStatus.Delivered, "Ensaladilla");
        var anulado = Comanda.PlatoEn(OrderItemStatus.Cancelled, "Chuletón");
        var pedido = Comanda.Confirmada(entregado, anulado);

        pedido.RecalculateStatusFromItems();

        pedido.Status.Should().Be(OrderStatus.Finalized);
    }

    /// <summary>Pero un plato devuelto sí: vuelve a la cola de su estación y la comanda retrocede.</summary>
    [Fact]
    public void UnPlatoDevuelto_ImpideLaFinalizacionYDevuelveLaComandaALaEstacion()
    {
        var entregado = Comanda.PlatoEn(OrderItemStatus.Delivered, "Ensaladilla");
        var enRuta = Comanda.PlatoEn(OrderItemStatus.InDelivery, "Chuletón");
        var pedido = Comanda.Confirmada(entregado, enRuta);

        enRuta.Return();
        pedido.RecalculateStatusFromItems();

        pedido.Status.Should().Be(OrderStatus.InPreparation);
        pedido.Status.Should().NotBe(OrderStatus.Finalized);
    }

    // ── Anulación derivada ────────────────────────────────────────────────────

    [Fact]
    public void AnularTodasLasLineas_CancelaLaComandaConSuMotivo()
    {
        var pedido = Comanda.ConfirmadaCon(OrderItemStatus.Ready, OrderItemStatus.Held);
        foreach (var plato in pedido.Items) plato.CancelItem();

        pedido.RecalculateStatusFromItems().Should().BeTrue();

        pedido.Status.Should().Be(OrderStatus.Cancelled);
        pedido.CancelledAt.Should().NotBeNull();
        pedido.CancellationReason.Should().Contain("anuladas");
    }

    [Fact]
    public void UnPedidoCanceladoEnBloque_NoResucitaDesdeSusPlatos()
    {
        var pedido = Comanda.ConfirmadaCon(OrderItemStatus.InPreparation);
        pedido.Cancel("El cliente se marchó", Guid.NewGuid());

        var cambio = pedido.RecalculateStatusFromItems();

        cambio.Should().BeFalse();
        pedido.Status.Should().Be(OrderStatus.Cancelled);
        pedido.CancellationReason.Should().Be("El cliente se marchó",
            "el motivo real no puede quedar pisado por el genérico de la anulación derivada");
    }

    // ── Guardas del recálculo ─────────────────────────────────────────────────

    [Fact]
    public void UnPedidoSinConfirmar_NoDerivaNada()
    {
        var pedido = Comanda.Pendiente(Comanda.PlatoEn(OrderItemStatus.Delivered));

        pedido.RecalculateStatusFromItems().Should().BeFalse();
        pedido.Status.Should().Be(OrderStatus.Pending);
    }

    /// <summary>
    /// Guarda contra una lectura sin <c>Include(Items)</c>: sin platos cargados, la regla (1)
    /// concluiría que no queda ninguna línea viva y cancelaría un pedido sano.
    /// </summary>
    [Fact]
    public void UnPedidoSinItemsCargados_NoSeCancelaSolo()
    {
        var pedido = Comanda.Confirmada();

        pedido.RecalculateStatusFromItems().Should().BeFalse();

        pedido.Status.Should().Be(OrderStatus.Confirmed);
        pedido.CancelledAt.Should().BeNull();
    }

    [Fact]
    public void RecalcularDosVecesSeguidas_SoloMueveElCursorLaPrimera()
    {
        var pedido = Comanda.ConfirmadaCon(OrderItemStatus.Ready, OrderItemStatus.Ready);
        pedido.RecalculateStatusFromItems().Should().BeTrue();
        var cursor = pedido.UpdatedAt;

        pedido.RecalculateStatusFromItems().Should().BeFalse();

        pedido.UpdatedAt.Should().Be(cursor, "sin cambio de estado no hay nada que sincronizar");
    }

    // ── Sellos de tiempo: se ponen una vez, el pedido puede ir y volver ───────

    [Fact]
    public void ReadyAt_NoSeReescribeCuandoUnPlatoDevueltoHaceRetrocederLaComanda()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.InDelivery);
        var pedido = Comanda.Confirmada(plato);
        pedido.RecalculateStatusFromItems();
        pedido.Status.Should().Be(OrderStatus.Ready);
        var readyAt = pedido.ReadyAt;
        readyAt.Should().NotBeNull();

        plato.Return();
        pedido.RecalculateStatusFromItems().Should().BeTrue();
        pedido.Status.Should().Be(OrderStatus.InPreparation);

        plato.MarkReady();
        pedido.RecalculateStatusFromItems().Should().BeTrue();

        pedido.Status.Should().Be(OrderStatus.Ready);
        pedido.ReadyAt.Should().Be(readyAt, "el sello es el del primer pase, no el del último");
    }

    [Fact]
    public void ConfirmedAt_NoSeReescribeCuandoLaComandaVuelveAConfirmed()
    {
        var servido = Comanda.PlatoEn(OrderItemStatus.InDelivery, "Ensaladilla");
        var retenido = Comanda.PlatoEn(OrderItemStatus.Held, "Chuletón");
        var pedido = Comanda.Confirmada(servido, retenido);
        var confirmadoEn = pedido.ConfirmedAt;
        confirmadoEn.Should().NotBeNull();

        pedido.RecalculateStatusFromItems();
        pedido.Status.Should().Be(OrderStatus.Ready);

        servido.MarkDelivered();
        pedido.RecalculateStatusFromItems();

        pedido.Status.Should().Be(OrderStatus.Confirmed);
        pedido.ConfirmedAt.Should().Be(confirmadoEn);
    }

    // ── El ciclo completo, de punta a punta ───────────────────────────────────

    /// <summary>
    /// Una comanda de dos platos con coursing, recorrida entera. Documenta el momento incómodo
    /// pero deliberado: al entregarse lo pedido "ahora", la comanda <b>retrocede</b> de READY a
    /// CONFIRMED en lugar de finalizarse, porque el Chuletón sigue esperando al PDA.
    /// </summary>
    [Fact]
    public void CicloCompletoDeUnaComandaConCoursing_TerminaEnFinalized()
    {
        var ahora = Comanda.Plato(ItemTiming.Now, "Ensaladilla");
        var luego = Comanda.Plato(ItemTiming.Later, "Chuletón");
        var pedido = Comanda.Confirmada(ahora, luego);

        // La Ensaladilla entra en la cola de su estación; el Chuletón queda retenido.
        pedido.RecalculateStatusFromItems();
        pedido.Status.Should().Be(OrderStatus.Confirmed);

        // Cocina da la orden de preparación: ahora sí hay trabajo en la estación (ADR-032).
        ahora.StartPreparation(Comanda.Cocina);
        pedido.RecalculateStatusFromItems();
        pedido.Status.Should().Be(OrderStatus.InPreparation);

        ahora.MarkReady();
        pedido.RecalculateStatusFromItems();
        pedido.Status.Should().Be(OrderStatus.Ready);

        ahora.ClaimForDelivery(Guid.NewGuid());
        pedido.RecalculateStatusFromItems().Should().BeFalse(
            "que un runner cargue un plato listo no cambia lo que ve el tablero del pedido");

        ahora.MarkDelivered();
        pedido.RecalculateStatusFromItems();
        pedido.Status.Should().Be(OrderStatus.Confirmed,
            "queda el Chuletón en espera: la comanda retrocede en vez de finalizarse");

        // El PDA ve la mesa y marcha el retenido; marchar no enciende los fuegos.
        luego.March(Pda);
        pedido.RecalculateStatusFromItems();
        pedido.Status.Should().Be(OrderStatus.Confirmed);

        luego.StartPreparation(Comanda.Cocina);
        pedido.RecalculateStatusFromItems();
        pedido.Status.Should().Be(OrderStatus.InPreparation);

        luego.MarkReady();
        pedido.RecalculateStatusFromItems();
        pedido.Status.Should().Be(OrderStatus.Ready);

        luego.ClaimForDelivery(Guid.NewGuid());
        luego.MarkDelivered();
        pedido.RecalculateStatusFromItems().Should().BeTrue();

        pedido.Status.Should().Be(OrderStatus.Finalized);
        pedido.IsActive.Should().BeFalse("una comanda finalizada sale del tablero");
    }
}
