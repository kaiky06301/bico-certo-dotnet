using System.Net;
using System.Net.Http.Headers;
using BicoCerto.Contratacoes.Api.Domain;

namespace BicoCerto.Contratacoes.Api.Infrastructure.Integracao;

/// <summary>Dados da solicitação devolvidos por GET /api/solicitacoes/{id} na API Java.</summary>
public record SolicitacaoJava(long Id, string Status, decimal PrecoMinimo, decimal PrecoMaximo,
                              CategoriaJava Categoria, ContatoJava Cliente);

public record CategoriaJava(long Id, string Nome);

public record ContatoJava(long Id, string Nome);

/// <summary>Aviso para a outra parte do pedido (gravado em POST /api/solicitacoes/{id}/avisos na API Java).</summary>
public record AvisoJava(string Tipo, long DestinatarioId, string Mensagem, decimal? Valor = null, long? OrcamentoId = null);

public interface ISolicitacaoJavaClient
{
    Task<SolicitacaoJava> ObterAsync(long solicitacaoId, string tokenBearer, CancellationToken ct);

    Task AvisarAsync(long solicitacaoId, AvisoJava aviso, string tokenBearer, CancellationToken ct);
}

/// <summary>
/// Consulta a API Java repassando o token do próprio usuário: assim a API Java aplica as mesmas regras de acesso
/// (o prestador só enxerga pedidos abertos da sua categoria).
/// </summary>
public class SolicitacaoJavaClient(HttpClient http) : ISolicitacaoJavaClient
{
    public async Task AvisarAsync(long solicitacaoId, AvisoJava aviso, string tokenBearer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/solicitacoes/{solicitacaoId}/avisos")
        {
            Content = JsonContent.Create(aviso)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenBearer);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<SolicitacaoJava> ObterAsync(long solicitacaoId, string tokenBearer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/solicitacoes/{solicitacaoId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenBearer);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ServicoExternoIndisponivelException("A API Java do Bico Certo não respondeu. Tente novamente.");
        }

        using (response)
        {
            return response.StatusCode switch
            {
                HttpStatusCode.OK => await response.Content.ReadFromJsonAsync<SolicitacaoJava>(ct)
                                     ?? throw new ServicoExternoIndisponivelException("Resposta vazia da API Java."),
                HttpStatusCode.NotFound => throw new RecursoNaoEncontradoException(
                    $"Solicitação {solicitacaoId} não encontrada na API Java."),
                HttpStatusCode.Forbidden => throw new AcessoNegadoException(
                    "Você não tem acesso a esta solicitação (ela não é da sua categoria ou já foi assumida)."),
                HttpStatusCode.Unauthorized => throw new AcessoNegadoException("Token recusado pela API Java."),
                _ => throw new ServicoExternoIndisponivelException(
                    $"A API Java respondeu {(int)response.StatusCode}. Tente novamente.")
            };
        }
    }
}
