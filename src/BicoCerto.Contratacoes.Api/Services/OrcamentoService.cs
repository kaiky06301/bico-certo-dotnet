using System.Security.Claims;
using BicoCerto.Contratacoes.Api.Contracts;
using BicoCerto.Contratacoes.Api.Data;
using BicoCerto.Contratacoes.Api.Domain;
using BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;
using BicoCerto.Contratacoes.Api.Infrastructure.Integracao;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BicoCerto.Contratacoes.Api.Services;

public class OrcamentoService(
    ContratacoesDbContext db,
    ISolicitacaoJavaClient javaClient,
    IOptions<JavaApiOptions> javaOptions,
    AvisoService avisos)
{
    private JavaApiOptions Java => javaOptions.Value;

    public async Task<OrcamentoResponse> CriarAsync(ClaimsPrincipal usuario, string token, OrcamentoRequest req,
                                                    CancellationToken ct)
    {
        var solicitacaoId = req.SolicitacaoId!.Value;
        var prestadorId = usuario.Id();

        if (await db.Orcamentos.AnyAsync(o => o.SolicitacaoId == solicitacaoId && o.PrestadorId == prestadorId, ct))
            throw new RegraNegocioException("Você já enviou um orçamento para esta solicitação. Edite o existente.");

        var solicitacao = await ValidarSolicitacaoAsync(solicitacaoId, req.Valor!.Value, req.Observacao,
            "na observação", req.ClienteId, token, ct);

        var orcamento = new Orcamento(solicitacaoId, solicitacao.ClienteId, prestadorId, usuario.Nome(), usuario.Email(),
            req.Valor.Value, req.PrazoDias!.Value, req.Observacao?.Trim());
        orcamento.DefinirFaixaReferencia(solicitacao.ReferenciaMinima, solicitacao.ReferenciaMaxima);
        orcamento.DefinirCondicoes(req.ValidadeDias, req.IncluiMaoDeObra, req.IncluiMaterial, req.IncluiDeslocamento,
            req.Adicionais, DateTime.UtcNow);
        db.Orcamentos.Add(orcamento);
        await db.SaveChangesAsync(ct);
        await avisos.AvisarAsync(solicitacaoId, orcamento.ClienteId, TiposAviso.Orcamento,
            $"{orcamento.PrestadorNome} enviou um orçamento de {AvisoService.Moeda(orcamento.Valor)} para o seu pedido"
            + (solicitacao.Categoria is null ? "." : $" de {solicitacao.Categoria.ToLowerInvariant()}."),
            orcamento.Valor, orcamento.Id);
        return OrcamentoResponse.De(orcamento);
    }

    public async Task<PaginaResponse<OrcamentoResponse>> ListarAsync(ClaimsPrincipal usuario, long? solicitacaoId,
        int? pagina, int? tamanho, CancellationToken ct)
    {
        var (p, t) = Paginacao.Normalizar(pagina, tamanho);
        var consulta = DoUsuario(usuario);
        if (solicitacaoId is not null) consulta = consulta.Where(o => o.SolicitacaoId == solicitacaoId);

        var total = await consulta.LongCountAsync(ct);
        var itens = await consulta.OrderByDescending(o => o.CriadoEm).ThenByDescending(o => o.Id)
            .Skip(p * t).Take(t).ToListAsync(ct);
        var pendentes = await PropostasPendentesAsync(itens.Select(o => o.Id).ToList(), ct);
        var pedidos = itens.Select(o => o.SolicitacaoId).Distinct().ToList();
        var contratados = await db.Contratacoes
            .Where(c => pedidos.Contains(c.SolicitacaoId) && c.Status != StatusContratacao.Cancelada)
            .Select(c => new { c.SolicitacaoId, c.OrcamentoId }).ToListAsync(ct);
        var respostas = itens.Select(o => OrcamentoResponse.De(o, pendentes.GetValueOrDefault(o.Id),
            contratados.Any(c => c.SolicitacaoId == o.SolicitacaoId && c.OrcamentoId != o.Id))).ToList();
        return PaginaResponse<OrcamentoResponse>.De(respostas, p, t, total);
    }

    public async Task<OrcamentoResponse> ObterAsync(ClaimsPrincipal usuario, long id, CancellationToken ct)
    {
        var orcamento = await ObterVisivelAsync(usuario, id, ct);
        var pendentes = await PropostasPendentesAsync([orcamento.Id], ct);
        return OrcamentoResponse.De(orcamento, pendentes.GetValueOrDefault(orcamento.Id));
    }

    /// <summary>Proposta da negociação esperando resposta, por orçamento (no máximo uma por orçamento).</summary>
    internal async Task<Dictionary<long, PropostaNegociacao>> PropostasPendentesAsync(IReadOnlyCollection<long> ids,
                                                                                     CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var pendentes = await db.Propostas
            .Where(p => ids.Contains(p.OrcamentoId) && p.Status == StatusProposta.Pendente)
            .ToListAsync(ct);
        return pendentes.GroupBy(p => p.OrcamentoId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CriadaEm).ThenByDescending(p => p.Id).First());
    }

    public async Task<OrcamentoResponse> AtualizarAsync(ClaimsPrincipal usuario, string token, long id,
                                                        OrcamentoUpdateRequest req, CancellationToken ct)
    {
        var orcamento = await ObterDoPrestadorAsync(usuario, id, ct);
        orcamento.GarantirPendente("editar");
        await ValidarSolicitacaoAsync(orcamento.SolicitacaoId, req.Valor!.Value, req.Observacao, "na observação",
            orcamento.ClienteId, token, ct);
        orcamento.Atualizar(req.Valor.Value, req.PrazoDias!.Value, req.Observacao?.Trim());
        orcamento.DefinirCondicoes(req.ValidadeDias, req.IncluiMaoDeObra, req.IncluiMaterial, req.IncluiDeslocamento,
            req.Adicionais, DateTime.UtcNow);
        // o prestador mudou o valor base: a proposta que estava em aberto deixa de valer
        var abertas = await db.Propostas.Where(p => p.OrcamentoId == id && p.Status == StatusProposta.Pendente)
            .ToListAsync(ct);
        abertas.ForEach(p => p.Substituir(DateTime.UtcNow));
        await db.SaveChangesAsync(ct);
        await avisos.AvisarAsync(orcamento.SolicitacaoId, orcamento.ClienteId, TiposAviso.Orcamento,
            $"{orcamento.PrestadorNome} atualizou o orçamento para {AvisoService.Moeda(orcamento.Valor)}.",
            orcamento.Valor, orcamento.Id);
        return OrcamentoResponse.De(orcamento);
    }

    /// <summary>O cliente dono do pedido recusa um orçamento pendente (a negociação em aberto deixa de valer).</summary>
    public async Task<OrcamentoResponse> RecusarAsync(ClaimsPrincipal usuario, long id, CancellationToken ct)
    {
        var orcamento = await db.Orcamentos.FindAsync([id], ct)
                        ?? throw new RecursoNaoEncontradoException($"Orçamento {id} não encontrado.");
        if (orcamento.ClienteId != usuario.Id())
            throw new AcessoNegadoException("Só o cliente dono do pedido pode recusar este orçamento.");
        orcamento.GarantirPendente("recusar");
        orcamento.Recusar();
        var abertas = await db.Propostas.Where(p => p.OrcamentoId == id && p.Status == StatusProposta.Pendente)
            .ToListAsync(ct);
        abertas.ForEach(p => p.Substituir(DateTime.UtcNow));
        await db.SaveChangesAsync(ct);
        await avisos.AvisarAsync(orcamento.SolicitacaoId, orcamento.PrestadorId, TiposAviso.Orcamento,
            $"O cliente recusou o seu orçamento de {AvisoService.Moeda(orcamento.Valor)} para o pedido #{orcamento.SolicitacaoId}.",
            orcamento.Valor, orcamento.Id);
        return OrcamentoResponse.De(orcamento);
    }

    /// <summary>O prestador retira a proposta enquanto ela ainda não foi aceita.</summary>
    public async Task ExcluirAsync(ClaimsPrincipal usuario, long id, CancellationToken ct)
    {
        var orcamento = await ObterDoPrestadorAsync(usuario, id, ct);
        orcamento.GarantirPendente("excluir");
        db.Orcamentos.Remove(orcamento);
        await db.SaveChangesAsync(ct);
        await avisos.AvisarAsync(orcamento.SolicitacaoId, orcamento.ClienteId, TiposAviso.Orcamento,
            $"{orcamento.PrestadorNome} retirou o orçamento de {AvisoService.Moeda(orcamento.Valor)} do seu pedido.",
            orcamento.Valor);
    }

    /// <summary>
    /// Com a integração ligada, confere na API Java se o pedido está ABERTO e busca a faixa de referência de preço
    /// calculada pela triagem. Valor acima da faixa é permitido, mas precisa de justificativa.
    /// </summary>
    internal async Task<SolicitacaoValidada> ValidarSolicitacaoAsync(long solicitacaoId, decimal valor,
        string? justificativa, string ondeExplicar, long? clienteInformado, string token, CancellationToken ct)
    {
        if (!Java.Habilitada)
        {
            var cliente = clienteInformado ?? throw new RegraNegocioException(
                "Integração com a API Java desligada: informe o clienteId da solicitação.");
            return new SolicitacaoValidada(cliente, 0m, 0m, null);
        }

        var solicitacao = await javaClient.ObterAsync(solicitacaoId, token, ct);
        if (!string.Equals(solicitacao.Status, "ABERTA", StringComparison.OrdinalIgnoreCase))
            throw new RegraNegocioException($"A solicitação {solicitacaoId} não está aberta para orçamentos.");

        FaixaReferencia.ExigirJustificativaSeAcima(valor, solicitacao.PrecoMaximo, justificativa, ondeExplicar);
        return new SolicitacaoValidada(solicitacao.Cliente.Id, solicitacao.PrecoMinimo, solicitacao.PrecoMaximo,
            solicitacao.Categoria?.Nome);
    }

    private IQueryable<Orcamento> DoUsuario(ClaimsPrincipal usuario)
    {
        var id = usuario.Id();
        return usuario.Papel() switch
        {
            Papeis.Admin => db.Orcamentos,
            Papeis.Prestador => db.Orcamentos.Where(o => o.PrestadorId == id),
            _ => db.Orcamentos.Where(o => o.ClienteId == id)
        };
    }

    internal async Task<Orcamento> ObterVisivelAsync(ClaimsPrincipal usuario, long id, CancellationToken ct)
    {
        var orcamento = await db.Orcamentos.FindAsync([id], ct)
                        ?? throw new RecursoNaoEncontradoException($"Orçamento {id} não encontrado.");
        var uid = usuario.Id();
        if (!usuario.EhAdmin() && orcamento.ClienteId != uid && orcamento.PrestadorId != uid)
            throw new AcessoNegadoException("Este orçamento pertence a outro usuário.");
        return orcamento;
    }

    private async Task<Orcamento> ObterDoPrestadorAsync(ClaimsPrincipal usuario, long id, CancellationToken ct)
    {
        var orcamento = await db.Orcamentos.FindAsync([id], ct)
                        ?? throw new RecursoNaoEncontradoException($"Orçamento {id} não encontrado.");
        if (orcamento.PrestadorId != usuario.Id())
            throw new AcessoNegadoException("Só o prestador que enviou o orçamento pode alterá-lo.");
        return orcamento;
    }
}

public record SolicitacaoValidada(long ClienteId, decimal ReferenciaMinima, decimal ReferenciaMaxima,
                                  string? Categoria);
