using System.ComponentModel.DataAnnotations;
using BicoCerto.Contratacoes.Api.Domain;

namespace BicoCerto.Contratacoes.Api.Contracts;

/// <summary>O cliente aceita um orçamento e marca a data do serviço.</summary>
public record ContratacaoRequest(
    [property: Required(ErrorMessage = "Informe o orçamento")]
    [property: Range(1, long.MaxValue, ErrorMessage = "Orçamento inválido")]
    long? OrcamentoId,

    [property: Required(ErrorMessage = "Informe a data agendada")]
    DateTime? DataAgendada);

public record ReagendamentoRequest(
    [property: Required(ErrorMessage = "Informe a nova data")]
    DateTime? DataAgendada);

public record StatusContratacaoRequest(
    [property: Required(ErrorMessage = "Informe o status: EmAndamento, Concluida ou Cancelada")]
    StatusContratacao? Status);

public record ContratacaoResponse(
    long Id, long OrcamentoId, long SolicitacaoId, long ClienteId, long PrestadorId,
    decimal ValorAcordado, DateTime DataAgendada, string Status, DateTime CriadaEm, DateTime AtualizadaEm)
{
    public static ContratacaoResponse De(Contratacao c) => new(c.Id, c.OrcamentoId, c.SolicitacaoId, c.ClienteId,
        c.PrestadorId, c.ValorAcordado, c.DataAgendada, c.Status.ToString(), c.CriadaEm, c.AtualizadaEm);
}
