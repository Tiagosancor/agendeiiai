import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';

import TelaInicial from '../telas/TelaInicial';
import TelaSobre from '../telas/TelaSobre';

/**
 * Parâmetros das rotas do stack principal. Nenhuma delas recebe parâmetro
 * hoje — são só telas de exemplo do scaffold de navegação (ver seção 2 e 4
 * de prompt-do-projeto.md: esqueleto mobile, sem telas de domínio ainda).
 */
export type RotasParametros = {
  Inicial: undefined;
  Sobre: undefined;
};

const Stack = createNativeStackNavigator<RotasParametros>();

/**
 * Navegação raiz do app. Um stack simples com duas telas provisórias,
 * só para comprovar que o scaffold de navegação funciona.
 */
export default function RotasPrincipais() {
  return (
    <NavigationContainer>
      <Stack.Navigator initialRouteName="Inicial">
        <Stack.Screen
          name="Inicial"
          component={TelaInicial}
          options={{ title: 'Agendeiiai' }}
        />
        <Stack.Screen
          name="Sobre"
          component={TelaSobre}
          options={{ title: 'Sobre' }}
        />
      </Stack.Navigator>
    </NavigationContainer>
  );
}
