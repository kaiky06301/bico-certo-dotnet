using System.Security.Claims;
using BicoCerto.Contratacoes.Api.Contracts;
using BicoCerto.Contratacoes.Api.Data;
using BicoCerto.Contratacoes.Api.Domain;
using BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;
using Microsoft.EntityFrameworkCore;

namespace BicoCerto.Contratacoes.Api.Services;

public class ContratacaoService(ContratacoesDbContext db, TimeProvider relogio)
{
    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    /// <summary>O cliente aceita um orçamento: cria a contratação e recusa os outros orçamentos do mesmo pedido.</summary>
    public async Task<ContratacaoResponse> CriarAsync(ClaimsPrincipal usuario, ContratacaoRequest req, CancellationToken ct)
    {
        var orcamento = await db.Orcamentos.FindAsync([req.OrcamentoId!.Value], ct)
                        ?? throw new RecursoNaoEncontradoException($"Orçamento {req.OrcamentoId} não encontrado.");
        if (orcamento.ClienteId != usuario.Id())
            throw new AcessoNegadoException("Só o cliente dono da solicitação pode aceitar este orçamento.");
        orcamento.GarantirPendente("aceitar");

        var jaContratada = await db.Contratacoes.AnyAsync(c => c.SolicitacaoId == orcamento.SolicitacaoId
                                                               && c.Status != StatusContratacao.Cancelada, ct);
        if (jaContratada)
            throw new RegraNegocioException("Esta solicitação já tem uma contratação ativa.");

        var contratacao = new Contratacao(orcamento, ParaUtc(req.DataAgendada!.Value), Agora);
        orcamento.Aceitar();

        var concorrentes = await db.Orcamentos.Where(o => o.SolicitacaoId == orcamento.SolicitacaoId
                                                         && o.Id != orcamento.Id
                                                         && o.Status == StatusOrcamento.Pendente).ToListAsync(ct);
        concorrentes.ForEach(o => o.Recusar());

        db.Contratacoes.Add(contratacao);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new RegraNegocioException("Este orçamento já foi contratado.");
        }
        return ContratacaoResponse.De(contratacao);
    }

    public async Task<PaginaResponse<ContratacaoResponse>> ListarAsync(ClaimsPrincipal usuario, StatusContratacao? status,
        int? pagina, int? tamanho, CancellationToken ct)
    {
        var (p, t) = Paginacao.Normalizar(pagina, tamanho);
        var uid = usuario.Id();
        var consulta = usuario.Papel() switch
        {
            Papeis.Admin => db.Contratacoes,
            Papeis.Prestador => db.Contratacoes.Where(c => c.PrestadorId == uid),
            _ => db.Contratacoes.Where(c => c.ClienteId == uid)
        };
        if (status is not null) consulta = consulta.Where(c => c.Status == status);

        var total = await consulta.LongCountAsync(ct);
        var itens = await consulta.OrderBy(c => c.DataAgendada).ThenBy(c => c.Id).Skip(p * t).Take(t).ToListAsync(ct);
        return PaginaResponse<ContratacaoResponse>.De(itens.Select(ContratacaoResponse.De).ToList(), p, t, total);
    }

    public async Task<ContratacaoResponse> ObterAsync(ClaimsPrincipal usuario, long id, CancellationToken ct) =>
        ContratacaoResponse.De(await ObterVisivelAsync(usuario, id, ct));

    public async Task<ContratacaoResponse> ReagendarAsync(ClaimsPrincipal usuario, long id, ReagendamentoRequest req,
                                                         CancellationToken ct)
    {
        var contratacao = await ObterDeParticipanteAsync(usuario, id, ct);
        contratacao.Reagendar(ParaUtc(req.DataAgendada!.Value), Agora);
        await db.SaveChangesAsync(ct);
        return ContratacaoResponse.De(contratacao);
    }

    /// <summary>Prestador inicia/conclui; qualquer participante pode cancelar.</summary>
    public async Task<ContratacaoResponse> AlterarStatusAsync(ClaimsPrincipal usuario, long id, StatusContratacao novo,
                                                             CancellationToken ct)
    {
        var contratacao = await ObterDeParticipanteAsync(usuario, id, ct);
        if ((novo is StatusContratacao.EmAndamento or StatusContratacao.Concluida)
            && contratacao.PrestadorId != usuario.Id())
            throw new AcessoNegadoException("Só o prestador pode iniciar ou concluir o serviço.");

        contratacao.AlterarStatus(novo, Agora);
        if (novo == StatusContratacao.Cancelada)
            await ReabrirOrcamentosAsync(contratacao.SolicitacaoId, ct);
        await db.SaveChangesAsync(ct);
        return ContratacaoResponse.De(contratacao);
    }

    /// <summary>
    /// Exclui a contratação antes do serviço (Agendada ou Cancelada). Os orçamentos do pedido voltam a ficar
    /// pendentes para o cliente escolher outro prestador.
    /// </summary>
    public async Task ExcluirAsync(ClaimsPrincipal usuario, long id, CancellationToken ct)
    {
        var contratacao = await ObterVisivelAsync(usuario, id, ct);
        if (contratacao.Status is not (StatusContratacao.Agendada or StatusContratacao.Cancelada))
            throw new RegraNegocioException("Só é possível excluir contratações Agendadas ou Canceladas.");

        await ReabrirOrcamentosAsync(contratacao.SolicitacaoId, ct);
        db.Contratacoes.Remove(contratacao);
        await db.SaveChangesAsync(ct);
    }

    private async Task ReabrirOrcamentosAsync(long solicitacaoId, CancellationToken ct)
    {
        var orcamentos = await db.Orcamentos.Where(o => o.SolicitacaoId == solicitacaoId
                                                       && o.Status != StatusOrcamento.Pendente).ToListAsync(ct);
        orcamentos.ForEach(o => o.Reabrir());
    }

    private async Task<Contratacao> ObterVisivelAsync(ClaimsPrincipal usuario, long id, CancellationToken ct)
    {
        var contratacao = await db.Contratacoes.FindAsync([id], ct)
                          ?? throw new RecursoNaoEncontradoException($"Contratação {id} não encontrada.");
        if (!usuario.EhAdmin() && !contratacao.EhParticipante(usuario.Id()))
            throw new AcessoNegadoException("Esta contratação pertence a outros usuários.");
        return contratacao;
    }

    private async Task<Contratacao> ObterDeParticipanteAsync(ClaimsPrincipal usuario, long id, CancellationToken ct)
    {
        var contratacao = await ObterVisivelAsync(usuario, id, ct);
        if (!contratacao.EhParticipante(usuario.Id()))
            throw new AcessoNegadoException("Só o cliente ou o prestador da contratação podem alterá-la.");
        return contratacao;
    }

    private static DateTime ParaUtc(DateTime data) => data.Kind switch
    {
        DateTimeKind.Utc => data,
        DateTimeKind.Local => data.ToUniversalTime(),
        _ => DateTime.SpecifyKind(data, DateTimeKind.Utc)
    };
}
