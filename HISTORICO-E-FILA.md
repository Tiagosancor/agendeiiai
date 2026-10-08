# Histórico e fila do projeto — Agendeiiai

Última atualização: 2026-10-08
Estado Git verificado: `main` = `origin/main` =
`0b8d3d653ba92f6a7cf6f6cf1472b97c0b4d6a91`

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

### Hardening D8.1 — implementado localmente, não publicado

| Campo | Registro |
|---|---|
| Responsável | Codex |
| Branch | `codex/fix-cache-404-negocio-inexistente` |
| Base | `0b8d3d653ba92f6a7cf6f6cf1472b97c0b4d6a91` |
| Commits | `bda0996` (histórico) e `fix: impede cache prolongado de negócio inexistente` (implementação) |
| Resultado | Respostas 404 de slug inexistente ou inválido mantêm a página humana e passam a declarar `Cache-Control: no-store` na resposta final do Next.js |
| Testes | 4/4 testes específicos e 83/83 testes direcionados passaram; lint, TypeScript e build passaram |
| Escopo | Somente frontend, proxy e testes Playwright; sem backend, API, banco, migrations ou dependências |
| Produção | Não alterada; o snapshot publicado continua em `0b8d3d653ba92f6a7cf6f6cf1472b97c0b4d6a91` |

O ajuste preserva a landing raiz, `app`, `www`, negócios válidos, redirecionamento de slug
anterior, caminhos, query strings e a política existente quando a API está indisponível.
O item F1 foi encerrado na branch local e ainda depende dos gates normais de integração e
publicação.

## Fila atual

### F2 — melhorias D4.1 da experiência pós-agendamento

- **Responsável:** Codex.
- **Camada:** UI/UX e testes Playwright.
- **Situação:** pendente; 0 V0, 0 V1, 7 V2 e 1 V3 na auditoria original.
- **Regra:** executar como polimento pontual, sem novo redesign e sem reconstruir regras no
  frontend.

Itens confirmados:

1. aproximar a ação **Remarcar** do compromisso em 375/390px;
2. tornar a comparação **DE → PARA** mais imediata;
3. reduzir a competição visual de **Fechar remarcação**;
4. aproximar a confirmação explícita de sucesso do compromisso atualizado;
5. tornar estados encerrados e sem ações mais claramente consultivos;
6. reduzir repetição e peso técnico do fuso IANA e de textos auxiliares;
7. melhorar a proporção das colunas da remarcação em 768px;
8. reforçar discretamente o contexto de mês e a indicação de “hoje”.

Antes de implementar, reproduzir os itens na versão atual para evitar corrigir algo que já
tenha mudado por outra fase.

### F3 — ressalvas visuais P2 do painel

- **Responsável:** Codex.
- **Camada:** UI/UX, acessibilidade e manutenção frontend.
- **Situação:** pendente, não bloqueante.

Itens registrados na homologação do painel:

1. alguns controles de ação chegam a aproximadamente 34px no tablet;
2. `AvisoAssinatura` ainda apresenta acabamento visual legado;
3. estados transversais ainda não estão completamente uniformizados;
4. há duplicação de CSS a reduzir somente quando houver benefício claro e testes de
   regressão.

Não transformar esse item em novo redesign do painel.

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

## Itens encerrados que não devem voltar automaticamente à fila

- Redesign amplo da landing.
- Redesign amplo do painel.
- Redesign do agendamento público.
- Redesign do pós-agendamento D4.
- Corrida do cancelamento público tratada na D7.
- Validação de horário passado e intervalo completo tratada na D7.
- Ausência de contexto de negócio nas rotas públicas tratada na D7.
- Migration para D2–D7: não houve e não é pendência.
- Mobile: adiado por decisão explícita, não iniciado.

## Próxima sequência recomendada

1. Codex: F2, polimento D4.1 do pós-agendamento.
2. Codex: F3, P2 pontuais do painel, após nova verificação visual.
3. Claude Code: B1, resposta 400 para horários inválidos.
4. Claude Code: auditar B2 somente quando o usuário abrir esse ciclo.

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
