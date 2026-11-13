using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BicoCerto.Contratacoes.Api.Infrastructure.Integracao;

/// <summary>Verifica o serviço externo usado pela API: a API Java do Bico Certo (rota pública do OpenAPI).</summary>
public class JavaApiHealthCheck(IHttpClientFactory httpClientFactory, IOptions<JavaApiOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var config = options.Value;
        if (!config.Habilitada)
            return HealthCheckResult.Healthy("Integração com a API Java desligada nesta configuração.");

        try
        {
            var http = httpClientFactory.CreateClient("health-java");
            http.Timeout = TimeSpan.FromSeconds(3);
            using var resposta = await http.GetAsync(new Uri(new Uri(config.BaseUrl), "/v3/api-docs"), ct);
            return resposta.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"API Java respondeu {(int)resposta.StatusCode}.")
                : HealthCheckResult.Degraded($"API Java respondeu {(int)resposta.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("API Java fora do ar: novos orçamentos ficam indisponíveis.", ex);
        }
    }
}
