using System.Security.Claims;

namespace BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;

public static class Papeis
{
    public const string Cliente = "CLIENTE";
    public const string Prestador = "PRESTADOR";
    public const string Admin = "ADMIN";
}

/// <summary>Lê as claims do JWT emitido pela API Java: sub (e-mail), uid (id do usuário) e papel.</summary>
public static class UsuarioLogado
{
    public static long Id(this ClaimsPrincipal usuario) =>
        long.TryParse(usuario.FindFirstValue("uid"), out var id)
            ? id
            : throw new UnauthorizedAccessException("Token sem a claim uid.");

    public static string Email(this ClaimsPrincipal usuario) => usuario.FindFirstValue("sub") ?? string.Empty;

    /// <summary>Nome do usuario (claim "nome"); se o token nao tiver, usa o e-mail.</summary>
    public static string Nome(this ClaimsPrincipal usuario) => usuario.FindFirstValue("nome") ?? usuario.Email();

    public static string Papel(this ClaimsPrincipal usuario) => usuario.FindFirstValue("papel") ?? string.Empty;

    public static bool EhAdmin(this ClaimsPrincipal usuario) => usuario.Papel() == Papeis.Admin;
}
