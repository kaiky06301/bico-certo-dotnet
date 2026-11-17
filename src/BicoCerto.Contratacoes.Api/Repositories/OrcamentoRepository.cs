using BicoCerto.Contratacoes.Api.Data;
using BicoCerto.Contratacoes.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BicoCerto.Contratacoes.Api.Repositories;

public class OrcamentoRepository(ContratacoesDbContext db) : IOrcamentoRepository
{
    public async Task<Orcamento?> ObterPorIdAsync(long id, CancellationToken ct) =>
        await db.Orcamentos.FindAsync([id], ct);

    public Task<bool> ExisteDoPrestadorAsync(long solicitacaoId, long prestadorId, CancellationToken ct) =>
        db.Orcamentos.AnyAsync(o => o.SolicitacaoId == solicitacaoId && o.PrestadorId == prestadorId, ct);

    public async Task<(List<Orcamento> Itens, long Total)> ListarAsync(long? clienteId, long? prestadorId,
        long? solicitacaoId, int pagina, int tamanho, CancellationToken ct)
    {
        IQueryable<Orcamento> consulta = db.Orcamentos;
        if (clienteId is not null) consulta = consulta.Where(o => o.ClienteId == clienteId);
        if (prestadorId is not null) consulta = consulta.Where(o => o.PrestadorId == prestadorId);
        if (solicitacaoId is not null) consulta = consulta.Where(o => o.SolicitacaoId == solicitacaoId);

        var total = await consulta.LongCountAsync(ct);
        var itens = await consulta.OrderByDescending(o => o.CriadoEm).ThenByDescending(o => o.Id)
            .Skip(pagina * tamanho).Take(tamanho).ToListAsync(ct);
        return (itens, total);
    }

    public Task<List<Orcamento>> ListarPendentesDoPedidoAsync(long solicitacaoId, long ignorarId, CancellationToken ct) =>
        db.Orcamentos.Where(o => o.SolicitacaoId == solicitacaoId && o.Id != ignorarId
                                 && o.Status == StatusOrcamento.Pendente).ToListAsync(ct);

    public Task<List<Orcamento>> ListarNaoPendentesDoPedidoAsync(long solicitacaoId, CancellationToken ct) =>
        db.Orcamentos.Where(o => o.SolicitacaoId == solicitacaoId && o.Status != StatusOrcamento.Pendente)
            .ToListAsync(ct);

    public void Adicionar(Orcamento orcamento) => db.Orcamentos.Add(orcamento);

    public void Remover(Orcamento orcamento) => db.Orcamentos.Remove(orcamento);

    public Task SalvarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
