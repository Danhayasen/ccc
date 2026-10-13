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

## Próximas tarefas, em ordem
1. **Validação no Editor** (itens 1–3 acima) e correções.
2. Ferramenta de Editor que gera **prefabs** a partir dos módulos (substituir `Resources` aos poucos).
3. **Fase 2 — loop principal:**
   - mais pedidos (lanche, cobertor) usando os objetivos já existentes;
   - eventos "objeto derrubado", "corredor bloqueado", "derramamento" e "equipamento indisponível";
   - carrinho de serviço com física;
   - tarefa de limpeza com esfregão;
   - passageiro prestativo que ajuda;
   - passageiro desastrado que derruba coisas (usar `Clumsiness`).
4. **Fase 3 — produção 3D:**
   - rig humanoide com skinning (Blender);
   - animações de andar, sentar, segurar e reagir;
   - Animation Rigging para as mãos;
   - variações modulares de roupa, cabelo e acessórios;
   - LOD.
5. **Fase 4 — aeronave completa:** primeira classe, mais fileiras, portas de serviço, área de carga com animais e progressão inicial.
6. **Fase 5 — multiplayer:**
   - Netcode for GameObjects com o host rodando o `MatchSession`;
   - `Try*` como ServerRpc;
   - sincronização de jogadores, passageiros e objetos relevantes;
   - lobby, desconexão e testes com 2 clientes.
7. **Fase 6 — polimento:** UI Toolkit, áudio com canais, tutorial, balanceamento e otimização medida.
8. **Fase 7 — build Windows** e validação final.
