using MestoBeach.API;
using MestoBeach.Analytics.Infrastructure;
using MestoBeach.API.Middleware;
using MestoBeach.Catalog.Infrastructure;
using MestoBeach.Identity.Infrastructure;
using MestoBeach.Notifications.Infrastructure;
using MestoBeach.Notifications.Infrastructure.Hubs;
using MestoBeach.Orders.Infrastructure;
using MestoBeach.Orders.Infrastructure.Hubs;
using MestoBeach.Payments.Infrastructure;
using MestoBeach.Sessions.Infrastructure;
using MestoBeach.Sessions.Infrastructure.Hubs;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// ─── Serilog ──────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Sin clave versionada: en Development se genera una local; en el resto, debe llegar por entorno.
DevJwtKeys.EnsureConfigured(builder);

// ─── Redis ────────────────────────────────────────────────────────────────────
var redisConn = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Redis connection string missing.");
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConn));

// ─── Shared / Context ─────────────────────────────────────────────────────────
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// ─── Modules ──────────────────────────────────────────────────────────────────
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddCatalogModule(builder.Configuration);
builder.Services.AddSessionsModule(builder.Configuration);
builder.Services.AddOrdersModule(builder.Configuration);
builder.Services.AddPaymentsModule(builder.Configuration);
builder.Services.AddAnalyticsModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);

// ─── JWT Authentication ───────────────────────────────────────────────────────
var rsa = RSA.Create();
rsa.ImportFromPem(builder.Configuration["Jwt:PublicKeyPem"]
    ?? throw new InvalidOperationException("JWT public key missing."));

// Un SOLO juego de parámetros de validación, compartido por el handler JwtBearer (que protege
// los endpoints [Authorize] con principal de ASP.NET) y por JwtValidationMiddleware (que puebla
// ICurrentUser, sobre el que se asientan las políticas por rol). Antes el middleware sólo
// *parseaba* el token con ReadJwtToken —sin comprobar la firma RS256 ni el exp—, así que un token
// con firma falsa poblaba ICurrentUser y colaba por cualquier política. Validándolo aquí y
// resolviéndolo por DI, ambos caminos verifican la misma firma con la misma clave.
var tokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuerSigningKey = true,
    IssuerSigningKey = new RsaSecurityKey(rsa),
    ValidateIssuer = false,
    ValidateAudience = false,
    ValidateLifetime = true,
    ClockSkew = TimeSpan.Zero
};
builder.Services.AddSingleton(tokenValidationParameters);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = tokenValidationParameters;
        // Support SignalR browser clients that cannot set custom headers —
        // they pass the JWT as ?access_token= query param on the WebSocket upgrade request.
        opts.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                // Only use query param if no Authorization header is present (header takes priority)
                var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
                if (string.IsNullOrEmpty(authHeader))
                {
                    var token = ctx.Request.Query["access_token"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(token) &&
                        ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        ctx.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

// ─── Authorization Policies ──────────────────────────────────────────────────
builder.Services.AddAuthorization(AuthorizationPolicies.ConfigurePolicies);

// ─── Controllers + JSON ───────────────────────────────────────────────────────
builder.Services.AddScoped<RequireServiceTokenFilter>();
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// ─── Swagger ─────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Mesto Beach API", Version = "v1" });
    // Avoid SchemaId conflicts when multiple controllers define types with the same name
    c.CustomSchemaIds(type => type.FullName!.Replace('+', '.'));
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ─── CORS ─────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000", "http://localhost:3001"];

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials())); // Required for HttpOnly cookies

// ─── Health checks ────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddRedis(redisConn);

// ─── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mesto Beach API v1"));

app.UseHttpsRedirection();
app.UseCors();
app.UseSerilogRequestLogging();

// Security headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseAuthentication();
app.UseMiddleware<JwtValidationMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<SessionsHub>("/hubs/sessions");
app.MapHub<OrdersHub>("/hubs/orders");
app.MapHub<RealtimeHub>("/hubs/realtime");

try
{
    Log.Information("Starting Mesto Beach API...");
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
