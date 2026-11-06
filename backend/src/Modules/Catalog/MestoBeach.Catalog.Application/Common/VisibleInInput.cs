using MestoBeach.Catalog.Domain.Entities;
using MestoBeach.Shared.Errors;

namespace MestoBeach.Catalog.Application.Common;

/// <summary>Validación y normalización de <c>visibleIn</c> en las escrituras del admin (ADR-041).</summary>
internal static class VisibleInInput
{
    /// <summary>
    /// null → (null, null): no se toca. Si llega, devuelve la lista normalizada o el error
    /// 400 <c>VISIBLE_IN_EMPTY</c> / <c>INVALID_MODE</c>.
    /// </summary>
    public static (List<string>? Value, Error? Error) Parse(List<string>? raw)
    {
        if (raw is null) return (null, null);
        var (value, code) = VisibleInModes.Normalize(raw);
        return code switch
        {
            null => (value, null),
            VisibleInModes.EmptyErrorCode => (null, Error.Validation(code,
                "visibleIn no puede quedar vacío: elige al menos RESTAURANT o NIGHT.")),
            _ => (null, Error.Validation(code, "visibleIn solo admite RESTAURANT y NIGHT.")),
        };
    }
}
