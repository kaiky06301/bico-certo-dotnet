using System.Text.Json.Serialization;
using BicoCerto.Contratacoes.Api.Data;
using BicoCerto.Contratacoes.Api.Endpoints;
using BicoCerto.Contratacoes.Api.Infrastructure.Autenticacao;
using BicoCerto.Contratacoes.Api.Infrastructure.Erros;
using BicoCerto.Contratacoes.Api.Infrastructure.Integracao;
using BicoCerto.Contratacoes.Api.Infrastructure.RateLimiting;
using BicoCerto.Contratacoes.Api.Services;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Banco: SQLite para rodar local sem instalar nada; SQL Server (Azure SQL) no deploy.
// Database__Provider=SqlServer + ConnectionStrings__Default=... via variaveis de ambiente.
var provedor = builder.Configuration["Database:Provider"] ?? "Sqlite";
var conexao = builder.Configuration.GetConnectionString("Default") ?? "Data Source=contratacoes.db";
builder.Services.AddDbContext<ContratacoesDbContext>(options =>
{
    if (provedor.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        options.UseSqlServer(conexao, sql => sql.EnableRetryOnFailure(3));
    else
        options.UseSqlite(conexao);
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<OrcamentoService>();
builder.Services.AddScoped<ContratacaoService>();
builder.Services.AddScoped<NegociacaoService>();
builder.Services.AddScoped<AvisoService>();
builder.Services.AddHttpContextAccessor();

// Integracao com a API Java (nucleo do Bico Certo)
builder.Services.Configure<JavaApiOptions>(builder.Configuration.GetSection(JavaApiOptions.Secao));
builder.Services.AddHttpClient<ISolicitacaoJavaClient, SolicitacaoJavaClient>((sp, http) =>
{
    var java = sp.GetRequiredService<IOptions<JavaApiOptions>>().Value;
    http.BaseAddress = new Uri(java.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(java.TimeoutSegundos);
});
builder.Services.AddHttpClient("health-java");

// Seguranca, protecao e erros
builder.Services.AddAutenticacaoJwt(builder.Configuration);
builder.Services.AddLimitacaoDeRequisicoes(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ManipuladorDeErros>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders("Location", "Retry-After", "Idempotent-Replayed")));

// Health checks: a propria API, o banco e o servico externo (API Java)
builder.Services.AddHealthChecks()
    .AddCheck("api", () => HealthCheckResult.Healthy("API no ar."), tags: ["live"])
    .AddDbContextCheck<ContratacoesDbContext>("banco-de-dados", tags: ["ready"])
    .AddCheck<JavaApiHealthCheck>("api-java", tags: ["ready"]);

// Swagger / OpenAPI com JWT
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Bico Certo - Contratações API",
        Version = "v1",
        Description = "Módulo .NET do Bico Certo (Global Solution 2026/2 - CitizenTech): orçamentos dos prestadores "
                      + "e contratação dos serviços. Use o token JWT emitido pela API Java (POST /auth/login)."
    });
    var esquema = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Cole apenas o token (sem a palavra Bearer).",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityDefinition("Bearer", esquema);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [esquema] = [] });
});

var app = builder.Build();

using (var escopo = app.Services.CreateScope())
{
    escopo.ServiceProvider.GetRequiredService<ContratacoesDbContext>().Database.EnsureCreated();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "Contratações API v1");
    o.DocumentTitle = "Bico Certo - Contratações API";
});

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapOrcamentos();
app.MapContratacoes();

var respostaHealth = new HealthCheckOptions { ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse };
app.MapHealthChecks("/health", respostaHealth).DisableRateLimiting();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).DisableRateLimiting();

app.Run();

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
