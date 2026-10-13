# Plano de testes

## Automáticos

### EditMode (regras, `Tests/EditMode`) — 123 testes
Rodam no Test Runner da Unity e fora dela (`Tools/Verificacao/verificar.sh`).

| Arquivo | Cobre |
|---|---|
| `MatchTests.cs` | Transições permitidas/proibidas, inicialização incompleta, embarque antes do voo, sinais fora de jogo, resultado único, tempo esgotado, reinício limpo, reinício bloqueado no voo |
| `TaskTests.cs` | Ativação/conclusão, sinal errado, sinal antes da ativação, recompensa única com sinais duplicados, prazo com penalidade, cancelamento, pré-requisitos e cascata, regra externa, id duplicado, sequência ordenada, contagem, isolamento entre ativações, ledger idempotente, fórmula de pontos |
| `PassengerAndEventTests.cs` | Personalidades distintas, pedido de água e entrega, sem pedido quando não permitido, medo na turbulência e conversa, calmo aguenta, aviso de cinto, volta ao acender o cinto, ciclo do banheiro, falha de rota → nova tentativa → recuperação, sem ciclos num voo calmo, desabilitado, tabela de transições; eventos: estado, condição, incompatibilidade, cooldown, repetições, roteirizado, **mesma semente = mesma sequência**, intervalo global; interação: alcance, estado, mãos/itens, seleção de alvo |
| `PrototypeScenarioTests.cs` | Viagem inteira: embarque, só a mala esquecida conta, pedido de água, turbulência garantida, alvo correto do "acalmar", lixo solicitado, vitória no pouso com pontos esperados (com bônus rápido); só obrigatórias = vitória sem estrelas; derrota por ignorar a água; reinício recria o conteúdo |
| `AudioTests.cs` | Cooldown contra rajada; limite de instâncias simultâneas; volume composto (geral × canal × base); canal mudo não ocupa voz; configurações salvas aplicadas aos canais; variação dentro dos limites e reproduzível; som desconhecido/intensidade zero; catálogo com ids únicos |
| `SaveAndProgressionTests.cs` | Ida e volta (uniforme, acessório, volume de vozes); migração v2→v3 (uniforme ou acessório); independente da cultura do sistema (pt-BR); arquivo vazio, lixo ou sem checksum → padrão; byte alterado detectado; migração v1→v2; versão futura preservada como backup; valores absurdos limitados, inconsistentes ou NaN recusados; item equipado bloqueado volta ao padrão; serviço sem arquivo ou corrompido; persistência entre "sessões" e falha de disco sem perder o arquivo anterior; sem segredos no arquivo; progressão idempotente por partida (inclusive após reiniciar o jogo); desbloqueios por estrelas e vitórias; derrota conta voo sem estrelas; recorde; limite da lista de partidas; só equipa o que foi desbloqueado |
| `Phase2Tests.cs` | Pedido do serviço mais urgente (água/lanche/cobertor); só aceita o que pediu; desistência custa paciência e permite novo pedido; limite de pedidos abertos; prestativo acalma vizinhos; desastrado derruba mais (60 sementes); bônus de resposta rápida; `TryEnd`; descarte por id; cada evento forçado (derramamento limpo e ignorado, carrinho, geladeira, mala caída, passageiro assustado, objeto derrubado); **resistência**: 40 viagens completas com robô (sem travar, resultado único, sem ciclos, todos os eventos aparecem); calibragem de pontos; robô lento perde |

**Resultado em 2026-10-13 (este ambiente, .NET 8.0.131):** `Passed! - Failed: 0, Passed: 123`.

Teste de resistência:
- 40 vitórias em 40 viagens e 1.304 tarefas concluídas.
- Os 7 tipos de evento ocorreram.
- Pontos do robô de 3 a 7 s: 4.238 / 4.641 / 5.003 (mín / média / máx).

O teste também revelou um problema de design, já corrigido: a viagem acabava assim que as obrigatórias eram concluídas, cerca de 30 s após a decolagem. Por isso eventos tardios nunca aconteciam. Agora a vitória é no pouso.

### Compilação do código Unity — `verificar.sh`
`SkyCrew.Runtime`, `SkyCrew.Editor` e `SkyCrew.Tests.PlayMode` são compilados com **avisos como erro**. Exceção: CS0649 em campos `[SerializeField]`, que a própria Unity suprime.
**Resultado:** os 3 compilam. Limites estão em TECHNICAL_DECISIONS §11.

### PlayMode (`Tests/PlayMode`) — 6 testes, **só na Unity**
1. O bootstrap chega ao embarque. O cenário tem navegação da porta até a última fileira e até o banheiro, e o vão entre fileiras **não** é navegável.
2. A passageira do embarque chega à poltrona 2F (posição a menos de 0,3 m).
3. Pegar a mala e guardar no bagageiro conclui a tarefa, trava a mala e esvazia as mãos.
4. Derramamento forçado → a mancha aparece → 3 esfregadas com o esfregão concluem a tarefa, encerram o evento e removem a mancha; o esfregão continua na mão.
5. Equipar o uniforme vermelho e o quepe dourado recria o visual com o quepe na altura da cabeça e uma única animação.
6. Um objeto solto não atravessa o piso.

**Ainda não executados**, porque não há Editor neste ambiente.

### Recursos 3D
- `gerar_modulos.py` reimporta cada FBX e confere altura esperada, base no chão, UV e material.
  **Resultado:** 16/16 módulos e props OK (tabela em ASSET_MANIFEST.md).
- `gerar_personagens.py` confere:
  - em cada personagem reimportado: 1 esqueleto e 1 malha ligados, todos os ossos, todo vértice com peso que soma 1 (no máximo 4 ossos), altura, base e rosto virado para +Z;
  - em cada animação e quadro: sem NaN e mãos/pés/cabeça dentro de limites;
  - que os loops fecham (primeiro quadro = último);
  - no *Andar*, que o pé esquerdo vai à frente junto com o braço direito;
  - no *Sentado*, que a coxa fica horizontal à frente;
  - no *Pânico*, que as duas mãos ficam acima dos ombros em **todos** os quadros;
  - que o FBX de animações contém as 9 ações.

  **Resultado:** 7/7 personagens (3 uniformes + 4 passageiros), 2/2 acessórios (y ≥ 0,85 m) e 10/10 animações OK. O *Pânico sentado* é checado em todos os quadros: mãos acima de 1,25 m e quadril abaixo de 0,5 m.
  Uma primeira versão do *Pânico* derrubava uma mão até a altura do ombro em alguns quadros (visto na prévia). A pose foi corrigida e a checagem passou a cobrir todos os quadros.
- `previas/poses.png`: um quadro de cada animação; `previas/uniformes.png`: uniformes com acessórios encaixados.
  A primeira versão da prévia de uniformes mostrou os personagens deitados. O erro era do script de prévia: ele **substituía** a rotação do nó raiz, que nos modelos com esqueleto carrega a conversão de eixos. A prévia foi corrigida; o código Unity já zera só a rotação da raiz do prefab, nunca a do esqueleto.
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
| 9 | E no Otto | Tarefa concluída; o voo continua até o pouso |
| 9a | Atender pedidos de lanche/cobertor (gavetas à direita na cozinha) | Balões "Lanche!"/"Cobertor!" somem; +100 (+25 se rápido) |
| 9b | Derramamento: correr por cima | Jogador escorrega |
| 9c | Derramamento: esfregão (armário do fundo) + E 3× na mancha | Mancha diminui e some; tarefa concluída |
| 9d | Corredor bloqueado: E no carrinho, puxar até a cozinha | Velocidade reduzida; ao entrar na cozinha a tarefa conclui; passageiros voltam a passar |
| 9e | Geladeira quebrada: luz vermelha pisca; E 3× | "Voltou a funcionar!"; água disponível de novo |
| 9f | Pouso (4:00) | Tela de resultados com estrelas |
| 10 | Esc no meio do voo → Continuar / Reiniciar / Sair | Pausa congela; reiniciar volta ao embarque sem resíduos |
| 11 | Ignorar a água por 75 s | Tarefa falha e a partida termina em derrota |
| 12 | Terminar uma viagem e reiniciar o jogo | Estrelas, voos e recorde persistem; desbloqueios aparecem na tela de resultados |
| 13 | Corromper `skycrew.save` à mão e abrir o jogo | Aviso na tela de preparo; jogo começa do zero; existe `skycrew.save.corrompido` |
| 14 | *SkyCrew > 5*, depois Play | Personagens animados: andar/correr pela velocidade, sentados nas poltronas, pânico (sentado nas poltronas) na turbulência, escorregão na mancha, gesto ao conversar |
| 15 | Menu principal → Configurações: mexer nos volumes e na sensibilidade, salvar | Áudio e câmera mudam na hora; persistem ao reabrir |
| 16 | Guarda-roupa com itens desbloqueados (use o Diagnóstico ou um save editado) | Uniforme troca; quepe/lenço acompanham a animação |
| 17 | Ouvir uma viagem | Motor contínuo; "plim" nos pedidos; acordes de tarefa concluída; zumbido de falha; alerta de evento; aviso de cinto; impactos sem acúmulo; música abaixa na turbulência |
| 18 | *SkyCrew > Diagnóstico* durante o Play | Valores ao vivo; "Forçar" cada evento; "Assustar" um passageiro gera o pânico |

## Ainda sem cobertura
- Desempenho (FPS/CPU/GPU): nada foi medido; meta provisória de 60 FPS em 1080p.
- Multiplayer, salvamento, áudio: não existem ainda.
