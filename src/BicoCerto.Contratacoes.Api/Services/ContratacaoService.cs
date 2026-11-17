using System.Security.Claims;
using BicoCerto.Contratacoes.Api.Contracts;
using BicoCerto.Contratacoes.Api.Domain;
using BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;
using BicoCerto.Contratacoes.Api.Repositories;

namespace BicoCerto.Contratacoes.Api.Services;

public class ContratacaoService(
    IContratacaoRepository contratacoes,
    IOrcamentoRepository orcamentos,
    IPropostaRepository propostas,
    TimeProvider relogio,
    AvisoService avisos)
{
    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    /// <summary>O cliente aceita um orçamento: cria a contratação e recusa os outros orçamentos do mesmo pedido.</summary>
    public async Task<ContratacaoResponse> CriarAsync(ClaimsPrincipal usuario, ContratacaoRequest req, CancellationToken ct)
    {
        var orcamento = await orcamentos.ObterPorIdAsync(req.OrcamentoId!.Value, ct)
                        ?? throw new RecursoNaoEncontradoException($"Orçamento {req.OrcamentoId} não encontrado.");
        if (orcamento.ClienteId != usuario.Id())
            throw new AcessoNegadoException("Só o cliente dono da solicitação pode aceitar este orçamento.");
        orcamento.GarantirPendente("aceitar");
        orcamento.GarantirDentroDaValidade(Agora);

        if (await contratacoes.ExisteAtivaNoPedidoAsync(orcamento.SolicitacaoId, ct))
            throw new RegraNegocioException("Esta solicitação já tem uma contratação ativa.");

        await GarantirHorarioLivreAsync(orcamento.PrestadorId, ParaUtc(req.DataAgendada!.Value), null, ct);
        var contratacao = new Contratacao(orcamento, ParaUtc(req.DataAgendada!.Value), Agora);
        orcamento.Aceitar();

        var concorrentes = await orcamentos.ListarPendentesDoPedidoAsync(orcamento.SolicitacaoId, orcamento.Id, ct);
        concorrentes.ForEach(o => o.Recusar());

        // negociações que ficaram sem resposta no pedido são encerradas (vale o valor vigente do orçamento aceito)
        var idsDoPedido = concorrentes.Select(o => o.Id).Append(orcamento.Id).ToList();
        var propostasAbertas = await propostas.ListarPendentesAsync(idsDoPedido, ct);
        propostasAbertas.ForEach(p => p.Substituir(Agora));

        contratacoes.Adicionar(contratacao);
        if (!await contratacoes.SalvarAsync(ct))
            throw new RegraNegocioException("Este orçamento já foi contratado.");
        await avisos.AvisarAsync(contratacao.SolicitacaoId, contratacao.PrestadorId, TiposAviso.Contratacao,
            $"{usuario.Nome()} aceitou o seu orçamento de {AvisoService.Moeda(contratacao.ValorAcordado)}. "
            + $"Serviço agendado para {AvisoService.DataHora(contratacao.DataAgendada)}.",
            contratacao.ValorAcordado, orcamento.Id);
        foreach (var outro in concorrentes)
            await avisos.AvisarAsync(outro.SolicitacaoId, outro.PrestadorId, TiposAviso.Orcamento,
                $"O cliente escolheu outro prestador para o pedido #{outro.SolicitacaoId}. Obrigado pelo orçamento!",
                outro.Valor, outro.Id);
        return ContratacaoResponse.De(contratacao);
    }

    public async Task<PaginaResponse<ContratacaoResponse>> ListarAsync(ClaimsPrincipal usuario, StatusContratacao? status,
        int? pagina, int? tamanho, CancellationToken ct)
    {
        var (p, t) = Paginacao.Normalizar(pagina, tamanho);
        var uid = usuario.Id();
        var (clienteId, prestadorId) = usuario.Papel() switch
        {
            Papeis.Admin => ((long?)null, (long?)null),
            Papeis.Prestador => (null, uid),
            _ => (uid, (long?)null)
        };
        var (itens, total) = await contratacoes.ListarAsync(clienteId, prestadorId, status, p, t, ct);
        return PaginaResponse<ContratacaoResponse>.De(itens.Select(ContratacaoResponse.De).ToList(), p, t, total);
    }

    public async Task<ContratacaoResponse> ObterAsync(ClaimsPrincipal usuario, long id, CancellationToken ct) =>
        ContratacaoResponse.De(await ObterVisivelAsync(usuario, id, ct));

    public async Task<ContratacaoResponse> ReagendarAsync(ClaimsPrincipal usuario, long id, ReagendamentoRequest req,
                                                         CancellationToken ct)
    {
        var contratacao = await ObterDeParticipanteAsync(usuario, id, ct);
        await GarantirHorarioLivreAsync(contratacao.PrestadorId, ParaUtc(req.DataAgendada!.Value), contratacao.Id, ct);
        contratacao.Reagendar(ParaUtc(req.DataAgendada!.Value), Agora);
        await contratacoes.SalvarAsync(ct);
        var outraParte = usuario.Id() == contratacao.ClienteId ? contratacao.PrestadorId : contratacao.ClienteId;
        await avisos.AvisarAsync(contratacao.SolicitacaoId, outraParte, TiposAviso.Agendamento,
            $"O serviço do pedido #{contratacao.SolicitacaoId} foi reagendado para {AvisoService.DataHora(contratacao.DataAgendada)}.");
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
        await contratacoes.SalvarAsync(ct);
        // conclusão e cancelamento pelo cliente já geram aviso na API Java (eventos do pedido)
        if (novo == StatusContratacao.EmAndamento)
            await avisos.AvisarAsync(contratacao.SolicitacaoId, contratacao.ClienteId, TiposAviso.Servico,
                $"{usuario.Nome()} iniciou o serviço do pedido #{contratacao.SolicitacaoId}.");
        else if (novo == StatusContratacao.Cancelada && usuario.Id() == contratacao.PrestadorId)
            await avisos.AvisarAsync(contratacao.SolicitacaoId, contratacao.ClienteId, TiposAviso.Cancelamento,
                $"{usuario.Nome()} cancelou o atendimento do pedido #{contratacao.SolicitacaoId}. "
                + "Os outros orçamentos voltaram a ficar disponíveis.");
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
        contratacoes.Remover(contratacao);
        await contratacoes.SalvarAsync(ct);
    }

    /// <summary>Intervalo mínimo entre dois serviços do mesmo prestador (deslocamento + execução).</summary>
    public static readonly TimeSpan IntervaloMinimo = TimeSpan.FromHours(2);

    /// <summary>Evita agenda sobreposta: o prestador não pode ter dois serviços ativos a menos de 2 horas.</summary>
    private async Task GarantirHorarioLivreAsync(long prestadorId, DateTime data, long? ignorar, CancellationToken ct)
    {
        var ativos = await contratacoes.ListarDatasOcupadasDoPrestadorAsync(prestadorId, ignorar, ct);
        var conflito = ativos.Where(d => (d - data).Duration() < IntervaloMinimo).Cast<DateTime?>().FirstOrDefault();
        if (conflito is not null)
            throw new RegraNegocioException(
                $"O prestador já tem um serviço agendado perto desse horário ({AvisoService.DataHora(conflito.Value)}). " +
                "Escolha outro horário.");
    }

    private async Task ReabrirOrcamentosAsync(long solicitacaoId, CancellationToken ct)
    {
        var doPedido = await orcamentos.ListarNaoPendentesDoPedidoAsync(solicitacaoId, ct);
        doPedido.ForEach(o => o.Reabrir());
    }

    private async Task<Contratacao> ObterVisivelAsync(ClaimsPrincipal usuario, long id, CancellationToken ct)
    {
        var contratacao = await contratacoes.ObterPorIdAsync(id, ct)
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
