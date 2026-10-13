# Publicar o SkyCrew no navegador (GitHub Pages)

O fluxo `.github/workflows/publicar-jogo.yml` roda a cada push nas branches `main` e
`claude/beautiful-gates-dsnswj`, e também pode ser iniciado à mão em **Actions**. Ele:

1. Compila o jogo para WebGL com a Unity 6 (`6000.0.84f1`) usando o GameCI.
   O método `SkyCrew.EditorTools.SkyCrewBuild.BuildWebGL` faz sozinho o que os menus
   SkyCrew 1, 4 e 5 fazem no Editor, e também liga o Input System.
2. Monta o site: página inicial, o jogo em `jogo/` e o visualizador do avião em `aviao/`.
3. Publica tudo na branch `gh-pages`.

Endereço: **https://danhayasen.github.io/ccc/**

## O que só o dono do repositório pode fazer (uma vez)

São dados da sua conta, então ninguém mais pode configurar por você.

### 1. Ligar o GitHub Pages

1. No repositório, abra **Settings → Pages**.
2. Em **Build and deployment → Source**, escolha **Deploy from a branch**.
3. Selecione a branch **gh-pages**, pasta **/ (root)**, e clique em **Save**.

A branch `gh-pages` aparece depois da primeira execução do fluxo.

### 2. Licença gratuita da Unity (para compilar o jogo)

Sem ela, o site publica só o visualizador do avião e uma página dizendo "em preparo".

1. Crie uma conta grátis em https://unity.com.
   Se você entrar com Google ou Apple, defina também uma senha no perfil da conta Unity,
   porque o GitHub precisa de e-mail e senha.
2. Instale o **Unity Hub**, entre com a conta, abra **Preferences/Settings → Licenses → Add**
   e escolha **Get a free personal license**.
3. Ache o arquivo de licença:
   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - Mac: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`
4. No GitHub, abra **Settings → Secrets and variables → Actions → New repository secret** e crie:

   | Nome | Valor |
   |------|-------|
   | `UNITY_LICENSE` | o conteúdo inteiro do arquivo `Unity_lic.ulf` (abra no Bloco de Notas e copie tudo) |
   | `UNITY_EMAIL` | o e-mail da conta Unity |
   | `UNITY_PASSWORD` | a senha da conta Unity |

5. Em **Actions → Publicar jogo no navegador**, clique em **Run workflow**.
   A primeira compilação demora uns 20 a 40 minutos, porque importa todos os modelos.
   As próximas usam cache e ficam mais rápidas.

Os segredos ficam criptografados no GitHub. Eles não aparecem nos logs nem no código.

## Limitações da versão de navegador

- Precisa de computador com teclado e mouse; celular não é suportado.
- O progresso é salvo no navegador (IndexedDB). Limpar os dados do site apaga o progresso.
- "Sair do jogo" não fecha a aba; é só fechar a aba.
- O som só começa depois do primeiro clique na página (regra dos navegadores).
