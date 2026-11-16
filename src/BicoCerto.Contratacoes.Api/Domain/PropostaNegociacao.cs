namespace BicoCerto.Contratacoes.Api.Domain;

public enum AutorProposta
{
    Cliente,
    Prestador
}

public enum StatusProposta
{
    /// <summary>Esperando a resposta da outra parte.</summary>
    Pendente,

    /// <summary>A outra parte aceitou: o valor passa a valer no orçamento.</summary>
    Aceita,

    /// <summary>A outra parte recusou: continua valendo o valor que já estava no orçamento.</summary>
    Recusada,

    /// <summary>Foi trocada por uma nova proposta (contraproposta) ou o orçamento foi contratado antes da resposta.</summary>
    Substituida
}

/// <summary>
/// Uma rodada da negociação de preço de um orçamento. Cliente e prestador se alternam: quem recebe a proposta
/// pode aceitar, recusar ou mandar outra (contraproposta). Enquanto ninguém aceita, vale o valor do orçamento.
/// </summary>
public class PropostaNegociacao
{
    public long Id { get; private set; }
    public long OrcamentoId { get; private set; }
    public AutorProposta Autor { get; private set; }
    public long AutorId { get; private set; }
    public string AutorNome { get; private set; } = string.Empty;
    public decimal Valor { get; private set; }
    public string? Comentario { get; private set; }
    public StatusProposta Status { get; private set; } = StatusProposta.Pendente;
    public DateTime CriadaEm { get; private set; }
    public DateTime? RespondidaEm { get; private set; }

    private PropostaNegociacao()
    {
    }

    public PropostaNegociacao(Orcamento orcamento, AutorProposta autor, long autorId, string autorNome, decimal valor,
                              string? comentario, DateTime agora)
    {
        orcamento.GarantirPendente("negociar");
        if (valor <= 0) throw new RegraNegocioException("O valor da proposta deve ser maior que zero.");
        if (valor == orcamento.Valor)
            throw new RegraNegocioException($"A proposta deve ter um valor diferente do atual (R$ {orcamento.Valor:N2}).");

        OrcamentoId = orcamento.Id;
        Autor = autor;
        AutorId = autorId;
        AutorNome = autorNome;
        Valor = valor;
        Comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
        CriadaEm = agora;
    }

    /// <summary>Só a outra parte responde; aceitar aplica o novo valor no orçamento.</summary>
    public void Responder(bool aceita, AutorProposta quemResponde, Orcamento orcamento, DateTime agora)
    {
        if (Status != StatusProposta.Pendente)
            throw new RegraNegocioException($"Esta proposta já foi respondida (status atual: {Status}).");
        if (quemResponde == Autor)
            throw new RegraNegocioException("Você não pode responder a sua própria proposta.");

        if (aceita) orcamento.AplicarValorNegociado(Valor);
        Status = aceita ? StatusProposta.Aceita : StatusProposta.Recusada;
        RespondidaEm = agora;
    }

    public void Substituir(DateTime agora)
    {
        if (Status != StatusProposta.Pendente) return;
        Status = StatusProposta.Substituida;
        RespondidaEm = agora;
    }
}
