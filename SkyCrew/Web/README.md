# SkyCrew — versão de navegador

Versão jogável no navegador, publicada em **https://danhayasen.github.io/ccc/jogo/**.

- **Regras:** o mesmo C# de `Assets/_Project/Scripts/Domain` que a Unity usa (partida, tarefas,
  passageiros, eventos, pontuação, progresso, interação, áudio), compilado para WebAssembly
  (`Web/Regras`, .NET 8). A fachada `Regras.cs` faz o papel do `GameBootstrap`: recebe sinais do mundo
  e devolve estado e eventos em JSON. Nenhuma regra de jogo é reescrita em JavaScript.
- **Mundo 3D:** three.js (`Web/jogo/js`), porta direta da camada `Runtime` da Unity (cabine, jogador,
  itens, passageiros, carrinho, mancha, turbulência, HUD, menus, sons sintetizados).
- **Modelos:** os mesmos FBX da Unity convertidos para GLB por `Tools/Blender/exportar_glb.py`.
- **Progresso:** salvo no `localStorage` do navegador (mesmo formato de save v3 da Unity).

## Rodar localmente

```bash
dotnet publish SkyCrew/Web/Regras -c Release -o /tmp/regras
mkdir -p SkyCrew/Web/jogo/regras && cp -r /tmp/regras/wwwroot/_framework SkyCrew/Web/jogo/regras/
npx http-server SkyCrew/Web/jogo -p 8124      # abra http://127.0.0.1:8124
```

## Testes

`Tools/Verificacao/Web/testar.mjs` abre o jogo no Chromium headless e um robô joga uma viagem
inteira usando as mesmas interações do jogador (`partida`), ou força cada evento (`eventos`).
Resultado em 2026-10-14: vitória com 2 estrelas, 22 tarefas concluídas e 0 falhas, sem erros no console;
os eventos carrinho, geladeira, mala caída, derramamento e passageiro assustado resolvidos.

## Diferenças em relação à Unity

- Navegação dos passageiros: o corredor é uma linha reta, então andam em Z por x = 0 (sem NavMesh).
- Física própria e simples (caixas alinhadas aos eixos), no lugar do PhysX.
- Sombras desligadas; luzes pontuais e ambiente.
