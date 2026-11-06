using FluentAssertions;
using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;

namespace MestoBeach.Orders.Tests;

/// <summary>Las acciones del ciclo por ítem, una por método público de <see cref="OrderItem"/>.</summary>
public enum AccionPlato
{
    Marchar,
    OrdenarPreparacion,
    DarPorListo,
    Reclamar,
    Entregar,
    Devolver,
    Liberar,
    Anular
}

/// <summary>
/// La máquina de estados del plato (ADR-025 / ADR-032 / RF-1810), que desde el ciclo por ítem es la unidad
/// de estado del servicio. Lo que se protege aquí no es la ruta feliz —esa se ve a simple vista—
/// sino el <b>rechazo de todo lo demás</b>: marchar algo que no está en espera, entregar un plato
/// que ningún runner reclamó, tocar un plato ya entregado o anulado.
/// <para>
/// La tabla de transiciones válidas está escrita a mano desde el ADR, <b>no</b> leída de
/// <c>OrderItem.CanTransitionTo</c>: una prueba que reimplantara la tabla del dominio no probaría
/// nada. Todo par (estado, acción) que no aparezca en ella debe fallar, y la teoría de
/// transiciones inválidas recorre exhaustivamente los 8 × 8 pares para comprobarlo.
/// </para>
/// </summary>
public class OrderItemStateMachineTests
{
    private static readonly Guid Pda = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Cocina = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid Viaje = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// El contrato de ADR-025 y ADR-032 (marchar no enciende los fuegos):
    /// <c>Held → Queued → InPreparation → Ready → InDelivery → Delivered</c>, con <c>Returned</c> y
    /// <c>Ready</c> (liberar) de vuelta desde la entrega, y <c>Cancelled</c> desde todo lo que
    /// aún no salió del pase. Un plato en manos del runner no se anula.
    /// </summary>
    private static readonly (OrderItemStatus Desde, AccionPlato Accion, OrderItemStatus Hasta)[] Validas =
    [
        (OrderItemStatus.Held,          AccionPlato.Marchar,            OrderItemStatus.Queued),
        (OrderItemStatus.Held,          AccionPlato.Anular,             OrderItemStatus.Cancelled),
        (OrderItemStatus.Queued,        AccionPlato.OrdenarPreparacion, OrderItemStatus.InPreparation),
        (OrderItemStatus.Queued,        AccionPlato.Anular,             OrderItemStatus.Cancelled),
        (OrderItemStatus.InPreparation, AccionPlato.DarPorListo,        OrderItemStatus.Ready),
        (OrderItemStatus.InPreparation, AccionPlato.Anular,             OrderItemStatus.Cancelled),
        (OrderItemStatus.Ready,         AccionPlato.Reclamar,           OrderItemStatus.InDelivery),
        (OrderItemStatus.Ready,         AccionPlato.Anular,             OrderItemStatus.Cancelled),
        (OrderItemStatus.InDelivery,    AccionPlato.Entregar,           OrderItemStatus.Delivered),
        (OrderItemStatus.InDelivery,    AccionPlato.Devolver,           OrderItemStatus.Returned),
        (OrderItemStatus.InDelivery,    AccionPlato.Liberar,            OrderItemStatus.Ready),
        (OrderItemStatus.Returned,      AccionPlato.DarPorListo,        OrderItemStatus.Ready),
        (OrderItemStatus.Returned,      AccionPlato.Anular,             OrderItemStatus.Cancelled)
    ];

    /// <summary>
    /// Pares que <b>deberían</b> rechazarse y hoy no se rechazan. No son excepciones de diseño:
    /// son el defecto que documenta la región "Huecos conocidos" al final de esta clase, y salen
    /// de la matriz exhaustiva solo para que el resto pueda usarse como red de seguridad.
    /// Cuando el dominio valide por acción, esta lista se vacía y la matriz los cubre sola.
    /// </summary>
    private static readonly (OrderItemStatus Desde, AccionPlato Accion)[] HuecosConocidos =
    [
        (OrderItemStatus.InDelivery,    AccionPlato.DarPorListo),
        (OrderItemStatus.InPreparation, AccionPlato.Liberar),
        (OrderItemStatus.Returned,      AccionPlato.Liberar)
    ];

    public static IEnumerable<object[]> TransicionesValidas() =>
        Validas.Select(t => new object[] { t.Desde, t.Accion, t.Hasta });

    /// <summary>
    /// Todo lo que la tabla no permite: 8 estados × 8 acciones menos las 13 válidas = 51 pares,
    /// menos los 3 huecos conocidos = 48 casos que deben fallar hoy.
    /// </summary>
    public static IEnumerable<object[]> TransicionesInvalidas() =>
        from estado in Enum.GetValues<OrderItemStatus>()
        from accion in Enum.GetValues<AccionPlato>()
        where !Validas.Any(v => v.Desde == estado && v.Accion == accion)
           && !HuecosConocidos.Any(h => h.Desde == estado && h.Accion == accion)
        select new object[] { estado, accion };

    private static void Ejecutar(OrderItem plato, AccionPlato accion)
    {
        switch (accion)
        {
            case AccionPlato.Marchar:            plato.March(Pda); break;
            case AccionPlato.OrdenarPreparacion: plato.StartPreparation(Cocina); break;
            case AccionPlato.DarPorListo:        plato.MarkReady(); break;
            case AccionPlato.Reclamar:           plato.ClaimForDelivery(Viaje); break;
            case AccionPlato.Entregar:           plato.MarkDelivered(); break;
            case AccionPlato.Devolver:           plato.Return(); break;
            case AccionPlato.Liberar:            plato.ReleaseFromDelivery(); break;
            case AccionPlato.Anular:             plato.CancelItem(); break;
            default: throw new ArgumentOutOfRangeException(nameof(accion), accion, null);
        }
    }

    /// <summary>Todo el estado observable del plato, para comprobar que un rechazo no deja rastro.</summary>
    private sealed record Instante(
        OrderItemStatus Status,
        DateTime UpdatedAt,
        DateTime? MarchedAt,
        Guid? MarchedBy,
        DateTime? QueuedAt,
        DateTime? PreparationStartedAt,
        Guid? PreparationOrderedBy,
        DateTime? ReadyAt,
        Guid? DeliveryTripId,
        DateTime? DeliveredAt);

    private static Instante Instantanea(OrderItem p) =>
        new(p.Status, p.UpdatedAt, p.MarchedAt, p.MarchedBy, p.QueuedAt, p.PreparationStartedAt,
            p.PreparationOrderedBy, p.ReadyAt, p.DeliveryTripId, p.DeliveredAt);

    // ── Punto de partida ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(ItemTiming.Now, OrderItemStatus.Queued)]
    [InlineData(ItemTiming.Later, OrderItemStatus.Held)]
    public void ElTimingQueEligeElComensal_DecideElEstadoInicialDelPlato(
        ItemTiming timing, OrderItemStatus esperado)
    {
        var plato = Comanda.Plato(timing);

        plato.Status.Should().Be(esperado);
        plato.MarchedAt.Should().BeNull();
        plato.ReadyAt.Should().BeNull();
        plato.DeliveredAt.Should().BeNull();
        plato.DeliveryTripId.Should().BeNull();
    }

    // ── La tabla, en los dos sentidos ─────────────────────────────────────────

    [Theory]
    [MemberData(nameof(TransicionesValidas))]
    public void CadaTransicionValida_LlevaAlPlatoAlEstadoQueDiceElAdr(
        OrderItemStatus desde, AccionPlato accion, OrderItemStatus hasta)
    {
        var plato = Comanda.PlatoEn(desde);
        var cursor = plato.UpdatedAt;

        Ejecutar(plato, accion);

        plato.Status.Should().Be(hasta);
        plato.UpdatedAt.Should().BeOnOrAfter(cursor, "cada cambio de estado sella el cursor del delta del KDS");
    }

    [Theory]
    [MemberData(nameof(TransicionesInvalidas))]
    public void CadaTransicionInvalida_SeRechazaSinTocarElPlato(OrderItemStatus desde, AccionPlato accion)
    {
        var plato = Comanda.PlatoEn(desde);
        var antes = Instantanea(plato);

        var acto = () => Ejecutar(plato, accion);

        acto.Should().Throw<InvalidOperationException>()
            .WithMessage("*no es válida*");
        Instantanea(plato).Should().Be(antes,
            "una transición rechazada no puede dejar rastro: ni estado, ni sellos, ni cursor");
    }

    // ── Los rechazos que más duelen en sala ───────────────────────────────────

    /// <summary>SDD-004: "marchar dos veces el mismo ítem devuelve 409 la segunda vez".</summary>
    [Fact]
    public void Marchar_UnPlatoQueYaSeMarcho_Falla()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.Held, "Chuletón");
        plato.March(Pda);
        var marchadoEn = plato.MarchedAt;

        var acto = () => plato.March(Guid.NewGuid());

        acto.Should().Throw<InvalidOperationException>()
            .WithMessage("*Chuletón*")
            .WithMessage("*en cola*");
        plato.Status.Should().Be(OrderItemStatus.Queued);
        plato.MarchedBy.Should().Be(Pda, "el segundo intento no puede robarle la trazabilidad al primero");
        plato.MarchedAt.Should().Be(marchadoEn);
    }

    [Theory]
    [InlineData(OrderItemStatus.Queued)]
    [InlineData(OrderItemStatus.InPreparation)]
    [InlineData(OrderItemStatus.Ready)]
    [InlineData(OrderItemStatus.InDelivery)]
    [InlineData(OrderItemStatus.Returned)]
    [InlineData(OrderItemStatus.Delivered)]
    [InlineData(OrderItemStatus.Cancelled)]
    public void Marchar_UnPlatoQueNoEstaEnEspera_Falla(OrderItemStatus estado)
    {
        var plato = Comanda.PlatoEn(estado);

        var acto = () => plato.March(Pda);

        acto.Should().Throw<InvalidOperationException>();
        plato.Status.Should().Be(estado);
    }

    /// <summary>
    /// El caso que rompe la contabilidad de la entrega: dar por entregado un plato que sigue en
    /// el pase, sin haber pasado por la bandeja de ningún runner (ADR-027).
    /// </summary>
    [Fact]
    public void Entregar_UnPlatoQueNadieReclamo_Falla()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.Ready, "Tartar");

        var acto = () => plato.MarkDelivered();

        acto.Should().Throw<InvalidOperationException>()
            .WithMessage("*está listo*");
        plato.Status.Should().Be(OrderItemStatus.Ready);
        plato.DeliveredAt.Should().BeNull();
        plato.DeliveryTripId.Should().BeNull();
    }

    [Theory]
    [InlineData(OrderItemStatus.Held)]
    [InlineData(OrderItemStatus.Queued)]
    [InlineData(OrderItemStatus.InPreparation)]
    [InlineData(OrderItemStatus.Ready)]
    [InlineData(OrderItemStatus.Returned)]
    public void Devolver_UnPlatoQueNoEstaEnEntrega_Falla(OrderItemStatus estado)
    {
        var plato = Comanda.PlatoEn(estado);

        var acto = () => plato.Return();

        acto.Should().Throw<InvalidOperationException>();
        plato.Status.Should().Be(estado);
    }

    /// <summary>Dos runners no pueden llevarse el mismo plato: el segundo reclamo se rechaza.</summary>
    [Fact]
    public void Reclamar_UnPlatoQueYaVaEnLaBandejaDeOtroRunner_Falla()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.Ready);
        plato.ClaimForDelivery(Viaje);

        var otroViaje = Guid.NewGuid();
        var acto = () => plato.ClaimForDelivery(otroViaje);

        acto.Should().Throw<InvalidOperationException>();
        plato.DeliveryTripId.Should().Be(Viaje, "el plato sigue siendo del primer viaje");
    }

    [Theory]
    [InlineData(OrderItemStatus.Delivered)]
    [InlineData(OrderItemStatus.Cancelled)]
    public void UnPlatoEnEstadoTerminal_NoAdmiteNingunaAccion(OrderItemStatus terminal)
    {
        foreach (var accion in Enum.GetValues<AccionPlato>())
        {
            var plato = Comanda.PlatoEn(terminal);

            var acto = () => Ejecutar(plato, accion);

            acto.Should().Throw<InvalidOperationException>(
                $"{terminal} es terminal y {accion} debe rechazarse");
            plato.Status.Should().Be(terminal);
        }
    }

    /// <summary>Un plato ya en la bandeja del runner no se anula: primero se cierra o se libera el viaje.</summary>
    [Fact]
    public void Anular_UnPlatoQueVaCaminoDelPuesto_Falla()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.InDelivery);

        var acto = () => plato.CancelItem();

        acto.Should().Throw<InvalidOperationException>()
            .WithMessage("*en entrega*");
        plato.Status.Should().Be(OrderItemStatus.InDelivery);
    }

    // ── Los sellos que deja cada transición válida ────────────────────────────

    [Fact]
    public void Marchar_SellaQuienLoMarchoYCuando()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.Held);

        plato.March(Pda);

        plato.Status.Should().Be(OrderItemStatus.Queued);
        plato.MarchedBy.Should().Be(Pda);
        plato.MarchedAt.Should().NotBeNull();
        plato.MarchedAt.Should().Be(plato.UpdatedAt);
        plato.QueuedAt.Should().Be(plato.UpdatedAt);
        plato.PreparationStartedAt.Should().BeNull("marchar no enciende los fuegos (ADR-032)");
    }

    [Fact]
    public void OrdenarPreparacion_SellaQuienDioLaOrdenYCuando()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.Queued);

        plato.StartPreparation(Cocina);

        plato.Status.Should().Be(OrderItemStatus.InPreparation);
        plato.PreparationOrderedBy.Should().Be(Cocina);
        plato.PreparationStartedAt.Should().Be(plato.UpdatedAt);
    }

    [Fact]
    public void DarPorListo_SellaReadyAt()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.InPreparation);

        plato.MarkReady();

        plato.ReadyAt.Should().Be(plato.UpdatedAt);
    }

    [Fact]
    public void Entregar_SellaDeliveredAtYCierraElCicloDelPlato()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.InDelivery);

        plato.MarkDelivered();

        plato.Status.Should().Be(OrderItemStatus.Delivered);
        plato.DeliveredAt.Should().Be(plato.UpdatedAt);
        plato.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Devolver_SueltaElViajeYDejaElPlatoVivoEnLaColaDeSuEstacion()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.InDelivery);
        plato.DeliveryTripId.Should().NotBeNull();

        plato.Return();

        plato.Status.Should().Be(OrderItemStatus.Returned);
        plato.DeliveryTripId.Should().BeNull();
        plato.DeliveredAt.Should().BeNull();
        plato.IsActive.Should().BeTrue("un plato devuelto sigue teniendo trabajo pendiente");
    }

    /// <summary>
    /// Liberar no es devolver: el plato nunca salió del pase, solo cambió de dueño, así que
    /// conserva el <c>ReadyAt</c> del primer pase.
    /// </summary>
    [Fact]
    public void Liberar_DevuelveElPlatoAlPaseConservandoSuReadyAt()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.InDelivery);
        var readyOriginal = plato.ReadyAt;
        readyOriginal.Should().NotBeNull();

        plato.ReleaseFromDelivery();

        plato.Status.Should().Be(OrderItemStatus.Ready);
        plato.DeliveryTripId.Should().BeNull();
        plato.ReadyAt.Should().Be(readyOriginal);
    }

    [Fact]
    public void UnPlatoDevuelto_VuelveAlPaseYPuedeEntregarseDeNuevo()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.Returned);

        plato.MarkReady();
        plato.Status.Should().Be(OrderItemStatus.Ready);

        plato.ClaimForDelivery(Viaje);
        plato.MarkDelivered();

        plato.Status.Should().Be(OrderItemStatus.Delivered);
        plato.DeliveredAt.Should().NotBeNull();
    }

    // ── Visibilidad en el tablero ─────────────────────────────────────────────

    [Theory]
    [InlineData(OrderItemStatus.Held, true)]
    [InlineData(OrderItemStatus.Queued, true)]
    [InlineData(OrderItemStatus.InPreparation, true)]
    [InlineData(OrderItemStatus.Ready, true)]
    [InlineData(OrderItemStatus.InDelivery, true)]
    [InlineData(OrderItemStatus.Returned, true)]
    [InlineData(OrderItemStatus.Delivered, false)]
    [InlineData(OrderItemStatus.Cancelled, false)]
    public void IsActive_DiceSiElPlatoSigueVivoEnElTablero(OrderItemStatus estado, bool vivo)
    {
        Comanda.PlatoEn(estado).IsActive.Should().Be(vivo);
    }

    /// <summary>
    /// El cursor del delta (ADR-022 a nivel de plato) solo debe moverse cuando algo cambió:
    /// reasignar la misma estación no es un cambio y no puede reenviar el ítem al KDS.
    /// </summary>
    [Fact]
    public void AsignarLaMismaEstacion_NoMueveElCursorDelKds()
    {
        var estacion = Guid.NewGuid();
        var plato = Comanda.Plato(stationId: estacion);
        var cursor = plato.UpdatedAt;

        plato.AssignStation(estacion);

        plato.UpdatedAt.Should().Be(cursor);
        plato.StationId.Should().Be(estacion);
    }

    [Fact]
    public void RecolocarElPlatoEnOtraEstacion_LoReenviaAlKds()
    {
        var plato = Comanda.Plato();
        var cursor = plato.UpdatedAt;
        var otraEstacion = Guid.NewGuid();

        plato.AssignStation(otraEstacion);

        plato.StationId.Should().Be(otraEstacion);
        plato.UpdatedAt.Should().BeOnOrAfter(cursor);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HUECOS CONOCIDOS — la transición se valida por destino, no por acción
    //
    // `OrderItem.Transition` solo comprueba que el ESTADO DESTINO esté permitido desde el estado
    // actual. Como `MarkReady` y `ReleaseFromDelivery` apuntan las dos a READY, cualquiera de las
    // dos sirve para recorrer las aristas de la otra: `InPreparation → Ready` (dar por listo),
    // `Returned → Ready` (rehacer) e `InDelivery → Ready` (liberar) se vuelven intercambiables,
    // aunque el diagrama del propio ADR-025 las dibuje como flechas distintas.
    //
    // Hoy no es explotable por API porque los handlers repiten la comprobación
    // (`MarkOrderItemReadyHandler` exige IN_PREPARATION; `ReleaseDeliveryTripHandler` filtra por
    // `Status == InDelivery`), así que el invariante vive en la capa de aplicación en vez de en
    // la entidad: cualquier llamador nuevo que se fíe del dominio lo rompe.
    //
    // Estas tres pruebas fijan el comportamiento ACTUAL para que el cambio se note. Cuando el
    // dominio valide por acción, hay que borrarlas, vaciar `HuecosConocidos` y dejar que la
    // matriz exhaustiva las cubra.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// La estación NO puede "dar por listo" un plato que ya va en la bandeja de un runner.
    /// Si pudiera, el plato quedaría a la vez en la cola de runners y en un viaje abierto —
    /// justo el doble reclamo que ADR-027 existe para impedir. Para sacarlo de un viaje está
    /// <c>ReleaseFromDelivery</c>, que sí suelta el viaje.
    /// </summary>
    [Fact]
    public void DarPorListoUnPlatoQueVaEnUnaBandeja_SeRechaza()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.InDelivery);
        var viajeAbierto = plato.DeliveryTripId;

        var act = () => plato.MarkReady();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ya lo lleva un runner*");
        plato.Status.Should().Be(OrderItemStatus.InDelivery, "el plato sigue en su viaje");
        plato.DeliveryTripId.Should().Be(viajeAbierto);
    }

    /// <summary>
    /// Liberar un viaje no debería tocar platos que siguen en la estación, pero si alguien llama
    /// a <c>ReleaseFromDelivery</c> sobre uno en preparación, el dominio lo pone en el pase:
    /// comida sin terminar anunciada como lista.
    /// </summary>
    [Fact]
    public void Hueco_LiberarUnPlatoQueSigueEnCocina_DeberiaFallarYNoFalla()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.InPreparation);

        plato.ReleaseFromDelivery();

        plato.Status.Should().Be(OrderItemStatus.Ready,
            "comportamiento actual, no deseado: liberar solo debería aplicarse a un plato en entrega");
        plato.ReadyAt.Should().NotBeNull("y sella el pase de un plato que nadie cocinó del todo");
    }

    [Fact]
    public void Hueco_LiberarUnPlatoDevuelto_DeberiaFallarYNoFalla()
    {
        var plato = Comanda.PlatoEn(OrderItemStatus.Returned);

        plato.ReleaseFromDelivery();

        plato.Status.Should().Be(OrderItemStatus.Ready,
            "comportamiento actual, no deseado: un plato devuelto lo repone la estación, no una liberación de viaje");
    }
}
