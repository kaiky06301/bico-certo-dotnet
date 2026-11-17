using BicoCerto.Contratacoes.Api.Domain;

namespace BicoCerto.Contratacoes.Api.Repositories;

/// <summary>Acesso a dados das propostas de negociação de preço.</summary>
public interface IPropostaRepository
{
    Task<List<PropostaNegociacao>> ListarDoOrcamentoAsync(long orcamentoId, CancellationToken ct);

    /// <summary>Propostas esperando resposta nos orçamentos informados.</summary>
    Task<List<PropostaNegociacao>> ListarPendentesAsync(IReadOnlyCollection<long> orcamentoIds, CancellationToken ct);

    Task<PropostaNegociacao?> ObterAsync(long propostaId, long orcamentoId, CancellationToken ct);

    void Adicionar(PropostaNegociacao proposta);
    Task SalvarAsync(CancellationToken ct);
}
