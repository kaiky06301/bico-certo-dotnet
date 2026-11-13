using System.Security.Claims;
using System.Text;
using BicoCerto.Contratacoes.Api.Data;
using BicoCerto.Contratacoes.Api.Domain;
using BicoCerto.Contratacoes.Api.Infrastructure.Integracao;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BicoCerto.Contratacoes.Tests.Integracao;

/// <summary>Sobe a API em memória com SQLite em memória e uma API Java simulada.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string Segredo = "segredo-dos-testes-de-integracao-com-32+caracteres";
    public const long ClienteId = 1;
    public const long OutroClienteId = 5;
    public const long PrestadorId = 2;
    public const long OutroPrestadorId = 3;
    public const long AdminId = 9;

    /// <summary>Pedido ABERTO do cliente 1 com preço justo máximo de R$ 200.</summary>
    public const long SolicitacaoAberta = 10;

    /// <summary>Segundo pedido ABERTO do cliente 1 (para testes que não podem dividir o mesmo pedido).</summary>
    public const long OutraSolicitacaoAberta = 12;

    /// <summary>Pedido que já não aceita orçamentos.</summary>
    public const long SolicitacaoFechada = 11;

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    protected virtual int LimiteEscritaPorMinuto => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _conexao.Open();
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", Segredo);
        builder.UseSetting("JavaApi:Habilitada", "true");
        builder.UseSetting("JavaApi:BaseUrl", "http://java.invalid");
        builder.UseSetting("RateLimiting:GlobalPorMinuto", "100000");
        builder.UseSetting("RateLimiting:EscritaPorMinuto", LimiteEscritaPorMinuto.ToString());

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ContratacoesDbContext>>();
            services.AddDbContext<ContratacoesDbContext>(o => o.UseSqlite(_conexao));
            services.RemoveAll<ISolicitacaoJavaClient>();
            services.AddSingleton<ISolicitacaoJavaClient, JavaClientFalso>();
        });
    }

    public HttpClient ClienteComo(long usuarioId, string papel)
    {
        var http = CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GerarToken(usuarioId, papel));
        return http;
    }

    /// <summary>Gera um token igual ao da API Java: HS256, issuer bico-certo-api, claims sub/uid/papel.</summary>
    public static string GerarToken(long usuarioId, string papel, string segredo = Segredo)
    {
        var descritor = new SecurityTokenDescriptor
        {
            Issuer = "bico-certo-api",
            Subject = new ClaimsIdentity([new Claim("sub", $"usuario{usuarioId}@teste.com")]),
            Claims = new Dictionary<string, object> { ["uid"] = usuarioId, ["papel"] = papel },
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(segredo)), SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descritor);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _conexao.Dispose();
    }

    private sealed class JavaClientFalso : ISolicitacaoJavaClient
    {
        public Task<SolicitacaoJava> ObterAsync(long solicitacaoId, string tokenBearer, CancellationToken ct) =>
            solicitacaoId switch
            {
                SolicitacaoAberta or OutraSolicitacaoAberta => Task.FromResult(Solicitacao(solicitacaoId, "ABERTA")),
                SolicitacaoFechada => Task.FromResult(Solicitacao(solicitacaoId, "CONCLUIDA")),
                _ => throw new RecursoNaoEncontradoException($"Solicitação {solicitacaoId} não encontrada na API Java.")
            };

        private static SolicitacaoJava Solicitacao(long id, string status) =>
            new(id, status, 70m, 200m, new CategoriaJava(2, "Encanamento"), new ContatoJava(ClienteId, "Maria"));
    }
}

/// <summary>Mesma API com limite de escrita baixo, para provar o HTTP 429.</summary>
public class ApiFactoryLimiteBaixo : ApiFactory
{
    protected override int LimiteEscritaPorMinuto => 3;
}
