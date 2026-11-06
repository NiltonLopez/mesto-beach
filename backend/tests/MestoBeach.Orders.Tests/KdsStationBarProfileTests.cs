using FluentAssertions;
using MestoBeach.Orders.Application.Commands;
using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Orders.Tests;

/// <summary>Perfil de flujo BAR y encendido por modo de las estaciones (ADR-037).</summary>
public class KdsStationBarProfileTests
{
    private static readonly Guid Barra = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void ReadyFromQueue_sella_preparacion_igual_a_listo()
    {
        var plato = Comanda.Plato(nombre: "Gin tonic");
        var ahora = new DateTime(2026, 10, 14, 23, 0, 0, DateTimeKind.Utc);

        plato.ReadyFromQueue(Barra, ahora);

        plato.Status.Should().Be(OrderItemStatus.Ready);
        plato.ReadyAt.Should().Be(ahora);
        plato.PreparationStartedAt.Should().Be(ahora);
        plato.PreparationOrderedBy.Should().Be(Barra);
    }

    [Theory]
    [InlineData(OrderItemStatus.Held)]
    [InlineData(OrderItemStatus.InPreparation)]
    [InlineData(OrderItemStatus.Ready)]
    public void ReadyFromQueue_solo_desde_la_cola(OrderItemStatus estado)
    {
        var plato = Comanda.PlatoEn(estado);
        var act = () => plato.ReadyFromQueue(Barra, DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void La_arista_Queued_Ready_no_existe_fuera_de_ReadyFromQueue()
    {
        var plato = Comanda.Plato();
        plato.CanTransitionTo(OrderItemStatus.Ready).Should().BeFalse();
        plato.Invoking(p => p.MarkReady()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsOnIn_exige_activa_y_modo_en_activeIn()
    {
        var cocina = new KdsStation(Guid.NewGuid(), Comanda.Tenant, "Cocina", [], true, KdsStation.FullPass, ["RESTAURANT"]);
        cocina.IsOnIn(VenueMode.RESTAURANT).Should().BeTrue();
        cocina.IsOnIn(VenueMode.NIGHT).Should().BeFalse();

        var barra = new KdsStation(Guid.NewGuid(), Comanda.Tenant, "Barra 1", [], true, KdsStation.Bar, ["RESTAURANT", "NIGHT"]);
        barra.IsOnIn(VenueMode.NIGHT).Should().BeTrue();
        barra.SetActive(false);
        barra.IsOnIn(VenueMode.NIGHT).Should().BeFalse();
    }

    [Fact]
    public void Estacion_de_v1_nace_full_pass_en_ambos_modos()
    {
        var s = new KdsStation(Guid.NewGuid(), Comanda.Tenant, "Parrilla", []);
        s.IsActive.Should().BeTrue();
        s.FlowProfile.Should().Be(KdsStation.FullPass);
        s.ActiveIn.Should().BeEquivalentTo(["RESTAURANT", "NIGHT"]);
    }

    [Fact]
    public void Validaciones_de_perfil_y_modos()
    {
        KdsStationRules.Normalize("GRILL", null).Error!.Code.Should().Be("INVALID_FLOW_PROFILE");
        KdsStationRules.Normalize(null, []).Error!.Code.Should().Be("ACTIVE_IN_EMPTY");
        KdsStationRules.Normalize(null, ["DAY"]).Error!.Code.Should().Be("INVALID_MODE");

        var ok = KdsStationRules.Normalize("bar", ["night"]);
        ok.Error.Should().BeNull();
        ok.FlowProfile.Should().Be("BAR");
        ok.ActiveIn.Should().Equal(new[] { "NIGHT" });
    }
}
