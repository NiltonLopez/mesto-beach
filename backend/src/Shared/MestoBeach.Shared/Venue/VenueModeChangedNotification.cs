using MediatR;

namespace MestoBeach.Shared.Venue;

/// <summary>
/// Cambio efectivo de modo (SDD-001 §7). Vive en Shared para que Orders y Catalog implementen
/// sus handlers sin referenciar Identity. Los handlers deben vivir en la capa Application de cada
/// módulo: cada *Module.cs solo registra ese ensamblado en MediatR.
/// </summary>
public sealed record VenueModeChangedNotification(
    Guid TenantId, VenueMode Mode, VenueMode Previous, DateTime ChangedAt, Guid ChangedBy)
    : INotification;
