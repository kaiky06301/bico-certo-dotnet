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
    /// <summary>Valor vigente: começa igual ao inicial e muda quando uma proposta da negociação é aceita.</summary>
    public decimal Valor { get; private set; }

    /// <summary>Valor que o prestador enviou no orçamento (antes de qualquer negociação).</summary>
    public decimal ValorInicial { get; private set; }

    /// <summary>Faixa de referência de preço da triagem (API Java) no momento do orçamento; 0 quando indisponível.</summary>
    public decimal ReferenciaMinima { get; private set; }
    public decimal ReferenciaMaxima { get; private set; }

    public int PrazoDias { get; private set; }
    public string? Observacao { get; private set; }
    public StatusOrcamento Status { get; private set; } = StatusOrcamento.Pendente;
    /// <summary>Até quando o cliente pode aceitar ou negociar (depois disso o orçamento fica "Expirado").</summary>
    public DateTime ValidoAte { get; private set; }

    /// <summary>O que o valor cobre, para evitar desentendimento com o cliente.</summary>
    public bool IncluiMaoDeObra { get; private set; } = true;
    public bool IncluiMaterial { get; private set; }
    public bool IncluiDeslocamento { get; private set; } = true;
    public string? Adicionais { get; private set; }

    public const int ValidadePadraoDias = 7;

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
        Valor = ValorInicial = valor;
        PrazoDias = prazoDias;
        Observacao = observacao;
        CriadoEm = AtualizadoEm = DateTime.UtcNow;
    }

    public void Atualizar(decimal valor, int prazoDias, string? observacao)
    {
        GarantirPendente("editar");
        if (valor <= 0) throw new RegraNegocioException("O valor do orçamento deve ser maior que zero.");
        if (prazoDias is < 1 or > 60) throw new RegraNegocioException("O prazo deve ser de 1 a 60 dias.");
        Valor = ValorInicial = valor;
        PrazoDias = prazoDias;
        Observacao = observacao;
        AtualizadoEm = DateTime.UtcNow;
    }

    /// <summary>Validade e o que está incluso. Editar o orçamento renova a validade.</summary>
    public void DefinirCondicoes(int? validadeDias, bool? maoDeObra, bool? material, bool? deslocamento,
                                 string? adicionais, DateTime agora)
    {
        var dias = validadeDias ?? ValidadePadraoDias;
        if (dias is < 1 or > 30) throw new RegraNegocioException("A validade deve ser de 1 a 30 dias.");
        ValidoAte = agora.AddDays(dias);
        IncluiMaoDeObra = maoDeObra ?? true;
        IncluiMaterial = material ?? false;
        IncluiDeslocamento = deslocamento ?? true;
        Adicionais = string.IsNullOrWhiteSpace(adicionais) ? null : adicionais.Trim();
    }

    public bool Expirado(DateTime agora) => Status == StatusOrcamento.Pendente && ValidoAte != default && agora > ValidoAte;

    public void GarantirDentroDaValidade(DateTime agora)
    {
        if (Expirado(agora))
            throw new RegraNegocioException(
                "Este orçamento expirou. O prestador precisa renovar (editar o orçamento) para continuar.");
    }

    public void DefinirFaixaReferencia(decimal minima, decimal maxima)
    {
        ReferenciaMinima = minima;
        ReferenciaMaxima = maxima;
    }

    /// <summary>Uma proposta da negociação foi aceita: o valor combinado passa a ser o vigente.</summary>
    public void AplicarValorNegociado(decimal valor)
    {
        GarantirPendente("negociar");
        if (valor <= 0) throw new RegraNegocioException("O valor negociado deve ser maior que zero.");
        Valor = valor;
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
