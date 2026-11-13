using BicoCerto.Contratacoes.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BicoCerto.Contratacoes.Api.Data;

public class ContratacoesDbContext(DbContextOptions<ContratacoesDbContext> options) : DbContext(options)
{
    public DbSet<Orcamento> Orcamentos => Set<Orcamento>();
    public DbSet<Contratacao> Contratacoes => Set<Contratacao>();
    public DbSet<RegistroIdempotencia> RegistrosIdempotencia => Set<RegistroIdempotencia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Orcamento>(e =>
        {
            e.ToTable("Orcamentos");
            e.HasKey(o => o.Id);
            e.Property(o => o.PrestadorNome).HasMaxLength(120).IsRequired();
            e.Property(o => o.PrestadorEmail).HasMaxLength(150).IsRequired();
            e.Property(o => o.Valor).HasPrecision(10, 2);
            e.Property(o => o.Observacao).HasMaxLength(500);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(o => o.SolicitacaoId);
            e.HasIndex(o => o.PrestadorId);
            // um prestador envia no maximo um orcamento por solicitacao
            e.HasIndex(o => new { o.SolicitacaoId, o.PrestadorId }).IsUnique();
        });

        modelBuilder.Entity<Contratacao>(e =>
        {
            e.ToTable("Contratacoes");
            e.HasKey(c => c.Id);
            e.Property(c => c.ValorAcordado).HasPrecision(10, 2);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(c => c.Orcamento).WithMany().HasForeignKey(c => c.OrcamentoId).OnDelete(DeleteBehavior.Restrict);
            // um orcamento gera no maximo uma contratacao (protecao extra contra duplicidade)
            e.HasIndex(c => c.OrcamentoId).IsUnique();
            e.HasIndex(c => c.ClienteId);
            e.HasIndex(c => c.PrestadorId);
        });

        modelBuilder.Entity<RegistroIdempotencia>(e =>
        {
            e.ToTable("RegistrosIdempotencia");
            e.HasKey(r => r.Id);
            e.Property(r => r.Chave).HasMaxLength(100).IsRequired();
            e.Property(r => r.Rota).HasMaxLength(200).IsRequired();
            e.Property(r => r.HashRequisicao).HasMaxLength(64).IsRequired();
            e.Property(r => r.Location).HasMaxLength(300);
            // a mesma chave so pode ser registrada uma vez por usuario e rota (barra requisicoes simultaneas)
            e.HasIndex(r => new { r.Chave, r.UsuarioId, r.Rota }).IsUnique();
        });
    }
}
