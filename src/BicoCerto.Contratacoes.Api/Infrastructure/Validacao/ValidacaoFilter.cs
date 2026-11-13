using System.ComponentModel.DataAnnotations;

namespace BicoCerto.Contratacoes.Api.Infrastructure.Validacao;

/// <summary>Aplica as DataAnnotations do DTO nas Minimal APIs e devolve 400 com os campos inválidos.</summary>
public class ValidacaoFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var dto = context.Arguments.OfType<T>().FirstOrDefault();
        if (dto is null)
            return Results.Problem(statusCode: 400, title: "Dados inválidos", detail: "Corpo da requisição ausente.");

        var erros = new List<ValidationResult>();
        if (!Validator.TryValidateObject(dto, new ValidationContext(dto), erros, validateAllProperties: true))
        {
            var porCampo = erros
                .SelectMany(e => e.MemberNames.DefaultIfEmpty(string.Empty), (e, campo) => (Campo: campo, e.ErrorMessage))
                .GroupBy(x => ParaCamelCase(x.Campo))
                .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Inválido").ToArray());
            return Results.ValidationProblem(porCampo, title: "Dados inválidos",
                detail: "Um ou mais campos estão inválidos.");
        }

        return await next(context);
    }

    private static string ParaCamelCase(string nome) =>
        string.IsNullOrEmpty(nome) ? nome : char.ToLowerInvariant(nome[0]) + nome[1..];
}

public static class ValidacaoExtensions
{
    public static RouteHandlerBuilder Validar<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidacaoFilter<T>>().ProducesValidationProblem();
}
