namespace BicoCerto.Contratacoes.Api.Infrastructure.Integracao;

public class JavaApiOptions
{
    public const string Secao = "JavaApi";

    /// <summary>Quando falso, a API funciona sozinha (ex.: deploy da DevOps) e o cliente é informado no corpo.</summary>
    public bool Habilitada { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:8080";

    public int TimeoutSegundos { get; set; } = 5;
}
