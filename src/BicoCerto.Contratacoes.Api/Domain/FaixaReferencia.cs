namespace BicoCerto.Contratacoes.Api.Domain;

/// <summary>
/// Faixa de referência de preço calculada pela triagem da API Java. Não é um teto: um serviço pode ter particularidades
/// que justifiquem um valor maior. Mas, acima da faixa, quem propõe precisa explicar o motivo para o morador.
/// </summary>
public static class FaixaReferencia
{
    public const string SemReferencia = "SemReferencia";
    public const string Abaixo = "Abaixo";
    public const string Dentro = "Dentro";
    public const string Acima = "Acima";

    public static string Posicao(decimal valor, decimal minimo, decimal maximo) =>
        maximo <= 0 ? SemReferencia : valor > maximo ? Acima : valor < minimo ? Abaixo : Dentro;

    public static void ExigirJustificativaSeAcima(decimal valor, decimal maximo, string? justificativa, string ondeExplicar)
    {
        if (maximo <= 0 || valor <= maximo || !string.IsNullOrWhiteSpace(justificativa)) return;
        throw new RegraNegocioException(
            $"O valor R$ {valor:N2} está acima da faixa de referência deste serviço (até R$ {maximo:N2}). " +
            $"Explique o motivo {ondeExplicar} para o morador entender a diferença.");
    }
}
