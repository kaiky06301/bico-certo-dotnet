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
    IOptions<JavaApiOptions> javaOptions)
{
    private JavaApiOptions Java => javaOptions.Value;

    public async Task<OrcamentoResponse> CriarAsync(ClaimsPrincipal usuario, string token, OrcamentoRequest req,
                                                    CancellationToken ct)
    {
        var solicitacaoId = req.SolicitacaoId!.Value;
        var prestadorId = usuario.Id();

        if (await db.Orcamentos.AnyAsync(o => o.SolicitacaoId == solicitacaoId && o.PrestadorId == prestadorId, ct))
            throw new RegraNegocioException("Você já enviou um orçamento para esta solicitação. Edite o existente.");

        var clienteId = await ValidarSolicitacaoAsync(solicitacaoId, req.Valor!.Value, req.ClienteId, token, ct);

        var orcamento = new Orcamento(solicitacaoId, clienteId, prestadorId, usuario.Nome(), usuario.Email(), req.Valor.Value,
            req.PrazoDias!.Value, req.Observacao?.Trim());
        db.Orcamentos.Add(orcamento);
        await db.SaveChangesAsync(ct);
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
        var respostas = itens.Select(o => OrcamentoResponse.De(o, pendentes.GetValueOrDefault(o.Id))).ToList();
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
        await ValidarSolicitacaoAsync(orcamento.SolicitacaoId, req.Valor!.Value, orcamento.ClienteId, token, ct);
        orcamento.Atualizar(req.Valor.Value, req.PrazoDias!.Value, req.Observacao?.Trim());
        // o prestador mudou o valor base: a proposta que estava em aberto deixa de valer
        var abertas = await db.Propostas.Where(p => p.OrcamentoId == id && p.Status == StatusProposta.Pendente)
            .ToListAsync(ct);
        abertas.ForEach(p => p.Substituir(DateTime.UtcNow));
        await db.SaveChangesAsync(ct);
        return OrcamentoResponse.De(orcamento);
    }

    /// <summary>O prestador retira a proposta enquanto ela ainda não foi aceita.</summary>
    public async Task ExcluirAsync(ClaimsPrincipal usuario, long id, CancellationToken ct)
    {
        var orcamento = await ObterDoPrestadorAsync(usuario, id, ct);
        orcamento.GarantirPendente("excluir");
        db.Orcamentos.Remove(orcamento);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Com a integração ligada, confere na API Java se o pedido está ABERTO e se o valor respeita o preço justo
    /// calculado pela triagem (com uma tolerância). Devolve o id do cliente dono do pedido.
    /// </summary>
    internal async Task<long> ValidarSolicitacaoAsync(long solicitacaoId, decimal valor, long? clienteInformado,
                                                     string token, CancellationToken ct)
    {
        if (!Java.Habilitada)
        {
            return clienteInformado ?? throw new RegraNegocioException(
                "Integração com a API Java desligada: informe o clienteId da solicitação.");
        }

        var solicitacao = await javaClient.ObterAsync(solicitacaoId, token, ct);
        if (!string.Equals(solicitacao.Status, "ABERTA", StringComparison.OrdinalIgnoreCase))
            throw new RegraNegocioException($"A solicitação {solicitacaoId} não está aberta para orçamentos.");

        RegrasPrecoJusto.Validar(valor, solicitacao.PrecoMaximo, Java.ToleranciaPrecoJusto);
        return solicitacao.Cliente.Id;
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

/// <summary>Protege o cidadão: orçamento muito acima do preço justo calculado pela triagem é recusado.</summary>
public static class RegrasPrecoJusto
{
    public static void Validar(decimal valor, decimal precoMaximoJusto, decimal tolerancia)
    {
        if (precoMaximoJusto <= 0) return;
        var limite = Math.Round(precoMaximoJusto * (1 + tolerancia), 2);
        if (valor > limite)
            throw new RegraNegocioException(
                $"O valor R$ {valor:N2} está acima do preço justo para este serviço (máximo aceito: R$ {limite:N2}).");
    }
}
