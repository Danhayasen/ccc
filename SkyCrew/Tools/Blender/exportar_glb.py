"""
Converte os FBX do projeto Unity em GLB para a versao de navegador (SkyCrew/Web/jogo/modelos).
Os FBX continuam sendo a fonte (os mesmos que a Unity importa); este script so muda o formato.

Uso:  python exportar_glb.py        (com o modulo bpy instalado, como os outros scripts)

- Personagens: malha + esqueleto, sem animacoes.
- Animacoes: um GLB so com o esqueleto e as 10 acoes (nomes limpos: "Andar", "Sentado"...).
  No three.js os clipes tocam em qualquer personagem porque os nomes dos ossos sao iguais.
"""
import os
import sys

import bpy

AQUI = os.path.dirname(os.path.abspath(__file__))
PROJETO = os.path.abspath(os.path.join(AQUI, "..", ".."))
ART = os.path.join(PROJETO, "Assets", "_Project", "Art")
SAIDA = os.path.join(PROJETO, "Web", "jogo", "modelos")
PALETA = os.path.join(ART, "Textures", "SkyCrew_Paleta.png")

GRUPOS = {
    "Cabine": os.path.join(ART, "Aircraft", "Resources", "SkyCrew", "Cabine"),
    "Props": os.path.join(ART, "Props", "Resources", "SkyCrew", "Props"),
    "Personagens": os.path.join(ART, "Characters", "Resources", "SkyCrew", "Personagens"),
    "Acessorios": os.path.join(ART, "Characters", "Resources", "SkyCrew", "Acessorios"),
}
ANIMACOES = os.path.join(ART, "Animations", "SkyCrew_Animacoes.fbx")


def limpar():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def importar(caminho, com_esqueleto):
    # Modelos com esqueleto foram exportados SEM "Apply Transform" (conversao de eixos no no raiz).
    bpy.ops.import_scene.fbx(filepath=caminho, axis_forward="-Z", axis_up="Y",
                             bake_space_transform=not com_esqueleto)
    for img in bpy.data.images:
        if img.filepath and "SkyCrew_Paleta" in img.filepath:
            img.filepath = PALETA
            img.reload()


def exportar(destino, animacoes):
    os.makedirs(os.path.dirname(destino), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=destino,
        export_format="GLB",
        export_yup=True,
        export_apply=False,
        export_animations=animacoes,
        export_animation_mode="ACTIONS",
        export_skins=True,
        export_morph=False,
        export_cameras=False,
        export_lights=False,
        export_image_format="AUTO",
    )


def main():
    total = 0
    for grupo, pasta in GRUPOS.items():
        com_esqueleto = grupo == "Personagens"
        for nome in sorted(os.listdir(pasta)):
            if not nome.endswith(".fbx"):
                continue
            limpar()
            importar(os.path.join(pasta, nome), com_esqueleto)
            destino = os.path.join(SAIDA, grupo, nome[:-4] + ".glb")
            exportar(destino, animacoes=False)
            total += 1
            print(f"OK  {grupo}/{nome[:-4]}.glb")

    limpar()
    importar(ANIMACOES, True)
    nomes = []
    for acao in bpy.data.actions:
        acao.name = acao.name.split("|")[-1]
        acao.use_fake_user = True
        nomes.append(acao.name)
    # Remove a malha (se houver): o GLB de animacoes so precisa do esqueleto.
    for o in list(bpy.data.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    exportar(os.path.join(SAIDA, "Animacoes.glb"), animacoes=True)
    print(f"OK  Animacoes.glb ({len(nomes)} clipes: {', '.join(sorted(nomes))})")
    print(f"{total + 1} arquivos em {SAIDA}")


if __name__ == "__main__":
    sys.exit(main())
