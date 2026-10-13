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
- **Vitória:** todas as tarefas de `RequiredTaskIds` concluídas.
- **Derrota:**
  - uma tarefa obrigatória falhou ou foi cancelada;
  - o tempo de voo acabou;
  - a satisfação média ficou abaixo de 0,2 por 3 s.
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

Necessidades em [0, 1]: paciência, conforto, medo, sede e banheiro.
Satisfação = 0,45·paciência + 0,30·conforto + 0,25·(1 − medo).

## Tarefas

`Available → Active → Completed | Failed | Cancelled`.
- A definição (imutável) fica separada do `TaskRuntime`.
- Os objetivos só contam sinais recebidos depois da ativação.
- Recompensa e penalidade são aplicadas pelo `ScoreLedger` com chave única (`tarefa:<id>` e `tarefa_falhou:<id>`). Assim, sinais duplicados não pontuam duas vezes.
- Cancelar ou falhar uma tarefa cancela em cascata as tarefas que dependem dela.

Objetivos disponíveis: entregar item, levar item a uma zona, descartar N itens, conversar, ação nomeada e sequência ordenada.

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

Depois sorteia com peso e chance por rodada. Eventos roteirizados (garantidos) são tentados de novo enquanto estiverem bloqueados. A mesma semente gera a mesma sequência (teste `MesmaSementeMesmaSequencia`). `CancelAll` dispara `Ended` para restaurar estados temporários.

## Pontuação

```
total   = recompensas − penalidades + satisfação_média × 500 + bônus_tempo
bônus_tempo = min(300, segundos_restantes × 2)   (só em vitória)
estrelas = nº de limites atingidos em {400, 800, 1200}   (0 em derrota)
```

## Cenário de cabine (unidades em metros)

As medidas vêm de `CabinLayout`:
- 6 fileiras 3+3 com passo de 0,9 m;
- corredor de 0,62 m;
- cozinha na frente (z de 3,0 a 5,2);
- banheiro no fundo (z de −3,4 a −5,2).

O visual vem de módulos FBX e os colisores são caixas definidas em código. Trocar um modelo não altera a física nem a navegação. A NavMesh é gerada a partir dos **colisores** com raio de agente de 0,22 m. O vão entre fileiras fica fora da malha, e o passageiro desliza do corredor até a poltrona.
