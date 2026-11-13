namespace BicoCerto.Contratacoes.Api.Domain;

/// <summary>Acordo fechado quando o cliente aceita um orçamento.</summary>
public class Contratacao
{
    public long Id { get; private set; }
    public long OrcamentoId { get; private set; }
    public Orcamento? Orcamento { get; private set; }
    public long SolicitacaoId { get; private set; }
    public long ClienteId { get; private set; }
    public long PrestadorId { get; private set; }
    public decimal ValorAcordado { get; private set; }
    public DateTime DataAgendada { get; private set; }
    public StatusContratacao Status { get; private set; } = StatusContratacao.Agendada;
    public DateTime CriadaEm { get; private set; }
    public DateTime AtualizadaEm { get; private set; }

    private Contratacao()
    {
    }

    public Contratacao(Orcamento orcamento, DateTime dataAgendada, DateTime agora)
    {
        GarantirDataFutura(dataAgendada, agora);
        OrcamentoId = orcamento.Id;
        Orcamento = orcamento;
        SolicitacaoId = orcamento.SolicitacaoId;
        ClienteId = orcamento.ClienteId;
        PrestadorId = orcamento.PrestadorId;
        ValorAcordado = orcamento.Valor;
        DataAgendada = dataAgendada;
        CriadaEm = AtualizadaEm = agora;
    }

    public bool EhParticipante(long usuarioId) => usuarioId == ClienteId || usuarioId == PrestadorId;

    public void Reagendar(DateTime novaData, DateTime agora)
    {
        if (Status != StatusContratacao.Agendada)
            throw new RegraNegocioException("Só é possível reagendar contratações com status Agendada.");
        GarantirDataFutura(novaData, agora);
        DataAgendada = novaData;
        AtualizadaEm = agora;
    }

    /// <summary>Máquina de estados: Agendada → EmAndamento → Concluida; Cancelada a partir de Agendada ou EmAndamento.</summary>
    public void AlterarStatus(StatusContratacao novo, DateTime agora)
    {
        var permitido = (Status, novo) switch
        {
            (StatusContratacao.Agendada, StatusContratacao.EmAndamento) => true,
            (StatusContratacao.EmAndamento, StatusContratacao.Concluida) => true,
            (StatusContratacao.Agendada, StatusContratacao.Cancelada) => true,
            (StatusContratacao.EmAndamento, StatusContratacao.Cancelada) => true,
            _ => false
        };
        if (!permitido)
            throw new RegraNegocioException($"Não é possível mudar a contratação de {Status} para {novo}.");
        Status = novo;
        AtualizadaEm = agora;
    }

    private static void GarantirDataFutura(DateTime data, DateTime agora)
    {
        if (data <= agora) throw new RegraNegocioException("A data agendada deve ser no futuro.");
        if (data > agora.AddDays(90)) throw new RegraNegocioException("A data agendada deve ser nos próximos 90 dias.");
    }
}
