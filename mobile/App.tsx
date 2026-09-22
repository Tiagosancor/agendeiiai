import { StatusBar } from 'expo-status-bar';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import RotasPrincipais from './src/navegacao/RotasPrincipais';

/**
 * Raiz do app. Esqueleto da fase 2 (app mobile do profissional) — sem
 * autenticação, sem chamadas à API e sem telas de domínio ainda (ver
 * seção 2 de prompt-do-projeto.md). Só a navegação básica, para provar que
 * o scaffold Expo + React Navigation funciona.
 */
export default function App() {
  return (
    <SafeAreaProvider>
      <RotasPrincipais />
      <StatusBar style="auto" />
    </SafeAreaProvider>
  );
}
