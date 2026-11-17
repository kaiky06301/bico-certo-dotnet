using System.Globalization;
using BicoCerto.Contratacoes.Api.Infrastructure.Integracao;
using Microsoft.Extensions.Options;

namespace BicoCerto.Contratacoes.Api.Services;

/// <summary>Tipos de aviso aceitos pela API Java (mesmos nomes do enum TipoAviso de lá).</summary>
public static class TiposAviso
{
    public const string Orcamento = "ORCAMENTO";
    public const string Negociacao = "NEGOCIACAO";
    public const string Contratacao = "CONTRATACAO";
    public const string Agendamento = "AGENDAMENTO";
    public const string Servico = "SERVICO";
    public const string Cancelamento = "CANCELAMENTO";
}

/// <summary>
/// Avisa a outra parte do pedido sobre o que aconteceu neste módulo (orçamento, negociação, contratação...).
/// Os avisos ficam na API Java, que já tem a tela de Avisos e o contador de não lidas.
/// É "melhor esforço": se a API Java falhar, a operação principal continua valendo e o erro vai para o log.
/// </summary>
public class AvisoService(
    ISolicitacaoJavaClient java,
    IOptions<JavaApiOptions> opcoes,
    IHttpContextAccessor contexto,
    ILogger<AvisoService> log)
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static string Moeda(decimal valor) => "R$ " + valor.ToString("N2", PtBr);

    public static string DataHora(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), SaoPaulo)
            .ToString("dd/MM 'às' HH:mm", PtBr);

    public async Task AvisarAsync(long solicitacaoId, long destinatarioId, string tipo, string mensagem,
                                  decimal? valor = null, long? orcamentoId = null)
    {
        if (!opcoes.Value.Habilitada) return;
        var token = contexto.HttpContext?.Request.Headers.Authorization.ToString()
            .Replace("Bearer ", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            await java.AvisarAsync(solicitacaoId,
                new AvisoJava(tipo, destinatarioId, mensagem.Length > 300 ? mensagem[..300] : mensagem, valor, orcamentoId),
                token, CancellationToken.None);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Aviso {Tipo} do pedido {Solicitacao} não foi entregue à API Java", tipo, solicitacaoId);
        }
    }
}
