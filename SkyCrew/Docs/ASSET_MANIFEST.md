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
| Comissária | `Art/Characters/Resources/SkyCrew/Personagens/Comissaria.fbx` | 1600 | 0,72×1,72×0,61 | entre os pés | idem |
| Passageiro 1 (boné) | `.../Personagens/Passageiro_1.fbx` | 1512 | 0,72×1,73×0,59 | entre os pés | idem |
| Passageiro 2 (bolsa) | `.../Personagens/Passageiro_2.fbx` | 1432 | 0,72×1,77×0,59 | entre os pés | idem |
| Passageiro 3 (óculos) | `.../Personagens/Passageiro_3.fbx` | 1452 | 0,72×1,68×0,54 | entre os pés | idem |
| Passageiro 4 (coque) | `.../Personagens/Passageiro_4.fbx` | 1452 | 0,72×1,72×0,61 | entre os pés | idem |
| Textura de paleta | `Art/Textures/SkyCrew_Paleta.png` | — | 256×256 px, 36 cores | — | idem |
| Prévias | `Tools/Blender/previas/*.png` | — | — | — | `renderizar_previa.py` (Cycles) |

## Verificações feitas em cada FBX
Feitas reimportando o FBX no Blender com os mesmos eixos da exportação (`gerar_modulos.py > validar`):
- escala (altura esperada ±15%);
- base no chão (y ≥ −0,12);
- existência de UV e material;
- contagem de triângulos.

A orientação foi conferida visualmente: a prévia *elenco* tem a câmera em +Z da Unity e mostra o rosto dos personagens.

## Personagens: hierarquia (rig provisório)
`<Raiz>` com filhos diretos `Corpo`, `Cabeca`, `Braco_E`, `Braco_D`, `Perna_E` e `Perna_D`, cada um com pivô na
articulação. O `ProceduralCharacterAnimator` gira essas partes. **Não há esqueleto nem skinning:** isso é da Fase 3.

## Ainda pendente
- Conferência no Editor da Unity: importação, material URP com a textura e orientação.
- Áudio: nenhum recurso ainda.
- Os modelos do primeiro pedido (`aviao/` na raiz do repositório, visualizador web) são independentes deste projeto.
