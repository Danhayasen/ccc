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
- Interface **IMGUI provisória**: sem menu principal, seleção de personagem, lobby ou configurações.
- **Animação procedural provisória**: sem rig e sem Animator.
- **Sem áudio.**
- **Sem multiplayer** e sem salvamento.
- `ItemDispenser` limita a 5 garrafas soltas ao mesmo tempo. Garrafas largadas pelo chão contam até sumirem.
- O passageiro "desliza" para dentro da poltrona (o vão entre fileiras não é navegável de propósito).
- Desempenho **não medido**.
- O tremor da turbulência é aplicado à câmera e aos objetos. A cabine em si não inclina, porque isso desalinharia a NavMesh.
