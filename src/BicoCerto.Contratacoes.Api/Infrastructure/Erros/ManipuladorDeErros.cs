using BicoCerto.Contratacoes.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BicoCerto.Contratacoes.Api.Infrastructure.Erros;

/// <summary>Converte exceções em respostas ProblemDetails (RFC 7807) com o status HTTP adequado.</summary>
public class ManipuladorDeErros(ILogger<ManipuladorDeErros> logger, IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, titulo) = ex switch
        {
            RegraNegocioException => (StatusCodes.Status422UnprocessableEntity, "Regra de negócio"),
            RecursoNaoEncontradoException => (StatusCodes.Status404NotFound, "Não encontrado"),
            AcessoNegadoException => (StatusCodes.Status403Forbidden, "Acesso negado"),
            ServicoExternoIndisponivelException => (StatusCodes.Status503ServiceUnavailable, "Serviço indisponível"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Requisição inválida"),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Erro inesperado em {Caminho}", http.Request.Path);

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = ex,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = titulo,
                Detail = status == 500 ? "Erro interno. Tente novamente mais tarde." : ex.Message,
                Instance = http.Request.Path
            }
        });
    }
}
