using System.ComponentModel.DataAnnotations;
using BicoCerto.Contratacoes.Api.Domain;

namespace BicoCerto.Contratacoes.Api.Contracts;

/// <summary>Proposta enviada pelo prestador para uma solicitação da API Java.</summary>
public record OrcamentoRequest(
    [property: Required(ErrorMessage = "Informe a solicitação")]
    [property: Range(1, long.MaxValue, ErrorMessage = "Solicitação inválida")]
    long? SolicitacaoId,

    [property: Required(ErrorMessage = "Informe o valor")]
    [property: Range(typeof(decimal), "1", "100000", ErrorMessage = "O valor deve estar entre R$ 1 e R$ 100.000")]
    decimal? Valor,

    [property: Required(ErrorMessage = "Informe o prazo em dias")]
    [property: Range(1, 60, ErrorMessage = "O prazo deve ser de 1 a 60 dias")]
    int? PrazoDias,

    [property: StringLength(500, ErrorMessage = "A observação deve ter até 500 caracteres")]
    string? Observacao,

    /// <summary>Usado só quando a integração com a API Java está desligada (ex.: ambiente da DevOps).</summary>
    long? ClienteId,
    /// <summary>Validade da proposta em dias (1 a 30; padrão 7).</summary>
    [property: Range(1, 30, ErrorMessage = "A validade deve ser de 1 a 30 dias")]
    int? ValidadeDias = null,

    bool? IncluiMaoDeObra = null,
    bool? IncluiMaterial = null,
    bool? IncluiDeslocamento = null,

    [property: StringLength(200, ErrorMessage = "Os serviços adicionais devem ter até 200 caracteres")]
    string? Adicionais = null);

public record OrcamentoUpdateRequest(
    [property: Required(ErrorMessage = "Informe o valor")]
    [property: Range(typeof(decimal), "1", "100000", ErrorMessage = "O valor deve estar entre R$ 1 e R$ 100.000")]
    decimal? Valor,

    [property: Required(ErrorMessage = "Informe o prazo em dias")]
    [property: Range(1, 60, ErrorMessage = "O prazo deve ser de 1 a 60 dias")]
    int? PrazoDias,

    [property: StringLength(500, ErrorMessage = "A observação deve ter até 500 caracteres")]
    string? Observacao,
    /// <summary>Validade da proposta em dias (1 a 30; padrão 7).</summary>
    [property: Range(1, 30, ErrorMessage = "A validade deve ser de 1 a 30 dias")]
    int? ValidadeDias = null,

    bool? IncluiMaoDeObra = null,
    bool? IncluiMaterial = null,
    bool? IncluiDeslocamento = null,

    [property: StringLength(200, ErrorMessage = "Os serviços adicionais devem ter até 200 caracteres")]
    string? Adicionais = null);

/// <summary>
/// Valor = valor vigente (muda quando uma proposta da negociação é aceita). ValorInicial = o que o prestador enviou.
/// PosicaoNaFaixa = Abaixo, Dentro ou Acima da faixa de referência da triagem (SemReferencia se indisponível).
/// PropostaPendente = proposta da negociação esperando resposta (null se não houver).
/// ClienteContratouOutro = o pedido já tem contratação ativa com outro prestador (para o prestador entender o "Recusado").
/// </summary>
public record OrcamentoResponse(
    long Id, long SolicitacaoId, long ClienteId, long PrestadorId, string PrestadorNome, string PrestadorEmail,
    decimal Valor, decimal ValorInicial, decimal ReferenciaMinima, decimal ReferenciaMaxima, string PosicaoNaFaixa,
    int PrazoDias, string? Observacao, string Status, DateTime CriadoEm, DateTime AtualizadoEm,
    PropostaResponse? PropostaPendente = null, bool ClienteContratouOutro = false,
    DateTime? ValidoAte = null, bool Expirado = false, bool IncluiMaoDeObra = true, bool IncluiMaterial = false,
    bool IncluiDeslocamento = true, string? Adicionais = null)
{
    public static OrcamentoResponse De(Orcamento o, PropostaNegociacao? pendente = null, bool contratouOutro = false) => new(o.Id, o.SolicitacaoId,
        o.ClienteId, o.PrestadorId, o.PrestadorNome, o.PrestadorEmail, o.Valor, o.ValorInicial, o.ReferenciaMinima,
        o.ReferenciaMaxima, FaixaReferencia.Posicao(o.Valor, o.ReferenciaMinima, o.ReferenciaMaxima), o.PrazoDias,
        o.Observacao, o.Status.ToString(), o.CriadoEm, o.AtualizadoEm,
        pendente is null ? null : PropostaResponse.De(pendente), contratouOutro,
        o.ValidoAte == default ? null : o.ValidoAte, o.Expirado(DateTime.UtcNow), o.IncluiMaoDeObra, o.IncluiMaterial,
        o.IncluiDeslocamento, o.Adicionais);
}

/// <summary>Nova proposta de valor na negociação (cliente ou prestador), com comentário opcional.</summary>
public record PropostaRequest(
    [property: Required(ErrorMessage = "Informe o valor proposto")]
    [property: Range(typeof(decimal), "1", "100000", ErrorMessage = "O valor deve estar entre R$ 1 e R$ 100.000")]
    decimal? Valor,

    [property: StringLength(300, ErrorMessage = "O comentário deve ter até 300 caracteres")]
    string? Comentario);

/// <summary>Resposta da outra parte a uma proposta pendente.</summary>
public record RespostaPropostaRequest(
    [property: Required(ErrorMessage = "Informe se aceita (true) ou recusa (false) a proposta")]
    bool? Aceita);

public record PropostaResponse(
    long Id, long OrcamentoId, string Autor, long AutorId, string AutorNome, decimal Valor, string? Comentario,
    string Status, DateTime CriadaEm, DateTime? RespondidaEm)
{
    public static PropostaResponse De(PropostaNegociacao p) => new(p.Id, p.OrcamentoId, p.Autor.ToString(), p.AutorId,
        p.AutorNome, p.Valor, p.Comentario, p.Status.ToString(), p.CriadaEm, p.RespondidaEm);
}
