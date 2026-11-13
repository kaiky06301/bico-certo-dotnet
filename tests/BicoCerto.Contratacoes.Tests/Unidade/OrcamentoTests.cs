using BicoCerto.Contratacoes.Api.Domain;

namespace BicoCerto.Contratacoes.Tests.Unidade;

public class OrcamentoTests
{
    private static Orcamento Novo() => new(10, 1, 2, "Prestador Teste", "prestador@teste.com", 150m, 2, "Levo material");

    [Fact]
    public void NovoOrcamentoComecaPendente()
    {
        var orcamento = Novo();
        Assert.Equal(StatusOrcamento.Pendente, orcamento.Status);
        Assert.Equal(150m, orcamento.Valor);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-10, 2)]
    [InlineData(100, 0)]
    [InlineData(100, 61)]
    public void ValorOuPrazoInvalidoELancaRegraDeNegocio(decimal valor, int prazo)
    {
        Assert.Throws<RegraNegocioException>(() => new Orcamento(10, 1, 2, "P", "p@t.com", valor, prazo, null));
    }

    [Fact]
    public void OrcamentoAceitoNaoPodeSerEditado()
    {
        var orcamento = Novo();
        orcamento.Aceitar();

        var erro = Assert.Throws<RegraNegocioException>(() => orcamento.Atualizar(200m, 3, null));
        Assert.Contains("pendentes", erro.Message);
    }

    [Fact]
    public void OrcamentoNaoPodeSerAceitoDuasVezes()
    {
        var orcamento = Novo();
        orcamento.Aceitar();

        Assert.Throws<RegraNegocioException>(() => orcamento.Aceitar());
    }

    [Fact]
    public void RecusarSoAfetaOrcamentoPendenteEReabrirVoltaParaPendente()
    {
        var aceito = Novo();
        aceito.Aceitar();
        aceito.Recusar();
        Assert.Equal(StatusOrcamento.Aceito, aceito.Status);

        aceito.Reabrir();
        Assert.Equal(StatusOrcamento.Pendente, aceito.Status);
    }
}
