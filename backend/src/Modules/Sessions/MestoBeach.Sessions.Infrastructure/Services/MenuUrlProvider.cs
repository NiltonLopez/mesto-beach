using Microsoft.Extensions.Configuration;
using MestoBeach.Sessions.Application.Common;

namespace MestoBeach.Sessions.Infrastructure.Services;

/// <summary>
/// Resuelve la base pública de la app del comensal: <c>MENU_PUBLIC_URL</c> (PRD-019) y, si falta,
/// <c>Frontend:BaseUrl</c>. Siempre sin barra final.
///
/// Importa para los QR: un enlace a <c>localhost</c> solo funciona en el propio
/// ordenador. Para escanear el QR con un móvil hay que publicar aquí la IP del
/// equipo en la red local (o el dominio, en producción).
/// </summary>
public sealed class MenuUrlProvider : IMenuUrlProvider
{
    public MenuUrlProvider(IConfiguration config)
    {
        var url = config["MENU_PUBLIC_URL"];
        if (string.IsNullOrWhiteSpace(url)) url = config["Frontend:BaseUrl"];
        if (string.IsNullOrWhiteSpace(url)) url = "http://localhost:3000";
        MenuBaseUrl = url.Trim().TrimEnd('/');
    }

    public string MenuBaseUrl { get; }
}
