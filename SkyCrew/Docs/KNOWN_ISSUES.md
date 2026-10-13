# Problemas conhecidos e limitações

## Bloqueios do ambiente de desenvolvimento
1. **Sem Editor da Unity neste ambiente** (contêiner Linux sem licença nem instalador). Nada da camada Unity foi
   *executado*: o código compila contra DLLs de referência, mas a cena, a física, a NavMesh, a câmera, a UI e os testes
   PlayMode precisam ser conferidos no Editor (roteiro em TEST_PLAN.md).
2. **Referências de compilação são da Unity 2021.3/2021.1**, não da 6. As APIs usadas existem nas duas. O ramo
   `#if UNITY_6000_0_OR_NEWER` em `CarryableItem` (`linearVelocity`/`linearDamping`) nunca foi compilado aqui.
3. **Stubs** do Input System e do Test Framework (`Tools/Verificacao/Stubs/PackageStubs.cs`) foram escritos a partir
   da API pública. Se alguma assinatura divergir, o erro só aparece no Editor.

## A conferir na primeira abertura no Editor
- **Versões no `manifest.json`** (URP 17.0.4, Input System 1.11.2, Test Framework 1.4.6): se não baterem com a sua
  Unity 6, aceite as sugeridas pelo Package Manager.
- **`ProjectVersion.txt`** não foi incluído de propósito: a versão exata não pôde ser verificada.
- A **cena `Prototype.unity` foi escrita à mão** (YAML mínimo). Se não abrir, use *SkyCrew > 2. Recriar cena do protótipo*.
- **Active Input Handling** precisa ser *Input System Package (New)*.
- **Materiais dos FBX:** se os modelos aparecerem brancos, abra o FBX e vá em *Materials > Search and Remap*
  (textura `Art/Textures/SkyCrew_Paleta.png`). O `AssetPostprocessor` já pede busca *RecursiveUp*.
- **Orientação dos FBX:** foi conferida só no Blender. Se algo vier deitado, marque *Bake Axis Conversion* no import.

## Limitações conhecidas do protótipo
- Interface **IMGUI provisória**: já tem menu principal, configurações e guarda-roupa; faltam lobby, seleção de cenário/personagem e tela de carregamento (só fazem sentido com multiplayer e mais aviões).
- **Animação:** personagens com esqueleto e 9 clipes, mas o Animator Controller só existe depois de rodar *SkyCrew > 5* no Editor. Até lá vale a animação procedural de reserva, que no modelo com esqueleto só gira quadril e braços de forma aproximada.
- **Rig Generic, não Humanoid:** os nomes já seguem o padrão Humanoid, mas a pose de repouso é com braços para baixo. Para usar animações de outras fontes (retargeting), configure Humanoid e use *Enforce T-Pose* no Avatar.
- **Orientação do FBX com esqueleto:** foi conferida só no Blender. Se o personagem vier deitado, marque *Bake Axis Conversion* no importador.
- **Áudio provisório:** sons sintetizados por código, sem gravações. Os níveis e a mixagem precisam ser ajustados ouvindo no Editor.
- **Sem multiplayer.**
- **Configurações:** sem opções gráficas e sem remapeamento de teclas na tela (o código das ações já permite `ApplyBindingOverride`).
- `ItemDispenser` limita a 5 garrafas soltas ao mesmo tempo. Garrafas largadas pelo chão contam até sumirem.
- O passageiro "desliza" para dentro da poltrona (o vão entre fileiras não é navegável de propósito).
- Desempenho **não medido**.
- O tremor da turbulência é aplicado à câmera e aos objetos. A cabine em si não inclina, porque isso desalinharia a NavMesh.
- **Carrinho:** ao ser pego, mantém o deslocamento em relação ao jogador (em linha reta pelo corredor). Se travar numa parede e o jogador se afastar mais de 1,2 m, ele se solta sozinho. A sensação de controle precisa ser ajustada no Editor.
- **Corredor bloqueado:** os passageiros que precisam passar tentam de novo 3 vezes e depois voltam para a poltrona (teleporte de recuperação). Funciona, mas falta uma reação visual de "esperando".
- O **robô** dos testes de resistência não anda pela cabine, então as notas calibradas valem para o tempo de reação e não incluem o deslocamento. Recalibrar com jogadores reais.
