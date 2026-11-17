using BicoCerto.Contratacoes.Api.Data;
using BicoCerto.Contratacoes.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BicoCerto.Contratacoes.Api.Repositories;

public class ContratacaoRepository(ContratacoesDbContext db) : IContratacaoRepository
{
    public async Task<Contratacao?> ObterPorIdAsync(long id, CancellationToken ct) =>
        await db.Contratacoes.FindAsync([id], ct);

    public Task<bool> ExisteAtivaNoPedidoAsync(long solicitacaoId, CancellationToken ct) =>
        db.Contratacoes.AnyAsync(c => c.SolicitacaoId == solicitacaoId && c.Status != StatusContratacao.Cancelada, ct);

    public async Task<(List<Contratacao> Itens, long Total)> ListarAsync(long? clienteId, long? prestadorId,
        StatusContratacao? status, int pagina, int tamanho, CancellationToken ct)
    {
        IQueryable<Contratacao> consulta = db.Contratacoes;
        if (clienteId is not null) consulta = consulta.Where(c => c.ClienteId == clienteId);
        if (prestadorId is not null) consulta = consulta.Where(c => c.PrestadorId == prestadorId);
        if (status is not null) consulta = consulta.Where(c => c.Status == status);

        var total = await consulta.LongCountAsync(ct);
        var itens = await consulta.OrderBy(c => c.DataAgendada).ThenBy(c => c.Id)
            .Skip(pagina * tamanho).Take(tamanho).ToListAsync(ct);
        return (itens, total);
    }

    public async Task<List<(long SolicitacaoId, long OrcamentoId)>> ListarAtivasDosPedidosAsync(
        IReadOnlyCollection<long> solicitacaoIds, CancellationToken ct)
    {
        var ativas = await db.Contratacoes
            .Where(c => solicitacaoIds.Contains(c.SolicitacaoId) && c.Status != StatusContratacao.Cancelada)
            .Select(c => new { c.SolicitacaoId, c.OrcamentoId }).ToListAsync(ct);
        return ativas.Select(c => (c.SolicitacaoId, c.OrcamentoId)).ToList();
    }

    public Task<List<DateTime>> ListarDatasOcupadasDoPrestadorAsync(long prestadorId, long? ignorarId,
                                                                    CancellationToken ct) =>
        db.Contratacoes
            .Where(c => c.PrestadorId == prestadorId && c.Id != (ignorarId ?? 0)
                        && (c.Status == StatusContratacao.Agendada || c.Status == StatusContratacao.EmAndamento))
            .Select(c => c.DataAgendada).ToListAsync(ct);

    public void Adicionar(Contratacao contratacao) => db.Contratacoes.Add(contratacao);

    public void Remover(Contratacao contratacao) => db.Contratacoes.Remove(contratacao);

    public async Task<bool> SalvarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}
