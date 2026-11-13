namespace BicoCerto.Contratacoes.Api.Domain;

/// <summary>Proposta de preço e prazo que um prestador envia para uma solicitação criada na API Java.</summary>
public class Orcamento
{
    public long Id { get; private set; }

    /// <summary>Id da solicitação na API Java (núcleo do Bico Certo).</summary>
    public long SolicitacaoId { get; private set; }

    /// <summary>Id (uid do JWT) do cliente dono da solicitação.</summary>
    public long ClienteId { get; private set; }

    /// <summary>Id (uid do JWT) do prestador que enviou a proposta.</summary>
    public long PrestadorId { get; private set; }

    public string PrestadorNome { get; private set; } = string.Empty;
    public string PrestadorEmail { get; private set; } = string.Empty;
    public decimal Valor { get; private set; }
    public int PrazoDias { get; private set; }
    public string? Observacao { get; private set; }
    public StatusOrcamento Status { get; private set; } = StatusOrcamento.Pendente;
    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    private Orcamento()
    {
    }

    public Orcamento(long solicitacaoId, long clienteId, long prestadorId, string prestadorNome, string prestadorEmail,
                     decimal valor, int prazoDias, string? observacao)
    {
        if (valor <= 0) throw new RegraNegocioException("O valor do orçamento deve ser maior que zero.");
        if (prazoDias is < 1 or > 60) throw new RegraNegocioException("O prazo deve ser de 1 a 60 dias.");

        SolicitacaoId = solicitacaoId;
        ClienteId = clienteId;
        PrestadorId = prestadorId;
        PrestadorNome = prestadorNome;
        PrestadorEmail = prestadorEmail;
        Valor = valor;
        PrazoDias = prazoDias;
        Observacao = observacao;
        CriadoEm = AtualizadoEm = DateTime.UtcNow;
    }

    public void Atualizar(decimal valor, int prazoDias, string? observacao)
    {
        GarantirPendente("editar");
        if (valor <= 0) throw new RegraNegocioException("O valor do orçamento deve ser maior que zero.");
        if (prazoDias is < 1 or > 60) throw new RegraNegocioException("O prazo deve ser de 1 a 60 dias.");
        Valor = valor;
        PrazoDias = prazoDias;
        Observacao = observacao;
        AtualizadoEm = DateTime.UtcNow;
    }

    public void Aceitar()
    {
        GarantirPendente("aceitar");
        Status = StatusOrcamento.Aceito;
        AtualizadoEm = DateTime.UtcNow;
    }

    public void Recusar()
    {
        if (Status != StatusOrcamento.Pendente) return;
        Status = StatusOrcamento.Recusado;
        AtualizadoEm = DateTime.UtcNow;
    }

    /// <summary>Volta a ficar disponível quando a contratação é desfeita antes do serviço.</summary>
    public void Reabrir()
    {
        Status = StatusOrcamento.Pendente;
        AtualizadoEm = DateTime.UtcNow;
    }

    public void GarantirPendente(string acao)
    {
        if (Status != StatusOrcamento.Pendente)
            throw new RegraNegocioException($"Só é possível {acao} orçamentos pendentes (status atual: {Status}).");
    }
}
