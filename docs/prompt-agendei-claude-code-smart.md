# Prompt para o Claude Code — Agendei (SaaS de agendamento e gestão)

> **Agendei** é o nome de trabalho do produto. Ele pode mudar depois, por isso é tratado apenas como configuração (seção 5).

Você é o engenheiro de software responsável por construir o **Agendei**: um SaaS multi-tenant e white-label de gestão e agendamento para **barbearias, salões, clínicas de estética e profissionais autônomos** (barbeiros, manicures, terapeutas capilares etc.), em web responsivo e instalável (PWA).

A referência de experiência é o Figaroo. O cliente final acessa uma página do negócio (como `https://tiago-barber.figaroo.com`), toca em "Agendar" e percorre um assistente em etapas: serviços, data e horário, dados, resumo e confirmação.

Responda e comente o código em português do Brasil. Nomes de classes, tabelas e endpoints também em português, exceto termos técnicos consagrados.

---

## 1. Objetivo geral (SMART)

| | |
|---|---|
| **S — Específico** | Entregar o MVP do Agendei: página pública por subdomínio para cada negócio, assistente de agendamento em 4 etapas com **confirmação por código (WhatsApp + e-mail)**, agenda por profissional (horários, almoço, folgas), cadastros (usuários, permissões, profissionais, serviços, clientes), aviso por e-mail ao profissional, cupons, financeiro básico e programa de fidelidade. |
| **M — Mensurável** | (1) O cliente conclui um agendamento em até 90 segundos, sem contar a digitação do código. (2) Teste de concorrência com 100 requisições simultâneas para o mesmo horário resulta em exatamente 1 agendamento. (3) Nenhum endpoint anônimo devolve dado pessoal de terceiros (teste automatizado). (4) Listar horários livres responde em p95 abaixo de 300 ms com 1.000 agendamentos por profissional. (5) Cobertura de testes de pelo menos 70% nas regras de domínio e de segurança. (6) Página pública com Lighthouse mobile de pelo menos 85 em desempenho e 90 em acessibilidade. (7) Fluxo completo coberto por teste ponta a ponta (Playwright). |
| **A — Alcançável** | Stack que o desenvolvedor já domina (C# + Next.js), monólito modular, provedores `Fake` para desenvolvimento, sem app nativo e sem agente de IA no MVP (ver seção 2). Cada sprint lista o que pode ser cortado se o prazo apertar. |
| **R — Relevante** | Validar o produto com os primeiros negócios pagantes, reduzir faltas (código de confirmação + lembretes) e construir a base para as fases seguintes (agente de WhatsApp em n8n e app mobile). |
| **T — Temporal** | 6 sprints de 1 semana (Sprint 0 a 5): MVP em cerca de 6 semanas. Os prazos são metas de planejamento e podem ser ajustados; o que não couber é cortado pela lista "se apertar" de cada sprint, nunca pelas regras de segurança. |

## 2. Escopo

**Dentro do MVP:** tudo o que está nas seções 5 a 10 e nas sprints da seção 11.

**Fora do MVP (fase 2), mas com o terreno preparado:**
- **Agente de WhatsApp (n8n):** será um serviço à parte, contratado separadamente. Não implemente agente, caixa de conversas nem coexistência agora. Prepare apenas: regras de negócio na camada de Aplicação (reutilizáveis por uma API futura), `Idempotency-Key` na criação de agendamento e um modelo de configurações por negócio que aceite novos recursos.
- **App mobile do profissional** (com notificação push): hoje o aviso ao profissional é por e-mail. Implemente o aviso atrás da interface `INotificador` (seção 9) para que o canal de push entre depois sem refatorar.
- Atualização em tempo real do painel (SignalR), domínio próprio por cliente, múltiplas unidades, pagamento online (PIX e cartão), relatórios avançados, API pública com chaves e webhooks.

## 3. Como trabalhar

- Trabalhe **por sprints**. Execute apenas a sprint que eu pedir. Ao terminar, resuma o que foi feito, o que ficou pendente, quais métricas da sprint foram atingidas e espere minha aprovação.
- Antes de codar a Sprint 0, crie um `CLAUDE.md` na raiz com stack, convenções, comandos (build, testes, migrations, subir ambiente) e as **regras de segurança da seção 8**, para que valham em todas as sessões.
- Se houver ambiguidade que mude a arquitetura, pergunte antes. Para detalhes menores, escolha o mais simples e registre em `docs/decisoes.md`.
- Escreva testes junto com o código. Nenhuma sprint termina com testes falhando.
- Commits pequenos, com mensagens claras, uma branch por sprint.

## 4. Stack e arquitetura

- **Backend:** C# com a última versão LTS do .NET, ASP.NET Core Web API, EF Core (Npgsql), FluentValidation, Serilog. Monólito modular em camadas (Domínio, Aplicação, Infraestrutura, API).
- **Banco:** PostgreSQL com a extensão `btree_gist`.
- **Frontend:** Next.js (App Router) + TypeScript + Tailwind, mobile-first, tema claro e escuro, **PWA**.
- **Jobs:** Hangfire com storage em PostgreSQL (lembretes, expiração de reservas, backups agendados).
- **E-mail:** interface `IEmailSender` com implementação `Fake` (dev) e uma real (Resend, Brevo ou Amazon SES, a decidir na Sprint 0).
- **WhatsApp:** interface `IMensageriaWhatsApp` com `Fake` (dev e testes) e `Oficial` (API Cloud da Meta), usada no MVP **apenas para enviar o código de confirmação** e, opcionalmente por negócio, confirmações e lembretes. Deixe a interface pronta para receber, no futuro, um provedor `EvolutionApi` (não oficial) sem alterar quem a consome — mas **não implemente esse provedor nesta fase**; quando chegar a hora, ele entra atrás de uma flag (`WhatsApp:PermitirNaoOficial`, padrão `false`) e com aviso claro na documentação de que provedores não oficiais violam os termos do WhatsApp e podem levar ao banimento do número, então nunca deve ser o único canal para o código de confirmação.
- **Arquivos (fotos, logos):** interface `IArmazenamentoArquivos` (local em dev; Cloudflare R2 ou S3 fora dele).
- **Autenticação:** JWT de curta duração + refresh token em cookie `httpOnly`. Autorização por **permissões** (policies), não só por perfil.
- **Testes:** xUnit + Testcontainers (PostgreSQL real) no backend; Playwright no fluxo público.
- **Infra local:** `docker-compose` com PostgreSQL.

**Estrutura do repositório.** O projeto inteiro nasce dentro de uma pasta raiz chamada **`Agendei/`** (o nome da pasta é a única exceção à regra da seção 5, porque renomear uma pasta não afeta o código):

```
Agendei/
├── CLAUDE.md
├── docker-compose.yml
├── .env.example
├── backend/     (Plataforma.sln; src/Plataforma.Dominio, .Aplicacao, .Infraestrutura, .Api; tests/)
├── frontend/    (app Next.js)
├── scripts/     (backup.sh, restore.sh)
└── docs/        (decisoes.md, deploy.md, migracao.md)
```

## 5. Marca e white-label

O produto se chama **Agendei** hoje, mas o nome é **configuração**, nunca código:
- Nome do produto, domínio base, remetente de e-mail e nome exibido no WhatsApp vêm de variáveis de ambiente (`Marca__NomeProduto`, `Marca__Dominio`, `Marca__EmailRemetente`).
- **Nunca** use o nome do produto em identificadores de código, namespaces, nomes de tabelas ou arquivos. Use nomes internos neutros (ex.: solução `Plataforma`). A única exceção é o nome da pasta raiz do repositório (`Agendei/`).
- Textos de interface, e-mails e mensagens usam o nome do **negócio** (o cliente que contratou). O nome do produto aparece só no rodapé, no remetente do código de WhatsApp e no painel administrativo.

**Cada negócio (tenant) configura:** nome exibido, tipo (barbearia, salão, clínica de estética, autônomo), logo, cores primária e secundária, título e subtítulo da página inicial, texto "sobre", endereço (bairro, cidade, rua, número, CEP), telefone, redes sociais, horário de funcionamento e o **slug** do subdomínio.

**Endereços:**
- Página pública: `https://{slug}.{dominio}`.
- Painel: `https://app.{dominio}`.
- Em desenvolvimento use `{slug}.localhost`.

**Slug:** 3 a 30 caracteres `[a-z0-9-]`, único, com lista de reservados (`www`, `app`, `api`, `admin`, `mail`, `static`, `cdn` e similares).

Todo texto personalizado pelo negócio é **texto puro** (nunca HTML), sempre escapado ao renderizar.

## 6. Experiência do cliente final (baseada no Figaroo)

### 6.1 Página do negócio (`{slug}.{dominio}`)

Página de rolagem única, mobile-first, com as cores e o logo do negócio:
1. Topo com nome do negócio e botão **Agendar**.
2. Hero: título, subtítulo e botão **Agendar Agora**.
3. **Equipe:** foto, nome abreviado e função de cada profissional ativo.
4. **Serviços:** grupo "Mais procurados" (com selo) e categorias recolhíveis com contador. Cada serviço mostra nome, preço, duração e botão **Agendar**.
5. **Localização:** bairro e cidade, telefone, endereço e horário de funcionamento.
6. **Fale Conosco:** formulário (nome, telefone, e-mail, mensagem). Envia e-mail ao negócio, com rate limit e captcha.
7. Rodapé com o nome do negócio.

### 6.2 Assistente de agendamento

Tela cheia no celular, com barra de etapas por ícones (serviços, data e horário, dados, resumo), botões voltar e fechar, e **rodapé fixo** com o total em R$, o resumo (serviços e duração total) e o botão **Continuar**. Enquanto a etapa estiver incompleta, o botão fica desabilitado e o texto orienta ("Selecione um serviço", "Selecione um horário").

1. **Serviços:** categorias recolhíveis ("Populares" primeiro). Seleção **múltipla** (o botão `+` vira `✓`). O total e a duração somam em tempo real.
2. **Data & Horário:** seletor de mês, faixa horizontal de dias com o dia da semana e lista de horários na grade de **15 minutos** (configurável), considerando a duração total dos serviços. Com mais de um profissional, oferece "Qualquer profissional" (padrão) ou escolher um; com um só, a escolha fica oculta. Horários ocupados, passados, bloqueados ou fora do expediente não aparecem. Ao escolher um horário, ele fica **reservado por 10 minutos** (seção 8.2).
3. **Seus Dados:** telefone com seletor de país (padrão +55) e máscara, e-mail e nome completo, mais uma caixa de aceite **obrigatória**: "Ao agendar, concordo com os termos de serviço e aceito receber lembretes e confirmações dos meus agendamentos por e-mail e WhatsApp". Ao preencher, o cliente toca em enviar código, digita os 6 dígitos (reenvio com espera de 60 s, opção "Mudar dados") e, ao validar, os campos mostram o selo verde de verificado.
4. **Resumo:** blocos *Quando* (data, faixa de horário, dia da semana, botão Editar), *Onde* (bairro, cidade e endereço), *Serviços* (nome, "com {profissional}", preço e faixa de horário), campo **Cupom** com botão Aplicar, **Observações** para o profissional (opcional), **Total** e o botão **Confirmar Agendamento**.

O agendamento só é criado ao confirmar o resumo e **somente se a verificação por código estiver válida** (seção 8.1). O total é sempre recalculado no servidor.

### 6.3 Depois de agendar

- Tela de sucesso com o resumo, botão "Adicionar ao calendário" (arquivo `.ics`) e link para cancelar ou remarcar.
- E-mail de confirmação imediato (e WhatsApp, se o negócio ativar), com link seguro (token) para cancelar ou remarcar, respeitando uma antecedência mínima configurável (padrão: 2 horas).

## 7. Regras de negócio (painel)

**Usuários e permissões.** Perfis iniciais: Administrador, Recepcionista, Profissional. O administrador define, por usuário, quais funções ele acessa e altera (ex.: ver a agenda de outros profissionais, editar clientes, ver o financeiro). Cadastro de usuário e de profissional: foto de perfil, nome, telefone, e-mail, endereço e CPF.

**Profissionais e agenda.**
- Horário de trabalho **individual** por dia da semana, com um ou mais intervalos (incluindo almoço), todos editáveis.
- Bloqueios e folgas por período (horas ou dias): enquanto bloqueado, o horário não é oferecido.
- Serviços que cada profissional executa, com preço e duração próprios opcionais.
- Agenda no painel em visão de dia e de semana, com criar, editar, mover, cancelar, encaixe manual e status (`Agendado`, `Concluido`, `Cancelado`, `Faltou`).

**Serviços.** Categoria, nome, preço, duração, indicador "popular" (grupo Mais procurados) e ativo/inativo.

**Clientes.** Ficha com contato, observações e histórico de atendimentos. O telefone é normalizado para E.164 e é a chave de identificação do cliente dentro do negócio.

**Cupons.** Código, tipo (percentual ou valor fixo), validade, limite de usos e escopo (todos os serviços ou selecionados). É revalidado no servidor ao criar o agendamento.

**Financeiro.** Registro do pagamento por atendimento, faturamento por período, profissional e serviço, e painel simples do mês.

**Fidelidade.** Cartão de selos: a cada X atendimentos concluídos, uma recompensa configurável pelo administrador.

## 8. Segurança (prioridade máxima)

### 8.1 Código de confirmação e identificação do cliente

O risco: qualquer pessoa poderia agendar em nome de um número alheio, gastar as mensagens do negócio ou descobrir quem é cliente. Implemente assim:

1. **Fluxo obrigatório:**
   - (a) O cliente informa telefone, e-mail, nome e aceite.
   - (b) `POST /publico/codigos` gera um código de 6 dígitos e o envia **pelos dois canais** (WhatsApp e e-mail), o mesmo código nos dois.
   - (c) `POST /publico/codigos/validar` confere o código e devolve um `tokenVerificacao` assinado, com validade de 15 minutos, vinculado ao negócio e ao telefone.
   - (d) `POST /publico/agendamentos` **exige** o token. Sem ele, ou com token de outro telefone ou negócio, a resposta é 401/403.

   Digitar o código comprova acesso a pelo menos um dos canais; a identidade do cliente é o **telefone**.
2. **O código:** gerado com aleatoriedade criptograficamente segura, guardado apenas como **hash**, válido por 5 minutos, de uso único, no máximo 5 tentativas. Pedir um novo código invalida o anterior.
3. **Anti-enumeração:** a resposta de solicitar o código é idêntica (corpo, status e tempo aproximado) exista o telefone no cadastro ou não. Nenhum endpoint anônimo devolve nome, e-mail, CPF, endereço, histórico ou qualquer dado de cliente, nem diz "cliente já cadastrado".
4. **Identificação automática:** ao criar o agendamento, o sistema busca o `Cliente` por `(negocio_id, telefone_e164)`, com **índice único**.
   - Se existe, vincula o agendamento a ele **sem sobrescrever** dados (preenche apenas campos vazios, como o e-mail; se o nome informado for diferente, guarda em `nome_informado` no agendamento para a recepção ver).
   - Se não existe, cria o cliente com origem "link público".
   - Tudo na mesma transação; duas requisições simultâneas com o mesmo telefone não podem gerar cadastros duplicados.
5. **Abuso e custo:**
   - Rate limit por IP, telefone, e-mail e negócio.
   - No máximo 3 códigos por telefone por hora e 10 por dia (configurável).
   - Teto diário de códigos por WhatsApp por negócio: ao atingir, envia só por e-mail.
   - Captcha invisível (Cloudflare Turnstile) quando detectar abuso.
   - Não envia código se o horário escolhido já não estiver disponível.
6. Nenhum dado pessoal em logs; telefones mascarados (`+55 71 9****-6802`); o código nunca é logado (exceto no provedor `Fake` em desenvolvimento).

**Testes obrigatórios:** agendar sem token; token de outro telefone ou negócio; código expirado, reutilizado e a 6ª tentativa; respostas indistinguíveis entre telefone existente e novo; rate limit e teto diário; vínculo com cliente existente sem sobrescrever dados; concorrência na criação de cliente com o mesmo telefone; nenhum endpoint anônimo devolve campo pessoal.

### 8.2 Conflito de horário (dois clientes no mesmo horário)

O risco: dois clientes escolhem o mesmo horário quase juntos e ambos conseguem agendar. Checar "está livre?" e depois inserir não basta.

1. **Garantia no banco:** *exclusion constraint* do PostgreSQL, criada em migration com SQL puro:
   `EXCLUDE USING gist (profissional_id WITH =, tstzrange(inicio, fim) WITH &&) WHERE (status IN ('Reservado','Agendado','Concluido'))`.
   Mesmo com bug na aplicação ou requisições paralelas, o banco recusa a sobreposição.
2. **Reserva temporária:** ao escolher um horário no assistente, cria-se uma linha `Reservado` com validade de 10 minutos, para o cliente digitar dados e código sem perder o horário. Como a constraint não enxerga `now()`, um job (Hangfire) expira reservas vencidas **e** a criação de qualquer agendamento expira, na mesma transação, as reservas vencidas do profissional naquele intervalo antes de inserir. Confirmar o agendamento transforma a reserva em `Agendado`.
3. **Criação em transação única.** Dentro dela valide também expediente, almoço e bloqueios do profissional (o que a constraint não cobre).
4. Ao receber a violação da constraint, devolva **HTTP 409** com mensagem amigável e os próximos horários livres. Nunca devolva erro 500 nem detalhes do banco.
5. Vários serviços ocupam **um único intervalo contínuo** (soma das durações).
6. Horários em **UTC** no banco; cada negócio tem seu fuso (padrão `America/Sao_Paulo`).
7. Cancelar ou remarcar libera o horário imediatamente.

**Testes obrigatórios:** 100 requisições simultâneas para o mesmo horário resultam em exatamente 1 vencedora; sobreposição parcial; almoço; folga; reserva expirada liberando o horário; cancelamento liberando o horário.

### 8.3 Multi-tenant e subdomínios

1. Toda entidade de negócio tem `NegocioId`, com *global query filter* do EF Core. Testes provam que um negócio nunca enxerga dados de outro.
2. **No painel, o negócio vem sempre do token JWT**, nunca de parâmetro, corpo ou cabeçalho. Nos endpoints públicos, o negócio é resolvido pelo slug (host) e só dá acesso a dados públicos.
3. O middleware do Next.js resolve o slug pelo host e responde 404 para slug inexistente. Em produção é necessário DNS e certificado **wildcard** (`*.{dominio}`).
4. **Cookies do painel** são *host-only* em `app.{dominio}`, nunca no domínio `.{dominio}`, para que as páginas públicas dos negócios não os recebam.
5. CORS com lista de origens derivada do domínio base; proteção contra CSRF; cabeçalhos de segurança.

### 8.4 Dados pessoais (LGPD) e segurança geral

- **CPF criptografado** em repouso (chave fora do repositório, versionada com `chave_id`), mascarado na interface (`***.456.789-**`) e completo apenas para quem tem permissão.
- Consentimento registrado no agendamento (data, IP, versão dos termos) e política de privacidade acessível na página pública.
- Exportação e exclusão/anonimização dos dados de um cliente sob demanda.
- Upload de foto e logo: só imagens, tamanho limitado, reprocessadas.
- HTTPS obrigatório, senhas com Argon2 ou bcrypt, segredos apenas em variáveis de ambiente ou user-secrets.

### 8.5 Portabilidade, backup e migração

O sistema começará em planos gratuitos e migrará depois para serviços pagos, **sem reescrever código e sem perder dados**:
1. **Containers:** `Dockerfile` da API e do front (multi-stage, usuário sem privilégios) e um `docker-compose` de produção para VPS com Caddy.
2. **Configuração 100% por variáveis de ambiente**, com `.env.example` documentado e validação das obrigatórias na inicialização.
3. **Sem estado no disco local:** fotos e logos via `IArmazenamentoArquivos`.
4. **Chaves portáveis:** chave do CPF versionada e chaves do ASP.NET Data Protection guardadas no banco ou em armazenamento externo. Documente exportar e importar.
5. **PostgreSQL padrão** + `btree_gist`, migrations versionadas aplicadas por passo explícito de deploy. Confirme `CREATE EXTENSION btree_gist` no provedor escolhido já na Sprint 0.
6. **Tolerância à hibernação dos planos gratuitos:** `/health`, retry com backoff na conexão com o banco, e lembretes que sobrevivem quando a API dorme (ao voltar, envia os pendentes ainda válidos e descarta os vencidos).
7. **Backup:** `scripts/backup.sh` (`pg_dump` custom, comprimido, **criptografado**, enviado para R2 ou S3, com retenção) e `scripts/restore.sh`, com **teste automatizado de restauração** (tabelas presentes, constraint de horários presente, isolamento por negócio funcionando).
8. **Modo manutenção** (503 amigável e criação de agendamentos bloqueada) para a virada de uma migração.
9. `docs/deploy.md` (teste gratuito com Vercel + Render + Neon, e VPS único com Caddy) e `docs/migracao.md` (subir o novo ambiente, restaurar, testar, baixar o TTL do DNS, ativar manutenção, apontar o domínio, manter o antigo em leitura, reversão).

**Teste obrigatório:** subir o sistema do zero em ambiente limpo usando apenas variáveis de ambiente e um backup restaurado, sem alterar código.

## 9. Notificações

Tudo passa pela interface `INotificador`, com canais `Email`, `WhatsApp` e `Push` (este último só como contrato vazio para a fase 2).

| Evento | Destinatário | Canais no MVP |
|---|---|---|
| Código de confirmação | Cliente | WhatsApp + e-mail |
| Agendamento confirmado | Cliente | E-mail (WhatsApp opcional por negócio) |
| Novo, remarcado ou cancelado | **Profissional** | **E-mail** (push na fase 2) |
| Lembrete (24 h e 2 h antes, configurável) | Cliente | E-mail (WhatsApp opcional por negócio) |
| Fale Conosco | Negócio | E-mail |

- O e-mail ao profissional traz cliente, serviços, horário e observações do cliente.
- Envios de WhatsApp fora o código são **opcionais e configuráveis por negócio**, porque cada mensagem tem custo na Meta.
- Configure SPF, DKIM e DMARC no domínio de envio e documente em `docs/deploy.md`.
- Lembretes e expiração de reservas rodam em jobs (Hangfire).

## 10. Modelo de dados inicial (ajuste se necessário)

`Negocio` (tenant: slug, tipo, fuso, marca, endereço, horário de funcionamento) · `Usuario` · `Permissao` / `UsuarioPermissao` · `Profissional` · `Categoria` · `Servico` · `ProfissionalServico` · `HorarioTrabalho` (dia da semana, início, fim, intervalos) · `BloqueioAgenda` · `Cliente` · `Agendamento` (status, `nome_informado`, observações, consentimento) · `AgendamentoServico` · `Cupom` · `CodigoVerificacao` (apenas hash) · `Notificacao` · `Pagamento` · `ProgramaFidelidade` · `SeloCliente` · `MensagemContato`.

## 11. Sprints (SMART)

**Sprint 0 — Fundação e marca configurável (semana 1)**
- **S:** solução .NET e app Next.js, docker-compose, CI básico, `CLAUDE.md`, Serilog, tratamento global de erros, multi-tenant com global query filter, configuração de marca (`Marca__*`), resolução do negócio pelo subdomínio, Dockerfiles, `.env.example` com validação, `/health` e checagem do `btree_gist`.
- **M:** `docker compose up` sobe tudo; `acme.localhost` resolve o negócio `acme` e slug inexistente dá 404; teste de isolamento entre negócios passa; nenhuma ocorrência do nome do produto fora da configuração e do nome da pasta raiz.
- **A:** só esqueleto, sem telas de negócio.
- **R:** tudo o mais depende de tenant, marca e portabilidade corretos desde o início.
- **T:** 1 semana.
- **Se apertar, corte:** CI além do build e testes.

**Sprint 1 — Acesso e cadastros (semana 2)**
- **S:** login e refresh, usuários, perfis e permissões configuráveis, cadastro de profissionais (foto, dados, CPF criptografado), serviços (categorias, populares), clientes, perfil do negócio (marca, endereço, horário), `scripts/backup.sh`, `scripts/restore.sh`.
- **M:** permissões bloqueiam ações sem acesso (teste por perfil); CPF nunca aparece em texto puro no banco nem nos logs; teste de restauração de backup passa.
- **A:** telas simples e responsivas, sem agenda ainda.
- **R:** o negócio precisa estar configurado para existir uma página pública.
- **T:** 1 semana.
- **Se apertar, corte:** foto de perfil (deixar iniciais como avatar).

**Sprint 2 — Agenda e disponibilidade (semana 3)**
- **S:** horários individuais, almoço, folgas e bloqueios, cálculo de horários livres na grade de 15 min, agenda no painel (dia e semana), encaixe manual, agendamento com a **constraint e a reserva temporária da seção 8.2**.
- **M:** teste de concorrência (100 requisições, 1 vencedora); listar horários em p95 abaixo de 300 ms com 1.000 agendamentos por profissional; testes de almoço, folga e expiração de reserva passam.
- **A:** sem página pública ainda; o painel cria os agendamentos.
- **R:** é o coração do produto e o maior risco técnico.
- **T:** 1 semana.
- **Se apertar, corte:** visão semanal (manter só a de dia).

**Sprint 3 — Página pública, assistente e código de confirmação (semana 4)**
- **S:** página do negócio (seção 6.1), assistente em 4 etapas (6.2), cupom, observações, **código por WhatsApp + e-mail conforme 8.1**, identificação automática do cliente pelo telefone, tela de sucesso, `.ics`, cancelar e remarcar por link. Provedores `Fake` primeiro; e-mail real; WhatsApp `Oficial` habilitável por configuração.
- **M:** todos os testes obrigatórios de 8.1; fluxo completo coberto por Playwright; Lighthouse mobile de pelo menos 85 em desempenho e 90 em acessibilidade; agendar em até 90 s sem contar o código.
- **A:** WhatsApp real depende do cadastro na Meta (seção 12); sem ele, o código vai por e-mail e o `Fake` cobre os testes.
- **R:** é a entrega que o cliente final vê e o motivo de contratar o produto.
- **T:** 1 semana.
- **Se apertar, corte:** remarcar por link (manter cancelar) e `.ics`.

**Sprint 4 — Notificações e financeiro (semana 5)**
- **S:** e-mail ao profissional (novo, remarcado, cancelado), confirmação e lembretes ao cliente com Hangfire, "Fale Conosco", pagamentos, faturamento por período, profissional e serviço, painel do mês.
- **M:** e-mail ao profissional em até 1 minuto após o agendamento (com o serviço ativo); lembretes sobrevivem à hibernação da API (teste); SPF/DKIM/DMARC documentados; painel do mês bate com os pagamentos registrados.
- **A:** só e-mail para o profissional; push fica para a fase 2.
- **R:** o profissional precisa saber do agendamento, e o dono precisa enxergar o faturamento.
- **T:** 1 semana.
- **Se apertar, corte:** filtro de faturamento por serviço.

**Sprint 5 — Fidelidade e acabamento (semana 6)**
- **S:** programa de selos, PWA polido, tema claro/escuro, LGPD (exportar e excluir), modo manutenção, `docs/deploy.md`, `docs/migracao.md`, revisão de segurança e de desempenho.
- **M:** teste de subir tudo do zero só com variáveis de ambiente e um backup; revisão de todos os itens das seções 8.1 a 8.5 sem pendência; métricas 1 a 7 da seção 1 atingidas ou justificadas.
- **A:** sem novos recursos além dos listados.
- **R:** deixa o produto pronto para o primeiro cliente pagante e para migrar de hospedagem.
- **T:** 1 semana.
- **Se apertar, corte:** tema escuro.

## 12. Pré-requisitos fora do código (fazer em paralelo, desde a Sprint 0)

Estes itens têm prazo de terceiros e podem travar a Sprint 3 se ficarem para o fim:
- Verificar o nome do produto no INPI, no registro.br e no Instagram antes de registrar domínio e conta da Meta (renomear depois é caro nessas duas frentes).
- Registrar o domínio, com DNS e certificado **wildcard**.
- Conta Meta Business verificada, número de WhatsApp oficial do produto, **template de autenticação** em português aprovado (código com botão "Copiar código") e forma de pagamento cadastrada na Meta.
- Escolher e configurar o provedor de e-mail (com SPF, DKIM e DMARC).
- Conta Cloudflare (Turnstile e R2).

## 13. Critérios de aceite gerais

- Toda a experiência funciona bem em celular e em desktop.
- O agendamento só existe depois de o código ser validado.
- Nenhum endpoint público expõe dado pessoal de terceiros.
- É impossível existir dois agendamentos sobrepostos para o mesmo profissional, provado por teste de concorrência.
- O nome do produto é apenas configuração, e trocá-lo não exige alterar código.
- Migrar de um provedor para outro exige apenas variáveis de ambiente e um backup restaurado.
- Testes passando e `CLAUDE.md` atualizado ao final de cada sprint.

## 14. Comece agora

Leia este documento inteiro. Confira se o diretório atual já é a pasta raiz **`Agendei/`** (rode `pwd` e `ls -a`): se sim, use-o como raiz; se não, crie a pasta `Agendei/` e entre nela. Rode `git init` (pule se já houver um `.git`) e inicialize todo o projeto a partir dessa raiz, na estrutura da seção 4. Depois crie o `CLAUDE.md` e execute **somente a Sprint 0**. Antes de codar, me pergunte: (1) qual provedor de e-mail usar (Resend, Brevo ou Amazon SES); (2) qual domínio de desenvolvimento usar. Ao final, me mostre o resumo, as métricas atingidas e as decisões que precisam da minha aprovação antes da Sprint 1.
