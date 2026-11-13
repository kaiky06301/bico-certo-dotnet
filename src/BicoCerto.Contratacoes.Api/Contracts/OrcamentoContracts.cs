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
    long? ClienteId);

public record OrcamentoUpdateRequest(
    [property: Required(ErrorMessage = "Informe o valor")]
    [property: Range(typeof(decimal), "1", "100000", ErrorMessage = "O valor deve estar entre R$ 1 e R$ 100.000")]
    decimal? Valor,

    [property: Required(ErrorMessage = "Informe o prazo em dias")]
    [property: Range(1, 60, ErrorMessage = "O prazo deve ser de 1 a 60 dias")]
    int? PrazoDias,

    [property: StringLength(500, ErrorMessage = "A observação deve ter até 500 caracteres")]
    string? Observacao);

public record OrcamentoResponse(
    long Id, long SolicitacaoId, long ClienteId, long PrestadorId, string PrestadorEmail,
    decimal Valor, int PrazoDias, string? Observacao, string Status, DateTime CriadoEm, DateTime AtualizadoEm)
{
    public static OrcamentoResponse De(Orcamento o) => new(o.Id, o.SolicitacaoId, o.ClienteId, o.PrestadorId,
        o.PrestadorEmail, o.Valor, o.PrazoDias, o.Observacao, o.Status.ToString(), o.CriadoEm, o.AtualizadoEm);
}
