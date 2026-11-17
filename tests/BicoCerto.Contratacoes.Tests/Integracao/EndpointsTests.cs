using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BicoCerto.Contratacoes.Api.Contracts;
using BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;

namespace BicoCerto.Contratacoes.Tests.Integracao;

public class EndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTime DataFutura = DateTime.UtcNow.AddDays(3);

    private HttpClient Cliente => factory.ClienteComo(ApiFactory.ClienteId, Papeis.Cliente);
    private HttpClient OutroCliente => factory.ClienteComo(ApiFactory.OutroClienteId, Papeis.Cliente);
    private HttpClient Admin => factory.ClienteComo(ApiFactory.AdminId, Papeis.Admin);

    private HttpClient Prestador(long id) => factory.ClienteComo(id, Papeis.Prestador);

    private static async Task<HttpResponseMessage> PostIdempotente(HttpClient http, string url, object corpo, string? chave = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
        request.Headers.Add("Idempotency-Key", chave ?? Guid.NewGuid().ToString());
        return await http.SendAsync(request);
    }

    private async Task<OrcamentoResponse> CriarOrcamento(long prestadorId, decimal valor = 150m,
                                                         long solicitacaoId = ApiFactory.SolicitacaoAberta)
    {
        var resposta = await PostIdempotente(Prestador(prestadorId), "/api/orcamentos",
            new { solicitacaoId, valor, prazoDias = 2, observacao = "Teste" });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<OrcamentoResponse>())!;
    }

    [Fact]
    public async Task SemTokenRetorna401()
    {
        var resposta = await factory.CreateClient().GetAsync("/api/orcamentos");
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task TokenAssinadoComOutroSegredoRetorna401()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer",
            ApiFactory.GerarToken(1, Papeis.Cliente, "outro-segredo-qualquer-com-mais-de-32-caracteres"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/orcamentos")).StatusCode);
    }

    [Fact]
    public async Task ClienteNaoPodeEnviarOrcamento()
    {
        var resposta = await PostIdempotente(Cliente, "/api/orcamentos",
            new { solicitacaoId = ApiFactory.SolicitacaoAberta, valor = 100, prazoDias = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task ValidacaoRetorna400ComOsCampos()
    {
        var resposta = await PostIdempotente(Prestador(ApiFactory.PrestadorId), "/api/orcamentos",
            new { valor = -1, prazoDias = 99 });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        var erros = corpo.GetProperty("errors");
        Assert.True(erros.TryGetProperty("solicitacaoId", out _));
        Assert.True(erros.TryGetProperty("valor", out _));
        Assert.True(erros.TryGetProperty("prazoDias", out _));
    }

    [Fact]
    public async Task SemIdempotencyKeyRetorna400()
    {
        var resposta = await Prestador(ApiFactory.PrestadorId).PostAsJsonAsync("/api/orcamentos",
            new { solicitacaoId = ApiFactory.SolicitacaoAberta, valor = 100, prazoDias = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task AcimaDaFaixaDeReferenciaExigeJustificativa()
    {
        var semMotivo = await PostIdempotente(Prestador(20), "/api/orcamentos",
            new { solicitacaoId = ApiFactory.SolicitacaoAberta, valor = 900, prazoDias = 1 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semMotivo.StatusCode);
        Assert.Contains("faixa de referência", await semMotivo.Content.ReadAsStringAsync());

        // com o motivo explicado, o valor acima da faixa e aceito e sinalizado para o morador
        var comMotivo = await PostIdempotente(Prestador(20), "/api/orcamentos",
            new { solicitacaoId = ApiFactory.SolicitacaoAberta, valor = 900, prazoDias = 1,
                  observacao = "Tubulacao embutida na parede: precisa quebrar e refazer o reboco" });
        Assert.Equal(HttpStatusCode.Created, comMotivo.StatusCode);
        var orcamento = (await comMotivo.Content.ReadFromJsonAsync<OrcamentoResponse>())!;
        Assert.Equal("Acima", orcamento.PosicaoNaFaixa);
        Assert.Equal(200m, orcamento.ReferenciaMaxima);
    }

    [Fact]
    public async Task SolicitacaoFechadaOuInexistenteNaoRecebeOrcamento()
    {
        var fechada = await PostIdempotente(Prestador(21), "/api/orcamentos",
            new { solicitacaoId = ApiFactory.SolicitacaoFechada, valor = 100, prazoDias = 1 });
        var inexistente = await PostIdempotente(Prestador(21), "/api/orcamentos",
            new { solicitacaoId = 777, valor = 100, prazoDias = 1 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, fechada.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Fact]
    public async Task MesmaIdempotencyKeyDevolveAMesmaRespostaSemDuplicar()
    {
        var http = Prestador(30);
        var chave = Guid.NewGuid().ToString();
        var corpo = new { solicitacaoId = ApiFactory.SolicitacaoAberta, valor = 120, prazoDias = 1 };

        var primeira = await PostIdempotente(http, "/api/orcamentos", corpo, chave);
        var segunda = await PostIdempotente(http, "/api/orcamentos", corpo, chave);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.True(segunda.Headers.Contains("Idempotent-Replayed"));
        var a = await primeira.Content.ReadFromJsonAsync<OrcamentoResponse>();
        var b = await segunda.Content.ReadFromJsonAsync<OrcamentoResponse>();
        Assert.Equal(a!.Id, b!.Id);

        var lista = await http.GetFromJsonAsync<PaginaResponse<OrcamentoResponse>>("/api/orcamentos");
        Assert.Equal(1, lista!.TotalElementos);
        Assert.Equal("Usuario 30", lista.Conteudo[0].PrestadorNome);
    }

    [Fact]
    public async Task MesmaChaveComOutroCorpoRetorna422()
    {
        var http = Prestador(31);
        var chave = Guid.NewGuid().ToString();
        await PostIdempotente(http, "/api/orcamentos",
            new { solicitacaoId = ApiFactory.SolicitacaoAberta, valor = 120, prazoDias = 1 }, chave);

        var reutilizada = await PostIdempotente(http, "/api/orcamentos",
            new { solicitacaoId = ApiFactory.SolicitacaoAberta, valor = 180, prazoDias = 1 }, chave);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reutilizada.StatusCode);
    }

    [Fact]
    public async Task FluxoCompletoAceitarIniciarConcluir()
    {
        var escolhido = await CriarOrcamento(40, 150m);
        var concorrente = await CriarOrcamento(41, 140m);

        // outro cliente nao pode aceitar
        var invasor = await PostIdempotente(OutroCliente, "/api/contratacoes",
            new { orcamentoId = escolhido.Id, dataAgendada = DataFutura });
        Assert.Equal(HttpStatusCode.Forbidden, invasor.StatusCode);

        // duplo clique com a mesma chave: uma contratacao so
        var chave = Guid.NewGuid().ToString();
        var corpo = new { orcamentoId = escolhido.Id, dataAgendada = DataFutura };
        var r1 = await PostIdempotente(Cliente, "/api/contratacoes", corpo, chave);
        var r2 = await PostIdempotente(Cliente, "/api/contratacoes", corpo, chave);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        var contratacao = (await r1.Content.ReadFromJsonAsync<ContratacaoResponse>())!;
        Assert.Equal(contratacao.Id, (await r2.Content.ReadFromJsonAsync<ContratacaoResponse>())!.Id);
        Assert.Equal(150m, contratacao.ValorAcordado);

        // chave nova tambem nao duplica: o pedido ja tem contratacao ativa
        var outraTentativa = await PostIdempotente(Cliente, "/api/contratacoes",
            new { orcamentoId = concorrente.Id, dataAgendada = DataFutura });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outraTentativa.StatusCode);

        // o orcamento concorrente foi recusado automaticamente
        var recusado = await Prestador(41).GetFromJsonAsync<OrcamentoResponse>($"/api/orcamentos/{concorrente.Id}");
        Assert.Equal("Recusado", recusado!.Status);
        var listaDoConcorrente = await Prestador(41).GetFromJsonAsync<PaginaResponse<OrcamentoResponse>>("/api/orcamentos");
        Assert.True(listaDoConcorrente!.Conteudo.Single(o => o.Id == concorrente.Id).ClienteContratouOutro);
        var listaDoEscolhido = await Prestador(40).GetFromJsonAsync<PaginaResponse<OrcamentoResponse>>("/api/orcamentos");
        Assert.False(listaDoEscolhido!.Conteudo.Single(o => o.Id == escolhido.Id).ClienteContratouOutro);

        // so o prestador conclui
        var clienteConclui = await Cliente.PatchAsJsonAsync($"/api/contratacoes/{contratacao.Id}/status",
            new { status = "Concluida" });
        Assert.Equal(HttpStatusCode.Forbidden, clienteConclui.StatusCode);

        var inicio = await Prestador(40).PatchAsJsonAsync($"/api/contratacoes/{contratacao.Id}/status",
            new { status = "EmAndamento" });
        var fim = await Prestador(40).PatchAsJsonAsync($"/api/contratacoes/{contratacao.Id}/status",
            new { status = "Concluida" });
        Assert.Equal(HttpStatusCode.OK, inicio.StatusCode);
        Assert.Equal("Concluida", (await fim.Content.ReadFromJsonAsync<ContratacaoResponse>())!.Status);

        // concluida nao pode ser excluida
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await Cliente.DeleteAsync($"/api/contratacoes/{contratacao.Id}")).StatusCode);
    }

    [Fact]
    public async Task CrudDoOrcamentoPeloPrestador()
    {
        var criado = await CriarOrcamento(50, 100m);
        var http = Prestador(50);

        var editado = await http.PutAsJsonAsync($"/api/orcamentos/{criado.Id}",
            new { valor = 130, prazoDias = 3, observacao = "Inclui pecas" });
        Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
        Assert.Equal(130m, (await editado.Content.ReadFromJsonAsync<OrcamentoResponse>())!.Valor);

        // outro prestador nao edita nem exclui
        Assert.Equal(HttpStatusCode.Forbidden, (await Prestador(51).DeleteAsync($"/api/orcamentos/{criado.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/orcamentos/{criado.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/orcamentos/{criado.Id}")).StatusCode);
    }

    [Fact]
    public async Task ExcluirContratacaoAgendadaReabreOsOrcamentos()
    {
        var orcamento = await CriarOrcamento(60, 110m, ApiFactory.OutraSolicitacaoAberta);
        var criada = await PostIdempotente(Cliente, "/api/contratacoes",
            new { orcamentoId = orcamento.Id, dataAgendada = DataFutura });
        var contratacao = (await criada.Content.ReadFromJsonAsync<ContratacaoResponse>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await Cliente.DeleteAsync($"/api/contratacoes/{contratacao.Id}")).StatusCode);
        var reaberto = await Prestador(60).GetFromJsonAsync<OrcamentoResponse>($"/api/orcamentos/{orcamento.Id}");
        Assert.Equal("Pendente", reaberto!.Status);
    }

    [Fact]
    public async Task ListagemEPaginadaEAdminVeTudo()
    {
        await CriarOrcamento(70);
        await CriarOrcamento(71);
        await CriarOrcamento(72);

        var pagina = await Admin.GetFromJsonAsync<PaginaResponse<OrcamentoResponse>>("/api/orcamentos?pagina=0&tamanho=2");

        Assert.Equal(2, pagina!.Conteudo.Count);
        Assert.Equal(2, pagina.Tamanho);
        Assert.True(pagina.TotalElementos >= 3);
        Assert.True(pagina.TotalPaginas >= 2);
    }

    [Fact]
    public async Task NegociacaoDeValorEntreClienteEPrestador()
    {
        const long prestadorId = 90;
        var orcamento = await CriarOrcamento(prestadorId, 180m, ApiFactory.SolicitacaoNegociacao);
        var prestador = Prestador(prestadorId);
        var url = $"/api/orcamentos/{orcamento.Id}/propostas";

        // cliente pede um valor menor, com comentario
        var r1 = await PostIdempotente(Cliente, url, new { valor = 140, comentario = "So a mao de obra, tenho o material" });
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        var pedidoCliente = (await r1.Content.ReadFromJsonAsync<PropostaResponse>())!;
        Assert.Equal("Cliente", pedidoCliente.Autor);

        // o orcamento mostra a proposta esperando resposta, mas o valor vigente ainda e o inicial
        var comPendente = await prestador.GetFromJsonAsync<OrcamentoResponse>($"/api/orcamentos/{orcamento.Id}");
        Assert.Equal(180m, comPendente!.Valor);
        Assert.Equal(pedidoCliente.Id, comPendente.PropostaPendente!.Id);

        // o cliente nao responde a propria proposta; quem nao participa nao negocia
        var propriaResposta = await Cliente.PatchAsJsonAsync($"{url}/{pedidoCliente.Id}", new { aceita = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, propriaResposta.StatusCode);
        var intruso = await PostIdempotente(OutroCliente, url, new { valor = 100 });
        Assert.Equal(HttpStatusCode.Forbidden, intruso.StatusCode);

        // contraproposta acima da faixa de referencia sem explicar o motivo e barrada
        var semMotivo = await PostIdempotente(prestador, url, new { valor = 900 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semMotivo.StatusCode);

        // prestador contrapropoe: a proposta do cliente e substituida
        var r2 = await PostIdempotente(prestador, url, new { valor = 160, comentario = "Fecho por 160" });
        var contraproposta = (await r2.Content.ReadFromJsonAsync<PropostaResponse>())!;

        // cliente recusa: continua valendo o valor inicial
        var recusa = await Cliente.PatchAsJsonAsync($"{url}/{contraproposta.Id}", new { aceita = false });
        Assert.Equal("Recusada", (await recusa.Content.ReadFromJsonAsync<PropostaResponse>())!.Status);
        var aposRecusa = await Cliente.GetFromJsonAsync<OrcamentoResponse>($"/api/orcamentos/{orcamento.Id}");
        Assert.Equal(180m, aposRecusa!.Valor);
        Assert.Null(aposRecusa.PropostaPendente);

        // nova rodada: prestador oferece 165 e o cliente aceita
        var r3 = await PostIdempotente(prestador, url, new { valor = 165 });
        var ultima = (await r3.Content.ReadFromJsonAsync<PropostaResponse>())!;
        var aceite = await Cliente.PatchAsJsonAsync($"{url}/{ultima.Id}", new { aceita = true });
        Assert.Equal(HttpStatusCode.OK, aceite.StatusCode);

        var historico = await Cliente.GetFromJsonAsync<List<PropostaResponse>>(url);
        Assert.Equal(["Substituida", "Recusada", "Aceita"], historico!.Select(p => p.Status));

        // a contratacao usa o valor combinado; o inicial continua registrado
        var contratada = await PostIdempotente(Cliente, "/api/contratacoes",
            new { orcamentoId = orcamento.Id, dataAgendada = DataFutura });
        Assert.Equal(165m, (await contratada.Content.ReadFromJsonAsync<ContratacaoResponse>())!.ValorAcordado);
        var final = await Cliente.GetFromJsonAsync<OrcamentoResponse>($"/api/orcamentos/{orcamento.Id}");
        Assert.Equal(180m, final!.ValorInicial);

        // depois de contratado nao se negocia mais
        var tarde = await PostIdempotente(Cliente, url, new { valor = 150 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tarde.StatusCode);
    }

    [Fact]
    public async Task ClienteRecusaOrcamentoPendente()
    {
        var orcamento = await CriarOrcamento(91, 150m, ApiFactory.SolicitacaoNegociacao);
        var url = $"/api/orcamentos/{orcamento.Id}/recusa";

        // so o cliente dono recusa
        Assert.Equal(HttpStatusCode.Forbidden, (await OutroCliente.PatchAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Prestador(91).PatchAsync(url, null)).StatusCode);

        var recusa = await Cliente.PatchAsync(url, null);
        Assert.Equal(HttpStatusCode.OK, recusa.StatusCode);
        Assert.Equal("Recusado", (await recusa.Content.ReadFromJsonAsync<OrcamentoResponse>())!.Status);

        // recusado nao pode ser recusado de novo nem aceito
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Cliente.PatchAsync(url, null)).StatusCode);
    }

    [Fact]
    public async Task AvisaAOutraParteAoReceberOrcamentoENegociar()
    {
        var orcamento = await CriarOrcamento(92, 175m, ApiFactory.SolicitacaoNegociacao);
        Assert.Contains(ApiFactory.Avisos, a => a.Aviso.Tipo == "ORCAMENTO" && a.Aviso.OrcamentoId == orcamento.Id
                                               && a.Aviso.DestinatarioId == ApiFactory.ClienteId
                                               && a.Aviso.Mensagem.Contains("R$ 175,00")
                                               && a.Aviso.Mensagem.Contains("encanamento"));

        await PostIdempotente(Cliente, $"/api/orcamentos/{orcamento.Id}/propostas", new { valor = 150, comentario = "Fecha?" });
        Assert.Contains(ApiFactory.Avisos, a => a.Aviso.Tipo == "NEGOCIACAO" && a.Aviso.OrcamentoId == orcamento.Id
                                               && a.Aviso.DestinatarioId == 92 && a.Aviso.Valor == 150m);

        await Cliente.PatchAsync($"/api/orcamentos/{orcamento.Id}/recusa", null);
        Assert.Contains(ApiFactory.Avisos, a => a.Aviso.Tipo == "ORCAMENTO" && a.Aviso.DestinatarioId == 92
                                               && a.Aviso.Mensagem.Contains("recusou"));
    }

    [Fact]
    public async Task HealthCheckMostraApiBancoEServicoExterno()
    {
        var resposta = await factory.CreateClient().GetAsync("/health");
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        var entradas = corpo.GetProperty("entries");

        Assert.Equal("Healthy", entradas.GetProperty("api").GetProperty("status").GetString());
        Assert.Equal("Healthy", entradas.GetProperty("banco-de-dados").GetProperty("status").GetString());
        // a API Java nao existe no teste: o servico externo aparece como Degraded, sem derrubar a API
        Assert.Equal("Degraded", entradas.GetProperty("api-java").GetProperty("status").GetString());
    }
}

public class RateLimitingTests(ApiFactoryLimiteBaixo factory) : IClassFixture<ApiFactoryLimiteBaixo>
{
    [Fact]
    public async Task EscritaAlemDoLimiteRetorna429ComRetryAfter()
    {
        var http = factory.ClienteComo(80, Papeis.Prestador);
        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/orcamentos")
            {
                Content = JsonContent.Create(new { solicitacaoId = 777, valor = 100, prazoDias = 1 })
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var resposta = await http.SendAsync(request);
            codigos.Add(resposta.StatusCode);
            if (resposta.StatusCode == HttpStatusCode.TooManyRequests)
                Assert.True(resposta.Headers.Contains("Retry-After"));
        }

        Assert.Equal(3, codigos.Count(c => c != HttpStatusCode.TooManyRequests));
        Assert.Equal(2, codigos.Count(c => c == HttpStatusCode.TooManyRequests));
    }
}
