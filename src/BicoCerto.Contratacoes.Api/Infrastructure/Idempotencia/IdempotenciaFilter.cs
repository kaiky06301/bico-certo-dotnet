using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BicoCerto.Contratacoes.Api.Data;
using BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;
using Microsoft.EntityFrameworkCore;

namespace BicoCerto.Contratacoes.Api.Infrastructure.Idempotencia;

/// <summary>
/// Idempotência por header "Idempotency-Key" (padrão usado por APIs de pagamento).
/// <list type="number">
/// <item>Chave nova: reserva a chave no banco, executa a operação e grava a resposta.</item>
/// <item>Mesma chave e mesmo corpo: devolve a resposta gravada sem executar de novo (header Idempotent-Replayed: true).</item>
/// <item>Mesma chave com outro corpo: 422 (a chave foi reutilizada por engano).</item>
/// <item>Mesma chave ainda em processamento (dois cliques ao mesmo tempo): 409.</item>
/// </list>
/// </summary>
public class IdempotenciaFilter(ContratacoesDbContext db, TimeProvider relogio, ILogger<IdempotenciaFilter> logger)
    : IEndpointFilter
{
    public const string Header = "Idempotency-Key";
    public const string HeaderReplay = "Idempotent-Replayed";
    private static readonly TimeSpan Validade = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var chave = http.Request.Headers[Header].ToString().Trim();
        if (string.IsNullOrEmpty(chave) || chave.Length > 100)
            return Results.Problem(statusCode: 400, title: "Idempotency-Key obrigatório",
                detail: $"Envie o header {Header} com um identificador único (ex.: um UUID) de até 100 caracteres.");

        var usuarioId = http.User.Id();
        var rota = $"{http.Request.Method} {http.Request.Path}";
        var hash = CalcularHash(context.Arguments);
        var agora = relogio.GetUtcNow().UtcDateTime;

        var existente = await db.RegistrosIdempotencia
            .FirstOrDefaultAsync(r => r.Chave == chave && r.UsuarioId == usuarioId && r.Rota == rota);

        if (existente is not null && existente.CriadoEm < agora - Validade)
        {
            db.RegistrosIdempotencia.Remove(existente);
            await db.SaveChangesAsync();
            existente = null;
        }

        if (existente is not null)
        {
            if (existente.HashRequisicao != hash)
                return Results.Problem(statusCode: 422, title: "Idempotency-Key reutilizada",
                    detail: "Esta chave já foi usada com outro conteúdo. Gere uma chave nova para uma nova operação.");
            if (existente.StatusCode == 0)
                return Results.Problem(statusCode: 409, title: "Requisição em processamento",
                    detail: "Uma requisição com esta chave ainda está sendo processada. Aguarde e consulte o resultado.");

            logger.LogInformation("Idempotency-Key {Chave} repetida: devolvendo a resposta gravada", chave);
            http.Response.Headers[HeaderReplay] = "true";
            if (existente.Location is not null) http.Response.Headers.Location = existente.Location;
            return Results.Content(existente.CorpoResposta ?? string.Empty, "application/json",
                Encoding.UTF8, existente.StatusCode);
        }

        // reserva a chave ANTES de executar: o indice unico barra a segunda requisicao simultanea
        var registro = new RegistroIdempotencia
        {
            Chave = chave, UsuarioId = usuarioId, Rota = rota, HashRequisicao = hash, StatusCode = 0, CriadoEm = agora
        };
        db.RegistrosIdempotencia.Add(registro);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.Entry(registro).State = EntityState.Detached;
            return Results.Problem(statusCode: 409, title: "Requisição em processamento",
                detail: "Uma requisição com esta chave já está sendo processada.");
        }

        object? resultado;
        try
        {
            resultado = await next(context);
        }
        catch
        {
            await Liberar(registro);
            throw;
        }

        var status = (resultado as IStatusCodeHttpResult)?.StatusCode ?? 200;
        if (status is >= 200 and < 300)
        {
            registro.StatusCode = status;
            registro.CorpoResposta = JsonSerializer.Serialize((resultado as IValueHttpResult)?.Value, Json);
            registro.Location = ObterLocation(resultado);
            await db.SaveChangesAsync();
        }
        else
        {
            // erro de validacao/regra: libera a chave para o usuario tentar de novo
            await Liberar(registro);
        }
        return resultado;
    }

    private async Task Liberar(RegistroIdempotencia registro)
    {
        db.ChangeTracker.Clear();
        db.RegistrosIdempotencia.Remove(registro);
        await db.SaveChangesAsync();
    }

    private static string? ObterLocation(object? resultado) =>
        resultado?.GetType().GetProperty("Location")?.GetValue(resultado) as string;

    private static string CalcularHash(IList<object?> argumentos)
    {
        var corpo = argumentos.Where(a => a is not null && a.GetType().Namespace?.EndsWith("Contracts") == true);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(corpo, Json));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

public static class IdempotenciaExtensions
{
    public static RouteHandlerBuilder Idempotente(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<IdempotenciaFilter>()
            .WithOpenApi(op =>
            {
                op.Parameters.Add(new Microsoft.OpenApi.Models.OpenApiParameter
                {
                    Name = IdempotenciaFilter.Header,
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Required = true,
                    Description = "Identificador único da operação (ex.: um UUID). Reenviar a mesma chave não duplica a operação.",
                    Schema = new Microsoft.OpenApi.Models.OpenApiSchema { Type = "string", Format = "uuid" }
                });
                return op;
            });
}
