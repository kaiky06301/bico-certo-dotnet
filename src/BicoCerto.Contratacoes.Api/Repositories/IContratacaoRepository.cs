using BicoCerto.Contratacoes.Api.Domain;

namespace BicoCerto.Contratacoes.Api.Repositories;

/// <summary>Acesso a dados das contratações (orçamento aceito + agendamento).</summary>
public interface IContratacaoRepository
{
    Task<Contratacao?> ObterPorIdAsync(long id, CancellationToken ct);

    /// <summary>O pedido já tem contratação que não foi cancelada?</summary>
    Task<bool> ExisteAtivaNoPedidoAsync(long solicitacaoId, CancellationToken ct);

    /// <summary>Lista paginada; clienteId/prestadorId nulos = sem filtro (admin).</summary>
    Task<(List<Contratacao> Itens, long Total)> ListarAsync(long? clienteId, long? prestadorId,
        StatusContratacao? status, int pagina, int tamanho, CancellationToken ct);

    /// <summary>Contratações ativas (não canceladas) dos pedidos informados: pedido → orçamento contratado.</summary>
    Task<List<(long SolicitacaoId, long OrcamentoId)>> ListarAtivasDosPedidosAsync(
        IReadOnlyCollection<long> solicitacaoIds, CancellationToken ct);

    /// <summary>Datas dos serviços Agendados/Em andamento do prestador (para evitar agenda sobreposta).</summary>
    Task<List<DateTime>> ListarDatasOcupadasDoPrestadorAsync(long prestadorId, long? ignorarId, CancellationToken ct);

    void Adicionar(Contratacao contratacao);
    void Remover(Contratacao contratacao);

    /// <summary>Salva as alterações. Retorna false se o banco recusou por duplicidade (índice único).</summary>
    Task<bool> SalvarAsync(CancellationToken ct);
}
