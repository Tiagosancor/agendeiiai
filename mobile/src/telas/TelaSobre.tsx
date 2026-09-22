import { StyleSheet, Text, View } from 'react-native';
import type { NativeStackScreenProps } from '@react-navigation/native-stack';

import type { RotasParametros } from '../navegacao/RotasPrincipais';

/**
 * Segunda tela provisória, só para validar a navegação entre telas
 * (empilhamento e botão de voltar) do scaffold Expo/React Navigation.
 */
type Props = NativeStackScreenProps<RotasParametros, 'Sobre'>;

export default function TelaSobre({ navigation }: Props) {
  return (
    <View style={estilos.container}>
      <Text style={estilos.titulo}>Sobre este esqueleto</Text>
      <Text style={estilos.corpo}>
        Este app ainda não fala com o backend. Ele existe apenas para preparar
        o terreno do app mobile do profissional (fase 2), que vai receber
        notificações push de agendamentos novos, remarcados e cancelados.
      </Text>
      <Text style={estilos.link} onPress={() => navigation.goBack()}>
        Voltar
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
  corpo: {
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
