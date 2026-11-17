using BicoCerto.Contratacoes.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BicoCerto.Contratacoes.Api.Repositories;

public class IdempotenciaRepository(ContratacoesDbContext db) : IIdempotenciaRepository
{
    public Task<RegistroIdempotencia?> ObterAsync(string chave, long usuarioId, string rota) =>
        db.RegistrosIdempotencia.FirstOrDefaultAsync(r => r.Chave == chave && r.UsuarioId == usuarioId && r.Rota == rota);

    public async Task<bool> ReservarAsync(RegistroIdempotencia registro)
    {
        db.RegistrosIdempotencia.Add(registro);
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // índice único (Chave, UsuarioId, Rota): outra requisição igual chegou primeiro
            db.Entry(registro).State = EntityState.Detached;
            return false;
        }
    }

    public Task ConcluirAsync(RegistroIdempotencia registro) => db.SaveChangesAsync();

    public async Task RemoverAsync(RegistroIdempotencia registro)
    {
        // descarta o que a operação que falhou deixou pendente no contexto antes de apagar a chave
        db.ChangeTracker.Clear();
        db.RegistrosIdempotencia.Remove(registro);
        await db.SaveChangesAsync();
    }
}
