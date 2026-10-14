# Avião 3D (estilizado / low poly / cartunesco)

Modelo do avião do jogo feito no **Blender** (por script) e mostrado com **Three.js**.

| Arquivo | O que é |
|---|---|
| `blender/gerar_aviao.py` | Script que constrói o avião inteiro no Blender e exporta os arquivos abaixo |
| `aviao.glb` | Modelo pronto para o jogo (Three.js, Godot, Unity, etc.) |
| `aviao.blend` | Arquivo do Blender para abrir e editar à mão |
| `aviao.js` | Módulo para usar o avião no jogo (visual toon, modo corte, turbulência) |
| `index.html` + `viewer.js` | Visualizador: abra para ver e testar o avião |

## Ver o avião

Abra `aviao/index.html` por um servidor local (o navegador bloqueia `.glb` aberto direto do disco):

```bash
npx serve .        # depois acesse http://localhost:3000/aviao/
```

Atalhos na URL: `?corte`, `?vista=lateral` (`frontal`, `traseira`, `superior`), `?turbulencia`.

## Usar no jogo

```js
import { carregarAviao } from './aviao/aviao.js';

const aviao = await carregarAviao();
cena.add(aviao.objeto);

aviao.modoCorte = true;     // mostra o interior
aviao.turbulencia(1);       // chacoalha tudo: passageiros pulam, malas voam
// no loop de animação:
aviao.atualizar(dt);
```

Peças com nome no modelo (dá para pegar com `aviao.objeto.getObjectByName(...)`):
`Fuselagem`, `Asa_Esquerda`, `Motor_Direito`, `Deriva`, `Trem_de_Pouso`,
`Passageiro_00`…, `Piloto_1`, `Comissaria`, `Mala_0`…, `Animais`, `Cockpit`, `Cozinha`, `Banheiros`…
Cada passageiro e cada mala são objetos separados, prontos para física e comédia.

## Mudar o modelo

Edite `blender/gerar_aviao.py` (cores em `COR`, tamanhos no topo, zonas `X_PRIMEIRA`, `X_ECONOMICA`…) e rode:

```bash
blender --background --python aviao/blender/gerar_aviao.py
```
