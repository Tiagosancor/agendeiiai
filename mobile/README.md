# mobile — esqueleto Expo (fase 2)

Esqueleto do futuro app mobile do **profissional**, que vai receber notificações
push sobre agendamentos novos, remarcados e cancelados. É trabalho de preparação
de terreno para a fase 2 do projeto (ver seções 2 e 4 de `prompt-do-projeto.md`,
na raiz do repositório) — **não está, ainda, ligado ao backend**.

## O que já existe

- Projeto Expo (template TypeScript), SDK mais recente estável.
- Navegação básica com React Navigation (`@react-navigation/native-stack`):
  duas telas provisórias (`Inicial` e `Sobre`) só para comprovar que o
  scaffold de navegação funciona.
- Nada de autenticação, chamadas à API, notificações push ou telas de
  domínio (agenda, cliente, agendamento etc.) — isso entra só quando a fase 2
  for priorizada.

## Estrutura

```
mobile/
├── App.tsx                       # raiz do app — monta o SafeAreaProvider e a navegação
├── index.ts                      # entry point padrão do Expo
├── src/
│   ├── navegacao/
│   │   └── RotasPrincipais.tsx   # stack de navegação e definição das rotas
│   └── telas/
│       ├── TelaInicial.tsx       # tela provisória de exemplo
│       └── TelaSobre.tsx         # segunda tela provisória, só p/ testar navegação
```

## Comandos

```bash
cd mobile
npm install         # instalar dependências
npx tsc --noEmit     # checar tipos
npx expo-doctor      # checar configuração/dependências
npx expo start        # subir o servidor de desenvolvimento (Expo Go ou emulador)
```

Não há build nativo (`ios/`/`android/`) gerado neste esqueleto — isso só é
necessário quando alguma biblioteca com código nativo for adicionada, ou na
hora de gerar o build final para as lojas (App Store / Google Play), que
depende das contas Apple Developer e Google Play Console — ver seção 12 da
especificação, "não urgente para quando o app mobile sair da fase 2".
