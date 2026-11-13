using BicoCerto.Contratacoes.Api.Contracts;
using BicoCerto.Contratacoes.Api.Domain;
using BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;
using BicoCerto.Contratacoes.Api.Infrastructure.Idempotencia;
using BicoCerto.Contratacoes.Api.Infrastructure.RateLimiting;
using BicoCerto.Contratacoes.Api.Infrastructure.Validacao;
using BicoCerto.Contratacoes.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BicoCerto.Contratacoes.Api.Endpoints;

public static class ContratacaoEndpoints
{
    public static RouteGroupBuilder MapContratacoes(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/contratacoes")
            .WithTags("Contratações")
            .RequireAuthorization()
            .WithOpenApi();

        grupo.MapPost("/", async Task<Created<ContratacaoResponse>> (ContratacaoRequest request, HttpContext http,
                    ContratacaoService service, CancellationToken ct) =>
                {
                    var criada = await service.CriarAsync(http.User, request, ct);
                    return TypedResults.Created($"/api/contratacoes/{criada.Id}", criada);
                })
            .RequireAuthorization(AutenticacaoExtensions.PoliticaCliente)
            .RequireRateLimiting(RateLimitingExtensions.PoliticaEscrita)
            .Validar<ContratacaoRequest>()
            .Idempotente()
            .WithSummary("Cliente aceita um orçamento e agenda o serviço (idempotente: clicar 2x não contrata 2x)")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        grupo.MapGet("/", async (HttpContext http, ContratacaoService service, StatusContratacao? status, int? pagina,
                    int? tamanho, CancellationToken ct) =>
                TypedResults.Ok(await service.ListarAsync(http.User, status, pagina, tamanho, ct)))
            .WithSummary("Lista as contratações do usuário logado (admin: todas), ordenadas pela data agendada");

        grupo.MapGet("/{id:long}", async (long id, HttpContext http, ContratacaoService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ObterAsync(http.User, id, ct)))
            .WithSummary("Detalhe de uma contratação")
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapPut("/{id:long}/agendamento", async (long id, ReagendamentoRequest request, HttpContext http,
                    ContratacaoService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ReagendarAsync(http.User, id, request, ct)))
            .RequireRateLimiting(RateLimitingExtensions.PoliticaEscrita)
            .Validar<ReagendamentoRequest>()
            .WithSummary("Cliente ou prestador reagenda a data do serviço");

        grupo.MapPatch("/{id:long}/status", async (long id, StatusContratacaoRequest request, HttpContext http,
                    ContratacaoService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AlterarStatusAsync(http.User, id, request.Status!.Value, ct)))
            .Validar<StatusContratacaoRequest>()
            .WithSummary("Muda o status: EmAndamento/Concluida (prestador) ou Cancelada (cliente ou prestador)");

        grupo.MapDelete("/{id:long}", async Task<NoContent> (long id, HttpContext http, ContratacaoService service,
                    CancellationToken ct) =>
                {
                    await service.ExcluirAsync(http.User, id, ct);
                    return TypedResults.NoContent();
                })
            .WithSummary("Exclui uma contratação Agendada ou Cancelada e reabre os orçamentos do pedido");

        return grupo;
    }
}
