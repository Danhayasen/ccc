# Arquitetura

## Camadas e assemblies

| Camada | Assembly (`.asmdef`) | Pasta | Depende de |
|---|---|---|---|
| A — Core / regras (sem Unity) | `SkyCrew.Domain` (`noEngineReferences`) | `Scripts/Domain` | nada |
| B — Gameplay (MonoBehaviours) | `SkyCrew.Runtime` | `Scripts/Runtime` | Domain, Unity.InputSystem |
| C — Apresentação | dentro de `SkyCrew.Runtime` (`UI/`, `Characters/`) | — | lê o Domain, nunca altera regras |
| D — Infraestrutura | Editor: `SkyCrew.Editor`; rede e salvamento nas fases futuras | `Scripts/Editor` | Runtime, URP |
| E — Testes | `SkyCrew.Tests.EditMode`, `SkyCrew.Tests.PlayMode` | `Tests/` | Domain / Runtime |

A regra principal: **a camada de regras não conhece a Unity**. O `MatchSession` é a única autoridade sobre o
estado da partida, das tarefas, dos eventos e dos passageiros. A camada Unity faz duas coisas:
1. envia fatos ao domínio (`GameplaySignal`, `TryDeliverToPassenger`, `TryTalkToPassenger`);
2. executa os comandos que o domínio emite (`PassengerCommand`).

Essa separação permite três coisas:
- testar as regras com `dotnet test`, em milissegundos e sem o Editor;
- no multiplayer, rodar o mesmo `MatchSession` no host e transformar as chamadas `Try*` em RPCs;
- não ter dependência circular, porque o Domain não referencia nada.

```
 Input System ─► PlayerInputReader ─► PlayerController ─► PlayerMotor / ThirdPersonCamera
                                             │
                                             ▼
                                        Interactor ──(InteractionRules.Validate)──► IInteractable.Execute
                                                                                        │
             ┌──────────────────────────── sinais / Try* ──────────────────────────────┘
             ▼
      MatchSession (Domain) ── TaskManager ── ScoreLedger
             │                 EventScheduler ── PrototypeScenario (liga pedidos/eventos a tarefas)
             │                 PassengerBrain[] ──► PassengerCommand ──► PassengerController (NavMeshAgent)
             ▼
      GameHud / TurbulenceDirector (só leem estado e eventos)
```

## Pastas (`Assets/_Project`)

`Art/` (FBX em `*/Resources/SkyCrew/...`, textura em `Art/Textures`) · `Scenes/Aircraft/Prototype.unity` ·
`Scripts/Domain|Runtime|Editor` · `Tests/EditMode|PlayMode` · `Data/` (ScriptableObjects gerados pelo menu) ·
`Settings/` (URP gerado pelo menu). As pastas vazias previstas na especificação têm um `LEIA-ME.txt`.

## Contratos

| Contrato | Onde | Papel |
|---|---|---|
| `IInteractable` | Runtime/Interaction | Verbo, texto, item aceito, disponibilidade, prioridade e `Execute` |
| `InteractionRules.Validate` | Domain/Interaction | Validação pura (alcance, estado da partida, mãos, item certo); usada ao exibir **e** ao executar |
| `ITaskObjective` / `ObjectiveDefinition` | Domain/Tasks | Novos objetivos sem mexer no `TaskManager` |
| `IEventCondition` | Domain/Events | Condições de elegibilidade de eventos |
| `IScoreSink` | Domain/Scoring | Recompensa idempotente por chave |
| `IPassengerWorld` | Domain/PassengerAI | O que o passageiro sabe do mundo (cinto, turbulência, serviço) |
| `IPlayerCommandSource` | Runtime/Player | Origem dos comandos do jogador (teclado, controle, rede, testes) |
| `IRandom` | Domain/Common | Aleatoriedade injetável e reproduzível (SplitMix64) |
| `WorldRequest` / `WorldCleared` | Domain/Content | O cenário pede ao mundo 3D para criar ou limpar algo (lixo, mancha, carrinho, mala); `WorldDirector` decide posições |
| `InteractionVerb.ApplyItem` | Domain/Interaction | Usar o item segurado sem entregá-lo (esfregão na mancha) |

`ISaveParticipant` e `IInteractionHandler` ainda não foram criados: não há salvamento nem handlers externos
nesta fase (a regra da especificação é não criar interfaces sem necessidade).

## Partida

| De \ Para | Ready | Boarding | InFlight | Results | Failed | ReturningToMenu | Initializing |
|---|---|---|---|---|---|---|---|
| Initializing | ✔ (sem etapas pendentes) | | | | ✔ | | |
| Ready | | ✔ | | | | ✔ | |
| Boarding | | | ✔ (tempo) | | ✔ | ✔ | |
| InFlight | | | | ✔ (vitória) | ✔ | ✔ | |
| Results / Failed | | | | | | ✔ | ✔ (reiniciar) |
| ReturningToMenu | | | | | | | ✔ |

- **Inicialização:** etapas `cenario`, `navmesh`, `jogador` e `passageiros`. A partida só fica `Ready` quando todas terminam.
- **Vitória (no pouso):** o tempo de voo acaba com todas as tarefas de `RequiredTaskIds` concluídas. O voo continua depois das obrigatórias: pedidos e imprevistos seguem até o pouso (loop principal).
- **Derrota imediata:**
  - uma tarefa obrigatória falhou ou foi cancelada;
  - a satisfação média ficou abaixo de 0,2 por 3 s.
- **Derrota no pouso:** alguma obrigatória ainda pendente.
- `Finish` gera o resultado **uma única vez**. `Reset` recria tarefas, eventos, pontuação e passageiros do zero.

## Passageiros

Estados: `Idle, Seated, Waiting, RequestingService, WalkingToDestination, Interacting, ReactingToEvent,
Recovering, ReturningToSeat, Disabled`. As transições permitidas ficam numa tabela explícita
(`PassengerBrain.Transitions`). Cada transição fica registrada num histórico de 32 linhas para diagnóstico.

- **Decisão:** a cada 0,25 s, não a cada quadro. As necessidades mudam por taxas contínuas.
- **Prioridade quando sentado:** configurável. O padrão é medo, depois serviço, depois banheiro.
- **Regras de segurança:**
  - com o aviso de cinto aceso, ninguém se levanta para ir ao banheiro;
  - quem estava a caminho volta para a poltrona.
- **Histerese do medo:** o passageiro reage com medo ≥ 0,55 e só sai da reação com medo < 0,4. Isso evita ciclos sem progresso.
- **Navegação:**
  1. Se a rota falhar, o passageiro tenta de novo até 3 vezes.
  2. Depois disso, se ia ao banheiro, desiste e volta para a poltrona.
  3. Se ia para a poltrona, é teleportado para ela (recuperação).
- **Corpo:** detecta travamento (3 s sem sair do lugar).
- **Corredor:** `AisleTrafficCoordinator` limita quantos andam ao mesmo tempo (padrão 2, em fila).

Necessidades em [0, 1]: paciência, conforto, medo, sede, fome, frio e banheiro.
Satisfação = 0,45·paciência + 0,30·conforto + 0,25·(1 − medo).

**Pedidos:**
- Sede, fome e frio geram pedidos de água, lanche e cobertor. O passageiro escolhe o que mais passou do limite.
- A cabine aceita no máximo `MaxOpenRequests` (3) pedidos abertos ao mesmo tempo.
- Se a tarefa do pedido falhar, o passageiro **desiste**: perde 0,2 de paciência e pode pedir de novo depois.

**Personalidades com efeito:**
- *Prestativo* (Helpfulness ≥ 0,7): sentado e calmo, reduz 0,03/s do medo dos outros.
- *Desastrado*: tem peso maior no sorteio do evento "objeto derrubado".

## Tarefas

`Available → Active → Completed | Failed | Cancelled`.
- A definição (imutável) fica separada do `TaskRuntime`.
- Os objetivos só contam sinais recebidos depois da ativação.
- Recompensa e penalidade são aplicadas pelo `ScoreLedger` com chave única (`tarefa:<id>` e `tarefa_falhou:<id>`). Assim, sinais duplicados não pontuam duas vezes.
- Cancelar ou falhar uma tarefa cancela em cascata as tarefas que dependem dela.

Objetivos disponíveis: entregar item, levar item a uma zona, descartar N itens, conversar, ação nomeada (N vezes) e sequência ordenada.

- "Levar à zona" e "descartar" aceitam o **id da instância** (`TargetId` do sinal). Assim, a mala que caiu não conclui a tarefa da mala esquecida, e um lixo não conta para duas tarefas.
- **Bônus de resposta rápida:** concluir uma tarefa com prazo usando até 50% do tempo rende +25% da recompensa (chave `tarefa_rapida:<id>`).

## Eventos

O `EventScheduler` filtra os eventos elegíveis por:
- estado permitido;
- não estar ativo;
- limite de eventos ativos;
- limite de repetições;
- cooldown próprio;
- intervalo global;
- tags incompatíveis com os eventos ativos;
- condições.

Depois sorteia com peso e chance por rodada. Eventos roteirizados (garantidos) são tentados de novo enquanto estiverem bloqueados. A mesma semente gera a mesma sequência (teste `MesmaSementeMesmaSequencia`). `TryEnd` encerra um evento quando o problema é resolvido. `CancelAll` dispara `Ended` para restaurar estados temporários.

### Eventos do protótipo

| Evento | Tags (incompatível com) | Duração | Tarefa criada | Efeito no mundo (`WorldRequest`) |
|---|---|---|---|---|
| Turbulência leve (garantida aos 25 s) | turbulência, cabine (turbulência) | 10 s | Acalmar o mais sensível + recolher 2 lixos | Lixo; tremor, tropeços, objetos pulando |
| Objeto derrubado | cabine | 4 s | Recolher 1 lixo | Lixo perto do passageiro sorteado (peso = desastrado) |
| Derramamento | corredor (corredor) | 46 s | Esfregar 3× (45 s) | Mancha no corredor; escorregão |
| Corredor bloqueado | corredor (corredor) | 51 s | Levar o carrinho à cozinha (50 s) | Carrinho com NavMeshObstacle |
| Geladeira quebrada | equipamento (equipamento) | 61 s | Bater 3× (60 s) | `IsEquipmentAvailable("geladeira") = false` |
| Mala caída | cabine | 4 s | Guardar a mala que caiu (60 s) | Mala despencando no corredor |
| Passageiro assustado | passageiro | 3 s | Acalmar (45 s) | Medo +0,8 |

O máximo é de 2 eventos ativos, com intervalo global de 20 s e chance de 35% a cada 6 s. Resolver a tarefa encerra o evento e o mundo limpa o que criou (`WorldCleared`).

## Pontuação

```
total    = recompensas (inclui bônus de resposta rápida) − penalidades + satisfação_média × 500
estrelas = nº de limites atingidos em {2000, 3000, 4000}   (0 em derrota)
```
Os limites foram calibrados por simulação (`LoopSoakTests.CalibragemDePontos`, 20 viagens cada):

| Tempo de reação | Média de pontos |
|---|---|
| ~5 s | ~4600 |
| 15 s | ~3500 |
| 25 s | ~2800 |
| 35 s | ~2600 |
| Só as obrigatórias | < 1000 |

## Cenário de cabine (unidades em metros)

As medidas vêm de `CabinLayout`:
- 6 fileiras 3+3 com passo de 0,9 m;
- corredor de 0,62 m;
- cozinha na frente (z de 3,0 a 5,2);
- banheiro no fundo (z de −3,4 a −5,2).

O visual vem de módulos FBX e os colisores são caixas definidas em código. Trocar um modelo não altera a física nem a navegação. A NavMesh é gerada a partir dos **colisores** com raio de agente de 0,22 m. O vão entre fileiras fica fora da malha, e o passageiro desliza do corredor até a poltrona.

## Salvamento e progressão

- `SaveData` (versão 3) guarda configurações (volumes geral, música, ambiente, efeitos e vozes; sensibilidade; inverter Y), estatísticas permanentes, desbloqueios, uniforme e acessório equipados e os ids das últimas 32 partidas contabilizadas. Não guarda estado de partida nem segredos (há teste para isso).
- `SaveSerializer` grava texto `chave=valor` versionado, com cultura invariante e checksum FNV-1a no fim. A leitura:
  - converte arquivos v1 e v2 (`Migrated`). Na v2, o campo único `equipado` vira uniforme **ou** acessório;
  - nunca deixa equipado algo bloqueado ou de categoria errada;
  - limita valores absurdos;
  - recusa arquivo inconsistente ou corrompido.
- `SaveService` sobre `ISaveStorage` (`FileSaveStorage` na Unity, gravação temporária e troca atômica; `MemorySaveStorage` nos testes).
  - Arquivo ausente vira padrão.
  - Arquivo corrompido ou de versão futura vira padrão, com o original copiado para `skycrew.save.corrompido`.
  - Falha de disco devolve `false` sem derrubar o jogo.
- `ProgressionService.Apply(save, resultados, idDaPartida)` é **idempotente por id de partida**: cada viagem recebe um GUID ao embarcar. Ele soma voos, vitórias, estrelas (só em vitória), tarefas, pontos e recorde, e libera itens do catálogo:

| Item | Exige |
|---|---|
| Uniforme azul, Avião SkyCrew 1 | já liberados |
| Lenço amarelo | 2 estrelas |
| Uniforme vermelho | 5 estrelas |
| Quepe dourado | 9 estrelas |
| Modificador: turbulência dobrada | 12 estrelas e 3 vitórias |
| Uniforme verde | 15 estrelas |
| Avião SkyCrew 2 | 24 estrelas e 6 vitórias |

## Personagens e animação

- **Esqueleto** com nomes do padrão Humanoid da Unity: `Hips, Spine, Chest, Neck, Head, {Left,Right}{UpperLeg,LowerLeg,Foot,UpperArm,LowerArm,Hand}`.
- **Skinning rígido por segmento:** cada vértice tem peso 1 num osso. É o visual de "boneco articulado", sem deformação estranha nas juntas.
- **Arquivos:** os personagens ficam em `Art/Characters/.../<Nome>.fbx` (malha + esqueleto, rig *Generic*). As animações ficam em `Art/Animations/SkyCrew_Animacoes.fbx` (só esqueleto + 9 ações). Como os caminhos dos ossos são idênticos, as animações servem para todos.
- **Animator** gerado pelo menu *SkyCrew > 5*:
  - parâmetros `Speed`, `Seated`, `Carrying`, `Reacting`, `Talk` e `Slip`;
  - blend trees de locomoção (parado, andar, correr) e de carregar;
  - estados sentado, pânico (de qualquer estado, sem reiniciar a si mesmo), escorregar e falar.
- **Em tempo de execução:** `CharacterAnimation.Attach` usa o Animator se o modelo e o controller existirem. Senão, cai na animação procedural (`ICharacterAnimation` é a interface comum). O "squash and stretch" é aplicado por cima nos dois casos.
- **Sentado:** a raiz do passageiro fica no piso, no centro da poltrona, e a animação abaixa o quadril de 0,56 m para 0,46 m (o estofado fica a 0,47 m).

### Uniformes e acessórios
- Os uniformes (azul, vermelho, verde) são modelos completos `Comissaria[_Vermelho|_Verde].fbx`.
- Os acessórios (`Lenco_Amarelo`, `Quepe_Dourado`) são malhas modeladas **no espaço da raiz do personagem**. `OutfitApplier` alinha o acessório à raiz enquanto o esqueleto ainda está em repouso e o prende ao osso (`Chest` ou `Head`). Assim ele acompanha qualquer animação.
- O guarda-roupa só equipa itens desbloqueados (`ProgressionService.TryEquip`).

## Áudio
- `AudioRouter` (domínio, testado) decide **se** e **como** um som toca:
  - volume por canal × volume geral;
  - cooldown por som;
  - máximo de instâncias simultâneas;
  - variação de volume e tom a partir de semente (reproduzível);
  - canal mudo não ocupa voz.
- Canais: Música, Ambiente, Física, Interação, Vozes, Interface e Alertas. A configuração "Efeitos" cobre física, interação, interface e alertas.
- `AudioDirector` (Unity) escuta o jogo e toca pelo roteador, com pool de 16 fontes (posicionais quando há ponto no mundo). Ele escuta:
  - pedidos de passageiros, tarefas novas, concluídas e falhas;
  - início de evento e aviso de cinto;
  - interações (pegar, guardar, conversar, esfregar, bater);
  - impactos de objetos;
  - escorregão;
  - vitória e derrota.
- Loops: motor (mais alto em voo; o tom sobe com a turbulência), ronco da turbulência e música (abaixa durante a turbulência).
- **Todos os sons são sintetizados em código** (`ProceduralClips`) e são provisórios. Os arquivos definitivos entram na Fase 6 com os mesmos ids.

## Menus (provisórios, IMGUI)
- **Fluxo:** Ready → menu principal → *Jogar* → embarque.
- **Pausa:** Continuar, Reiniciar viagem, Configurações, Menu principal.
- **Resultados:** Jogar de novo (direto, sem menu) ou Menu principal.
- Configurações são aplicadas na hora (câmera e áudio) e salvas.
- Testes PlayMode usam `GameBootstrap.AutoStart` para pular o menu.

## Ferramentas de Editor (Parte 19)
| Menu | Função |
|---|---|
| SkyCrew > 1. Configurar projeto (URP) | Cria e atribui o pipeline URP |
| SkyCrew > 2. Recriar cena do protótipo | Cena com o `GameBootstrap` + Build Settings |
| SkyCrew > 3. Validar recursos 3D | Relatório: modelos, triângulos, tamanhos, esqueleto/Animator, clipes, controller |
| SkyCrew > 4. Criar dados padrão | ScriptableObjects de perfis e configuração |
| SkyCrew > 5. Gerar Animator dos personagens | Clipes com loop + Animator Controller |
| SkyCrew > Diagnóstico | Janela ao vivo: partida, eventos (forçar), tarefas, passageiros (necessidades, histórico, assustar/dar sede/frio), pular embarque, pousar agora |
