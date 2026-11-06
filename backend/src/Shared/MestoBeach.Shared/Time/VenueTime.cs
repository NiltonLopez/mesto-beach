namespace MestoBeach.Shared.Time;

/// <summary>
/// La hora del local. El local está en España, así que el "día de negocio"
/// se corta a medianoche de <c>Europe/Madrid</c>, no en UTC.
///
/// Existe porque el sistema heredado tenía cableado el desfase de Colombia (UTC-5)
/// en varios sitios: los informes cortaban el día cinco horas tarde. España además
/// tiene horario de verano, así que un desfase fijo estaría mal media parte del año;
/// por eso se convierte con <see cref="TimeZoneInfo"/> y no restando horas.
/// </summary>
public static class VenueTime
{
    /// <summary>Identificador IANA. Se usa también en SQL: <c>created_at AT TIME ZONE ...</c>.</summary>
    public const string TimeZoneId = "Europe/Madrid";

    /// <summary>Identificador equivalente en Windows, por si la máquina no resuelve IANA.</summary>
    private const string WindowsTimeZoneId = "Romance Standard Time";

    private static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // .NET resuelve identificadores IANA en Windows cuando hay ICU; si no lo hay,
            // se cae al identificador nativo antes que devolver UTC en silencio.
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsTimeZoneId);
        }
    }

    /// <summary>
    /// Convierte un rango de días naturales del local al rango UTC equivalente,
    /// con el extremo superior exclusivo (<c>[desde, hasta)</c>).
    /// </summary>
    public static (DateTime From, DateTime ToExclusive) ToUtcRange(DateOnly from, DateOnly to)
    {
        var localFrom = from.ToDateTime(TimeOnly.MinValue);              // Kind = Unspecified
        var localToExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        return (
            TimeZoneInfo.ConvertTimeToUtc(localFrom, Zone),
            TimeZoneInfo.ConvertTimeToUtc(localToExclusive, Zone));
    }
}
