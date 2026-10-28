# Histórico e fila do projeto — Agendeiiai

Última atualização: 2026-10-17
Estado Git verificado: `main` = `origin/main` =
`5e8d803fb633abed151603ba1950a918a19d91c7`

## Reconciliação atual — 2026-10-16

Este snapshot prevalece sobre os estados e a fila histórica abaixo, que ainda descrevem
etapas anteriores. O registro detalhado intermediário está em `HISTORICO-E-FILA.local.md`.

- B7.4-C, monitoramento administrativo da outbox: integrada e publicada pelo PR #21,
  merge `4053edb0f244d6bf4e24bdf40458bd53c8af51a3`, em 15/10/2026 às 14:54:34
  (America/Fortaleza). Head homologado: `e454fadfc71565a51f96bb214f84a613f42a1a48`.
- GitHub consultado em 16/10: PR merged; main no mesmo merge; CI Backend (.NET) e
  Frontend (Next.js) aprovadas, run `38781229376`. Árvore do merge idêntica à do head
  homologado (`git diff e454fad HEAD` vazio).
- Vercel Production: success para o merge; Railway: success para o mesmo merge,
  deployment `f476944b-02ec-4588-853a-8d80f63ac006`, conforme status do GitHub.
- Smoke somente leitura em 16/10: API `/health`, landing, login do painel, privacidade,
  `sw.js`, manifesto do painel e `/plataforma/notificacoes` responderam 200. Os endpoints
  administrativos `/plataforma/notificacoes/resumo` e `/outbox` responderam 401 sem token.
  A resposta 200 da página não comprova consulta autenticada nem leitura da fila.
- Homologação anterior registrada: lint sem errors (12 warnings preexistentes), TypeScript,
  build, 16/16 E2E isolados, 1/1 smoke full-stack e 2/2 regressões administrativas.
  Nesta reconciliação não se repetiu a suíte; a CI do merge foi consultada.
- Main local atualizada por fast-forward. Nenhum novo merge remoto, deploy, migration,
  alteração de flags, mutação de dados ou comunicação real foi executado nesta sessão.
- Já integrados no Git e fora da fila de implementação: concorrência de transferência e
  confirmação (B2), limite de cupom, chaves idempotentes frontend (PR #15), busca/cadastro
  rápido na Agenda (PR #17), obrigatoriedade da chave (PR #18), jobs multitenant (PR #19)
  e backend da outbox (PR #20).
- Ativação das famílias da outbox no Railway: realizada pelo dono, conforme confirmação
  nesta conversa em 16/10/2026. Ativação retirada da fila; não foi repetida pelo agente.
  Essa confirmação não equivale a uma verificação independente da migration ou do
  processamento da fila em produção.

### Pendências vigentes após a reconciliação

| Item | Responsável | Origem e estado |
|---|---|---|
| Conferir evidência da migration e acompanhar processamento da outbox | Operação compartilhada/dono | Famílias já ativadas no Railway pelo dono; conferir somente leitura no Neon/monitoramento, sem reaplicar migration nem repetir ativação |
| Resend com chave idempotente | Claude Code | Ressalva B7.4-B; backlog, risco residual de e-mail repetido após aceite |
| Durabilidade dos alertas de estoque e avisos de assinatura | Claude Code | Auditoria B7.4; backlog fora das famílias entregues |
| Escape de HTML dos e-mails | Claude Code | Auditoria B7.4; backlog registrado, sem implementação nesta etapa |
| Acompanhamento de falhas e estados desconhecidos | Operação/dono | Contrato da outbox; acompanhamento humano, sem reenvio automático de ambíguos WhatsApp |
| Conferir desconexão de checks/previews Netlify | Operação/dono | Ressalva do PR #15; configuração removida no PR #14, integração externa não verificada |
| Limpeza de dados retroativos (B3) | Claude Code/dono | Depende de plano e autorização específica; não executada |
| Relógio central (B4) e comentários XML (T1) | Claude Code | Baixa prioridade; não constituem defeito funcional confirmado |
| Mobile | Dono | Adiado explicitamente; `mobile/` intocado |

## Atualização local pós-merge do favicon — 2026-10-17

- PR #22, `fix: atualiza favicon da marca`, foi integrado à `main` pelo merge
  `5e8d803fb633abed151603ba1950a918a19d91c7`. A `main` local foi avançada por
  fast-forward e está alinhada com `origin/main`.
- A branch `codex/atualiza-favicon-marca` continha o commit inicial `539d0f5` e a
  correção `cf4ad64`. O favicon final usa o símbolo neon existente em
  `frontend/public/brand/calendario-confirmado.svg`, com gradiente ciano, azul e roxo.
- Arquivos integrados: `frontend/public/brand/favicon.svg` e
  `frontend/public/favicon.ico`. O ICO contém tamanhos 16, 32 e 48 px.
- Validação pública somente leitura: `/favicon.ico` respondeu HTTP 200 e seus bytes
  corresponderam exatamente ao arquivo local (`SHA-256 491f2946ee65d0c66efa2e7ef18904a967412248c4ed8f4d79a68b6e0e228bc8`).
  `/health` respondeu HTTP 200 com PostgreSQL e `btree_gist` saudáveis.
- A publicação efetiva foi confirmada pelo conteúdo servido. Não foi feita publicação
  manual nem mutação em produção. O estado detalhado de CI/Vercel não foi consultado
  independentemente após o merge.
- A alteração deste histórico permanece local e sem commit. Arquivos locais preexistentes
  foram preservados.

## Finalidade deste documento

Este arquivo preserva o histórico operacional, a divisão de responsabilidades e a fila
de trabalho entre sessões. Ele complementa `prompt-do-projeto.md`, `AGENTS.md` e
`docs/decisoes.md`.

- `prompt-do-projeto.md` continua sendo a fonte da verdade para requisitos do produto.
- `AGENTS.md` continua sendo o guia técnico e operacional do repositório.
- Este arquivo registra o que já foi concluído/publicado e o que permanece na fila.
- Relatórios de conversa só entram aqui quando houver evidência no Git, nos testes ou no
  relatório final homologado da fase.

## Regra de manutenção

Antes de iniciar uma nova fase, ler este documento e conferir o estado real do Git.
Depois de uma etapa concluída, homologada, integrada ou publicada:

1. atualizar o snapshot da produção quando houver release;
2. registrar branch, commits principais e resultado da etapa;
3. mover os itens correspondentes da fila para o histórico concluído;
4. adicionar novos itens de backlog com origem, responsável e situação;
5. distinguir claramente trabalho apenas local de trabalho integrado ou publicado;
6. não registrar como concluído algo que foi apenas proposto ou implementado sem
   homologação;
7. manter hashes completos para releases e hashes curtos apenas em tabelas de leitura.

Se este documento divergir do Git ou dos providers, a evidência atual do Git/providers
prevalece e o documento deve ser corrigido.

## Responsabilidades acordadas

| Área | Responsável principal | Escopo |
|---|---|---|
| Backend e engenharia | Claude Code | Backend, API, domínio, regras de negócio, banco, migrations, concorrência, Docker, infraestrutura e testes backend |
| Frontend e experiência | Codex | Next.js, proxy e Route Handlers do frontend, service worker, cache frontend, UI/UX, acessibilidade, responsividade e Playwright |
| Integração e release | Compartilhado, com gate do usuário | Auditoria cruzada, compatibilidade, CI, providers, smoke e release controlado |
| Produto e prioridades | Usuário | Decisões de regra de negócio, prioridade da fila e autorização de release |

Essa divisão define a responsabilidade principal. Auditorias podem atravessar camadas para
entender contratos e impacto, sem transferir silenciosamente a implementação para o outro
responsável.

## Restrições permanentes

- `mobile/` permanece fora de escopo até backend e frontend web estarem completos e o
  usuário abrir explicitamente a fase mobile.
- Não alterar produção, fazer push, merge, deploy, migration ou comunicação real sem a
  autorização correspondente da fase.
- A landing aprovada não deve passar por outro redesign amplo. Aceitam-se correções
  comprovadas, acessibilidade, regressões, responsividade e mudanças comerciais explícitas.
- O painel aprovado não deve sofrer novo redesign amplo. Melhorias futuras devem ser
  pontuais e sustentadas por backlog ou auditoria.

## Linha do tempo consolidada

### Landing pública — concluída, integrada e publicada

| Marco | Commit | Resultado |
|---|---|---|
| Redesign inicial | `75ef5b6` | Nova linguagem navy, cyan, azul e violeta; responsividade e temas |
| Demos e identidade | `43f49f2` | Movimento do Hero, microdemo, Agenda demonstrativa e identidade refinada |
| Polimento mobile | `23a2299` | Ajustes para 375–768px, touch e ausência de overflow |
| Integração | `c577194` | Nova landing integrada à linha principal |

Direção aprovada: preservar estrutura, conteúdo, preços, Hero, demonstrações e identidade.
Não recolocar a landing na fila como redesign sem uma nova decisão explícita de produto.

### Painel — concluído, integrado e publicado

| Fase | Commit | Resultado |
|---|---|---|
| Estrutura, sidebar, login e Agenda | `035e177` | Fundação visual e navegação do painel |
| Cadastros principais | `37dc02b` | Usuários, profissionais, serviços e clientes |
| Módulos operacionais | `3a2186b` | Financeiro, comissões, vendas e estoque |
| Cupons e fidelidade | `7f2a34a` | Identidade aplicada aos módulos |
| Meu negócio | `c487989` | Tela reorganizada visualmente |
| Assinatura | `a4c3395` | Estados e ações preservados na nova linguagem |
| Início | `a943334` | Dashboard inicial redesenhado |
| Polimento final | `35650d7` | Ajustes visuais finais homologados |
| Integração | `a64304c` | Redesign completo do painel integrado |

O release preservou permissões, contratos, handlers e regras existentes. O painel foi
validado em dark/light e nos principais breakpoints.

### Experiência pública de criação — concluída, integrada e publicada

| Marco | Commit | Resultado |
|---|---|---|
| Estabilização | `fae43d2` | Respostas atrasadas, retries e proteção contra estados obsoletos |
| UX mobile | `87810d6` | Fluxo público mobile aprimorado |
| Redesign | `cbe375d` | Jornada pública redesenhada |
| Ajustes finais | `3008431` | Hierarquia, estados indisponíveis e acabamento final |
| Integração | `5c8ecf2` | Nova experiência pública publicada |

A homologação final registrou 75/75 testes públicos e nenhuma alteração de backend nessa
fase visual.

### Pós-agendamento D1–D6 — concluído e publicado

| Marco | Commit | Resultado |
|---|---|---|
| D1 | — | Auditoria funcional e visual; riscos mapeados antes do redesign |
| D2 backend | `d51457b` | Contrato canônico, capabilities, regras e disponibilidade por token |
| D2 frontend | `9b7ac7b` | Estabilização da gestão pública |
| D2.1 frontend | `e31e57a` | Rotas sensíveis por token removidas do cache do service worker |
| D3 | `106a54d` | Remarcação por slots e consumo das decisões do servidor |
| D4 | `f04c5da` | Redesign da experiência pós-agendamento |
| Release | `aba5fb35e9099f7fb735c9cee779f522874eb202` | Backend e frontend publicados e validados |

D5 homologou o release candidate com 293 testes unitários, 273 de integração e 208
validações frontend conhecidas. D6 publicou o conjunto sem incidentes e sem migration.

### Hardening D7 — concluído e publicado

| Marco | Commit | Resultado |
|---|---|---|
| Contexto público | `305e587` | Ausência de negócio tratada sem 500 |
| Regras de horário | `22ca98b` | Horários inválidos recusados de forma consistente |
| Cancelamento público | `2dc8e98` | Estados públicos permitidos restringidos |
| Testes determinísticos | `d532e22` | Cobertura ampliada sem dependência da hora de execução |
| Atomicidade do cancelamento | `ee44f3f` | Regra pública revalidada dentro da transação |
| Escritores de status | `ae4ba62` | Escritores serializados com trava e releitura da linha |
| Release | `0b8d3d653ba92f6a7cf6f6cf1472b97c0b4d6a91` | Hardening publicado e validado |

O release D7 não alterou frontend, banco, migrations, schema, dependências ou
infraestrutura. Railway, Vercel e CI ficaram saudáveis; não houve rollback.

### Hardening D8 — concluído, integrado e publicado

| Marco | Commit | Resultado |
|---|---|---|
| Ajuste comercial incorporado à base | `f23cd8c` | Teste grátis reduzido de 30 para 15 dias de forma controlada, sem migration |
| Histórico inicial da D8.1 | `bda0996` | Histórico e fila passaram a ser preservados no repositório |
| Cache de negócio inexistente | `a137fe9` | Respostas 404 humanas de slug inexistente ou inválido passaram a declarar `Cache-Control: no-store` |
| Reconciliação da base | `250579a` | Nova `main` incorporada à branch D8.1 sem conflito nem perda funcional |
| Atualização documental do RC | `adcb75f` | Base, hashes e estado de homologação registrados antes do release |
| Release D8.1 | `085a4be90b783283e7f6c9bf8b0f9591707b2570` | Correção de cache publicada e validada em Vercel e Railway |
| Testes temporais | `5dc70b01aee34b738a56571c2bf247a3443cea59` | Dois testes de integração tornados determinísticos, sem alteração de produção |
| Integração dos testes | `d1cc803a222cda947832ab7d3d69d612e46ccd38` | PR #1 integrado com CI verde e providers saudáveis |

A D8.1 preservou landing, painel, `www`, negócios válidos, redirecionamento de slug
anterior, caminhos, query strings e a política de indisponibilidade da API. O release
alterou somente `frontend/src/proxy.ts`, dois testes Playwright e este histórico. Slug
inexistente válido e slug inválido retornam página humana com HTTP 404 e
`Cache-Control: no-store`; não houve alteração de backend, banco, migration, schema,
dependência ou infraestrutura.

O primeiro run da CI backend do release (`37873076443`) falhou sem log acessível. Uma
reexecução autorizada identificou falhas diferentes e não determinísticas em
`EncaixeTestes` e `AgendaSemanaTestes`, ambas causadas por cenários de teste dependentes da
virada do dia. O código de produção estava correto: o encaixe que ultrapassava o expediente
devia responder 400, e a agenda diária devia listar em ambos os dias um atendimento que
atravessava a meia-noite.

A correção ficou restrita aos dois arquivos de teste. O PR #1 validou Backend e Frontend e
foi integrado por `d1cc803`. A validação local registrou 314/314 testes unitários e 346/346
testes de integração, sem ignorados. A CI do merge passou, Railway e Vercel publicaram o
commit correto, `/health`, landing e login responderam 200. Não houve incidente, rollback,
migration, operação manual no banco, comunicação real ou mutação de dados de produção.

### F2 / D9 — polimento D4.1 publicado

| Item | Resultado |
|---|---|
| Branch | `codex/polimento-pos-agendamento-d4-1` |
| Base de produção | `d1cc803a222cda947832ab7d3d69d612e46ccd38` |
| Documento herdado | `dbb2e21256c92d83ffe95daf3bc227e0a9ce2265` |
| Implementação | `19fc5e9` — `feat: aprimora experiencia pos-agendamento` |
| Homologação documental | `b4fcc40c17365b4966202a47e21157e08522052e` |
| Pull request | PR #2, CI Backend e Frontend aprovadas, Vercel Preview saudável |
| Release | `21375e6b12e8b26483ee886b631b51c1faa7007b` |
| Situação | Publicada e validada sem incidentes |

Os oito pontos da auditoria foram reproduzidos e corrigidos sem alterar contratos ou
regras: a ação de remarcação ganhou prioridade no mobile; a revisão explicita
`DE → PARA`; fechar a remarcação tornou-se secundário; o sucesso aparece junto ao
compromisso atualizado; estados encerrados ficaram consultivos; o fuso IANA deixou de ser
repetido; a coluna da remarcação recebeu mais espaço em 768px; e a faixa de datas passou
a informar o mês e identificar hoje por texto.

A validação registrou lint sem errors e com 12 warnings preexistentes de `<img>`,
TypeScript e build aprovados, 7/7 testes novos, 94/94 testes de pós-agendamento e service
worker, 32/32 regressões públicas e 1/1 smoke full-stack local com backend real e
provedores Fake. Não houve alteração de backend, banco, migration, schema, dependência,
lockfile, infraestrutura, provider ou configuração de deploy.

O merge preservou exatamente a árvore homologada. CI Backend e Frontend passaram; Vercel
e Railway publicaram automaticamente o commit correto; `/health`, landing, privacidade,
login, assets e `sw.js` responderam 200, sem novos erros JavaScript ou respostas 5xx. O
fluxo pós-agendamento não foi exercitado em produção por não existir token ou fixture
segura. Não houve migration, operação manual no banco, comunicação real, mutação de dados,
incidente ou rollback.

### F3 / D10 — polimento visual P2 do painel publicado

| Item | Resultado |
|---|---|
| Branch | `codex/polimento-painel-p2` |
| Base de produção | `21375e6b12e8b26483ee886b631b51c1faa7007b` |
| Documento herdado | `f4eedd34b9eb79ca05d7649e4eba5f9ec4bf34de` |
| Implementação | `1706ac2` — `style: refina estados e controles do painel` |
| Homologação documental | `76725c7a226cf7574f67e9b1d6afa972f318ac0c` |
| Pull request | PR #3, CI Backend e Frontend aprovadas |
| Release | `a5f1d72dbf92ace753e0928b58c6fb7053caa6a6` |
| Situação | Publicada e validada sem incidentes |

A auditoria confirmou que as ações textuais das tabelas de cadastros mediam 34px em
768px. O ajuste elevou apenas esses alvos para 44px entre 768 e 1023px, preservando a
densidade do desktop e os cards mobile. O `AvisoAssinatura` recebeu acabamento alinhado
aos tokens do AppShell, com hierarquia, ícone, estado destacado, foco e resposta mobile,
sem alterar regras de exibição, textos, prazos, link ou chamada de API.

Os estados compartilhados de carregamento, vazio e status já estavam coerentes e não
foram alterados. A duplicação entre folhas de estilo foi deliberadamente preservada porque
a consolidação ampliaria o acoplamento entre módulos sem benefício comprovado nesta fase.

A validação registrou lint com 0 errors e 12 warnings preexistentes de `<img>`, TypeScript,
build e `git diff --check` aprovados, além de 41/41 testes Playwright passando. Foram
cobertos 375px, 390px, 768px e desktop, temas claro/escuro, movimento reduzido, foco,
overflow, Agenda, sessão, permissões, Assinatura, cadastros, módulos operacionais,
benefícios, Meu negócio e autenticação. Não houve alteração de backend, banco,
migration, schema, dependência, lockfile ou infraestrutura.

O merge preservou a árvore homologada. CI Backend e Frontend passaram; Vercel e Railway
publicaram automaticamente `a5f1d72`. `/health`, landing, login, privacidade e `sw.js`
responderam 200. Não houve migration, operação de banco, comunicação real, mutação de
dados, incidente ou rollback.

### B1 — validação de horários profissionais publicada

| Item | Resultado |
|---|---|
| Branch | `fix/validacao-horarios-profissional` |
| Base de produção | `a5f1d72dbf92ace753e0928b58c6fb7053caa6a6` |
| Documento herdado | `83917148626a3f0a47bd5054e3afd2b91f7ae7e1` |
| Implementação | `09a6eb0` — `fix: valida intervalos de trabalho invalidos` |
| Homologação documental | `9eb191abac3488d1455b569fdd25b6d3145045f4` |
| Pull request | PR #4, CI Backend e Frontend aprovadas |
| Release | `9423e0894f9d22edf035d53610ac404f66d0401c` |
| Situação | Publicada e validada sem incidentes |

O `PUT /painel/profissionais/{id}/horarios` agora recusa com HTTP 400 e mensagem segura
qualquer intervalo cujo fim seja igual ou anterior ao início, incluindo turnos que
atravessariam a meia-noite. A validação acontece na entrada da API antes de o gerenciador
alterar horários rastreados; a regra equivalente do domínio permanece como defesa final.

A cobertura confirma os três casos inválidos, ausência de gravação, preservação do horário
anterior e isolamento de outro tenant, além do caminho válido já existente. A homologação
local registrou build sem warnings ou errors, 314/314 testes unitários e 349/349 testes de
integração aprovados. Não houve alteração de frontend, banco, schema, migration,
dependência, Docker ou infraestrutura.

O merge preservou a árvore homologada e integrou somente o controller, seus testes de
integração e este histórico. A CI #56 (`38144231493`) passou no commit de merge, com
Backend (.NET) em 7m02s e Frontend (Next.js) em 46s. Railway publicou o commit correto
com status SUCCESS e sem erros nos logs; a Vercel concluiu o deployment automático.
`/health`, landing, login e privacidade responderam 200, sem respostas 5xx observadas.
O endpoint de alteração de horários não foi exercitado em produção para evitar mutação de
dados. Não houve migration, operação manual no banco, comunicação real, incidente ou
rollback.

### Frontend do lembrete de retorno — homologado localmente

| Item | Resultado |
|---|---|
| Responsável | Codex |
| Branch | `codex/lembrete-retorno-frontend` |
| Base do contrato backend | `c230265266d0c72e4d202df7c44b0aebf537b1c5` (`feature/lembrete-retorno`) |
| Implementação frontend | `b70feae` — `feat: adiciona configuracao de lembrete de retorno` |
| Situação | Homologada localmente; ainda não integrada nem publicada |

Foi criada a tela protegida `/painel/configuracoes/notificacoes`, visível no menu somente
para quem possui `GerenciarConfiguracoesDoNegocio`. A interface permite ativar o lembrete,
configurar dias, horário e mensagem, inserir as quatro variáveis aceitas, conferir uma
pré-visualização, validar os campos antes do envio e consultar o relatório dos últimos 30
dias. O acesso à API ficou isolado em `lembrete-retorno-service.ts`.

A listagem de clientes passou a exibir e, quando autorizado, alterar o aceite individual
do lembrete. A homologação cobriu temas claro/escuro, 375px, 390px, 768px e 1440px,
ausência de overflow, salvamento e restauração dos dados locais, validação sem request,
relatório e opt-out. O lint passou com 0 errors e 12 warnings preexistentes de `<img>`, o
build de produção passou e 9/9 testes Playwright da funcionalidade e regressões afetadas
foram aprovados. Não houve alteração frontend de regra de negócio nem alteração nesta
etapa de backend, banco, schema, migration, dependência ou infraestrutura. Nenhum push,
merge ou deploy foi realizado.

## Fila atual

### B2 — concorrências fora do escopo homologado da D7

- **Responsável:** Claude Code.
- **Camada:** backend/domínio/concorrência.
- **Situação:** backlog técnico; requer auditoria antes de qualquer mudança.
- **Pontos preservados:** transferência, confirmação de reserva e expiração por job não
  usam a mesma trava de linha dos escritores cobertos pela D7.
- **Cobertura ausente:** teste com concorrência realmente paralela.
- **Regra:** não assumir bug sem reprodução e análise das transações existentes.

### B3 — limpeza de dados retroativos antigos

- **Responsável:** Claude Code, condicionado a decisão do usuário.
- **Camada:** domínio/banco/operação de dados.
- **Situação:** não autorizada.
- **Contexto:** a regra que recusa agendamento retroativo com mais de cinco minutos já está
  publicada; dados antigos não foram limpos.
- **Regra:** qualquer limpeza exige plano próprio, análise de impacto e autorização
  explícita. Não executar SQL ou migration por iniciativa do agente.

### T1 — comentários XML cosméticos

- **Responsável:** Claude Code quando tocar naturalmente nos arquivos.
- **Situação:** baixa prioridade; não abrir ciclo próprio.
- **Arquivos citados:** `IServicoAgendamentos.cs` e `GestaoPublicaAgendamento.cs`.

### B4 — abstração central de relógio

- **Responsável:** Claude Code.
- **Camada:** backend, domínio, jobs e testes.
- **Situação:** backlog técnico de baixa prioridade; não há bug funcional aberto.
- **Origem:** a D8 identificou testes que dependiam do relógio real e da virada do dia. Os
  dois casos comprovadamente instáveis já foram corrigidos sem alterar produção.
- **Objetivo futuro:** auditar a adoção de `TimeProvider` ou abstração equivalente para
  serviços e jobs que hoje leem diretamente o relógio do sistema, permitindo testes
  determinísticos de regras temporais.
- **Regra:** não abrir refatoração transversal sem escopo próprio, análise de impacto e
  testes de regressão; os usos restantes de horário atual não foram classificados como
  defeito.

## Itens encerrados que não devem voltar automaticamente à fila

- Redesign amplo da landing.
- Redesign amplo do painel.
- Redesign do agendamento público.
- Redesign do pós-agendamento D4.
- Polimento pós-agendamento F2/D9 publicado e validado.
- Polimento visual P2 do painel F3/D10 publicado e validado.
- Corrida do cancelamento público tratada na D7.
- Validação de horário passado e intervalo completo tratada na D7.
- Ausência de contexto de negócio nas rotas públicas tratada na D7.
- Validação de intervalos de trabalho inválidos B1 publicada e validada.
- Migration para D2–D7: não houve e não é pendência.
- Mobile: adiado por decisão explícita, não iniciado.

## Próxima sequência recomendada

1. Claude Code: auditar B2 somente quando o usuário abrir esse ciclo.
2. Claude Code: avaliar B4 apenas em ciclo técnico próprio e de baixa prioridade.

A ordem pode ser alterada pelo usuário. Cada item deve usar branch própria, preservar os
gates de release e não misturar frontend visual com correção backend sem necessidade.

## Agente de WhatsApp — Fase 2 (consulta) — 2026-10-19/20

```text
Etapa: Agente de WhatsApp, Fase 2 (somente consulta: serviços, profissionais, horários)
Responsável: Claude Code (backend e infra); sem frontend
Branch: ajuste-agente-whatsapp-fase2
Base: main (5d55c75)
Commits: cf6d981 (modelo de dados/migration), 19f1cee (agente de consulta), e67827d (testes),
  4337a96 (compose/.env.example), 3597f20 (valida Ai__Provider indefinido)
Status: integrada e implantada — PR #27, merge 20521e0 (CI do GitHub verde). Em 2026-10-20 o dono informou ter
  aplicado o SQL da migration no Neon e que o deploy saiu (informação do dono; não verificada por mim).
  Agente segue em Ai__Provider=Eco; Gemini real ainda não testado
Arquivos/camadas: IAiProvider/GeminiProvider, orquestrador, fila com trava por conversa, ferramentas de
  leitura, limites, sanitizador, minimizador de dados pessoais, IConsultaHorariosPublicos extraído do
  controller público, docker-compose.yml, .env.example, CLAUDE.md/AGENTS.md (locais)
Testes: build 0 erros; unidade 511 aprovados; integração filtrada (WhatsAppAgente*, Idempotencia, Outbox)
  355 aprovados. NÃO rodou: suíte de integração completa após as correções, Gemini real, export-openapi,
  subida real da stack
Migrations: AgenteWhatsAppConsulta (usos_ia + 2 colunas nulas) — antes do deploy; SQL gerado localmente
  (agente-whatsapp-consulta.sql, não versionado) para o Neon
Frontend alterado: não (nesta etapa)
Backend alterado: sim; nenhuma rota, DTO ou permissão nova
Produção: ativação futura: migration -> deploy com Ai__Provider=Eco (padrão) -> Ai__NegociosHabilitados (piloto)
  -> Gemini PAGO (decisão do dono, 2026-10-19; gratuito só em dev/homologação)
Próximo gate: avaliação manual com o Gemini real (dono tem a chave; roteiro em avaliacao-manual.md); ligar o piloto
```

Revisão cruzada (Codex, `.agents/handoffs/agente-whatsapp-fase2/revisao-backend*.md`), 4 rodadas: 1ª, 5 importantes +
1 sugestão; 2ª, 3 importantes + 1 sugestão; 3ª (a pedido do dono, além do limite de 2 voltas), 2 importantes +
1 sugestão; 4ª: **aprovado com ressalvas** (sem críticos/importantes; sugestão do enum corrigida). Todos os
achados foram corrigidos.

### Gemini real: modelo e thoughtSignature — PR #29 (2026-10-23)

Primeira conversa real com o Gemini (negócio piloto do dono, chave **sem faturamento**) revelou dois defeitos, ambos corrigidos
na `main` (commits `322ecd1` e `c47f85c`, merge `6efb0f3`; CI Backend/Frontend/Vercel verdes):
- **Modelo padrão:** `gemini-2.5-flash-lite` aparecia em `GET /v1beta/models` mas a chamada real dava 404 "no longer available to new users"
  (o mesmo para `gemini-2.5-flash`). Padrão trocado para `gemini-3.1-flash-lite` (respondeu OK com a chave do dono, junto com
  `gemini-3.5-flash-lite`, `gemini-3.5-flash` e os `-latest`). **Lição:** estar na lista de modelos não prova que a chave pode chamá-lo —
  teste com `generateContent` real antes de fixar um nome. Troca sem deploy por `Ai__Gemini__Model`.
- **`thoughtSignature`:** com ferramenta, a 2ª iteração dava HTTP 400 (log: `provedor de IA falhou ... iteração 2: http HTTP 400`) e o cliente caía
  no fallback. O `GeminiProvider` descartava a `thoughtSignature` do `functionCall`, que os modelos Gemini 3 exigem de volta. Agora é lida e
  reescrita idêntica no turno do modelo (compatível com modelos sem assinatura; nunca em log/banco). Diagnóstico por leitura do código e do
  contrato do Gemini 3 — **o corpo do 400 nunca foi visto** (o código não o registra, de propósito).
- **Estado:** a 1ª chamada sem ferramenta funcionou em produção (200, ~4 s, resposta enviada pela Evolution). Uma chamada deu timeout de 15 s
  (`Ai__Gemini__TimeoutSegundos`; sugerido 30). O **deploy do Railway do merge `6efb0f3` falhou na fase de build por erro de infraestrutura**
  ("Failed to build an image", sem causa nos logs; CI do GitHub verde) — reexecutar o deploy do mesmo commit.
- **Pendente de confirmação do dono:** com o deploy novo, repetir uma pergunta que use ferramenta ("quais profissionais vocês têm?") e
  confirmar que o 400 sumiu; só então rodar os roteiros de `avaliacao-manual.md`. Revisão do Codex da correção (`gemini-3-assinatura`)
  foi iniciada antes do merge e não concluiu até este registro.
- **Ruído de log (não relacionado):** `POST /webhooks/evolution/status` responde 401 em rajada — o token do webhook do provedor antigo
  (códigos de confirmação) não bate; não afeta o agente. Resolvido em 2026-10-26 (ver bloco abaixo).

### Webhook com corpo grande (PR #31) e modelo de reserva do Gemini (PR #32) — 2026-10-26

- **PR #31 (merge `d6ee28f`, commit `7b2df8d`):** `POST /webhooks/evolution/status` autentica antes de ler o corpo, com teto de 256 KB; acima dele
  responde 200 sem processar (Warning curto, sem pilha). Corrige o 500 em `contacts.update` com foto. Testes em `WhatsAppEvolutionTestes`.
- **PR #32 (merge `741aa75`, commits `4ec6f93` e `e829649`):** `Ai__Gemini__ModeloReserva` (opcional; vazio = comportamento anterior; deve ser diferente
  do principal). A reserva entra depois da nova tentativa existente, só em timeout, erro de rede ou HTTP 404/408/429/500/502/503/504 (nunca 400/401/403);
  `thoughtSignature` não cruza modelos (a reserva refaz a rodada); o principal usa no máximo 60% do prazo de 90 s; disjuntor de 60 s **por negócio**
  (`DisjuntorModeloIa`); `UsoIa.Modelo` guarda quem respondeu. Revisão do Codex: reprovou a 1ª versão (disjuntor compartilhado entre negócios; timeout
  durante a ferramenta não chamava a reserva); corrigido em `e829649` com teste para cada. Build ok, 532 unidade, 86 integração `WhatsAppAgente`;
  a suíte completa de integração ficou para a CI. **Não validado com o Gemini real:** status exatos de 404/503, existência/qualidade do modelo de reserva
  e contagem de cota nas tentativas que falham.
- **Estado em produção (dono, 2026-10-26):** `Ai__Gemini__ModeloReserva` definida no Railway; falta testar pergunta com ferramenta e olhar o log.
- **401 em rajada em `/webhooks/evolution/status` — resolvido:** `WhatsApp__Evolution__SegredoWebhook` estava com a URL inteira em vez de só o segredo
  (o token de `?token=` nunca batia). Corrigido pelo dono; log passou a mostrar só 200. O `WEBHOOK_GLOBAL_URL` não tinha `\n`. O webhook global só dispara
  `MESSAGES_UPDATE` (único `WEBHOOK_EVENTS_*` configurado) — suficiente para o status de entrega; nada a restringir.
- **Rotação da apikey da Evolution:** concluída pelo dono antes deste registro.
- **PR #34 (merge `7383e24`, commit `48bb073`):** `ProcessadorWebhookWhatsApp.AplicarAsync` confere com `AnyAsync` se o evento de status já existe antes de
  inserir; o repetido (a Evolution entrega 2x quase junto) deixa de gerar `ERR` com pilha do EF Core (a resposta já era 200). O índice único e o
  `catch` seguem como garantia da corrida rara. Teste novo de idempotência; 14 de integração `WhatsAppEvolution` ok. **A confirmar em produção:** sumiço
  dos `ERR` de `ix_eventos_webhook_whatsapp_provedor_id_evento`.
- **Reserva do Gemini em produção (2026-10-26):** 1º teste real — principal 503, reserva `gemini-3.5-flash-lite` respondeu e a mensagem foi enviada; o disjuntor
  abriu e as seguintes foram direto à reserva. A reserva é lenta (8–19 s por volta); pergunta com ferramentas (3 voltas) estourou o prazo de **40 s**
  (`Ai__TempoMaximoProcessamentoSegundos`, padrão 40; validador 5–85; o lease de 90 s é outra coisa) e caiu no fallback. Decisão do dono: ajustar pela variável
  (60–75 s). O faturamento do Gemini segue pendente e é o que mais reduz os 503. O modelo chamou `listar_profissionais` 3x na mesma volta (desperdício; sem ajuste).
- **Sessão da Evolution e "Aguardando mensagem" — observando:** o celular de destino às vezes mostrava "Aguardando mensagem" (WhatsApp sem conseguir decifrar; o envio
  aparece como 201 na API). Hipótese: sessão/identidade do aparelho recriada. Ações do dono: Evolution atualizada de v2.3.0 para **v2.3.7** e
  `DATABASE_SAVE_DATA_INSTANCE=true` criada (antes só havia `DATABASE_ENABLED/PROVIDER/CONNECTION_*`); Serverless desligado; `/evolution/instances` conferido.
  Após o redeploy o WhatsApp **continuou conectado** sem QR e o aviso aparentemente parou. **Não resolvido de forma comprovada** (era intermitente): reabrir se
  voltar; testar com um número novo e dedicado ao negócio antes do piloto. Não se sabe qual das duas mudanças (v2.3.7 ou a variável) ajudou.

### Agente de WhatsApp — Fase 3 (agendamento pelo agente), fatias 1 a 3 — 2026-10-26/27

Plano e modelo de ameaças de escrita em `.agents/handoffs/agente-whatsapp-fase3/plano.md` (local). Decisões do dono: P0 identificar pelo número do WhatsApp **sem código**
(exceção à seção 8.1, a registrar como 8.1.7 na especificação); nome do cliente nunca vai à IA; confirmação em duas etapas com resumo montado pelo servidor e "sim"
avaliado pelo servidor antes de chamar a IA; cancelar/remarcar/listar ficam na Fase 4; limites de 1 pendência por conversa, 5 preparos/telefone/dia, 3 agendamentos
futuros, 3 confirmações/dia e 20 reservas ativas por negócio; aviso de atendimento automatizado a cada 90 dias por conversa. **Tudo desligado por padrão
(`Ai__Agendamento__Modo=Desligado`).**
- **Fatia 1 — PR #36 (merge `cb70898`, commit `d076e81`):** enums `OrigemAgendamento.WhatsApp`/`OrigemCliente.WhatsApp` (nada os grava nesta fatia); `JidDoTelefone` descarta
  `@g.us`, `@broadcast` e `@newsletter` antes de olhar `senderPn`; `TelefoneE164.FormasEquivalentes()` (9º dígito). CI completa verde.
- **Fatia 2 — PR #38 (merge `09de0cf`):** `PendenciaAgendamentoWhatsApp`, migration `AgenteWhatsAppAgendamento` (**aplicada no Neon pelo dono em 2026-10-26**), `CriarReservaAgenteAsync`/
  `ConfirmarReservaAgenteAsync`, expiração das pendências no `JobExpirarReservas`, travas `FOR UPDATE` escopadas por `negocio_id`, validação do nome no domínio. Passou por 5 revisões do
  Codex (retry/`ConfirmadaAgora` por id aleatório, cancelamento mantém telefone, instante lido depois da trava, anonimização × confirmação, escopo por negócio, nome só para cliente novo).
  Regra de parada aprovada pelo dono: depois da última rodada, revisão só reaberta por achado Crítico. Ordem global de locks: pendência → agendamento → cliente.
- **Fatia 3 — PR #39 (merge `9345c87`):** `Ai__Agendamento__*`, ferramenta `preparar_agendamento` (uma vez por rodada), resumo e confirmação pelo servidor (`ConfirmadorPendenciaAgente`),
  guarda de promessa falsa, prompt v2 só com o modo ligado, aviso automatizado, `VersaoTermos.Atual` 2.0 (vale também para o assistente web). Bug real achado pelo teste de concorrência e
  corrigido (`ChangeTracker.Clear()` após reserva sem sucesso). Codex reprovou a revisão com 3 importantes; o **achado 1 foi corrigido** (o "sim" só confirma se nada do cliente desde o
  resumo foi contestação, `aca2198`). Os achados 2 e 3 do Codex foram resolvidos no **PR #41 (merge `cb8952a`)**: (a) o lote do "sim" só é marcado como processado depois do envio da confirmação, com
  recuperação para envio não realizado e `JaAnunciada` para a queda entre o envio e a marcação; (b) só recusa de conexão, DNS e TLS são falha definitiva — conexão encerrada, resposta
  truncada e timeout são **ambíguos** (não liberam a reserva nem reenviam). Risco aceito: com falha definitiva repetida da Evolution a confirmação é retentada a cada repescagem do recuperador.
- **Política do negócio v2.0 — PR #37 (merge `31827db`):** `/privacidade` do negócio menciona o assistente com IA, o envio ao Google (Gemini), o "SIM" como aceite e mostra "Versão 2.0".
  O e2e novo (`politica-negocio-ia.spec.ts`) nunca rodou contra a stack deste código; conferir a página na produção.
- **Gemini com faturamento (2026-10-27):** o dono comprou R$ 200 em créditos e ligou o faturamento. Medição pós-faturamento: todos os modelos testados 200/200/200; `3.1-flash-lite` ~1,2 s,
  `3.5-flash-lite` ~1,1 s, `flash-lite-latest` ~1,1 s, `3.5-flash` ~2,7 s (antes, na cota gratuita: 503 em 4 de 6 e 15–26 s na reserva). Configuração atual mantida; nada a mudar no código.
- **Não verificado:** comportamento do agendamento com Gemini real (roteiros novos em `avaliacao-manual.md` da fase), envio real pela Evolution, latência e custo por agendamento.
- **Pré-requisitos de ativação com clientes reais:** roteiros da Fase 2 e da Fase 3 completos com Gemini real; número dedicado ao piloto (ver `docs/ativacao-agente-fase3.md`, local) com a sessão da
  Evolution comprovadamente estável; aviso automatizado conferido; conferir a política v2.0 no ar. Faturamento do Gemini: **feito**.

### Agente de WhatsApp — piloto ativado e correções dos primeiros testes — 2026-10-27/28

O dono ativou o piloto (`Ai__Agendamento__Modo=Piloto`) com número dedicado e testou quatro conversas. Diagnóstico por `usos_ia` e `mensagens_whatsapp` (os logs do Railway
só guardam as horas mais recentes e as conversas já tinham saído da janela):
- **Interação 2 (tudo numa mensagem):** fluxo completo funcionou — resumo do servidor, "Sim", confirmação com link de gestão.
- **Interação 3 (escolha em duas mensagens):** o histórico do modelo só tem texto (resultados de ferramenta não voltam, decisão de segurança da Fase 2), então na mensagem seguinte o modelo
  chamou `preparar_agendamento` com `servico_id`/`profissional_id` **inventados** → "houve um erro ao processar o agendamento".
- **Interação 4 ("Juliana"):** o modelo varria `listar_profissionais` serviço por serviço e estourava `MaxIteracoesFerramentas` (5, o máximo do validador) → fallback com o link. Cinco chamadas
  seguidas, entrada subindo ~218 tokens por volta, aparecem em `usos_ia`.
- **Interação 1 (resposta cortada no meio da frase):** o limite de saída padrão era 512 e o `finishReason` era ignorado pelo orquestrador; no horário o dono trocava de modelo, então a causa exata
  não foi comprovada. Os 503 do principal só existiam na chave sem faturamento: de 17:05 UTC em diante `usos_ia` não tem uma falha.
- **Correções (todas com CI completa verde):** PR #42 (merge `9d8a1e5`) — `Ai__MaxTokensSaida` 512 → 1536, `MAX_TOKENS` registrado e tratado (nova tentativa com o limite dobrado, nunca envia texto cortado),
  prompt aceita datas naturais; PR #43 (merge `a462652`) — nota de contexto das ferramentas gravada pelo servidor (`contexto_consulta`, até 1000 caracteres, 2 notas e 1500 caracteres no histórico,
  ids completos sempre preservados e horários em rodízio entre profissionais), `listar_profissionais` devolve os serviços de cada profissional, última chamada sem ferramentas antes do fallback,
  primeiro nome do profissional (homônimos → pergunta; resumo do servidor mostra o nome completo). `preparar_agendamento` continua validando id e oferta no servidor. Revisão do Codex do #43:
  sem crítico; o importante da nota com vários profissionais foi corrigido por **exceção aprovada pelo dono à regra de parada**; a evidência da integração completa é a CI.
- **A conferir em produção, com o Gemini real:** repetir a Interação 3 (duas mensagens), a Interação 4 e o primeiro nome; só então os roteiros completos da Fase 3.
- **Teste instável:** `CommitAmbiguoProxyTestes.Iniciar_com_conexao_derrubada_no_commit…` falhou uma vez na CI do PR #42 e passou na reexecução, sem relação com o agente (derruba a conexão no COMMIT por um proxy;
  depende de tempo).

### Política de privacidade do produto — PR #28 (frontend)

Texto do dono publicado em `/privacidade` do domínio raiz, sem a faixa de rascunho (`PaginaLegal` ganhou a prop
`rascunho`, padrão true; `/termos` mantém a faixa). Branch `ajuste-politica-privacidade`, commit 00c9443, merge
1c55509. Lint/tsc/build ok; e2e ajustado mas não rodado. Nome, domínio e WhatsApp do canal vêm de
`MARCA_NOME_PRODUTO`/`MARCA_DOMINIO`/`CONTATO_WHATSAPP`. **Decisão do dono (2026-10-20):** o texto cita criar/remarcar/
cancelar, aviso de atendimento automatizado e reconhecimento do cliente, que ainda não existem — aceitável, pois o
produto só será comercializado com tudo implementado. A política **do negócio** (subdomínio) não menciona IA.

| Item | Responsável | Origem e estado |
|---|---|---|
| Validar Gemini real: pergunta com ferramenta após o deploy + roteiros de `avaliacao-manual.md` | Dono/Claude Code | Pré-requisito da ativação; em andamento (ver bloco acima) |
| Deploy do Railway do merge 6efb0f3 falhou no build (infra) | Dono | Redeploy do mesmo commit |
| Validar o modelo de reserva com o Gemini real e ligar o faturamento do Gemini | Dono | Reserva testada (funciona, mas lenta); subir `Ai__TempoMaximoProcessamentoSegundos` (60–75), testar reserva mais rápida e ligar o faturamento antes de clientes reais |
| "Aguardando mensagem" no celular de destino | Dono | Observando após v2.3.7 + `DATABASE_SAVE_DATA_INSTANCE=true`; testar com número novo dedicado |
| Fase 3: ativar o piloto (`Ai__Agendamento__Modo=Piloto` + GUID) e rodar os roteiros com Gemini real | Dono/Claude Code | Código completo e desligado (PRs #36, #38, #39, #41); guia em `docs/ativacao-agente-fase3.md` (local) |
| Conferir `/privacidade` de um negócio em produção (v2.0) e rodar o e2e novo contra stack construída do código | Dono/Claude Code | PR #37 mergeado; e2e nunca rodou contra esta versão |
| Item 8.1.7 na especificação: agendamento pelo WhatsApp dispensa código | Dono | Decisão P0 aprovada em 2026-10-26; `prompt-do-projeto.md` é local |
| Sugestões de cobertura do Codex em código já mergeado: corpo grande sem Content-Length (PR #31) e expiração do disjuntor (PR #32) | Claude Code | Baixa prioridade |
| Repetir com o Gemini real as Interações 3 e 4 e o primeiro nome do profissional; depois os roteiros da Fase 3 | Dono | Após o deploy dos PRs #42 e #43 |
| Teste instável `CommitAmbiguoProxyTestes.Iniciar_com_conexao_derrubada_no_commit…` | Claude Code | Falhou uma vez na CI do PR #42; passou na reexecução; estabilizar (baixa prioridade) |
| Railway guarda só as horas recentes de log | Dono | Copiar as linhas `Agente:` na hora ao reproduzir; `usos_ia` e `mensagens_whatsapp` (papel Ferramenta) são a fonte persistente de diagnóstico |
| Confirmar que o `ERR` de chave duplicada em `eventos_webhook_whatsapp` sumiu (PR #34) | Dono | Olhar o log do Railway por alguns dias |
| Política de privacidade **do negócio** (subdomínio) mencionar a IA | Dono (texto) / Codex | Necessário antes do piloto com clientes reais |
| Apagar conversas (`mensagens_whatsapp`) ao anonimizar cliente | Claude Code | LGPD; `Cliente.Anonimizar` não apaga; Fases 4/5 |
| Aviso de atendimento automatizado e demais funções citadas na política | Claude Code | Fases 3/4; exigidas antes de comercializar |
| Sanitizador: `ok.vamos` pode sumir, `Golpe.Ai` escapa; minimizador é heurística | Claude Code | Risco residual conhecido, documentado em `contrato.md` |
| `CONTATO_WHATSAPP` no frontend usa `/D/g` (remove a letra D) em `app/page.tsx` | Codex | Provável erro de regex (`/\D/g`); não corrigido |
| Seção 2 do `prompt-do-projeto.md` ainda cita n8n | Dono | Desatualizada (arquivo local) |

## Modelo de registro de uma nova etapa

Ao concluir uma etapa, acrescentar ou atualizar:

```text
Etapa:
Responsável:
Branch:
Base:
Commits:
Status: local | homologada | integrada | publicada
Arquivos/camadas:
Testes:
Migrations:
Frontend alterado:
Backend alterado:
Produção:
Backlog criado ou encerrado:
Próximo gate:
```
