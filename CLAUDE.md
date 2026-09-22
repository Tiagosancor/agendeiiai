# CLAUDE.md — Agendei

Guia para trabalhar neste repositório. Vale para todas as sessões, em qualquer sprint.
A especificação completa está em `docs/prompt-agendei-claude-code-smart.md` — este
arquivo é o resumo operacional; a especificação é a fonte da verdade em caso de dúvida.

## O que é o projeto

SaaS multi-tenant e white-label de agendamento e gestão para barbearias, salões,
clínicas de estética e profissionais autônomos. Nome de trabalho: **Agendei** (é
configuração, nunca código — ver "Marca e white-label" abaixo).

Trabalhamos **por sprints** (seção 11 da especificação). Só execute a sprint pedida;
ao terminar, resuma o que foi feito, o que ficou pendente, as métricas atingidas e
espere aprovação antes de seguir para a próxima.

## Stack

- **Backend:** C# / .NET 10 (LTS), ASP.NET Core Web API, EF Core (Npgsql), FluentValidation,
  Serilog. Monólito modular em camadas: `Plataforma.Dominio`, `.Aplicacao`,
  `.Infraestrutura`, `.Api`.
- **Banco:** PostgreSQL (17 em dev) com a extensão `btree_gist`.
- **Frontend:** Next.js 16 (App Router) + TypeScript + Tailwind, mobile-first, PWA.
  Next.js 16 usa `proxy.ts` (não `middleware.ts` — convenção descontinuada; ver
  `docs/decisoes.md`).
- **Jobs:** Hangfire com storage em PostgreSQL.
- **E-mail:** interface `IEmailSender`; provedor real decidido: **Resend** (`Fake` em dev).
- **WhatsApp:** interface `IMensageriaWhatsApp`; `Fake` em dev/testes, `Oficial` (API Cloud
  da Meta) para produção. Provedores não oficiais (Baileys, Evolution) são **proibidos**
  como único canal do código de confirmação — ver seção 4 da especificação para os
  detalhes de quando/como um provedor não oficial poderia entrar, futuramente, atrás de flag.
- **Arquivos:** interface `IArmazenamentoArquivos` (local em dev; R2/S3 fora dele).
- **Autenticação:** JWT curto + refresh token em cookie `httpOnly`; autorização por
  permissões (policies), não só por perfil.
- **Testes:** xUnit + Testcontainers (Postgres real) no backend; Playwright no fluxo
  público (a partir da Sprint 3).

## Estrutura do repositório

```
Agendei/
├── CLAUDE.md
├── docker-compose.yml
├── .env.example
├── backend/
│   ├── Plataforma.slnx
│   ├── src/
│   │   ├── Plataforma.Dominio/         # entidades, value objects, regras de negócio puras
│   │   ├── Plataforma.Aplicacao/       # casos de uso, abstrações (interfaces) consumidas pela API
│   │   ├── Plataforma.Infraestrutura/  # EF Core, provedores concretos, DI
│   │   └── Plataforma.Api/             # controllers, middlewares, Program.cs (composition root)
│   └── tests/
│       ├── Plataforma.Testes.Unidade/     # sem banco — Dominio, lógica isolada
│       └── Plataforma.Testes.Integracao/  # Testcontainers + WebApplicationFactory
├── frontend/    # Next.js
├── scripts/     # backup.sh, restore.sh
└── docs/        # decisoes.md, deploy.md, migracao.md, prompt-agendei-claude-code-smart.md
```

## Marca e white-label

O nome do produto (**Agendei**) é configuração, nunca código:
- Vem de variáveis de ambiente (`Marca__NomeProduto`, `Marca__Dominio`,
  `Marca__EmailRemetente`), lidas via `OpcoesMarca` (`Plataforma.Infraestrutura.Opcoes`).
- **Nunca** usar o nome do produto em identificadores de código, namespaces, tabelas ou
  arquivos. A solução interna se chama `Plataforma`. A única exceção é o nome da pasta
  raiz do repositório (`Agendei/`).
- Textos de interface, e-mails e mensagens usam o nome do **negócio** (tenant), não o do
  produto.

## Convenções

- Comentários e nomes de classes/tabelas/endpoints em **português do Brasil**, exceto
  termos técnicos consagrados.
- Banco: `snake_case` automático via `EFCore.NamingConventions`
  (`UseSnakeCaseNamingConvention()`) — não mapeie `HasColumnName` manualmente a não ser
  que precise fugir da convenção.
- Multi-tenant: toda entidade de um negócio implementa `IEntidadeDoNegocio`
  (`Plataforma.Dominio.Comum`) — o *global query filter* do `PlataformaDbContext` cobre
  automaticamente qualquer entidade que implemente essa interface, sem código extra.
  **Nunca** capture o tenant como `Expression.Constant` num filtro do EF Core — o modelo é
  cacheado entre instâncias do `DbContext` e isso trava todo mundo no tenant da primeira
  requisição. Use o padrão em `PlataformaDbContext.AplicarFiltroDoNegocio<T>` (método de
  instância + reflection), que o EF reavalia por instância corretamente.
- Configuração: **100% por variáveis de ambiente** (seção 8.5.2). Nunca eager-capture um
  valor de `IConfiguration` em uma variável local antes de registrar um serviço — isso
  quebra testes com `WebApplicationFactory`, que só consegue sobrescrever configuração
  logo antes do `Build()`. Resolva `IConfiguration` (ou `IOptions<T>`) de dentro do
  delegate/factory do serviço (ver `InfraestruturaServiceCollectionExtensions`).
- Ambiguidade que mude arquitetura → perguntar antes. Detalhe menor → decidir e registrar
  em `docs/decisoes.md`.
- Commits pequenos, mensagens claras, uma branch por sprint.

## Comandos

### Backend

.NET 10 pode não estar em `PATH` por padrão nesta máquina — se `dotnet --version` não
mostrar `10.x`, rode com `export PATH="$HOME/AppData/Local/dotnet:$PATH"` antes (bash) ou
`$env:PATH = "$env:LOCALAPPDATA\dotnet;$env:PATH"` (PowerShell). Isso já foi adicionado ao
PATH do usuário via `setx`, então sessões/terminais **novos** devem pegar automaticamente.

```bash
cd backend

dotnet build                                    # compilar tudo
dotnet test                                     # todos os testes (precisa de Docker para os de integração)
dotnet test tests/Plataforma.Testes.Unidade     # só unitários (rápido, sem Docker)
dotnet run --project src/Plataforma.Api         # subir a API sozinha (precisa de Postgres — docker-compose ou local)

# Migrations (dotnet-ef instalado como global tool)
dotnet ef migrations add NomeDaMigration \
  --project src/Plataforma.Infraestrutura --startup-project src/Plataforma.Api \
  --output-dir Persistencia/Migracoes

dotnet ef database update \
  --project src/Plataforma.Infraestrutura --startup-project src/Plataforma.Api
```

Em **Development**, a API aplica migrations pendentes sozinha ao subir e semeia um
negócio "acme" de exemplo se o banco estiver vazio (`SemeadorDesenvolvimento`). Em
produção isso não acontece — migrations são passo explícito de deploy (seção 8.5.5).

### Frontend

```bash
cd frontend
npm run dev      # dev server
npm run lint
npm run build
```

### Ambiente completo (docker-compose)

```bash
cp .env.example .env    # primeira vez; ajuste se precisar
docker compose up -d
docker compose logs -f api        # ou frontend / postgres
docker compose down               # para tudo (mantém o volume do Postgres)
```

Depois de subir: API em `http://localhost:5080` (`/health`, `/publico/negocio` via
`Host: {slug}.agendei.localhost`), frontend em `http://localhost:3000`. Teste local com
`curl -H "Host: acme.agendei.localhost" http://localhost:5080/publico/negocio` (não dá
para usar subdomínios `*.localhost` de verdade contra `localhost:PORTA` sem um proxy
reverso — em dev local sem Docker, o navegador resolve `*.localhost` sozinho contra
`127.0.0.1`, mas a porta continua tendo que ser informada, e o Next dev server também
precisa do `Host` correto: prefira acessar via `acme.agendei.localhost:3000` diretamente
no navegador quando não estiver atrás de proxy).

**Portas do host já ocupadas nesta máquina por outros projetos:** 5432
(`imobicrm-db`) e 5433 (`controlefacil-db`). O Postgres deste projeto usa **5434**
(`POSTGRES_PORTA_HOST`). Se outra porta do `docker-compose.yml` colidir no seu ambiente,
ajuste via `.env`.

## Segurança (seção 8 da especificação — prioridade máxima)

Resumo operacional; a especificação tem o detalhe completo e os testes obrigatórios de
cada regra.

### 8.1 — Código de confirmação e identificação do cliente
- Fluxo: `POST /publico/codigos` → `POST /publico/codigos/validar` (devolve
  `tokenVerificacao`, 15 min, vinculado a negócio+telefone) → `POST /publico/agendamentos`
  **exige** o token (401/403 sem ele ou de outro telefone/negócio).
- Código: 6 dígitos, aleatoriedade criptográfica, guardado só como **hash**, válido 5 min,
  uso único, máx. 5 tentativas. Novo código invalida o anterior.
- **Anti-enumeração:** resposta de solicitar código é idêntica exista ou não o telefone.
  Nenhum endpoint anônimo devolve dado pessoal de terceiros nem confirma cadastro.
- Cliente identificado por `(negocio_id, telefone_e164)`, índice único, vínculo sem
  sobrescrever dados existentes, tudo em transação única.
- Rate limit por IP/telefone/e-mail/negócio; teto diário de WhatsApp por negócio; captcha
  invisível sob abuso.
- Nenhum dado pessoal em log; telefone mascarado; código nunca logado (exceto `Fake` em dev).

### 8.2 — Conflito de horário
- *Exclusion constraint* do Postgres (`EXCLUDE USING gist`) é a garantia real —
  a aplicação nunca confia só na checagem prévia de disponibilidade.
- Reserva temporária (`Reservado`, 10 min) antes da confirmação; reservas vencidas
  expiram via job **e** na própria transação de criação, antes de inserir.
- Violação da constraint → **HTTP 409** amigável com próximos horários livres. Nunca 500
  nem detalhe de banco.
- Horários em UTC no banco; fuso por negócio (padrão `America/Sao_Paulo`).

### 8.3 — Multi-tenant e subdomínios
- Toda entidade de negócio tem `NegocioId` com *global query filter* (ver "Convenções"
  acima) — testado em `FiltroMultiTenantTestes` e `ResolucaoNegocioTestes`.
- **Painel:** negócio sempre do JWT, nunca de parâmetro/corpo/cabeçalho.
- **Público:** negócio resolvido pelo slug do host (`ResolucaoNegocioMiddleware` na API;
  `proxy.ts` no frontend, que consulta `/publico/negocios-por-slug/{slug}`).
- Cookies do painel são *host-only* em `app.{dominio}`, nunca em `.{dominio}`.
- CORS com lista de origens derivada do domínio base; proteção CSRF; cabeçalhos de segurança.

### 8.4 — LGPD e segurança geral
- CPF criptografado em repouso, mascarado na interface, completo só com permissão.
- Consentimento registrado (data, IP, versão dos termos); exportação/exclusão sob demanda.
- HTTPS obrigatório em produção; senhas com Argon2/bcrypt; segredos só em variável de
  ambiente/user-secrets — nunca hardcoded, nunca commitado (`.env` está no `.gitignore`).

### 8.5 — Portabilidade, backup e migração
- Tudo por variável de ambiente, com validação das obrigatórias na inicialização
  (`OpcoesMarca` valida via Data Annotations + `ValidateOnStart`; a connection string
  falha rápido se ausente — ver `InfraestruturaServiceCollectionExtensions`).
- Sem estado em disco local (arquivos via `IArmazenamentoArquivos`).
- `docker compose up` sobe tudo do zero (validado manualmente na Sprint 0).
- Retry com backoff na conexão com o banco (`EnableRetryOnFailure`) — tolerância à
  hibernação de planos gratuitos.
- Backup: `scripts/backup.sh`/`restore.sh` com teste automatizado de restauração
  (a partir da Sprint 1).

## Onde estão as coisas (referência rápida)

| O quê | Onde |
|---|---|
| Entidade `Negocio`, `Slug`, `TipoNegocio` | `backend/src/Plataforma.Dominio/Negocios/` |
| `IEntidadeDoNegocio`, `EntidadeBase` | `backend/src/Plataforma.Dominio/Comum/` |
| `IContextoNegocio` | `backend/src/Plataforma.Aplicacao/Abstracoes/` |
| `PlataformaDbContext` (filtro multi-tenant) | `backend/src/Plataforma.Infraestrutura/Persistencia/` |
| `OpcoesMarca` | `backend/src/Plataforma.Infraestrutura/Opcoes/` |
| DI (`AdicionarInfraestrutura`) | `backend/src/Plataforma.Infraestrutura/DependencyInjection/` |
| `ResolucaoNegocioMiddleware`, `TratamentoGlobalErrosMiddleware` | `backend/src/Plataforma.Api/Middlewares/` |
| `Program.cs` (composition root) | `backend/src/Plataforma.Api/` |
| `proxy.ts` (resolução de subdomínio no front) | `frontend/src/proxy.ts` |
| Decisões técnicas registradas | `docs/decisoes.md` |
| Especificação completa | `docs/prompt-agendei-claude-code-smart.md` |
