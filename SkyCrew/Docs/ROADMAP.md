# Roadmap

## Fase 0 — Diagnóstico e preparação ✔
Estrutura do projeto, asmdefs, manifest, documentação e harness de testes e compilação.

## Fase 1 — Protótipo mínimo jogável (código pronto, falta validação no Editor)
Seção de cabine, jogador, 4 passageiros, interações, objetos carregáveis, 3 tarefas obrigatórias + 1 extra,
turbulência, vitória/derrota, pausa e resultados.
**Para fechar a fase:**
1. abrir na Unity 6;
2. rodar os testes EditMode e PlayMode;
3. executar o roteiro manual (TEST_PLAN.md);
4. corrigir o que aparecer.

## Fase 2 — Loop principal (código pronto, falta validação no Editor)
- 7 eventos com tarefas;
- pedidos de água, lanche e cobertor;
- desistência de pedidos e limite de pedidos abertos;
- prestativo e desastrado com efeito;
- bônus de resposta rápida e estrelas calibradas;
- vitória no pouso;
- mancha com esfregão, carrinho puxável, geladeira com conserto;
- teste de resistência com 40 viagens.

## Próximas tarefas, em ordem
1. **Validação no Editor** das Fases 1 e 2 (TEST_PLAN.md) e correções.
2. Ferramenta de Editor que gera **prefabs** a partir dos módulos (substituir `Resources` aos poucos).
3. **Fase 3 — produção 3D:**
   - rig humanoide com skinning (Blender);
   - animações de andar, sentar, segurar, reagir e escorregar;
   - Animation Rigging para as mãos;
   - variações modulares de roupa, cabelo e acessórios;
   - LOD.
4. **Fase 4 — aeronave completa:** primeira classe, mais fileiras, portas de serviço, área de carga com animais, progressão inicial e salvamento.
5. **Fase 5 — multiplayer:**
   - Netcode for GameObjects com o host rodando o `MatchSession`;
   - `Try*` e sinais como ServerRpc;
   - sincronização de jogadores, passageiros, carrinho e objetos;
   - lobby, desconexão e testes com 2 clientes.
6. **Fase 6 — polimento:** UI Toolkit, áudio com canais, tutorial, balanceamento com jogadores reais e otimização medida.
7. **Fase 7 — build Windows** e validação final.
