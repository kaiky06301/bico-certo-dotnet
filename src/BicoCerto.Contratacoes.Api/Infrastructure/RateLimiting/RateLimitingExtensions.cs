using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BicoCerto.Contratacoes.Api.Infrastructure.RateLimiting;

public class RateLimitingOptions
{
    public const string Secao = "RateLimiting";

    /// <summary>Limite geral por IP (protege a API inteira contra uso excessivo).</summary>
    public int GlobalPorMinuto { get; set; } = 100;

    /// <summary>Limite de escrita por usuário (anti-spam de orçamentos e contratações).</summary>
    public int EscritaPorMinuto { get; set; } = 10;
}

public static class RateLimitingExtensions
{
    public const string PoliticaEscrita = "escrita";

    public static IServiceCollection AddLimitacaoDeRequisicoes(this IServiceCollection services, IConfiguration configuration)
    {
        var opcoes = configuration.GetSection(RateLimitingOptions.Secao).Get<RateLimitingOptions>() ?? new();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // janela fixa por IP para todas as rotas
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = opcoes.GlobalPorMinuto,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // janela deslizante por usuario logado nas rotas de escrita
            options.AddPolicy(PoliticaEscrita, http =>
            {
                var chave = http.User.Identity?.IsAuthenticated == true
                    ? "usuario:" + http.User.FindFirst("uid")?.Value
                    : "ip:" + http.Connection.RemoteIpAddress;
                return RateLimitPartition.GetSlidingWindowLimiter(chave, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = opcoes.EscritaPorMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                });
            });

            options.OnRejected = async (context, ct) =>
            {
                var retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var tempo)
                    ? (int)Math.Ceiling(tempo.TotalSeconds)
                    : 60;
                context.HttpContext.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
                var problema = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Muitas requisições",
                    Detail = $"Limite de requisições atingido. Tente novamente em {retry} segundos.",
                    Instance = context.HttpContext.Request.Path
                };
                await context.HttpContext.Response.WriteAsJsonAsync(problema, (System.Text.Json.JsonSerializerOptions?)null,
                    "application/problem+json", ct);
            };
        });
        return services;
    }
}
