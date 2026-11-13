-- =============================================================================
-- Bico Certo - Contratacoes API (.NET) - DDL para Azure SQL / SQL Server
-- Gerado a partir do modelo do EF Core:
--   dotnet ef dbcontext script -p src/BicoCerto.Contratacoes.Api -o scripts/script-bd.sql
-- Tabelas: Orcamentos, Contratacoes e RegistrosIdempotencia (+ indices)
-- Obs.: a API tambem cria as tabelas sozinha na primeira execucao (EnsureCreated).
-- =============================================================================

CREATE TABLE [Orcamentos] (
    [Id] bigint NOT NULL IDENTITY,
    [SolicitacaoId] bigint NOT NULL,
    [ClienteId] bigint NOT NULL,
    [PrestadorId] bigint NOT NULL,
    [PrestadorNome] nvarchar(120) NOT NULL,
    [PrestadorEmail] nvarchar(150) NOT NULL,
    [Valor] decimal(10,2) NOT NULL,
    [PrazoDias] int NOT NULL,
    [Observacao] nvarchar(500) NULL,
    [Status] nvarchar(20) NOT NULL,
    [CriadoEm] datetime2 NOT NULL,
    [AtualizadoEm] datetime2 NOT NULL,
    CONSTRAINT [PK_Orcamentos] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [RegistrosIdempotencia] (
    [Id] bigint NOT NULL IDENTITY,
    [Chave] nvarchar(100) NOT NULL,
    [UsuarioId] bigint NOT NULL,
    [Rota] nvarchar(200) NOT NULL,
    [HashRequisicao] nvarchar(64) NOT NULL,
    [StatusCode] int NOT NULL,
    [CorpoResposta] nvarchar(max) NULL,
    [Location] nvarchar(300) NULL,
    [CriadoEm] datetime2 NOT NULL,
    CONSTRAINT [PK_RegistrosIdempotencia] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Contratacoes] (
    [Id] bigint NOT NULL IDENTITY,
    [OrcamentoId] bigint NOT NULL,
    [SolicitacaoId] bigint NOT NULL,
    [ClienteId] bigint NOT NULL,
    [PrestadorId] bigint NOT NULL,
    [ValorAcordado] decimal(10,2) NOT NULL,
    [DataAgendada] datetime2 NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [CriadaEm] datetime2 NOT NULL,
    [AtualizadaEm] datetime2 NOT NULL,
    CONSTRAINT [PK_Contratacoes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Contratacoes_Orcamentos_OrcamentoId] FOREIGN KEY ([OrcamentoId]) REFERENCES [Orcamentos] ([Id]) ON DELETE NO ACTION
);
GO


CREATE INDEX [IX_Contratacoes_ClienteId] ON [Contratacoes] ([ClienteId]);
GO


CREATE UNIQUE INDEX [IX_Contratacoes_OrcamentoId] ON [Contratacoes] ([OrcamentoId]);
GO


CREATE INDEX [IX_Contratacoes_PrestadorId] ON [Contratacoes] ([PrestadorId]);
GO


CREATE INDEX [IX_Orcamentos_PrestadorId] ON [Orcamentos] ([PrestadorId]);
GO


CREATE INDEX [IX_Orcamentos_SolicitacaoId] ON [Orcamentos] ([SolicitacaoId]);
GO


CREATE UNIQUE INDEX [IX_Orcamentos_SolicitacaoId_PrestadorId] ON [Orcamentos] ([SolicitacaoId], [PrestadorId]);
GO


CREATE UNIQUE INDEX [IX_RegistrosIdempotencia_Chave_UsuarioId_Rota] ON [RegistrosIdempotencia] ([Chave], [UsuarioId], [Rota]);
GO


