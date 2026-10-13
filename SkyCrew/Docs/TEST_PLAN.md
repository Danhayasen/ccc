# Plano de testes

## Automáticos

### EditMode (regras, `Tests/EditMode`) — 68 testes
Rodam no Test Runner da Unity e fora dela (`Tools/Verificacao/verificar.sh`).

| Arquivo | Cobre |
|---|---|
| `MatchTests.cs` | Transições permitidas/proibidas, inicialização incompleta, embarque antes do voo, sinais fora de jogo, resultado único, tempo esgotado, reinício limpo, reinício bloqueado no voo |
| `TaskTests.cs` | Ativação/conclusão, sinal errado, sinal antes da ativação, recompensa única com sinais duplicados, prazo com penalidade, cancelamento, pré-requisitos e cascata, regra externa, id duplicado, sequência ordenada, contagem, isolamento entre ativações, ledger idempotente, fórmula de pontos |
| `PassengerAndEventTests.cs` | Personalidades distintas, pedido de água e entrega, sem pedido quando não permitido, medo na turbulência e conversa, calmo aguenta, aviso de cinto, volta ao acender o cinto, ciclo do banheiro, falha de rota → nova tentativa → recuperação, sem ciclos num voo calmo, desabilitado, tabela de transições; eventos: estado, condição, incompatibilidade, cooldown, repetições, roteirizado, **mesma semente = mesma sequência**, intervalo global; interação: alcance, estado, mãos/itens, seleção de alvo |
| `PrototypeScenarioTests.cs` | Viagem inteira: embarque, mala, pedido de água, turbulência garantida, alvo correto do "acalmar", lixo solicitado, vitória com pontos esperados; derrota por ignorar a água; reinício recria o conteúdo |

**Resultado em 2026-10-13 (este ambiente, .NET 8.0.131):** `Passed! - Failed: 0, Passed: 68`.

### Compilação do código Unity — `verificar.sh`
`SkyCrew.Runtime`, `SkyCrew.Editor` e `SkyCrew.Tests.PlayMode` são compilados com **avisos como erro**. Exceção: CS0649 em campos `[SerializeField]`, que a própria Unity suprime.
**Resultado:** os 3 compilam. Limites estão em TECHNICAL_DECISIONS §11.

### PlayMode (`Tests/PlayMode`) — 4 testes, **só na Unity**
1. O bootstrap chega ao embarque. O cenário tem navegação da porta até a última fileira e até o banheiro, e o vão entre fileiras **não** é navegável.
2. A passageira do embarque chega à poltrona 2F (posição a menos de 0,3 m).
3. Pegar a mala e guardar no bagageiro conclui a tarefa, trava a mala e esvazia as mãos.
4. Um objeto solto não atravessa o piso.

**Ainda não executados**, porque não há Editor neste ambiente.

### Recursos 3D
- `gerar_modulos.py` reimporta cada FBX e confere altura esperada, base no chão, UV e material.
  **Resultado:** 16/16 OK (tabela em ASSET_MANIFEST.md).
- No Editor: **SkyCrew > 3. Validar recursos 3D**.

## Roteiro manual da Fase 1 (no Editor)

| # | Passo | Esperado |
|---|---|---|
| 1 | Play na cena Prototype | "Prepare-se para o embarque!" e, 2 s depois, fase *Embarque* |
| 2 | Andar com WASD, olhar com o mouse, Shift, Espaço | Movimento suave; câmera não atravessa paredes; sem atravessar poltronas |
| 3 | E na mala do corredor, depois E num bagageiro | Tarefa "Guardar a mala" concluída (+150) |
| 4 | Observar Lia | Caminha da porta até 2F e senta |
| 5 | Esperar o voo | Balão "Água!" sobre a Bia (3C) e tarefa com prazo |
| 6 | E na geladeira (cozinha, esquerda), depois E na Bia | Tarefa concluída; garrafa some; "Obrigado!" |
| 7 | Entregar água para outra pessoa | Recusa e a garrafa volta para a mão |
| 8 | Turbulência (25 s de voo) | Aviso de cinto; tremor; objetos pulam; tropeço; "!!" sobre o Otto; tarefa urgente |
| 9 | E no Otto | Tarefa concluída e, com as 3 obrigatórias, tela de resultados com estrelas |
| 10 | Esc no meio do voo → Continuar / Reiniciar / Sair | Pausa congela; reiniciar volta ao embarque sem resíduos |
| 11 | Ignorar a água por 75 s | Tarefa falha e a partida termina em derrota |

## Ainda sem cobertura
- Desempenho (FPS/CPU/GPU): nada foi medido; meta provisória de 60 FPS em 1080p.
- Multiplayer, salvamento, áudio: não existem ainda.
