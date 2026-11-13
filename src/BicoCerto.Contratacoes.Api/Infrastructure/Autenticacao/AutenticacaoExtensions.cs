using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;

/// <summary>
/// Valida o MESMO token JWT emitido pela API Java (login único): HS256, mesmo segredo e mesmo emissor.
/// O papel vem na claim "papel" (CLIENTE, PRESTADOR ou ADMIN).
/// </summary>
public static class AutenticacaoExtensions
{
    public const string PoliticaCliente = "cliente";
    public const string PoliticaPrestador = "prestador";
    public const string PoliticaAdmin = "admin";

    public static IServiceCollection AddAutenticacaoJwt(this IServiceCollection services, IConfiguration configuration)
    {
        var segredo = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(segredo) || Encoding.UTF8.GetByteCount(segredo) < 32)
            throw new InvalidOperationException(
                "Configure Jwt__Secret (variável de ambiente) com o mesmo segredo da API Java, com 32+ caracteres.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; // mantém sub, uid e papel com os nomes originais
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(segredo)),
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "bico-certo-api",
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "papel"
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaCliente, p => p.RequireRole(Papeis.Cliente))
            .AddPolicy(PoliticaPrestador, p => p.RequireRole(Papeis.Prestador))
            .AddPolicy(PoliticaAdmin, p => p.RequireRole(Papeis.Admin));

        return services;
    }
}
