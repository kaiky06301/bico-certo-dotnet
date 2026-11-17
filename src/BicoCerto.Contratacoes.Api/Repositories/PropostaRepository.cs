using BicoCerto.Contratacoes.Api.Data;
using BicoCerto.Contratacoes.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BicoCerto.Contratacoes.Api.Repositories;

public class PropostaRepository(ContratacoesDbContext db) : IPropostaRepository
{
    public Task<List<PropostaNegociacao>> ListarDoOrcamentoAsync(long orcamentoId, CancellationToken ct) =>
        db.Propostas.Where(p => p.OrcamentoId == orcamentoId)
            .OrderBy(p => p.CriadaEm).ThenBy(p => p.Id).ToListAsync(ct);

    public Task<List<PropostaNegociacao>> ListarPendentesAsync(IReadOnlyCollection<long> orcamentoIds,
                                                              CancellationToken ct) =>
        db.Propostas.Where(p => orcamentoIds.Contains(p.OrcamentoId) && p.Status == StatusProposta.Pendente)
            .ToListAsync(ct);

    public Task<PropostaNegociacao?> ObterAsync(long propostaId, long orcamentoId, CancellationToken ct) =>
        db.Propostas.FirstOrDefaultAsync(p => p.Id == propostaId && p.OrcamentoId == orcamentoId, ct);

    public void Adicionar(PropostaNegociacao proposta) => db.Propostas.Add(proposta);

    public Task SalvarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
