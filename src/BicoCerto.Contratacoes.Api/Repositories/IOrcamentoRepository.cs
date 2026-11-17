using BicoCerto.Contratacoes.Api.Domain;

namespace BicoCerto.Contratacoes.Api.Repositories;

/// <summary>Acesso a dados dos orçamentos (Repository Pattern: os Services não conhecem o DbContext).</summary>
public interface IOrcamentoRepository
{
    Task<Orcamento?> ObterPorIdAsync(long id, CancellationToken ct);

    /// <summary>O prestador já mandou orçamento para este pedido?</summary>
    Task<bool> ExisteDoPrestadorAsync(long solicitacaoId, long prestadorId, CancellationToken ct);

    /// <summary>Lista paginada; clienteId/prestadorId nulos = sem filtro (admin).</summary>
    Task<(List<Orcamento> Itens, long Total)> ListarAsync(long? clienteId, long? prestadorId, long? solicitacaoId,
        int pagina, int tamanho, CancellationToken ct);

    /// <summary>Outros orçamentos ainda pendentes do mesmo pedido.</summary>
    Task<List<Orcamento>> ListarPendentesDoPedidoAsync(long solicitacaoId, long ignorarId, CancellationToken ct);

    /// <summary>Orçamentos do pedido que já saíram de Pendente (aceitos/recusados).</summary>
    Task<List<Orcamento>> ListarNaoPendentesDoPedidoAsync(long solicitacaoId, CancellationToken ct);

    void Adicionar(Orcamento orcamento);
    void Remover(Orcamento orcamento);
    Task SalvarAsync(CancellationToken ct);
}
