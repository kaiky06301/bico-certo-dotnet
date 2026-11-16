using BicoCerto.Contratacoes.Api.Domain;

namespace BicoCerto.Contratacoes.Tests.Unidade;

public class PropostaNegociacaoTests
{
    private static readonly DateTime Agora = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static Orcamento NovoOrcamento() => new(10, 1, 2, "Prestador", "p@teste.com", 150m, 2, null);

    private static PropostaNegociacao Proposta(Orcamento o, AutorProposta autor, decimal valor) =>
        new(o, autor, autor == AutorProposta.Cliente ? 1 : 2, "Fulano", valor, "  so a mao de obra  ", Agora);

    [Fact]
    public void AceitarMudaOValorVigenteEMantemOInicial()
    {
        var orcamento = NovoOrcamento();
        var proposta = Proposta(orcamento, AutorProposta.Cliente, 120m);

        proposta.Responder(true, AutorProposta.Prestador, orcamento, Agora);

        Assert.Equal(StatusProposta.Aceita, proposta.Status);
        Assert.Equal(120m, orcamento.Valor);
        Assert.Equal(150m, orcamento.ValorInicial);
        Assert.Equal("so a mao de obra", proposta.Comentario);
    }

    [Fact]
    public void RecusarMantemOValorAtual()
    {
        var orcamento = NovoOrcamento();
        var proposta = Proposta(orcamento, AutorProposta.Prestador, 130m);

        proposta.Responder(false, AutorProposta.Cliente, orcamento, Agora);

        Assert.Equal(StatusProposta.Recusada, proposta.Status);
        Assert.Equal(150m, orcamento.Valor);
    }

    [Fact]
    public void AutorNaoRespondeAPropriaProposta()
    {
        var orcamento = NovoOrcamento();
        var proposta = Proposta(orcamento, AutorProposta.Cliente, 120m);

        var erro = Assert.Throws<RegraNegocioException>(
            () => proposta.Responder(true, AutorProposta.Cliente, orcamento, Agora));
        Assert.Contains("própria", erro.Message);
    }

    [Fact]
    public void PropostaJaRespondidaOuSubstituidaNaoPodeSerRespondidaDeNovo()
    {
        var orcamento = NovoOrcamento();
        var respondida = Proposta(orcamento, AutorProposta.Cliente, 120m);
        respondida.Responder(false, AutorProposta.Prestador, orcamento, Agora);
        var substituida = Proposta(orcamento, AutorProposta.Cliente, 110m);
        substituida.Substituir(Agora);

        Assert.Throws<RegraNegocioException>(() => respondida.Responder(true, AutorProposta.Prestador, orcamento, Agora));
        Assert.Throws<RegraNegocioException>(() => substituida.Responder(true, AutorProposta.Prestador, orcamento, Agora));
    }

    [Fact]
    public void NaoNegociaOrcamentoJaAceitoNemComOMesmoValor()
    {
        var orcamento = NovoOrcamento();
        Assert.Throws<RegraNegocioException>(() => Proposta(orcamento, AutorProposta.Cliente, 150m));

        orcamento.Aceitar();
        Assert.Throws<RegraNegocioException>(() => Proposta(orcamento, AutorProposta.Cliente, 120m));
    }
}
