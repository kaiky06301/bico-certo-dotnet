using BicoCerto.Contratacoes.Api.Contracts;
using BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;
using BicoCerto.Contratacoes.Api.Infrastructure.Idempotencia;
using BicoCerto.Contratacoes.Api.Infrastructure.RateLimiting;
using BicoCerto.Contratacoes.Api.Infrastructure.Validacao;
using BicoCerto.Contratacoes.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BicoCerto.Contratacoes.Api.Endpoints;

public static class OrcamentoEndpoints
{
    public static RouteGroupBuilder MapOrcamentos(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/orcamentos")
            .WithTags("Orçamentos")
            .RequireAuthorization()
            .WithOpenApi();

        grupo.MapPost("/", async Task<Created<OrcamentoResponse>> (OrcamentoRequest request, HttpContext http,
                    OrcamentoService service, CancellationToken ct) =>
                {
                    var criado = await service.CriarAsync(http.User, Token(http), request, ct);
                    return TypedResults.Created($"/api/orcamentos/{criado.Id}", criado);
                })
            .RequireAuthorization(AutenticacaoExtensions.PoliticaPrestador)
            .RequireRateLimiting(RateLimitingExtensions.PoliticaEscrita)
            .Validar<OrcamentoRequest>()
            .Idempotente()
            .WithSummary("Prestador envia orçamento para uma solicitação (idempotente, valida o preço justo na API Java)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        grupo.MapGet("/", async (HttpContext http, OrcamentoService service, long? solicitacaoId, int? pagina,
                    int? tamanho, CancellationToken ct) =>
                TypedResults.Ok(await service.ListarAsync(http.User, solicitacaoId, pagina, tamanho, ct)))
            .WithSummary("Lista orçamentos (cliente: dos seus pedidos; prestador: os que enviou; admin: todos)");

        grupo.MapGet("/{id:long}", async (long id, HttpContext http, OrcamentoService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ObterAsync(http.User, id, ct)))
            .WithSummary("Detalhe de um orçamento")
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapPut("/{id:long}", async (long id, OrcamentoUpdateRequest request, HttpContext http,
                    OrcamentoService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AtualizarAsync(http.User, Token(http), id, request, ct)))
            .RequireAuthorization(AutenticacaoExtensions.PoliticaPrestador)
            .RequireRateLimiting(RateLimitingExtensions.PoliticaEscrita)
            .Validar<OrcamentoUpdateRequest>()
            .WithSummary("Prestador edita um orçamento pendente");

        grupo.MapDelete("/{id:long}", async Task<NoContent> (long id, HttpContext http, OrcamentoService service,
                    CancellationToken ct) =>
                {
                    await service.ExcluirAsync(http.User, id, ct);
                    return TypedResults.NoContent();
                })
            .RequireAuthorization(AutenticacaoExtensions.PoliticaPrestador)
            .WithSummary("Prestador retira (exclui) um orçamento pendente");

        return grupo;
    }

    internal static string Token(HttpContext http) =>
        http.Request.Headers.Authorization.ToString().Replace("Bearer ", string.Empty, StringComparison.OrdinalIgnoreCase);
}
