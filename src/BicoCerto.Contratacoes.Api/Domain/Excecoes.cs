namespace BicoCerto.Contratacoes.Api.Domain;

/// <summary>Regra de negócio violada: a requisição é válida, mas não pode ser executada no estado atual (422).</summary>
public class RegraNegocioException(string mensagem) : Exception(mensagem);

/// <summary>Recurso inexistente (404).</summary>
public class RecursoNaoEncontradoException(string mensagem) : Exception(mensagem);

/// <summary>Usuário autenticado tentando acessar recurso de outra pessoa (403).</summary>
public class AcessoNegadoException(string mensagem) : Exception(mensagem);

/// <summary>Serviço externo (API Java) indisponível (503).</summary>
public class ServicoExternoIndisponivelException(string mensagem) : Exception(mensagem);
