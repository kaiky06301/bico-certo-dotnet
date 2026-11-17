-- =============================================================================
-- Bico Certo - Contratacoes API (.NET) - DDL para Azure SQL / SQL Server
-- Gerado a partir do modelo do EF Core:
--   dotnet ef dbcontext script -p src/BicoCerto.Contratacoes.Api -o scripts/script-bd.sql
-- Tabelas: Orcamentos, PropostasNegociacao, Contratacoes e RegistrosIdempotencia (+ indices)
-- Obs.: a API tambem cria as tabelas sozinha na primeira execucao (EnsureCreated).
-- =============================================================================
CREATE TABLE "Orcamentos" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Orcamentos" PRIMARY KEY AUTOINCREMENT,
    "SolicitacaoId" INTEGER NOT NULL,
    "ClienteId" INTEGER NOT NULL,
    "PrestadorId" INTEGER NOT NULL,
    "PrestadorNome" TEXT NOT NULL,
    "PrestadorEmail" TEXT NOT NULL,
    "Valor" TEXT NOT NULL,
    "ValorInicial" TEXT NOT NULL,
    "ReferenciaMinima" TEXT NOT NULL,
    "ReferenciaMaxima" TEXT NOT NULL,
    "PrazoDias" INTEGER NOT NULL,
    "Observacao" TEXT NULL,
    "Status" TEXT NOT NULL,
    "ValidoAte" TEXT NOT NULL,
    "IncluiMaoDeObra" INTEGER NOT NULL,
    "IncluiMaterial" INTEGER NOT NULL,
    "IncluiDeslocamento" INTEGER NOT NULL,
    "Adicionais" TEXT NULL,
    "CriadoEm" TEXT NOT NULL,
    "AtualizadoEm" TEXT NOT NULL
);


CREATE TABLE "RegistrosIdempotencia" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_RegistrosIdempotencia" PRIMARY KEY AUTOINCREMENT,
    "Chave" TEXT NOT NULL,
    "UsuarioId" INTEGER NOT NULL,
    "Rota" TEXT NOT NULL,
    "HashRequisicao" TEXT NOT NULL,
    "StatusCode" INTEGER NOT NULL,
    "CorpoResposta" TEXT NULL,
    "Location" TEXT NULL,
    "CriadoEm" TEXT NOT NULL
);


CREATE TABLE "Contratacoes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Contratacoes" PRIMARY KEY AUTOINCREMENT,
    "OrcamentoId" INTEGER NOT NULL,
    "SolicitacaoId" INTEGER NOT NULL,
    "ClienteId" INTEGER NOT NULL,
    "PrestadorId" INTEGER NOT NULL,
    "ValorAcordado" TEXT NOT NULL,
    "DataAgendada" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "CriadaEm" TEXT NOT NULL,
    "AtualizadaEm" TEXT NOT NULL,
    CONSTRAINT "FK_Contratacoes_Orcamentos_OrcamentoId" FOREIGN KEY ("OrcamentoId") REFERENCES "Orcamentos" ("Id") ON DELETE RESTRICT
);


CREATE TABLE "PropostasNegociacao" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PropostasNegociacao" PRIMARY KEY AUTOINCREMENT,
    "OrcamentoId" INTEGER NOT NULL,
    "Autor" TEXT NOT NULL,
    "AutorId" INTEGER NOT NULL,
    "AutorNome" TEXT NOT NULL,
    "Valor" TEXT NOT NULL,
    "Comentario" TEXT NULL,
    "Status" TEXT NOT NULL,
    "CriadaEm" TEXT NOT NULL,
    "RespondidaEm" TEXT NULL,
    CONSTRAINT "FK_PropostasNegociacao_Orcamentos_OrcamentoId" FOREIGN KEY ("OrcamentoId") REFERENCES "Orcamentos" ("Id") ON DELETE CASCADE
);


CREATE INDEX "IX_Contratacoes_ClienteId" ON "Contratacoes" ("ClienteId");


CREATE UNIQUE INDEX "IX_Contratacoes_OrcamentoId" ON "Contratacoes" ("OrcamentoId");


CREATE INDEX "IX_Contratacoes_PrestadorId" ON "Contratacoes" ("PrestadorId");


CREATE INDEX "IX_Orcamentos_PrestadorId" ON "Orcamentos" ("PrestadorId");


CREATE INDEX "IX_Orcamentos_SolicitacaoId" ON "Orcamentos" ("SolicitacaoId");


CREATE UNIQUE INDEX "IX_Orcamentos_SolicitacaoId_PrestadorId" ON "Orcamentos" ("SolicitacaoId", "PrestadorId");


CREATE INDEX "IX_PropostasNegociacao_OrcamentoId_Status" ON "PropostasNegociacao" ("OrcamentoId", "Status");


CREATE UNIQUE INDEX "IX_RegistrosIdempotencia_Chave_UsuarioId_Rota" ON "RegistrosIdempotencia" ("Chave", "UsuarioId", "Rota");


