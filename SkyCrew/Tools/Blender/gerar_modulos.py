"""
SkyCrew - gera os modulos 3D do prototipo (cabine, props e personagens) em FBX para a Unity.

Estilo: low poly cartunesco, "pintado a mao" por uma textura de paleta (degrades + pinceladas).
Todos os modelos usam UM material e UMA textura (Art/Textures/SkyCrew_Paleta.png).

Rodar (Blender 4.x/5.x):
    blender --background --python Tools/Blender/gerar_modulos.py
ou com o modulo bpy:  python Tools/Blender/gerar_modulos.py

Convencao: as medidas abaixo estao em COORDENADAS DA UNITY (metros, Y para cima, Z = frente).
A funcao U() converte para o Blender; o exportador FBX (Forward -Z, Up Y, Apply Transform)
devolve os eixos da Unity. Pivos: no chao, no centro do objeto (personagens: entre os pes).
"""
import math
import os
import random
import sys

import bpy  # precisa vir antes de bmesh/mathutils
import bmesh
import numpy as np
from mathutils import Euler, Matrix, Vector

AQUI = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(AQUI, "..", "..", "Assets", "_Project", "Art"))
PASTAS = {
    "Cabine": os.path.join(ASSETS, "Aircraft", "Resources", "SkyCrew", "Cabine"),
    "Props": os.path.join(ASSETS, "Props", "Resources", "SkyCrew", "Props"),
    "Personagens": os.path.join(ASSETS, "Characters", "Resources", "SkyCrew", "Personagens"),
}
TEXTURA = os.path.join(ASSETS, "Textures", "SkyCrew_Paleta.png")
PREVIA = os.path.join(AQUI, "previas")

random.seed(3)

# ---------------------------------------------------------------------------
# Paleta (identidade SkyCrew: azul, branco, cinza, amarelo, vermelho, verde, marinho)
# ---------------------------------------------------------------------------
def hexcor(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


PALETA = [
    ("branco", "#f4f1ea"), ("azul", "#2a64c8"), ("azul_escuro", "#1b356e"), ("azul_claro", "#7fb4ea"),
    ("cinza", "#a9b0ba"), ("cinza_escuro", "#4d5561"), ("metal", "#c7ccd3"), ("preto", "#23262d"),
    ("creme", "#efe3c8"), ("bege", "#d9b98a"), ("carpete", "#53688a"), ("corredor", "#7d93b8"),
    ("amarelo", "#f5c842"), ("dourado", "#d9a531"), ("vermelho", "#e0574f"), ("verde", "#5db760"),
    ("laranja", "#f0a33a"), ("roxo", "#8b5fbf"), ("rosa", "#ef8fa3"), ("marrom", "#7a4b2e"),
    ("pele_1", "#f6cfa8"), ("pele_2", "#d9a074"), ("pele_3", "#a8693f"), ("pele_4", "#6e4127"),
    ("cabelo_castanho", "#5a3520"), ("cabelo_preto", "#2a2220"), ("cabelo_loiro", "#e8c46a"),
    ("cabelo_ruivo", "#c4542b"), ("cabelo_grisalho", "#c9c6c0"), ("vidro", "#2f4a6b"),
    ("ceu", "#bfe3ff"), ("luz", "#fff1c2"), ("olho", "#ffffff"), ("pupila", "#1d1f26"),
    ("boca", "#7c2a2a"), ("jeans", "#4f6fa8"),
]
CORES = {nome: i for i, (nome, _) in enumerate(PALETA)}
GRADE = 8              # 8 x 8 amostras
AMOSTRA = 32           # pixels por amostra
TAM_TEX = GRADE * AMOSTRA


def gerar_paleta():
    """Cada amostra: degrade (claro em cima, escuro embaixo) + pinceladas suaves."""
    img = np.zeros((TAM_TEX, TAM_TEX, 4), dtype=np.float32)
    img[..., 3] = 1.0
    rng = np.random.default_rng(5)
    for i, (_, h) in enumerate(PALETA):
        cor = np.array(hexcor(h))
        gx, gy = i % GRADE, i // GRADE
        y = np.linspace(0, 1, AMOSTRA)[:, None]                 # 0 = base, 1 = topo
        x = np.linspace(0, 1, AMOSTRA)[None, :]
        luz = 0.80 + 0.30 * y                                   # sombra embaixo, luz em cima
        quente = np.stack([1 + 0.03 * y, 1 + 0.01 * y, 1 - 0.03 * y], axis=-1)
        pincel = 1 + 0.035 * np.sin(x * 9 + y * 3 + rng.random() * 6) * np.sin(y * 17 + rng.random() * 6)
        ruido = 1 + (rng.random((AMOSTRA, AMOSTRA)) - 0.5) * 0.03
        bloco = cor[None, None, :] * (luz * pincel * ruido)[..., None] * quente
        img[gy * AMOSTRA:(gy + 1) * AMOSTRA, gx * AMOSTRA:(gx + 1) * AMOSTRA, :3] = np.clip(bloco, 0, 1)
    imagem = bpy.data.images.new("SkyCrew_Paleta", TAM_TEX, TAM_TEX, alpha=False)
    imagem.pixels.foreach_set(img.ravel())
    os.makedirs(os.path.dirname(TEXTURA), exist_ok=True)
    imagem.filepath_raw = TEXTURA
    imagem.file_format = "PNG"
    imagem.save()
    return imagem


def uv_da_cor(nome, altura_norm):
    """Ponto da paleta: centro horizontal da amostra, altura conforme o degrade."""
    i = CORES[nome]
    gx, gy = i % GRADE, i // GRADE
    margem = 0.18
    u = (gx + 0.5) / GRADE
    v = (gy + margem + (1 - 2 * margem) * altura_norm) / GRADE
    return u, v


# ---------------------------------------------------------------------------
# Conversao Unity -> Blender
# ---------------------------------------------------------------------------
def U(x, y, z):
    return Vector((-x, -z, y))


def U_rot(rx=0.0, ry=0.0, rz=0.0):
    """Rotacao em graus nos eixos da Unity -> matriz do Blender (aproximacao por eixos)."""
    m = Matrix.Identity(4)
    if ry:
        m = m @ Matrix.Rotation(math.radians(ry), 4, Vector((0, 0, 1)))   # Unity Y = Blender Z
    if rx:
        m = m @ Matrix.Rotation(math.radians(-rx), 4, Vector((1, 0, 0)))  # Unity X = Blender -X
    if rz:
        m = m @ Matrix.Rotation(math.radians(-rz), 4, Vector((0, 1, 0)))  # Unity Z = Blender -Y
    return m


# ---------------------------------------------------------------------------
# Construtor de malhas
# ---------------------------------------------------------------------------
MATERIAL = None


def material_paleta(imagem):
    mat = bpy.data.materials.new("SkyCrew_Paleta")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.85
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = imagem
    tex.interpolation = "Linear"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


class Malha:
    def __init__(self, nome):
        self.nome = nome
        self.bm = bmesh.new()
        # Cor como atributo da face: operacoes como bevel recriam faces e copiam o atributo.
        self.camada = self.bm.faces.layers.int.new("cor")

    def _pintar(self, faces, cor):
        for f in faces:
            f[self.camada] = CORES[cor]

    def _marcar(self, verts, cor):
        faces = set()
        for v in verts:
            faces.update(v.link_faces)
        self._pintar(faces, cor)
        return faces

    def caixa(self, centro, tamanho, cor, rot=(0, 0, 0), chanfro=0.0):
        """centro/tamanho em coordenadas da Unity."""
        sx, sy, sz = tamanho
        m = (Matrix.Translation(U(*centro)) @ U_rot(*rot)
             @ Matrix.Diagonal((sx, sz, sy, 1)))
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        faces = self._marcar(res["verts"], cor)
        if chanfro > 0:
            arestas = list({e for f in faces for e in f.edges})
            novo = bmesh.ops.bevel(self.bm, geom=arestas, offset=chanfro, segments=1,
                                   affect="EDGES", clamp_overlap=True)
            self._pintar(novo["faces"], cor)
        return faces

    def cilindro(self, centro, raio, altura, cor, eixo="Y", lados=10, raio2=None, rot=(0, 0, 0)):
        r2 = raio if raio2 is None else raio2
        # cone do Blender nasce no eixo Z do Blender = Y da Unity
        giro = {"Y": Matrix.Identity(4),
                "X": Matrix.Rotation(math.radians(90), 4, Vector((0, 1, 0))),
                "Z": Matrix.Rotation(math.radians(90), 4, Vector((1, 0, 0)))}[eixo]
        m = Matrix.Translation(U(*centro)) @ U_rot(*rot) @ giro
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=lados, radius1=raio,
                                    radius2=r2, depth=altura, matrix=m)
        return self._marcar(res["verts"], cor)

    def esfera(self, centro, raios, cor, segs=10, aneis=7, rot=(0, 0, 0)):
        rx, ry, rz = raios
        m = (Matrix.Translation(U(*centro)) @ U_rot(*rot) @ Matrix.Diagonal((rx, rz, ry, 1)))
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=segs, v_segments=aneis, radius=1.0, matrix=m)
        return self._marcar(res["verts"], cor)

    def icosfera(self, centro, raio, cor, subdiv=1, tremor=0.0):
        m = Matrix.Translation(U(*centro))
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=raio, matrix=m)
        for v in res["verts"]:
            v.co += Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))) * tremor
        return self._marcar(res["verts"], cor)

    def perfil_extrudado(self, pontos_xy, z0, z1, cor):
        """Poligono (x, y) da Unity extrudado entre z0 e z1 (Unity)."""
        a = [self.bm.verts.new(U(x, y, z0)) for x, y in pontos_xy]
        b = [self.bm.verts.new(U(x, y, z1)) for x, y in pontos_xy]
        faces = [self.bm.faces.new(a), self.bm.faces.new(list(reversed(b)))]
        n = len(pontos_xy)
        for i in range(n):
            j = (i + 1) % n
            faces.append(self.bm.faces.new((a[i], b[i], b[j], a[j])))
        self._pintar(faces, cor)
        return faces

    def criar(self, pai=None, origem_unity=(0, 0, 0)):
        """Cria o objeto; vertices relativos a origem (pivo) dada em coords Unity."""
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        desloc = U(*origem_unity)
        for v in bm.verts:
            v.co -= desloc
        zs = [v.co.z for v in bm.verts] or [0.0]
        zmin, zmax = min(zs), max(zs)
        alt = max(zmax - zmin, 1e-4)
        uv = bm.loops.layers.uv.new("UVMap")
        for f in bm.faces:
            cor = PALETA[f[self.camada]][0]
            for loop in f.loops:
                h = (loop.vert.co.z - zmin) / alt
                loop[uv].uv = uv_da_cor(cor, h)
            f.smooth = False
        me = bpy.data.meshes.new(self.nome)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(MATERIAL)
        obj = bpy.data.objects.new(self.nome, me)
        bpy.context.scene.collection.objects.link(obj)
        if pai is not None:
            obj.parent = pai
            bpy.context.view_layer.update()
        obj.location = desloc if pai is None else desloc - pai.matrix_world.translation
        return obj


def vazio(nome, pai=None, unity=(0, 0, 0)):
    obj = bpy.data.objects.new(nome, None)
    bpy.context.scene.collection.objects.link(obj)
    if pai is not None:
        obj.parent = pai
        bpy.context.view_layer.update()
    obj.location = U(*unity) if pai is None else U(*unity) - pai.matrix_world.translation
    return obj


# ---------------------------------------------------------------------------
# Exportacao
# ---------------------------------------------------------------------------
def exportar(raiz, pasta, nome):
    os.makedirs(pasta, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    objs = [raiz] + list(raiz.children_recursive)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = raiz
    caminho = os.path.join(pasta, nome + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=caminho, use_selection=True, object_types={"EMPTY", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False,
        path_mode="STRIP", embed_textures=False, use_mesh_modifiers=True,
    )
    return caminho


# ---------------------------------------------------------------------------
# MODULOS DA CABINE (medidas batem com Scripts/Runtime/Aircraft/CabinLayout.cs)
# ---------------------------------------------------------------------------
HALF = 1.9
TETO = 2.25
SEG = 2.08


def modulo_segmento():
    raiz = vazio("Segmento")
    m = Malha("Segmento_Malha")
    L = SEG
    # piso com corredor mais claro
    m.caixa((0, -0.04, 0), (HALF * 2, 0.08, L), "carpete")
    m.caixa((0, 0.002, 0), (0.62, 0.004, L), "corredor")
    # paredes com rodape e chanfro superior
    for s in (-1, 1):
        m.caixa((s * (HALF + 0.04), 1.0, 0), (0.08, 2.0, L), "creme")
        m.caixa((s * (HALF - 0.01), 0.06, 0), (0.04, 0.12, L), "cinza")
        m.perfil_extrudado([(s * (HALF + 0.08), 2.0), (s * (HALF + 0.08), TETO + 0.08),
                            (s * 1.62, TETO + 0.08), (s * 1.62, TETO + 0.02), (s * HALF, 2.0)],
                           -L / 2, L / 2, "creme")
        # janelas: moldura + vidro com ceu
        for zc in (-0.52, 0.52):
            m.caixa((s * (HALF - 0.005), 1.15, zc), (0.03, 0.46, 0.34), "cinza", chanfro=0.04)
            m.caixa((s * (HALF - 0.02), 1.15, zc), (0.02, 0.36, 0.24), "ceu", chanfro=0.03)
    # teto com faixa de luz
    m.caixa((0, TETO + 0.04, 0), (3.3, 0.08, L), "branco")
    m.caixa((0, TETO - 0.005, 0), (0.5, 0.02, L * 0.9), "luz")
    m.criar(raiz)
    return raiz


def modulo_parede(nome, com_porta):
    raiz = vazio(nome)
    m = Malha(nome + "_Malha")
    m.caixa((0, 1.15, 0), (HALF * 2, 2.3, 0.08), "creme")
    if com_porta:
        m.caixa((0, 0.98, -0.05), (0.8, 1.96, 0.04), "azul_escuro", chanfro=0.03)
        m.caixa((0.28, 1.0, -0.08), (0.06, 0.16, 0.04), "dourado")
        m.caixa((0, 2.12, -0.06), (1.2, 0.18, 0.03), "azul")     # placa "SkyCrew"
        m.caixa((-0.25, 2.12, -0.075), (0.22, 0.08, 0.01), "branco")
        m.caixa((0.15, 2.12, -0.075), (0.42, 0.05, 0.01), "branco")
    else:
        for s in (-1, 1):
            m.caixa((s * 1.1, 1.3, -0.05), (1.0, 1.6, 0.03), "cinza", chanfro=0.03)
    m.criar(raiz)
    return raiz


def modulo_poltrona():
    raiz = vazio("Poltrona")
    m = Malha("Poltrona_Malha")
    w = 0.46
    m.caixa((0, 0.18, 0.0), (0.2, 0.36, 0.34), "cinza_escuro")                 # pe
    m.caixa((0, 0.40, 0.02), (w, 0.13, 0.52), "azul", chanfro=0.04)            # assento
    m.caixa((0, 0.83, -0.24), (w, 0.78, 0.12), "azul", rot=(-10, 0, 0), chanfro=0.05)   # encosto
    m.caixa((0, 1.16, -0.28), (w * 0.82, 0.16, 0.13), "branco", rot=(-10, 0, 0), chanfro=0.03)  # capa
    for s in (-1, 1):
        m.caixa((s * (w / 2 + 0.015), 0.6, -0.02), (0.04, 0.05, 0.42), "cinza")  # bracos
    m.caixa((0, 0.86, -0.32), (w * 0.7, 0.22, 0.02), "cinza_escuro", rot=(-10, 0, 0))  # bolsa
    m.criar(raiz)
    return raiz


def modulo_bagageiro():
    """Lado esquerdo da cabine: a porta fica virada para o corredor (+X)."""
    raiz = vazio("Bagageiro")
    m = Malha("Bagageiro_Malha")
    m.caixa((0, 0.22, 0), (0.65, 0.44, 2.6), "branco", chanfro=0.03)
    for zc in (-0.86, 0.0, 0.86):
        m.caixa((0.33, 0.22, zc), (0.02, 0.38, 0.8), "branco", chanfro=0.02)
        m.caixa((0.345, 0.36, zc), (0.02, 0.05, 0.22), "cinza_escuro")
    m.caixa((0.25, -0.01, 0), (0.12, 0.02, 2.5), "luz")
    m.criar(raiz)
    return raiz


def modulo_cozinha():
    raiz = vazio("Cozinha")
    m = Malha("Cozinha_Malha")
    for s in (-1, 1):
        x = s * 1.575
        m.caixa((x, 0.5, 0), (0.65, 1.0, 2.0), "metal", chanfro=0.02)
        m.caixa((x, 1.03, 0), (0.7, 0.06, 2.05), "cinza_escuro")
        m.caixa((s * 1.6, 1.9, 0), (0.6, 0.6, 2.0), "metal", chanfro=0.02)
        for k in range(3):
            z = -0.62 + k * 0.62
            m.caixa((s * 1.245, 0.48, z), (0.02, 0.84, 0.56), "cinza")        # carrinho
            m.caixa((s * 1.23, 0.82, z), (0.03, 0.05, 0.3), "vermelho")       # puxador
            m.caixa((s * 1.295, 1.9, z), (0.02, 0.5, 0.56), "cinza")
    # cafeteira e xicaras
    m.caixa((1.55, 1.2, -0.5), (0.3, 0.32, 0.26), "preto", chanfro=0.03)
    for k in range(3):
        m.cilindro((1.4, 1.1, 0.1 + k * 0.14), 0.045, 0.08, "branco", lados=8)
    # caixa de garrafas de agua (geladeira) do lado esquerdo
    m.caixa((-1.45, 1.12, 0.3), (0.45, 0.14, 0.62), "azul", chanfro=0.02)
    for k in range(4):
        for j in range(2):
            x = -1.55 + j * 0.16
            z = 0.08 + k * 0.15
            m.cilindro((x, 1.31, z), 0.045, 0.24, "azul_claro", lados=8)
            m.cilindro((x, 1.45, z), 0.025, 0.04, "azul_escuro", lados=6)
    m.criar(raiz)
    return raiz


def modulo_banheiro():
    raiz = vazio("Banheiro")
    m = Malha("Banheiro_Malha")
    m.caixa((0, 0.01, 0), (1.38, 0.02, 1.76), "branco")                           # piso claro
    m.cilindro((0, 0.2, -0.5), 0.2, 0.4, "branco", lados=10, raio2=0.24)          # vaso
    m.cilindro((0, 0.43, -0.48), 0.25, 0.05, "branco", lados=10)
    m.caixa((0, 0.65, -0.78), (0.42, 0.42, 0.16), "branco", chanfro=0.04)
    m.caixa((0.45, 0.85, 0.15), (0.4, 0.12, 0.45), "metal")                       # pia
    m.cilindro((0.45, 0.9, 0.15), 0.13, 0.03, "cinza", lados=8)
    m.caixa((0.66, 1.45, 0.15), (0.02, 0.5, 0.42), "azul_claro")                  # espelho
    m.cilindro((-0.55, 0.62, -0.2), 0.07, 0.13, "branco", eixo="X", lados=8)      # papel
    m.caixa((0, 2.0, 0.85), (0.5, 0.12, 0.02), "verde")                           # placa livre
    m.criar(raiz)
    return raiz


# ---------------------------------------------------------------------------
# PROPS
# ---------------------------------------------------------------------------
def prop_mala():
    raiz = vazio("Mala")
    m = Malha("Mala_Malha")
    m.caixa((0, 0.2, 0), (0.5, 0.36, 0.24), "vermelho", chanfro=0.05)
    m.caixa((0, 0.2, 0), (0.52, 0.04, 0.25), "marrom")                  # faixa
    m.caixa((0, 0.41, 0), (0.16, 0.05, 0.05), "preto", chanfro=0.015)  # alca
    for x in (-0.18, 0.18):
        m.cilindro((x, 0.025, 0.08), 0.025, 0.03, "preto", eixo="X", lados=8)
    m.caixa((0.12, 0.28, 0.125), (0.1, 0.07, 0.01), "amarelo")         # etiqueta
    m.criar(raiz)
    return raiz


def prop_garrafa():
    raiz = vazio("Garrafa")
    m = Malha("Garrafa_Malha")
    m.cilindro((0, 0.1, 0), 0.045, 0.2, "azul_claro", lados=10)
    m.cilindro((0, 0.215, 0), 0.045, 0.03, "azul_claro", lados=10, raio2=0.022)
    m.cilindro((0, 0.245, 0), 0.022, 0.03, "azul_escuro", lados=8)
    m.cilindro((0, 0.1, 0), 0.047, 0.07, "branco", lados=10)          # rotulo
    m.criar(raiz)
    return raiz


def prop_lixo():
    raiz = vazio("Lixo")
    m = Malha("Lixo_Malha")
    m.icosfera((0, 0.07, 0), 0.07, "branco", subdiv=1, tremor=0.018)
    m.criar(raiz)
    return raiz


def prop_lixeira():
    raiz = vazio("Lixeira")
    m = Malha("Lixeira_Malha")
    m.cilindro((0, 0.32, 0), 0.19, 0.64, "cinza_escuro", lados=10, raio2=0.2)
    m.cilindro((0, 0.67, 0), 0.21, 0.06, "metal", lados=10)
    m.caixa((0, 0.67, 0.2), (0.18, 0.04, 0.04), "amarelo")
    m.caixa((0, 0.35, 0.2), (0.16, 0.16, 0.01), "verde")               # simbolo
    m.criar(raiz)
    return raiz


def prop_lanche():
    raiz = vazio("Lanche")
    m = Malha("Lanche_Malha")
    m.caixa((0, 0.05, 0), (0.2, 0.1, 0.14), "amarelo", chanfro=0.02)          # caixinha
    m.caixa((0, 0.05, 0.071), (0.12, 0.06, 0.005), "vermelho")                 # logo
    m.caixa((0, 0.101, 0), (0.08, 0.004, 0.14), "laranja")                     # fita
    m.criar(raiz)
    return raiz


def prop_cobertor():
    raiz = vazio("Cobertor")
    m = Malha("Cobertor_Malha")
    m.caixa((0, 0.06, 0), (0.34, 0.12, 0.26), "azul_claro", chanfro=0.04)
    for z in (-0.08, 0.08):
        m.caixa((0, 0.06, z), (0.35, 0.125, 0.025), "branco")                  # listras
    m.criar(raiz)
    return raiz


def prop_esfregao():
    raiz = vazio("Esfregao")
    m = Malha("Esfregao_Malha")
    m.cilindro((0, 0.65, 0), 0.018, 1.1, "amarelo", lados=6)                   # cabo
    m.caixa((0, 0.06, 0), (0.32, 0.06, 0.1), "azul", chanfro=0.015)             # base
    for k in range(7):
        m.caixa((-0.14 + k * 0.047, 0.02, 0), (0.035, 0.04, 0.09), "branco")  # franjas
    m.criar(raiz)
    return raiz


def prop_carrinho():
    raiz = vazio("Carrinho")
    m = Malha("Carrinho_Malha")
    m.caixa((0, 0.52, 0), (0.4, 0.92, 0.8), "metal", chanfro=0.02)
    for k in range(3):
        y = 0.25 + k * 0.28
        m.caixa((0, y, 0.405), (0.36, 0.24, 0.01), "cinza")                    # gavetas (frente)
        m.caixa((0, y + 0.06, 0.415), (0.14, 0.03, 0.02), "vermelho")
    m.caixa((0, 1.0, -0.42), (0.36, 0.05, 0.05), "vermelho", chanfro=0.01)     # alca
    m.caixa((0, 0.99, 0), (0.42, 0.03, 0.82), "cinza_escuro")                  # tampo
    m.cilindro((0.1, 1.06, 0.15), 0.05, 0.1, "branco", lados=8)                # copos
    m.cilindro((-0.08, 1.06, 0.2), 0.05, 0.1, "branco", lados=8)
    for x in (-0.15, 0.15):
        for z in (-0.3, 0.3):
            m.cilindro((x, 0.04, z), 0.04, 0.03, "preto", eixo="X", lados=8)  # rodinhas
    m.criar(raiz)
    return raiz


def prop_mancha():
    raiz = vazio("Mancha")
    m = Malha("Mancha_Malha")
    rnd = random.Random(9)
    pontos = []
    for k in range(14):
        a = 2 * math.pi * k / 14
        r = 0.42 * (0.75 + 0.35 * rnd.random())
        pontos.append((math.cos(a) * r, math.sin(a) * r * 0.75))
    # poligono no plano XZ da Unity, extrudado bem fino em Y
    bm = m.bm
    base = [bm.verts.new(U(x, 0.0, z)) for x, z in pontos]
    topo = [bm.verts.new(U(x, 0.008, z)) for x, z in pontos]
    faces = [bm.faces.new(topo), bm.faces.new(list(reversed(base)))]
    for i in range(len(pontos)):
        j = (i + 1) % len(pontos)
        faces.append(bm.faces.new((base[i], base[j], topo[j], topo[i])))
    m._pintar(faces, "laranja")
    m.esfera((0.12, 0.009, 0.05), (0.12, 0.002, 0.08), "amarelo", segs=8, aneis=3)   # brilho
    m.criar(raiz)
    return raiz


# ---------------------------------------------------------------------------
# PERSONAGENS (cabecao, olhos grandes; partes separadas com pivo na articulacao)
# ---------------------------------------------------------------------------
def personagem(nome, pele, roupa, roupa2, calca, cabelo, estilo_cabelo, acessorio=None, sapato="preto"):
    raiz = vazio(nome)
    # Corpo (tronco) - pivo nos pes
    corpo = Malha("Corpo")
    corpo.esfera((0, 0.86, 0), (0.25, 0.32, 0.19), roupa, segs=10, aneis=7)
    corpo.caixa((0, 1.12, 0.12), (0.18, 0.1, 0.04), roupa2, rot=(25, 0, 0))     # gola/lenco
    corpo.cilindro((0, 1.17, 0), 0.08, 0.1, pele, lados=8)                      # pescoco
    if acessorio == "lenco":
        corpo.caixa((0.05, 1.05, 0.17), (0.07, 0.16, 0.03), "amarelo", rot=(0, 0, 15))
        corpo.caixa((0.14, 0.98, 0.17), (0.08, 0.05, 0.02), "dourado")          # broche
    if acessorio == "bolsa":
        corpo.caixa((-0.2, 0.75, 0.1), (0.06, 0.18, 0.14), "marrom", chanfro=0.02)
    corpo.criar(raiz)

    # Cabeca - pivo no pescoco. Cada parte e uma malha FILHA DIRETA da raiz (o "Apply Transform"
    # do exportador FBX gira netos da hierarquia; validado na reimportacao).
    c = Malha("Cabeca")
    c.esfera((0, 1.43, 0), (0.25, 0.25, 0.23), pele, segs=12, aneis=8)
    for s in (-1, 1):
        c.esfera((s * 0.085, 1.47, 0.19), (0.07, 0.085, 0.05), "olho", segs=8, aneis=6)
        c.esfera((s * 0.08, 1.46, 0.235), (0.032, 0.042, 0.02), "pupila", segs=6, aneis=4)
        c.esfera((s * 0.075, 1.48, 0.248), (0.011, 0.011, 0.006), "olho", segs=5, aneis=3)   # brilho
        c.caixa((s * 0.09, 1.575, 0.2), (0.09, 0.022, 0.02), cabelo, rot=(0, 0, s * -8))     # sobrancelha
        c.esfera((s * 0.245, 1.42, 0.0), (0.035, 0.06, 0.045), pele, segs=6, aneis=4)       # orelhas
    c.esfera((0, 1.40, 0.235), (0.035, 0.03, 0.035), pele, segs=6, aneis=4)                 # nariz
    c.caixa((0, 1.33, 0.21), (0.1, 0.025, 0.02), "boca", chanfro=0.008)                     # sorriso
    if estilo_cabelo == "coque":
        c.esfera((0, 1.55, -0.03), (0.26, 0.17, 0.24), cabelo, segs=10, aneis=6)
        c.esfera((0, 1.53, -0.25), (0.1, 0.1, 0.09), cabelo, segs=8, aneis=5)
    elif estilo_cabelo == "curto":
        c.esfera((0, 1.57, -0.02), (0.26, 0.15, 0.245), cabelo, segs=10, aneis=6)
    elif estilo_cabelo == "volumoso":
        c.esfera((0, 1.53, -0.04), (0.31, 0.24, 0.28), cabelo, segs=10, aneis=6)
    elif estilo_cabelo == "careca":
        c.esfera((0, 1.42, -0.12), (0.255, 0.12, 0.16), cabelo, segs=10, aneis=5)
    if acessorio == "lenco":                                                                   # quepe
        c.cilindro((0, 1.66, 0.0), 0.17, 0.1, "azul_escuro", lados=12, raio2=0.19)
        c.cilindro((0, 1.62, 0.0), 0.175, 0.025, "dourado", lados=12)
        c.esfera((0, 1.66, 0.17), (0.04, 0.03, 0.01), "dourado", segs=6, aneis=4)
    if acessorio == "oculos":
        for s in (-1, 1):
            c.cilindro((s * 0.085, 1.47, 0.245), 0.06, 0.012, "preto", eixo="Z", lados=10)
        c.caixa((0, 1.48, 0.25), (0.06, 0.015, 0.01), "preto")
    if acessorio == "bone":
        c.esfera((0, 1.6, -0.02), (0.26, 0.12, 0.25), "vermelho", segs=10, aneis=5)
        c.caixa((0, 1.6, 0.25), (0.22, 0.025, 0.16), "vermelho", chanfro=0.01)
    c.criar(raiz, origem_unity=(0, 1.2, 0))

    # Bracos - pivo no ombro
    for s, lado in ((-1, "E"), (1, "D")):
        mb = Malha(f"Braco_{lado}")
        mb.esfera((s * 0.29, 0.88, 0), (0.075, 0.2, 0.075), roupa, segs=8, aneis=5)
        mb.esfera((s * 0.3, 0.66, 0.02), (0.065, 0.065, 0.065), pele, segs=8, aneis=5)     # mao
        mb.criar(raiz, origem_unity=(s * 0.25, 1.06, 0))

    # Pernas - pivo no quadril
    for s, lado in ((-1, "E"), (1, "D")):
        mp = Malha(f"Perna_{lado}")
        mp.esfera((s * 0.11, 0.33, 0), (0.09, 0.26, 0.09), calca, segs=8, aneis=5)
        mp.esfera((s * 0.11, 0.05, 0.05), (0.08, 0.055, 0.13), sapato, segs=8, aneis=5)
        mp.criar(raiz, origem_unity=(s * 0.11, 0.56, 0))
    return raiz


VARIANTES = [
    # (nome, pele, roupa, detalhe, calca, cabelo, estilo, acessorio)
    ("Passageiro_1", "pele_2", "vermelho", "branco", "jeans", "cabelo_preto", "curto", "bone"),
    ("Passageiro_2", "pele_1", "verde", "amarelo", "cinza_escuro", "cabelo_ruivo", "volumoso", "bolsa"),
    ("Passageiro_3", "pele_3", "laranja", "branco", "marrom", "cabelo_grisalho", "careca", "oculos"),
    ("Passageiro_4", "pele_4", "roxo", "rosa", "jeans", "cabelo_castanho", "coque", None),
]


# ---------------------------------------------------------------------------
# Validacao: reimporta cada FBX e confere tamanho/orientacao em eixos Unity
# ---------------------------------------------------------------------------
def validar(caminho, esperado_altura, tolerancia=0.15):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=caminho, axis_forward="-Z", axis_up="Y", bake_space_transform=True)
    pts = []
    tris = 0
    for o in bpy.data.objects:
        if o.type == "MESH":
            for v in o.data.vertices:
                w = o.matrix_world @ v.co
                pts.append((-w.x, w.z, -w.y))          # Blender -> Unity
            o.data.calc_loop_triangles()
            tris += len(o.data.loop_triangles)
            assert o.data.uv_layers, f"{caminho}: sem UV"
            assert o.data.materials and o.data.materials[0], f"{caminho}: sem material"
    a = np.array(pts)
    mn, mx = a.min(0), a.max(0)
    altura = mx[1] - mn[1]
    ok = abs(altura - esperado_altura) <= tolerancia * max(1.0, esperado_altura) and mn[1] > -0.12
    return ok, tris, mn, mx


# ---------------------------------------------------------------------------
def principal():
    global MATERIAL
    bpy.ops.wm.read_factory_settings(use_empty=True)
    imagem = gerar_paleta()
    MATERIAL = material_paleta(imagem)

    itens = [
        ("Cabine", "Segmento", modulo_segmento, TETO + 0.16),
        ("Cabine", "Parede_Frente", lambda: modulo_parede("Parede_Frente", True), 2.3),
        ("Cabine", "Parede_Fundo", lambda: modulo_parede("Parede_Fundo", False), 2.3),
        ("Cabine", "Poltrona", modulo_poltrona, 1.28),
        ("Cabine", "Bagageiro", modulo_bagageiro, 0.46),
        ("Cabine", "Cozinha", modulo_cozinha, 2.2),
        ("Cabine", "Banheiro", modulo_banheiro, 2.06),
        ("Props", "Mala", prop_mala, 0.44),
        ("Props", "Garrafa", prop_garrafa, 0.26),
        ("Props", "Lixo", prop_lixo, 0.14),
        ("Props", "Lixeira", prop_lixeira, 0.7),
        ("Props", "Lanche", prop_lanche, 0.1),
        ("Props", "Cobertor", prop_cobertor, 0.125),
        ("Props", "Esfregao", prop_esfregao, 1.2),
        ("Props", "Carrinho", prop_carrinho, 1.11),
        ("Props", "Mancha", prop_mancha, 0.011),
    ]
    # Personagens: gerados com esqueleto e animacoes por gerar_personagens.py (Fase 3).
    # A funcao personagem() acima continua aqui so como referencia do boneco rigido da Fase 1.

    caminhos = []
    for pasta, nome, fabrica, altura in itens:
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        raiz = fabrica()
        caminhos.append((exportar(raiz, PASTAS[pasta], nome), nome, altura))

    print("\nVALIDACAO (reimportando os FBX):")
    falhas = 0
    for caminho, nome, altura in caminhos:
        ok, tris, mn, mx = validar(caminho, altura)
        tam = mx - mn
        print(f"  {'OK ' if ok else 'ERRO'} {nome:16s} {tris:5d} tri  "
              f"tam(UnityXYZ)={tam[0]:.2f}x{tam[1]:.2f}x{tam[2]:.2f}  base_y={mn[1]:.2f}")
        falhas += 0 if ok else 1
    print("Textura:", TEXTURA)
    if falhas:
        print(f"{falhas} modelo(s) com problema")
        sys.exit(1)


if __name__ == "__main__":
    principal()
