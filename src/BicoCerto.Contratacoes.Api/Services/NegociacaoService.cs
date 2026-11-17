using System.Security.Claims;
using BicoCerto.Contratacoes.Api.Contracts;
using BicoCerto.Contratacoes.Api.Data;
using BicoCerto.Contratacoes.Api.Domain;
using BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;
using Microsoft.EntityFrameworkCore;

namespace BicoCerto.Contratacoes.Api.Services;

/// <summary>
/// Negociação de preço de um orçamento entre o cliente e o prestador.
/// Regras: só os dois participantes negociam; uma nova proposta substitui a que estava em aberto (contraproposta);
/// só quem recebeu a proposta responde; aceitar muda o valor vigente; recusar ou não responder mantém o valor atual;
/// acima da faixa de referência de preço da triagem a proposta é aceita, mas o comentário (justificativa) é obrigatório.
/// </summary>
public class NegociacaoService(ContratacoesDbContext db, OrcamentoService orcamentos, TimeProvider relogio,
    AvisoService avisos)
{
    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    public async Task<List<PropostaResponse>> ListarAsync(ClaimsPrincipal usuario, long orcamentoId, CancellationToken ct)
    {
        await orcamentos.ObterVisivelAsync(usuario, orcamentoId, ct);
        var propostas = await db.Propostas.Where(p => p.OrcamentoId == orcamentoId)
            .OrderBy(p => p.CriadaEm).ThenBy(p => p.Id).ToListAsync(ct);
        return propostas.Select(PropostaResponse.De).ToList();
    }

    public async Task<PropostaResponse> ProporAsync(ClaimsPrincipal usuario, string token, long orcamentoId,
                                                   PropostaRequest req, CancellationToken ct)
    {
        var orcamento = await orcamentos.ObterVisivelAsync(usuario, orcamentoId, ct);
        var autor = PapelNaNegociacao(usuario, orcamento);
        orcamento.GarantirPendente("negociar");
        orcamento.GarantirDentroDaValidade(Agora);

        var valor = req.Valor!.Value;
        await orcamentos.ValidarSolicitacaoAsync(orcamento.SolicitacaoId, valor, req.Comentario, "no comentário",
            orcamento.ClienteId, token, ct);

        var proposta = new PropostaNegociacao(orcamento, autor, usuario.Id(), usuario.Nome(), valor, req.Comentario, Agora);

        // contraproposta: a proposta que estava esperando resposta deixa de valer
        var abertas = await db.Propostas.Where(p => p.OrcamentoId == orcamentoId && p.Status == StatusProposta.Pendente)
            .ToListAsync(ct);
        abertas.ForEach(p => p.Substituir(Agora));

        db.Propostas.Add(proposta);
        await db.SaveChangesAsync(ct);
        var paraQuem = autor == AutorProposta.Cliente ? orcamento.PrestadorId : orcamento.ClienteId;
        var quem = autor == AutorProposta.Cliente ? "O cliente" : orcamento.PrestadorNome;
        await avisos.AvisarAsync(orcamento.SolicitacaoId, paraQuem, TiposAviso.Negociacao,
            $"{quem} propôs {AvisoService.Moeda(valor)} no pedido #{orcamento.SolicitacaoId}"
            + (string.IsNullOrWhiteSpace(req.Comentario) ? "." : $": \"{req.Comentario.Trim()}\""),
            valor, orcamento.Id);
        return PropostaResponse.De(proposta);
    }

    public async Task<PropostaResponse> ResponderAsync(ClaimsPrincipal usuario, long orcamentoId, long propostaId,
                                                      bool aceita, CancellationToken ct)
    {
        var orcamento = await orcamentos.ObterVisivelAsync(usuario, orcamentoId, ct);
        var quemResponde = PapelNaNegociacao(usuario, orcamento);

        var proposta = await db.Propostas.FirstOrDefaultAsync(p => p.Id == propostaId && p.OrcamentoId == orcamentoId, ct)
                       ?? throw new RecursoNaoEncontradoException(
                           $"Proposta {propostaId} não encontrada no orçamento {orcamentoId}.");

        if (aceita) orcamento.GarantirDentroDaValidade(Agora);
        proposta.Responder(aceita, quemResponde, orcamento, Agora);
        await db.SaveChangesAsync(ct);
        var quem = quemResponde == AutorProposta.Cliente ? "O cliente" : orcamento.PrestadorNome;
        await avisos.AvisarAsync(orcamento.SolicitacaoId, proposta.AutorId, TiposAviso.Negociacao,
            aceita
                ? $"{quem} aceitou a sua proposta de {AvisoService.Moeda(proposta.Valor)}. Esse passa a ser o valor do orçamento."
                : $"{quem} recusou a sua proposta de {AvisoService.Moeda(proposta.Valor)}. Continua valendo {AvisoService.Moeda(orcamento.Valor)}.",
            proposta.Valor, orcamento.Id);
        return PropostaResponse.De(proposta);
    }

    /// <summary>Só o cliente dono do pedido e o prestador do orçamento negociam (nem o admin entra na conversa).</summary>
    private static AutorProposta PapelNaNegociacao(ClaimsPrincipal usuario, Orcamento orcamento)
    {
        var uid = usuario.Id();
        if (uid == orcamento.ClienteId) return AutorProposta.Cliente;
        if (uid == orcamento.PrestadorId) return AutorProposta.Prestador;
        throw new AcessoNegadoException("Só o cliente e o prestador deste orçamento podem negociar o valor.");
    }
}
