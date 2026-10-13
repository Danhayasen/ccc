# Manifesto de recursos

Todos os recursos abaixo são **originais**: foram gerados por script neste projeto (Blender 5.2.2 via módulo `bpy`) e
não usam modelos, texturas ou bibliotecas de terceiros. A licença segue a do projeto. Nenhum recurso externo ou
licenciado foi importado.

| Recurso | Arquivo | Triângulos | Tamanho (Unity X×Y×Z, m) | Pivô | Origem |
|---|---|---|---|---|---|
| Segmento da cabine | `Art/Aircraft/Resources/SkyCrew/Cabine/Segmento.fbx` | 480 | 3,96×2,41×2,08 | piso, centro | `gerar_modulos.py` |
| Parede da frente (porta da cabine) | `.../Cabine/Parede_Frente.fbx` | 104 | 3,80×2,30×0,14 | piso, centro | idem |
| Parede do fundo | `.../Cabine/Parede_Fundo.fbx` | 100 | 3,80×2,30×0,10 | piso, centro | idem |
| Poltrona | `.../Cabine/Poltrona.fbx` | 180 | 0,53×1,24×0,64 | piso, centro do assento | idem |
| Bagageiro (2,6 m) | `.../Cabine/Bagageiro.fbx` | 224 | 0,68×0,46×2,60 | base, centro (porta em +X) | idem |
| Cozinha | `.../Cabine/Cozinha.fbx` | 972 | 3,85×2,20×2,05 | piso, centro | idem |
| Banheiro (mobília) | `.../Cabine/Banheiro.fbx` | 220 | 1,38×2,06×1,76 | piso, centro | idem |
| Mala | `Art/Props/Resources/SkyCrew/Props/Mala.fbx` | 168 | 0,52×0,44×0,25 | base | idem |
| Garrafa de água | `.../Props/Garrafa.fbx` | 136 | 0,09×0,26×0,09 | base | idem |
| Lixo (papel amassado) | `.../Props/Lixo.fbx` | 20 | 0,14×0,16×0,12 | base | idem |
| Lixeira | `.../Props/Lixeira.fbx` | 96 | 0,40×0,70×0,43 | base | idem |
| Lanche | `.../Props/Lanche.fbx` | 68 | 0,20×0,10×0,14 | base | idem |
| Cobertor | `.../Props/Cobertor.fbx` | 68 | 0,35×0,13×0,26 | base | idem |
| Esfregão | `.../Props/Esfregao.fbx` | 148 | 0,32×1,20×0,10 | base | idem |
| Carrinho de serviço | `.../Props/Carrinho.fbx` | 340 | 0,42×1,11×0,87 | piso, centro (alça em −Z) | idem |
| Mancha de suco | `.../Props/Mancha.fbx` | 84 | 0,79×0,01×0,59 | piso, centro | idem |
| Comissária | `Art/Characters/Resources/SkyCrew/Personagens/Comissaria.fbx` | 1936 | 0,72×1,72×0,61 | entre os pés | `gerar_personagens.py` |
| Passageiro 1 (boné) | `.../Personagens/Passageiro_1.fbx` | 1848 | ~0,7×1,73×0,6 | entre os pés | idem |
| Passageiro 2 (bolsa) | `.../Personagens/Passageiro_2.fbx` | 1768 | ~0,7×1,77×0,6 | entre os pés | idem |
| Passageiro 3 (óculos) | `.../Personagens/Passageiro_3.fbx` | 1788 | ~0,7×1,68×0,55 | entre os pés | idem |
| Passageiro 4 (coque) | `.../Personagens/Passageiro_4.fbx` | 1788 | ~0,7×1,72×0,6 | entre os pés | idem |
| Biblioteca de animações (9 clipes, 24 fps) | `Art/Animations/SkyCrew_Animacoes.fbx` | — | esqueleto | — | idem |
| Textura de paleta | `Art/Textures/SkyCrew_Paleta.png` | — | 256×256 px, 36 cores | — | idem |
| Prévias | `Tools/Blender/previas/*.png` | — | — | — | `renderizar_previa.py` (Cycles) |

## Verificações feitas em cada FBX
Feitas reimportando o FBX no Blender com os mesmos eixos da exportação (`gerar_modulos.py > validar`):
- escala (altura esperada ±15%);
- base no chão (y ≥ −0,12);
- existência de UV e material;
- contagem de triângulos.

A orientação foi conferida visualmente: a prévia *elenco* tem a câmera em +Z da Unity e mostra o rosto dos personagens.

## Personagens: esqueleto e animações (Fase 3)
- `Armature` com 17 ossos de nomes Humanoid e uma malha com skinning rígido (peso 1 por vértice).
- Exportados **sem** *Apply Transform* (ele não é confiável com esqueletos). A conversão de eixos fica num nó filho, e a raiz do prefab segue sem rotação.
- Clipes:

| Clipe | Quadros | Loop |
|---|---|---|
| Parado | 49 | sim |
| Andar | 25 | sim |
| Correr | 17 | sim |
| Carregar | 25 | sim |
| CarregarParado | 25 | sim |
| Sentado | 49 | sim |
| Pânico | 17 | sim |
| Escorregar | 30 | não |
| Falar | 25 | não |

## Ainda pendente
- Conferência no Editor da Unity: importação, material URP com a textura e orientação.
- Áudio: nenhum recurso ainda.
- Os modelos do primeiro pedido (`aviao/` na raiz do repositório, visualizador web) são independentes deste projeto.
