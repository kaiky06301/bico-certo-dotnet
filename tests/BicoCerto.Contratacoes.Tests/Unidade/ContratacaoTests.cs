using BicoCerto.Contratacoes.Api.Domain;

namespace BicoCerto.Contratacoes.Tests.Unidade;

public class ContratacaoTests
{
    private static readonly DateTime Agora = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private static Contratacao Nova() =>
        new(new Orcamento(10, 1, 2, "P", "p@t.com", 150m, 2, null), Agora.AddDays(2), Agora);

    [Fact]
    public void ContratacaoCopiaValorEParticipantesDoOrcamento()
    {
        var contratacao = Nova();

        Assert.Equal(StatusContratacao.Agendada, contratacao.Status);
        Assert.Equal(150m, contratacao.ValorAcordado);
        Assert.True(contratacao.EhParticipante(1));
        Assert.True(contratacao.EhParticipante(2));
        Assert.False(contratacao.EhParticipante(3));
    }

    [Fact]
    public void DataNoPassadoOuMuitoDistanteELancaRegraDeNegocio()
    {
        var orcamento = new Orcamento(10, 1, 2, "P", "p@t.com", 150m, 2, null);
        Assert.Throws<RegraNegocioException>(() => new Contratacao(orcamento, Agora.AddHours(-1), Agora));
        Assert.Throws<RegraNegocioException>(() => new Contratacao(orcamento, Agora.AddDays(91), Agora));
    }

    [Fact]
    public void FluxoValidoAgendadaEmAndamentoConcluida()
    {
        var contratacao = Nova();
        contratacao.AlterarStatus(StatusContratacao.EmAndamento, Agora);
        contratacao.AlterarStatus(StatusContratacao.Concluida, Agora);

        Assert.Equal(StatusContratacao.Concluida, contratacao.Status);
    }

    [Theory]
    [InlineData(StatusContratacao.Concluida)]
    [InlineData(StatusContratacao.Agendada)]
    public void TransicaoInvalidaAPartirDeAgendadaELancaRegraDeNegocio(StatusContratacao destino)
    {
        Assert.Throws<RegraNegocioException>(() => Nova().AlterarStatus(destino, Agora));
    }

    [Fact]
    public void ContratacaoConcluidaNaoPodeSerCanceladaNemReagendada()
    {
        var contratacao = Nova();
        contratacao.AlterarStatus(StatusContratacao.EmAndamento, Agora);
        contratacao.AlterarStatus(StatusContratacao.Concluida, Agora);

        Assert.Throws<RegraNegocioException>(() => contratacao.AlterarStatus(StatusContratacao.Cancelada, Agora));
        Assert.Throws<RegraNegocioException>(() => contratacao.Reagendar(Agora.AddDays(3), Agora));
    }

    [Fact]
    public void ReagendarAtualizaADataQuandoAgendada()
    {
        var contratacao = Nova();
        contratacao.Reagendar(Agora.AddDays(5), Agora);

        Assert.Equal(Agora.AddDays(5), contratacao.DataAgendada);
    }
}
