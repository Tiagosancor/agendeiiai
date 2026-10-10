# Histórico e fila do projeto — Agendeiiai

Última atualização: 2026-10-10
Estado Git verificado: `main` = `origin/main` =
`a5f1d72dbf92ace753e0928b58c6fb7053caa6a6`

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

## Fila atual

### B1 — PUT de horários inválidos devolve 500

- **Responsável:** Claude Code.
- **Camada:** backend/API/validação.
- **Situação:** pendente de ciclo próprio.
- **Problema confirmado:** `PUT /painel/profissionais/{id}/horarios` com `fim <= início`
  devolve 500 para requisição direta. O dado não é gravado.
- **Objetivo:** validação explícita e resposta 400 apropriada, com testes.

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
- Migration para D2–D7: não houve e não é pendência.
- Mobile: adiado por decisão explícita, não iniciado.

## Próxima sequência recomendada

1. Claude Code: B1, resposta 400 para horários inválidos.
2. Claude Code: auditar B2 somente quando o usuário abrir esse ciclo.
3. Claude Code: avaliar B4 apenas em ciclo técnico próprio e de baixa prioridade.

A ordem pode ser alterada pelo usuário. Cada item deve usar branch própria, preservar os
gates de release e não misturar frontend visual com correção backend sem necessidade.

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
