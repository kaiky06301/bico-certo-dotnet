using BicoCerto.Contratacoes.Api.Data;

namespace BicoCerto.Contratacoes.Api.Repositories;

/// <summary>Acesso a dados dos registros de Idempotency-Key.</summary>
public interface IIdempotenciaRepository
{
    Task<RegistroIdempotencia?> ObterAsync(string chave, long usuarioId, string rota);

    /// <summary>Grava a chave antes de executar a operação. Retorna false se outra requisição já reservou.</summary>
    Task<bool> ReservarAsync(RegistroIdempotencia registro);

    /// <summary>Grava a resposta da operação no registro já reservado.</summary>
    Task ConcluirAsync(RegistroIdempotencia registro);

    /// <summary>Apaga o registro (chave expirada ou operação que falhou), liberando a chave.</summary>
    Task RemoverAsync(RegistroIdempotencia registro);
}
