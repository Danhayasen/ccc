# SkyCrew (nome provisório)

Jogo 3D cartunesco de simulação caótica de passageiros: você é da tripulação de um avião comercial fictício
e precisa atender pedidos, organizar a cabine e lidar com turbulência, com comédia física e (no futuro)
cooperação online.

- **Engine:** Unity 6 (6000.x) · C# · URP · Input System
- **Plataforma inicial:** Windows PC
- **Estado:** Fase 0 concluída e Fase 1 (protótipo mínimo jogável) implementada, **ainda não executada no Editor da Unity** (veja *Estado real*)

![Elenco](Tools/Blender/previas/elenco.png)
![Cabine](Tools/Blender/previas/cabine.png)

*Prévias renderizadas no Blender a partir dos próprios FBX que a Unity importa.*

## Estado real (Fase 1)

| Sistema | Estado | Evidência |
|---|---|---|
| Ciclo da partida (estados, transições, resultado único, reinício) | Implementado e testado | 23 testes EditMode (inclui casos parametrizados) |
| Tarefas (objetivos modulares, prazo, dependências, recompensa única) | Implementado e testado | 13 testes |
| Passageiros: necessidades, 7 personalidades, máquina de estados | Implementado e testado | 12 testes |
| Eventos dinâmicos (turbulência; elegibilidade, compatibilidade, semente) | Implementado e testado | 8 testes |
| Pontuação (fórmula documentada) e regras de interação | Implementado e testado | 8 testes |
| Viagem completa do protótipo (3 tarefas + turbulência + vitória/derrota) | Implementado e testado (só regras) | 4 testes de cenário |
| Jogador (CharacterController, câmera 3ª pessoa, Input System) | Implementado, **compila**, não executado | `verificar.sh` |
| Interação, pegar/soltar/entregar, física limitada | Implementado, **compila**, não executado | `verificar.sh` |
| Passageiro com NavMesh em tempo de execução, fila no corredor | Implementado, **compila**, não executado | `verificar.sh` |
| HUD, pausa e tela de resultados (IMGUI provisório) | Implementado, **compila**, não executado | `verificar.sh` |
| Cena do protótipo e configuração URP | Preparados; **exigem o Editor** para validar | `SkyCrew > ...` |
| Modelos 3D (cabine modular, props, 5 personagens) | Gerados e validados no Blender | reimportação dos FBX |
| Testes PlayMode | Escritos e **compilam**; rodam só na Unity | — |
| Animação | Provisória (procedural); rig na Fase 3 | — |
| Áudio, menu principal, multiplayer, salvamento | Ainda não implementados | ROADMAP |

## Como abrir e jogar

1. Instale a **Unity 6** (qualquer 6000.x LTS) pelo Unity Hub e use **Add project from disk** nesta pasta `SkyCrew/`.
   O projeto não traz `ProjectVersion.txt`; o Hub pergunta a versão.
2. Aguarde o Package Manager instalar URP, Input System e Test Framework (`Packages/manifest.json`).
   Se a versão de algum pacote não existir na sua Unity, aceite a versão sugerida.
3. Quando a Unity perguntar se quer ativar o novo Input System, clique **Yes** (o Editor reinicia).
   Confira em *Project Settings > Player > Active Input Handling* = **Input System Package (New)**.
4. Menu **SkyCrew > 1. Configurar projeto (URP)**.
5. Abra `Assets/_Project/Scenes/Aircraft/Prototype.unity` e aperte **Play**.
   Se a cena não abrir, use **SkyCrew > 2. Recriar cena do protótipo**.
6. Opcional: **SkyCrew > 3. Validar recursos 3D** (relatório no Console) e **SkyCrew > 4. Criar dados padrão**.

### Controles

WASD mover · Mouse câmera · Shift correr · Espaço pular · **E** interagir (sem alvo: soltar) · **Tab** detalhes das tarefas · **Esc** pausar. Controle de videogame também é mapeado.

### Objetivo da viagem do protótipo

1. **Embarque (30 s):** guarde a mala esquecida no corredor num bagageiro. A passageira Lia embarca e caminha até a poltrona 2F.
2. **Em voo:** a Bia (exigente) pede **água**. Pegue uma garrafa na geladeira da cozinha e entregue.
3. Aos 25 s de voo vem a **turbulência**: o aviso de cinto acende, objetos voam, você tropeça, e o Otto (ansioso) entra em pânico. **Converse** com ele.
4. Extra: recolha o lixo derrubado e jogue na lixeira.

Você vence ao concluir as 3 tarefas obrigatórias. Você perde se o tempo de voo (4 min) acabar, se uma tarefa obrigatória falhar ou se a satisfação da cabine despencar.

## Testes e verificação

- **Na Unity:** *Window > General > Test Runner* → EditMode (68 testes de regras) e PlayMode (4 testes de cena).
- **Sem a Unity** (Linux/Mac/Windows com .NET 8):
  ```bash
  Tools/Verificacao/verificar.sh
  ```
  Esse comando roda os testes EditMode com `dotnet test`. Também compila o código Unity (Runtime, Editor e testes PlayMode) contra as DLLs de referência da Unity baixadas do NuGet. Detalhes e limites estão em [Docs/TEST_PLAN.md](Docs/TEST_PLAN.md).

## Modelos 3D

Gerados por script no Blender (sem recursos de terceiros):

```bash
blender --background --python Tools/Blender/gerar_modulos.py      # FBX + textura de paleta + validação
blender --background --python Tools/Blender/renderizar_previa.py  # prévias em Tools/Blender/previas
```

## Documentação

[Arquitetura](Docs/ARCHITECTURE.md) · [Decisões técnicas](Docs/TECHNICAL_DECISIONS.md) · [Plano de testes](Docs/TEST_PLAN.md) ·
[Recursos 3D](Docs/ASSET_MANIFEST.md) · [Problemas conhecidos](Docs/KNOWN_ISSUES.md) · [Roadmap](Docs/ROADMAP.md)

## Originalidade

O gênero (simulação caótica em avião) tem como referência geral jogos como *Dear Passengers*. Nenhum recurso,
personagem, texto ou identidade visual desse jogo foi usado. Todos os modelos, textura, código e textos foram
criados para este projeto.
