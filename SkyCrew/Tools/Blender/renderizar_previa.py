"""
Monta uma cena de previa com os FBX exportados (como a Unity os recebe) e renderiza PNGs.
Uso: blender --background --python Tools/Blender/renderizar_previa.py
"""
import math
import os
import sys

import bpy
from mathutils import Vector

AQUI = os.path.dirname(os.path.abspath(__file__))
ART = os.path.normpath(os.path.join(AQUI, "..", "..", "Assets", "_Project", "Art"))
SAIDA = os.path.join(AQUI, "previas")
CAB = os.path.join(ART, "Aircraft", "Resources", "SkyCrew", "Cabine")
PROPS = os.path.join(ART, "Props", "Resources", "SkyCrew", "Props")
PERS = os.path.join(ART, "Characters", "Resources", "SkyCrew", "Personagens")


def U(x, y, z):
    return Vector((-x, -z, y))


def importar(caminho, pos, rot_y=0.0):
    antes = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=caminho, axis_forward="-Z", axis_up="Y", bake_space_transform=True)
    novos = [o for o in bpy.data.objects if o not in antes]
    raizes = [o for o in novos if o.parent is None]
    for r in raizes:
        r.location = U(*pos)
        r.rotation_euler = (0, 0, math.radians(rot_y))
    # textura: o FBX guarda so o nome do arquivo (como na Unity)
    for o in novos:
        for slot in getattr(o, "material_slots", []):
            if slot.material and slot.material.use_nodes:
                for n in slot.material.node_tree.nodes:
                    if n.type == "TEX_IMAGE" and n.image:
                        n.image.filepath = os.path.join(ART, "Textures", "SkyCrew_Paleta.png")
                        n.image.reload()
    return raizes


def camera(pos, alvo, lente=28):
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    bpy.context.scene.collection.objects.link(cam)
    cam.location = U(*pos)
    direcao = U(*alvo) - cam.location
    cam.rotation_euler = direcao.to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lente
    bpy.context.scene.camera = cam
    return cam


def preparar_render(largura=1280, altura=720, amostras=48):
    cena = bpy.context.scene
    cena.render.engine = "CYCLES"
    cena.cycles.device = "CPU"
    cena.cycles.samples = amostras
    cena.cycles.use_denoising = True
    try:
        cena.cycles.denoiser = "OPENIMAGEDENOISE"
    except TypeError:
        cena.cycles.use_denoising = False
    cena.render.resolution_x = largura
    cena.render.resolution_y = altura
    cena.render.film_transparent = False
    cena.view_settings.view_transform = "Standard"
    mundo = bpy.data.worlds.new("Mundo")
    mundo.use_nodes = True
    mundo.node_tree.nodes["Background"].inputs["Color"].default_value = (0.75, 0.85, 1.0, 1)
    mundo.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.9
    cena.world = mundo


def luz(pos, energia=300, tamanho=2.0):
    l = bpy.data.objects.new("Luz", bpy.data.lights.new("Luz", "AREA"))
    l.data.energy = energia
    l.data.size = tamanho
    l.data.color = (1.0, 0.95, 0.88)
    bpy.context.scene.collection.objects.link(l)
    l.location = U(*pos)
    l.rotation_euler = (0, 0, 0)  # area aponta para -Z do Blender (para baixo)
    return l


def renderizar(nome):
    os.makedirs(SAIDA, exist_ok=True)
    bpy.context.scene.render.filepath = os.path.join(SAIDA, nome)
    bpy.ops.render.render(write_still=True)
    print("render:", os.path.join(SAIDA, nome))


def cena_cabine():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    preparar_render()
    for i, z in enumerate((-2.08, 0.0, 2.08)):
        importar(os.path.join(CAB, "Segmento.fbx"), (0, 0, z))
    importar(os.path.join(CAB, "Parede_Fundo.fbx"), (0, 0, -3.12))
    for row, z in enumerate((1.4, 0.5, -0.4, -1.3)):
        for x in (-1.27, -0.79, -0.31 - 0.24 + 0.0, 0.55, 1.03, 1.51):
            importar(os.path.join(CAB, "Poltrona.fbx"), (x, 0, z))
    importar(os.path.join(CAB, "Bagageiro.fbx"), (-1.55, 1.75, 0.0))
    importar(os.path.join(CAB, "Bagageiro.fbx"), (1.55, 1.75, 0.0), rot_y=180)
    importar(os.path.join(PERS, "Comissaria.fbx"), (0.0, 0, 1.0), rot_y=180)
    importar(os.path.join(PROPS, "Mala.fbx"), (0.05, 0, -0.9), rot_y=90)
    importar(os.path.join(PROPS, "Lixo.fbx"), (-0.1, 0, 0.3))
    for z in (1.4, 0.5, -0.4, -1.3):
        luz((0, 2.1, z), energia=60, tamanho=0.6)
    luz((0, 6, 6), energia=400, tamanho=4)
    camera((0.2, 1.65, 3.2), (0.0, 0.85, -1.0), lente=20)
    renderizar("cabine.png")


def cena_elenco():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    preparar_render(1280, 720)
    nomes = ["Passageiro_1", "Passageiro_2", "Comissaria", "Passageiro_3", "Passageiro_4"]
    for i, n in enumerate(nomes):
        importar(os.path.join(PERS, n + ".fbx"), ((i - 2) * 0.85, 0, 0))
    props = [("Mala", (-1.2, 0, 1.0)), ("Garrafa", (-0.5, 0, 1.1)), ("Lixeira", (0.4, 0, 1.0)), ("Lixo", (1.1, 0, 1.1))]
    for n, p in props:
        importar(os.path.join(PROPS, n + ".fbx"), p)
    bpy.ops.mesh.primitive_plane_add(size=20, location=(0, 0, 0))
    chao = bpy.context.active_object
    mat = bpy.data.materials.new("Chao")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.82, 0.86, 0.95, 1)
    chao.data.materials.append(mat)
    luz((1.5, 4, 4), energia=500, tamanho=3)
    luz((-3, 3, 2), energia=150, tamanho=3)
    camera((0, 1.25, 4.6), (0, 0.85, 0), lente=35)   # camera em +Z da Unity: ve a FRENTE
    renderizar("elenco.png")


if __name__ == "__main__":
    qual = sys.argv[-1] if sys.argv[-1] in ("cabine", "elenco") else "todas"
    if qual in ("elenco", "todas"):
        cena_elenco()
    if qual in ("cabine", "todas"):
        cena_cabine()
