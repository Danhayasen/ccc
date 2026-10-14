# Decisões técnicas

Cada decisão registra as alternativas avaliadas e o motivo da escolha.

## 1. Regras em C# puro (assembly sem referência à Unity)
- **Alternativas:** lógica dentro de MonoBehaviours; ScriptableObjects com estado.
- **Escolha:** `SkyCrew.Domain` com `noEngineReferences: true`.
- **Por quê:**
  - testes rápidos e determinísticos, inclusive fora do Editor (este ambiente não tem Unity);
  - autoridade única, pronta para o host no multiplayer;
  - sem dependência circular.
- **Custo:** a camada Unity precisa traduzir sinais e comandos.

## 2. Movimento do jogador: `CharacterController`
- **Alternativas:** Rigidbody dinâmico; Rigidbody cinemático com sweep manual.
- **Escolha:** CharacterController. Num corredor de 0,62 m, um Rigidbody dinâmico treme ao encostar em poltronas e passageiros, e é mais difícil de deixar estável em várias taxas de quadro.
- A comédia física fica nos **objetos** (Rigidbody com limites) e no **tropeço** controlado (`PlayerMotor.Stumble`), sem misturar os dois sistemas no mesmo personagem.

## 3. Navegação: NavMesh gerada em tempo de execução pelo módulo AI nativo
- **Alternativas:** pacote AI Navigation (`NavMeshSurface`); bake manual no Editor.
- **Escolha:** `NavMeshBuilder.BuildNavMeshData` sobre os colisores, sem pacote extra.
- **Por quê:**
  - o cenário é montado por código;
  - funciona igual em testes PlayMode;
  - dá para compilar e verificar aqui.
- O raio do agente é sobrescrito para 0,22 m nas configurações de build.
- **Revisar na Fase 3/4**, quando o avião for montado no Editor: aí o `NavMeshSurface` do AI Navigation passa a fazer sentido.

## 4. Cenário montado por código + modelos em `Resources`
- **Alternativas:** cena e prefabs montados à mão no Editor; Addressables.
- **Escolha:** `PrototypeLevelBuilder` mais FBX em pastas `Resources`, com plano B em primitivas se faltar um modelo.
- **Por quê:** sem o Editor, cenas e prefabs não podem ser criados nem validados. Montar por código é verificável e não quebra se um modelo faltar.
- A cena precisa de apenas um objeto (`GameBootstrap`).
- **Dívida técnica:** gerar prefabs no Editor (ferramenta prevista no ROADMAP) e migrar para Addressables quando houver mais conteúdo.

## 5. Arte: uma textura de paleta pintada (atlas de cores)
- **Alternativas:** cores por vértice; um material por cor.
- **Escolha:** cada face aponta para uma amostra de uma paleta 256×256. A amostra tem degradê de luz e pinceladas, o que dá o visual "pintado à mão".
- **Por quê:**
  - um material e uma textura para tudo (batching);
  - o URP Lit não mostra cor de vértice;
  - a paleta é fácil de trocar.
- **Exportação FBX:** Forward −Z, Up Y, *Apply Transform* e *FBX All*.
  - O *Apply Transform* gira os **netos** da hierarquia (verificado na reimportação). Por isso os personagens têm hierarquia de um nível só: cada parte é filha direta da raiz, com pivô na articulação.

## 6. Entrada: ações do Input System criadas em código
- **Alternativas:** asset `.inputactions` com classe gerada; componente `PlayerInput`.
- **Escolha:** ações em `PlayerInputReader`. Não depende de arquivo gerado pelo Editor, e o remapeamento futuro usa `ApplyBindingOverride`.
- `IPlayerCommandSource` desacopla a entrada da origem: teclado, rede ou testes.

## 7. Interface provisória em IMGUI
- **Alternativas:** UI Toolkit (exige PanelSettings e tema criados no Editor); uGUI (pacote).
- **Escolha:** IMGUI na Fase 1, porque funciona sem nenhum asset. A interface só **lê** o estado; ações passam pelo `GameBootstrap`.
- **Substituir por UI Toolkit na Fase 6.**

## 8. Aleatoriedade: SplitMix64 próprio
- `System.Random` não garante a mesma sequência entre runtimes, e `UnityEngine.Random` é global.
- O `DeterministicRandom` torna os eventos reproduzíveis nos testes e, futuramente, entre host e clientes.
- Efeitos puramente visuais (tremor, posição do lixo) usam `UnityEngine.Random`.

## 9. Pausa
- **Solo:** `Time.timeScale = 0`. No multiplayer, a pausa será só local, sem parar a simulação do host (Fase 5).

## 10. Multiplayer adiado
- Seguindo a especificação, o modo solo vem primeiro. O candidato para a Fase 5 é o **Netcode for GameObjects**, com autoridade do host.
- O domínio já concentra as ações em métodos `Try*` validados, que vão virar ServerRpc.

## 11. Verificação sem o Editor
- **Testes de regras:** `dotnet test` com NUnit 3.14 (API compatível com o NUnit da Unity).
- **Código Unity:** compilado contra `UnityEngine.Modules 2021.3.33` e `UnityEditor.dll 2021.1` (NuGet), mais stubs do Input System e do Test Framework.
  - Isso **não** prova o comportamento na Unity 6, só que as APIs usadas existem e que os tipos batem.
  - O código evita APIs que mudaram na Unity 6. Onde mudaram (`Rigidbody.linearVelocity` e `linearDamping`), há `#if UNITY_6000_0_OR_NEWER`; esse ramo não pôde ser compilado aqui.
