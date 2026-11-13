using BicoCerto.Contratacoes.Api.Domain;
using BicoCerto.Contratacoes.Api.Services;

namespace BicoCerto.Contratacoes.Tests.Unidade;

public class RegrasPrecoJustoTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(260)]
    public void ValorDentroDaToleranciaEAceito(decimal valor)
    {
        var excecao = Record.Exception(() => RegrasPrecoJusto.Validar(valor, 200m, 0.30m));
        Assert.Null(excecao);
    }

    [Fact]
    public void ValorAcimaDaToleranciaERecusadoInformandoOLimite()
    {
        var erro = Assert.Throws<RegraNegocioException>(() => RegrasPrecoJusto.Validar(260.01m, 200m, 0.30m));
        Assert.Contains("260", erro.Message);
    }

    [Fact]
    public void SemPrecoDeReferenciaNaoBloqueia()
    {
        Assert.Null(Record.Exception(() => RegrasPrecoJusto.Validar(9999m, 0m, 0.30m)));
    }
}
