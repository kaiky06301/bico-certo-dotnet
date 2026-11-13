namespace BicoCerto.Contratacoes.Api.Contracts;

/// <summary>Mesmo envelope de paginação usado pela API Java, para o app Mobile tratar as duas APIs igual.</summary>
public record PaginaResponse<T>(IReadOnlyList<T> Conteudo, int Pagina, int Tamanho, long TotalElementos, int TotalPaginas)
{
    public static PaginaResponse<T> De(IReadOnlyList<T> itens, int pagina, int tamanho, long total) =>
        new(itens, pagina, tamanho, total, tamanho == 0 ? 0 : (int)Math.Ceiling(total / (double)tamanho));
}

public static class Paginacao
{
    public const int TamanhoPadrao = 10;
    public const int TamanhoMaximo = 50;

    public static (int Pagina, int Tamanho) Normalizar(int? pagina, int? tamanho) =>
        (Math.Max(pagina ?? 0, 0), Math.Clamp(tamanho ?? TamanhoPadrao, 1, TamanhoMaximo));
}
