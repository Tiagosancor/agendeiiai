import { StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';

import type { RotasParametros } from '../navegacao/RotasPrincipais';

/**
 * Tela inicial provisória do esqueleto mobile.
 *
 * Não representa nenhuma tela de domínio (agenda, cliente, agendamento etc.) —
 * serve apenas para provar que o scaffold de navegação funciona. As telas
 * reais deste app (voltadas ao profissional, com notificações push sobre
 * agendamentos novos/remarcados/cancelados) entram na fase 2, conforme a
 * seção 2 da especificação (prompt-do-projeto.md).
 */
type Props = NativeStackScreenProps<RotasParametros, 'Inicial'>;

export default function TelaInicial({ navigation }: Props) {
  return (
    <View style={estilos.container}>
      <Text style={estilos.titulo}>Agendei — esqueleto mobile</Text>
      <Text style={estilos.subtitulo}>
        Fase 2: app do profissional. Ainda sem telas de domínio.
      </Text>
      <Text style={estilos.link} onPress={() => navigation.navigate('Sobre')}>
        Ir para a tela Sobre
      </Text>
    </View>
  );
}

const estilos = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: 24,
    gap: 12,
  },
  titulo: {
    fontSize: 20,
    fontWeight: '600',
    textAlign: 'center',
  },
  subtitulo: {
    fontSize: 14,
    color: '#555',
    textAlign: 'center',
  },
  link: {
    marginTop: 16,
    fontSize: 16,
    color: '#2563eb',
  },
});
