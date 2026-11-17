# Bico Certo - Contratações API (.NET)

> **Global Solution 2026/2 · CitizenTech · Advanced Business Development with .NET (FIAP)**
> Módulo de **orçamentos e contratação** do Bico Certo: o prestador do bairro envia um orçamento para o pedido do morador e o morador contrata com segurança, **sabendo se o valor está dentro da faixa de referência** e **sem contratar duas vezes** por causa de um clique repetido.

| | |
|---|---|
| **Repositório** | `LINK_DO_REPOSITORIO` |
| **API Java (núcleo do Bico Certo)** | https://github.com/kaiky06301/bico-certo-java |
| **Swagger (local)** | http://localhost:5090/swagger |
| **Health check** | http://localhost:5090/health |

---

## Sumário
1. [O problema e a solução](#1-o-problema-e-a-solução)
2. [Arquitetura](#2-arquitetura)
3. [Modelagem](#3-modelagem)
4. [Requisitos atendidos](#4-requisitos-atendidos)
5. [Idempotência](#5-idempotência)
6. [Rate limiting](#6-rate-limiting)
7. [Autenticação JWT](#7-autenticação-jwt)
8. [Health checks](#8-health-checks)
9. [Como rodar](#9-como-rodar)
10. [Endpoints](#10-endpoints)
11. [Testes (xUnit + Postman)](#11-testes-xunit--postman)
12. [Deploy na Azure (DevOps)](#12-deploy-na-azure-devops)
13. [Estrutura do projeto](#13-estrutura-do-projeto)
14. [Declaração de uso de IA generativa](#14-declaração-de-uso-de-ia-generativa)
15. [Integrantes](#15-integrantes)

---

## 1. O problema e a solução

O **Bico Certo** conecta moradores de bairros periféricos a prestadores de serviço locais (eletricistas, encanadores, diaristas). A **API Java** recebe o pedido, faz a triagem com IA (categoria, urgência e **faixa de preço de referência**) e avisa os prestadores. Esta **API .NET** cuida da parte comercial:

1. O **prestador** envia um **orçamento** (valor, prazo e observação) para um pedido aberto.
2. O valor é comparado com a **faixa de referência** calculada pela triagem da API Java. A faixa **não é um teto**, porque um serviço pode ter particularidades que justifiquem um valor maior. Mas, **acima da faixa, quem propõe precisa explicar o motivo** (observação ou comentário), e o orçamento aparece marcado como `Acima` para o morador. Isso **protege o cidadão** sem engessar o prestador.
3. **Negociação de valor:** cliente e prestador podem propor outro valor, com um comentário opcional. Quem recebe a proposta **aceita**, **recusa** ou manda uma **contraproposta**. Se ninguém aceitar, continua valendo o valor atual. Proposta acima da faixa de referência exige comentário explicando o motivo.
4. O **cliente** compara as propostas e **aceita uma**, o que gera a **contratação** com data agendada. As outras propostas são recusadas automaticamente.
5. O prestador inicia e conclui o serviço. Cliente e prestador podem reagendar ou cancelar.

**Valor para o cidadão:** quem contrata sabe se o preço está dentro da referência (e, se não estiver, por quê) e não fica com duas contratações (e duas cobranças) por causa de internet instável. O prestador ganha um fluxo profissional de proposta, aceite e agenda.

**ODS:** 10 (redução das desigualdades) e 11 (cidades e comunidades sustentáveis).

---

## 2. Arquitetura

```mermaid
flowchart LR
    APP["App Mobile<br/>(React Native)"]

    subgraph JAVA["API Java (Spring Boot)"]
        LOGIN["/auth/login<br/>emite JWT"]
        SOL["/api/solicitacoes<br/>triagem com IA + faixa de referencia"]
    end

    subgraph NET["Contratacoes API (.NET 8 Minimal API)"]
        RL["Rate Limiter<br/>429"]
        AUTH["JwtBearer<br/>mesmo segredo do Java"]
        FIL["Filtros:<br/>Validacao + Idempotencia"]
        EP["Endpoints<br/>/api/orcamentos<br/>/api/contratacoes"]
        SRV["Services<br/>regras de negocio"]
        CLI["SolicitacaoJavaClient<br/>(HttpClient)"]
        HC["/health<br/>api + banco + api-java"]
    end

    DB[("SQLite (local)<br/>Azure SQL (nuvem)<br/>EF Core")]

    APP -- "1. login" --> LOGIN
    APP -- "2. Bearer JWT" --> RL --> AUTH --> FIL --> EP --> SRV
    SRV --> DB
    SRV --> CLI -- "GET solicitacao<br/>(token do usuario)" --> SOL
    HC -.-> DB
    HC -.-> SOL
```

- **Login único:** o app faz login uma vez na API Java e usa o **mesmo token** nas duas APIs.
- **Integração:** o `SolicitacaoJavaClient` consulta `GET /api/solicitacoes/{id}` na API Java **repassando o token do próprio usuário**. Assim a API Java aplica as mesmas regras de acesso, e o .NET recebe o status do pedido, o cliente e a faixa de preço de referência.
- **Avisos para a outra parte:** depois de cada evento deste módulo (orçamento recebido, editado ou recusado, contraproposta, proposta aceita ou recusada, contratação, reagendamento, início do serviço, cancelamento pelo prestador), o `AvisoService` grava um aviso na API Java com `POST /api/solicitacoes/{id}/avisos`, também com o token do usuário. Os avisos aparecem na tela de Avisos do app com tipo, valor e atalho para a proposta. É "melhor esforço": se a API Java falhar, a operação principal continua valendo e o erro vai para o log.
- **Funciona sozinha:** com `JavaApi__Habilitada=false` a API roda sem o Java (útil no deploy da DevOps). Nesse modo o `clienteId` é informado no corpo do orçamento.

---

## 3. Modelagem

```mermaid
erDiagram
    ORCAMENTOS ||--o| CONTRATACOES : "aceito gera"
    ORCAMENTOS ||--o{ PROPOSTAS_NEGOCIACAO : "negociado em"
    ORCAMENTOS {
        bigint Id PK
        bigint SolicitacaoId "id do pedido na API Java"
        bigint ClienteId "uid do cliente"
        bigint PrestadorId "uid do prestador"
        nvarchar PrestadorNome
        nvarchar PrestadorEmail
        decimal Valor "valor vigente"
        decimal ValorInicial "valor enviado pelo prestador"
        decimal ReferenciaMinima "faixa da triagem"
        decimal ReferenciaMaxima "faixa da triagem"
        int PrazoDias
        nvarchar Observacao
        nvarchar Status "Pendente, Aceito, Recusado"
        datetime2 CriadoEm
        datetime2 AtualizadoEm
    }
    PROPOSTAS_NEGOCIACAO {
        bigint Id PK
        bigint OrcamentoId FK "cascade"
        nvarchar Autor "Cliente ou Prestador"
        bigint AutorId
        nvarchar AutorNome
        decimal Valor
        nvarchar Comentario "opcional"
        nvarchar Status "Pendente, Aceita, Recusada, Substituida"
        datetime2 CriadaEm
        datetime2 RespondidaEm
    }
    CONTRATACOES {
        bigint Id PK
        bigint OrcamentoId FK "UNIQUE"
        bigint SolicitacaoId
        bigint ClienteId
        bigint PrestadorId
        decimal ValorAcordado
        datetime2 DataAgendada
        nvarchar Status "Agendada, EmAndamento, Concluida, Cancelada"
        datetime2 CriadaEm
        datetime2 AtualizadaEm
    }
    REGISTROS_IDEMPOTENCIA {
        bigint Id PK
        nvarchar Chave "UNIQUE com UsuarioId + Rota"
        bigint UsuarioId
        nvarchar Rota
        nvarchar HashRequisicao "SHA-256 do corpo"
        int StatusCode
        nvarchar CorpoResposta
        nvarchar Location
        datetime2 CriadoEm
    }
```

**Índices únicos que reforçam as regras no banco:**
- `(SolicitacaoId, PrestadorId)` em Orçamentos: um prestador manda só um orçamento por pedido.
- `OrcamentoId` em Contratações: um orçamento vira no máximo uma contratação.
- `(Chave, UsuarioId, Rota)` nos registros de idempotência: barra duas requisições simultâneas com a mesma chave.

**Negociação de valor (uma proposta pendente por vez):**

```mermaid
stateDiagram-v2
    [*] --> Pendente: cliente ou prestador propoe outro valor
    Pendente --> Aceita: a outra parte aceita (vira o valor do orcamento)
    Pendente --> Recusada: a outra parte recusa (continua o valor atual)
    Pendente --> Substituida: contraproposta ou contratacao antes da resposta
    Aceita --> [*]
    Recusada --> [*]
    Substituida --> [*]
```

**Máquina de estados da contratação:**

```mermaid
stateDiagram-v2
    [*] --> Agendada: cliente aceita o orcamento
    Agendada --> Agendada: reagendar
    Agendada --> EmAndamento: prestador inicia
    EmAndamento --> Concluida: prestador conclui
    Agendada --> Cancelada: cliente ou prestador
    EmAndamento --> Cancelada: cliente ou prestador
    Concluida --> [*]
```

---

## 4. Requisitos atendidos

| # | Requisito da GS | Onde está |
|---|---|---|
| 01 | API RESTful com **Minimal APIs**, validações e códigos HTTP adequados | `Endpoints/`, `ValidacaoFilter` (400), `ManipuladorDeErros` (404/403/422/503) |
| 02 | **OpenAPI/Swagger** com parâmetros, contratos e respostas | `/swagger`, com resumo por rota, `Produces`, header `Idempotency-Key` e botão Authorize (JWT) |
| 03 | **Idempotência** em requisições repetidas | `IdempotenciaFilter` + tabela `RegistrosIdempotencia` ([seção 5](#5-idempotência)) |
| 04 | **Rate limiting** com HTTP **429** | `RateLimitingExtensions`: global por IP + escrita por usuário ([seção 6](#6-rate-limiting)) |
| 05 | **JWT** com autenticação e controle de acesso + **Health checks** | `AutenticacaoExtensions` (políticas por papel), `/health`, `/health/live`, `/health/ready` |
| 06 | Testes **unitários** e de **integração** com **xUnit** | `tests/` com **48 testes** ([seção 11](#11-testes-xunit--postman)) |
| | EF Core | `ContratacoesDbContext` (SQLite local / SQL Server na Azure) |

**Sem credenciais no código:** o segredo do JWT e a connection string vêm de **variáveis de ambiente**. O `appsettings.json` não tem senha nem segredo.

---

## 5. Idempotência

Rotas protegidas: `POST /api/orcamentos` e `POST /api/contratacoes`. O app gera um UUID por operação e envia no header **`Idempotency-Key`**. Se a internet falhar e o app reenviar, a operação **não é executada de novo**.

```mermaid
sequenceDiagram
    participant App
    participant F as IdempotenciaFilter
    participant DB as RegistrosIdempotencia
    participant S as ContratacaoService

    App->>F: POST /api/contratacoes (Idempotency-Key: abc)
    F->>DB: existe chave abc deste usuario nesta rota?
    DB-->>F: nao
    F->>DB: reserva a chave (status 0) - indice unico
    F->>S: executa: cria contratacao, aceita orcamento
    S-->>F: 201 Created
    F->>DB: grava status 201 + corpo da resposta
    F-->>App: 201 Created

    Note over App: sem sinal, o app reenvia
    App->>F: POST /api/contratacoes (Idempotency-Key: abc)
    F->>DB: existe? sim, mesmo hash do corpo
    F-->>App: 201 com a MESMA resposta + Idempotent-Replayed: true
```

| Situação | Resposta |
|---|---|
| Sem o header | **400**, pedindo o `Idempotency-Key` |
| Chave nova | Executa e grava a resposta |
| Mesma chave e mesmo corpo | Devolve a resposta gravada com `Idempotent-Replayed: true` e **não executa de novo** |
| Mesma chave com **outro** corpo | **422** (chave reutilizada por engano) |
| Mesma chave enquanto a primeira ainda processa | **409** |
| A operação deu erro (400/422) | A chave é liberada para nova tentativa |

Além do filtro, as **regras de negócio** também impedem duplicidade: um pedido só pode ter **uma contratação ativa**, então até com uma chave nova a segunda tentativa recebe 422.

---

## 6. Rate limiting

Usa o middleware nativo `Microsoft.AspNetCore.RateLimiting`:

| Política | Algoritmo | Limite padrão | Chave |
|---|---|---|---|
| Global (todas as rotas) | Janela fixa | 100 req/min | IP |
| `escrita` (criar/editar orçamento, aceitar e reagendar) | Janela deslizante (6 segmentos) | 10 req/min | Usuário do JWT (`uid`) |

Ao estourar o limite: **HTTP 429** com o header `Retry-After` e um corpo ProblemDetails:
```json
{ "title": "Muitas requisições", "status": 429, "detail": "Limite de requisições atingido. Tente novamente em 60 segundos." }
```
Os limites podem ser ajustados por configuração (`RateLimiting__GlobalPorMinuto` e `RateLimiting__EscritaPorMinuto`). Os health checks não passam pelo limitador.

---

## 7. Autenticação JWT

- Valida tokens **HS256** com o **mesmo segredo** e o mesmo emissor (`bico-certo-api`) da API Java.
- Claims usadas: `sub` (e-mail), `uid` (id do usuário) e `papel` (`CLIENTE`, `PRESTADOR` ou `ADMIN`).
- **Políticas por papel:** só PRESTADOR envia, edita e exclui orçamento; só CLIENTE aceita (cria contratação); ADMIN enxerga tudo.
- **Regras de dono:** cada usuário só vê e altera o que é seu (ex.: outro cliente recebe 403 ao tentar aceitar um orçamento que não é dele; só o prestador inicia ou conclui o serviço).
- Token ausente, expirado ou assinado com outro segredo: **401**.

---

## 8. Health checks

| Rota | Verifica |
|---|---|
| `/health` | Tudo: `api`, `banco-de-dados` (EF Core) e `api-java` (serviço externo) |
| `/health/live` | Só se a API está no ar (liveness) |
| `/health/ready` | Banco e API Java (readiness) |

Se a API Java cair, o status fica **Degraded** ("novos orçamentos ficam indisponíveis"), mas a API continua respondendo as consultas:

```json
{
  "status": "Degraded",
  "entries": {
    "api": { "status": "Healthy", "description": "API no ar." },
    "banco-de-dados": { "status": "Healthy" },
    "api-java": { "status": "Degraded", "description": "API Java fora do ar: novos orçamentos ficam indisponíveis." }
  }
}
```

---

## 9. Como rodar

**Pré-requisito:** .NET SDK 8.

```bash
git clone LINK_DO_REPOSITORIO
cd bico-certo-dotnet

# o segredo DEVE ser o mesmo JWT_SECRET da API Java (32+ caracteres)
export Jwt__Secret="mesmo-segredo-da-api-java"

dotnet run --project src/BicoCerto.Contratacoes.Api --launch-profile http
```

Abra **http://localhost:5090/swagger**. O banco SQLite (`contratacoes.db`) é criado sozinho na primeira execução.

**Para usar com a API Java** (fluxo completo): suba a API Java na porta 8080 ([instruções no README dela](https://github.com/kaiky06301/bico-certo-java#8-como-rodar)), faça login em `POST http://localhost:8080/auth/login` e use o `accessToken` no botão **Authorize** do Swagger.

**Para rodar sem a API Java:**
```bash
export JavaApi__Habilitada=false
export JWT_SECRET="$Jwt__Secret"
python3 scripts/gerar-token.py --papel PRESTADOR --uid 5   # gera um token de teste
```

**Com Docker:**
```bash
docker build -f dockerfiles/Dockerfile -t bicocerto-contratacoes .
docker run -p 5090:8080 -e Jwt__Secret="..." -e JavaApi__BaseUrl="http://host.docker.internal:8080" bicocerto-contratacoes
```

### Variáveis de ambiente

| Variável | Padrão | Descrição |
|---|---|---|
| `Jwt__Secret` | **obrigatória** | Mesmo segredo da API Java (32+ caracteres) |
| `Database__Provider` | `Sqlite` | `Sqlite` ou `SqlServer` |
| `ConnectionStrings__Default` | `Data Source=contratacoes.db` | Connection string do banco |
| `JavaApi__Habilitada` | `true` | Liga a validação com a API Java |
| `JavaApi__BaseUrl` | `http://localhost:8080` | Endereço da API Java |
| `RateLimiting__GlobalPorMinuto` | `100` | Limite global por IP |
| `RateLimiting__EscritaPorMinuto` | `10` | Limite de escrita por usuário |

---

## 10. Endpoints

Todos exigem `Authorization: Bearer <token>`. As listagens são paginadas (`?pagina=0&tamanho=10`) e devolvem o mesmo formato da API Java: `{ conteudo, pagina, tamanho, totalElementos, totalPaginas }`.

| Método | Rota | Papel | Idempotente | Descrição |
|---|---|---|---|---|
| POST | `/api/orcamentos` | PRESTADOR | ✅ | Envia orçamento (valida pedido aberto; acima da faixa de referência exige observação explicando) |
| GET | `/api/orcamentos?solicitacaoId=` | logado | | Lista (cliente: dos seus pedidos; prestador: os seus; admin: todos) |
| GET | `/api/orcamentos/{id}` | dono | | Detalhe |
| PUT | `/api/orcamentos/{id}` | PRESTADOR dono | | Edita orçamento pendente |
| DELETE | `/api/orcamentos/{id}` | PRESTADOR dono | | Retira orçamento pendente |
| PATCH | `/api/orcamentos/{id}/recusa` | CLIENTE dono | | Recusa um orçamento pendente (a negociação em aberto deixa de valer) |
| GET | `/api/orcamentos/{id}/propostas` | participante | | Histórico da negociação de valor |
| POST | `/api/orcamentos/{id}/propostas` | cliente ou prestador do orçamento | ✅ | Propõe outro valor com comentário opcional (contraproposta substitui a pendente; acima da faixa de referência exige comentário) |
| PATCH | `/api/orcamentos/{id}/propostas/{propostaId}` | a outra parte | | `{"aceita": true}` muda o valor do orçamento; `false` mantém o valor atual |
| POST | `/api/contratacoes` | CLIENTE dono | ✅ | Aceita o orçamento e agenda a data |
| GET | `/api/contratacoes?status=` | logado | | Lista, ordenada pela data agendada |
| GET | `/api/contratacoes/{id}` | participante | | Detalhe |
| PUT | `/api/contratacoes/{id}/agendamento` | participante | | Reagenda |
| PATCH | `/api/contratacoes/{id}/status` | participante | | `EmAndamento` e `Concluida` (prestador) ou `Cancelada` |
| DELETE | `/api/contratacoes/{id}` | participante | | Exclui uma contratação Agendada ou Cancelada e reabre os orçamentos |
| GET | `/health`, `/health/live`, `/health/ready` | público | | Monitoramento |

### Códigos HTTP

| Status | Quando |
|---|---|
| 200 / 201 / 204 | Sucesso (201 com header `Location`) |
| 400 | Validação (`errors` por campo) ou falta o `Idempotency-Key` |
| 401 | Token ausente, expirado ou inválido |
| 403 | Papel sem permissão ou recurso de outro usuário |
| 404 | Orçamento, contratação ou pedido inexistente |
| 409 | Mesma `Idempotency-Key` ainda em processamento |
| 422 | Regra de negócio (acima da faixa de referência sem justificativa, pedido fechado, transição inválida, chave reutilizada) |
| 429 | Rate limit |
| 503 | API Java indisponível ao validar um orçamento |

### Exemplo

```http
POST /api/contratacoes
Authorization: Bearer eyJhbGciOiJIUzI1NiJ9...
Idempotency-Key: 3f1c7a52-8f0e-4c55-9d43-2b8f2f9a0c11
Content-Type: application/json

{ "orcamentoId": 1, "dataAgendada": "2026-11-28T14:00:00Z" }
```
```json
201 Created
Location: /api/contratacoes/1

{ "id": 1, "orcamentoId": 1, "solicitacaoId": 4, "clienteId": 2, "prestadorId": 5,
  "valorAcordado": 150.00, "dataAgendada": "2026-11-28T14:00:00Z", "status": "Agendada",
  "criadaEm": "2026-11-13T12:00:00Z", "atualizadaEm": "2026-11-13T12:00:00Z" }
```

---

## 11. Testes (xUnit + Postman)

```bash
dotnet test
```

**48 testes, todos passando.** Os de integração usam `WebApplicationFactory`, SQLite em memória e uma API Java simulada.

| Tipo | Classe | O que cobre |
|---|---|---|
| Unidade | `OrcamentoTests` | Criação, valores e prazos inválidos, edição só quando pendente, aceite único |
| Unidade | `ContratacaoTests` | Datas válidas, máquina de estados e reagendamento |
| Unidade | `PropostaNegociacaoTests` | Aceitar muda o valor, recusar mantém, autor não responde a própria proposta, proposta já respondida, orçamento já aceito |
| Unidade | `FaixaReferenciaTests` | Posição na faixa (abaixo, dentro, acima) e justificativa obrigatória acima da faixa |
| Integração | `EndpointsTests` | 401 sem token e com token falso, 403 por papel e por dono, validação 400, falta do `Idempotency-Key`, replay idempotente, chave reutilizada (422), valor acima da faixa sem justificativa (422) e com justificativa (201, marcado `Acima`), pedido fechado ou inexistente, fluxo completo, negociação de valor (proposta, contraproposta, recusa, aceite, faixa de referência e contratação pelo valor combinado), CRUD, paginação e health check |
| Integração | `RateLimitingTests` | 429 com `Retry-After` |

**Postman:** `postman/Bico-Certo-Contratacoes.postman_collection.json`, com **34 requisições e 44 asserções** (faz o login na API Java e percorre o fluxo inteiro). Com as duas APIs no ar:
```bash
npx newman run postman/Bico-Certo-Contratacoes.postman_collection.json --delay-request 200
```
Resultado da última execução: **34 requisições, 44 asserções, 0 falhas**.

---

## 12. Deploy na Azure (DevOps)

Esta é a API escolhida para a disciplina de DevOps. O que já está pronto no repositório:

| Item exigido | Arquivo |
|---|---|
| `script-bd.sql` (DDL) em `/scripts` | `scripts/script-bd.sql`, gerado pelo EF Core para SQL Server/Azure SQL |
| Dockerfile em `/dockerfiles` (se usar container) | `dockerfiles/Dockerfile` (multi-stage, usuário sem root) |
| Testes automáticos no CI | `dotnet test --logger trx` |
| Variáveis de ambiente | Todas as configurações via `Seção__Chave` (tabela da [seção 9](#9-como-rodar)) |

Configuração no **App Service** (Azure SQL):
```
Database__Provider=SqlServer
ConnectionStrings__Default=Server=tcp:<servidor>.database.windows.net,1433;Database=<banco>;User ID=<usuario>;Password=<senha>;Encrypt=True;
Jwt__Secret=<segredo, como variavel secreta da pipeline>
JavaApi__Habilitada=false
```

Para demonstrar o CRUD no vídeo sem a API Java no ar, gere tokens com `scripts/gerar-token.py` e confira cada operação com `SELECT` direto nas tabelas `Orcamentos` e `Contratacoes`.

---

## 13. Estrutura do projeto

```
bico-certo-dotnet/
├── src/BicoCerto.Contratacoes.Api/
│   ├── Domain/            entidades, regras e maquina de estados
│   ├── Data/              DbContext (EF Core) e registro de idempotencia
│   ├── Contracts/         DTOs (records) com DataAnnotations
│   ├── Services/          regras de negocio (orcamentos, negociacao, contratacoes)
│   ├── Endpoints/         Minimal APIs agrupadas por recurso
│   ├── Infrastructure/
│   │   ├── Autenticacao/  JWT compartilhado com a API Java + politicas por papel
│   │   ├── Idempotencia/  filtro Idempotency-Key
│   │   ├── RateLimiting/  politicas global e de escrita (429)
│   │   ├── Validacao/     filtro de validacao para Minimal APIs
│   │   ├── Erros/         IExceptionHandler -> ProblemDetails
│   │   └── Integracao/    cliente HTTP da API Java + health check
│   └── Program.cs
├── tests/BicoCerto.Contratacoes.Tests/   xUnit: Unidade/ e Integracao/
├── scripts/               script-bd.sql e gerar-token.py
├── dockerfiles/           Dockerfile
└── postman/               colecao Postman
```

---

## 14. Declaração de uso de IA generativa

O grupo usou ferramentas de IA generativa **apenas como apoio**:

| Uso | Para quê |
|---|---|
| Pesquisa | Consultar documentação e exemplos (Minimal APIs, Rate Limiting, EF Core) |
| Explicação de erros | Entender mensagens de erro de compilação e de configuração durante o desenvolvimento |
| Revisão de texto | Revisar a escrita e a formatação da documentação |

Todo o código foi revisado, testado e entendido pelo grupo antes da entrega.

---

## 15. Integrantes

**Grupo:** Bico Certo Labs

| Nome | RM | Turma |
|---|---|---|
| Erick Bernardes Bradaschia | RM565733 | 2TDSR |
| Gabriel Santos Claudino | RM564054 | 2TDSR |
| Kaiky de Oliveira Silva | RM566067 | 2TDSR |
| Lucas Fortes de Lima | RM559523 | 2TDSR |
| Jonathan Moreira Gomes | RM565060 | 2TDSR |
