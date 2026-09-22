# Decisões técnicas

Registro das decisões de detalhe tomadas durante o desenvolvimento (seção 3 do prompt
de especificação: "para detalhes menores, escolha o mais simples e registre aqui").

## Sprint 0

### Versão do .NET
**Decisão:** .NET 10 (LTS, lançado em novembro de 2025 — a LTS mais recente na data de
início do projeto). Instalado localmente porque só havia .NET 6 e 9 (STS) na máquina.

### Provedor de e-mail
**Decisão:** Resend, escolhido pelo usuário na Sprint 0 (seção 4 permitia Resend, Brevo
ou Amazon SES). A implementação real de `IEmailSender` entra numa sprint futura; por ora
só o provedor `Fake` existe.

### Domínio de desenvolvimento
**Decisão:** `agendei.localhost`, escolhido pelo usuário na Sprint 0. Negócios ficam em
`{slug}.agendei.localhost` e o painel em `app.agendei.localhost`. Navegadores modernos
resolvem `*.localhost` para `127.0.0.1` sem configuração extra de DNS/hosts.

### Porta do PostgreSQL no host
**Decisão:** `5434` (em vez da 5432 padrão), porque a máquina de desenvolvimento já tinha
**dois** outros Postgres locais rodando, um na 5432 e outro na 5433 (de outros projetos —
`imobicrm-db` e `controlefacil-db`). Configurável via `POSTGRES_PORTA_HOST`.

### Convenção de nomes de tabela/coluna
**Decisão:** pacote `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention()`) em vez
de mapear `HasColumnName` manualmente em cada propriedade. Converte `PascalCase` (C#)
para `snake_case` (Postgres) automaticamente, mantendo a convenção para todas as
entidades futuras sem esforço extra.

### Filtro multi-tenant "fail closed"
**Decisão:** o *global query filter* por `NegocioId` (seção 8.3.1) usa
`e.NegocioId == IContextoNegocio.NegocioId`. Quando nenhum negócio foi resolvido na
requisição (`NegocioId` é `null`), a comparação nunca bate e nenhuma linha é retornada —
em vez de, por exemplo, ignorar o filtro. Prioriza segurança sobre conveniência: um bug
que esqueça de resolver o tenant vaza zero linhas, nunca linhas de outro negócio.

### Migrations em desenvolvimento
**Decisão:** a API aplica migrations pendentes automaticamente ao subir, mas **só**
quando `ASPNETCORE_ENVIRONMENT=Development` (feito em `Program.cs`). Em produção,
migrations continuam sendo um passo explícito de deploy (seção 8.5.5) — nunca automático.

### Variáveis `NEXT_PUBLIC_*` e build da imagem Docker do frontend
**Observação (não é bem uma decisão, é uma pegadinha do Next.js):** variáveis com
prefixo `NEXT_PUBLIC_` são embutidas no bundle do navegador **em tempo de build**, não
lidas em tempo de execução do container. Como o `docker-compose` só define variáveis em
tempo de execução, qualquer `NEXT_PUBLIC_*` que o frontend precisar exigirá passá-la como
`build.args` no `docker-compose.yml` e `ARG`/`ENV` no estágio de build do `Dockerfile`.
Por isso, `proxy.ts` lê `MARCA_DOMINIO` e `API_URL_INTERNA` **sem** o prefixo
`NEXT_PUBLIC_` — código de servidor (proxy/middleware, route handlers, server components) lê
`process.env` normalmente em tempo de execução, prefixo nenhum necessário. Quando algum
componente client-side precisar de uma URL pública da API, resolver então com build args.

### `middleware.ts` → `proxy.ts` (Next.js 16)
**Observação:** no Next.js 16, a convenção de arquivo `middleware.ts`/`export function
middleware` foi descontinuada em favor de `proxy.ts`/`export function proxy` (mesma ideia,
nome novo). Migramos com o codemod oficial (`npx @next/codemod@canary middleware-to-proxy .`)
assim que o `create-next-app` avisou sobre a depreciação no build. O comportamento é
idêntico; só o nome do arquivo e da função export mudou.

### Endpoint de resolução de negócio por slug explícito
**Decisão:** além de `GET /publico/negocio` (resolvido pelo host via
`ResolucaoNegocioMiddleware`), existe `GET /publico/negocios-por-slug/{slug}`, usado pelo
middleware do Next.js para resolução servidor-a-servidor antes de renderizar. Receber o
slug pela rota não fere a seção 8.3.2 (que proíbe o **painel** confiar em tenant vindo de
parâmetro/corpo/cabeçalho) porque aqui o dado devolvido já é público por definição — é
exatamente a mesma informação que apareceria na página pública do negócio.

### Falha da API ao resolver slug no proxy do Next.js
**Decisão:** se a chamada de `proxy.ts` para a API falhar (rede, API fora do ar), o proxy
**deixa passar a requisição** em vez de responder 404. Prioriza não esconder um negócio
real por uma instabilidade temporária de infraestrutura. Reavaliar se isso ainda fizer
sentido quando a página pública real existir (Sprint 3).
