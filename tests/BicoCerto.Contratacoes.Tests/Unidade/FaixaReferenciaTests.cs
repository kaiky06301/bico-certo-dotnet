using BicoCerto.Contratacoes.Api.Domain;

namespace BicoCerto.Contratacoes.Tests.Unidade;

public class FaixaReferenciaTests
{
    [Theory]
    [InlineData(50, "Abaixo")]
    [InlineData(70, "Dentro")]
    [InlineData(200, "Dentro")]
    [InlineData(200.01, "Acima")]
    public void PosicaoNaFaixaDeReferencia(decimal valor, string esperado)
    {
        Assert.Equal(esperado, FaixaReferencia.Posicao(valor, 70m, 200m));
    }

    [Fact]
    public void SemFaixaDaTriagemNaoClassifica()
    {
        Assert.Equal(FaixaReferencia.SemReferencia, FaixaReferencia.Posicao(9999m, 0m, 0m));
    }

    [Theory]
    [InlineData(150, null)]
    [InlineData(200, "")]
    [InlineData(900, "Fiação antiga: precisa trocar o quadro inteiro")]
    public void DentroDaFaixaOuComJustificativaEPermitido(decimal valor, string? justificativa)
    {
        Assert.Null(Record.Exception(
            () => FaixaReferencia.ExigirJustificativaSeAcima(valor, 200m, justificativa, "na observação")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void AcimaDaFaixaSemJustificativaELancaRegraDeNegocio(string? justificativa)
    {
        var erro = Assert.Throws<RegraNegocioException>(
            () => FaixaReferencia.ExigirJustificativaSeAcima(260m, 200m, justificativa, "na observação"));
        Assert.Contains("faixa de referência", erro.Message);
        Assert.Contains("na observação", erro.Message);
    }
}
