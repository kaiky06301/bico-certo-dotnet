namespace BicoCerto.Contratacoes.Api.Data;

/// <summary>
/// Guarda a resposta de uma requisição feita com o header Idempotency-Key.
/// Se a mesma chave chegar de novo (ex.: o app reenviou por falta de sinal), a resposta gravada é devolvida
/// e a operação NÃO é executada outra vez.
/// </summary>
public class RegistroIdempotencia
{
    public long Id { get; set; }
    public string Chave { get; set; } = string.Empty;
    public long UsuarioId { get; set; }
    public string Rota { get; set; } = string.Empty;
    public string HashRequisicao { get; set; } = string.Empty;

    /// <summary>0 enquanto a primeira requisição ainda está sendo processada.</summary>
    public int StatusCode { get; set; }

    public string? CorpoResposta { get; set; }
    public string? Location { get; set; }
    public DateTime CriadoEm { get; set; }
}
