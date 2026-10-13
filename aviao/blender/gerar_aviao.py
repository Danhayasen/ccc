"""
Gera o aviao do jogo no Blender (estilo low poly cartunesco / pintado a mao)
e exporta para ../aviao.glb (usado pelo Three.js) e ../aviao.blend.

Como rodar:
    blender --background --python aviao/blender/gerar_aviao.py
ou, com o modulo bpy instalado (pip install bpy):
    python aviao/blender/gerar_aviao.py

Eixos no Blender: X = frente (nariz), Y = esquerda, Z = cima.
O exportador glTF converte para Y-up (padrao do Three.js).
"""
import math
import os
import random

import bpy  # precisa vir antes de bmesh/mathutils
import bmesh
import numpy as np
from mathutils import Euler, Matrix, Vector

AQUI = os.path.dirname(os.path.abspath(__file__))
SAIDA_GLB = os.path.join(AQUI, "..", "aviao.glb")
SAIDA_BLEND = os.path.join(AQUI, "..", "aviao.blend")

random.seed(7)
rng = np.random.default_rng(7)

# ---------------------------------------------------------------------------
# Proporcoes (cartunescas: fuselagem curta e gordinha, nariz redondo)
# ---------------------------------------------------------------------------
R = 2.25            # raio da fuselagem
NARIZ_X = 15.5
CAUDA_X = -15.5
INICIO_NARIZ = 11.0
INICIO_CAUDA = -6.5
PISO_Z = -0.75
CHAO_Z = -4.4
ESPESSURA_CASCO = 0.12

# Zonas do interior (x de inicio, x de fim)
X_COCKPIT = (14.2, 12.0)
X_PRIMEIRA = (11.8, 8.8)
X_EXECUTIVA = (8.6, 5.0)
X_ECONOMICA = (4.8, -2.6)
X_GALLEY = (-2.8, -4.6)
X_BANHEIROS = (-4.8, -6.9)
X_ANIMAIS = (-7.1, -10.3)

# Corte lateral (lado esquerdo, +Y): faixa de angulos removida
CORTE_X = (X_ANIMAIS[1] - 0.2, 14.2)
CORTE_ANG = (math.radians(-20), math.radians(112))

# ---------------------------------------------------------------------------
# Paleta (inspirada na referencia: branco + azul)
# ---------------------------------------------------------------------------
def hexcor(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


COR = {
    "branco": hexcor("#f4f1ea"),
    "azul": hexcor("#2452a8"),
    "azul_escuro": hexcor("#1b356e"),
    "azul_claro": hexcor("#6fa3dc"),
    "cinza": hexcor("#a9b0ba"),
    "cinza_escuro": hexcor("#4d5561"),
    "metal": hexcor("#c7ccd3"),
    "preto": hexcor("#23262d"),
    "vidro": hexcor("#27415f"),
    "creme": hexcor("#efe3c8"),
    "bege": hexcor("#d9b98a"),
    "marrom": hexcor("#8a5a3b"),
    "carpete": hexcor("#5b6f8f"),
    "tela": hexcor("#59d0ff"),
    "luz": hexcor("#fff3c4"),
    "roxo": hexcor("#8b5fbf"),
    "laranja": hexcor("#f0a33a"),
    "vermelho": hexcor("#e0574f"),
    "caramelo": hexcor("#d39a4f"),
    "pele": hexcor("#f2c39b"),
    "rosa": hexcor("#ef8fa3"),
    "verde": hexcor("#68b36b"),
    "amarelo": hexcor("#f5d04c"),
}

# ---------------------------------------------------------------------------
# Cena limpa
# ---------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
cena = bpy.context.scene


def novo_vazio(nome, pai=None, loc=(0, 0, 0)):
    obj = bpy.data.objects.new(nome, None)
    obj.location = loc
    obj.empty_display_size = 0.5
    cena.collection.objects.link(obj)
    if pai is not None:
        obj.parent = pai
    return obj


RAIZ = novo_vazio("Aviao")
EXTERIOR = novo_vazio("Exterior", RAIZ)
CASCO_CORTE = novo_vazio("Casco_Corte", RAIZ)   # some no modo corte
INTERIOR = novo_vazio("Interior", RAIZ)
ROTULOS = novo_vazio("Rotulos", RAIZ)

# ---------------------------------------------------------------------------
# Materiais
# ---------------------------------------------------------------------------
MATERIAIS = {}


def material(nome, cor=(1, 1, 1), rugosidade=0.8, metalico=0.0,
             emissao=None, forca=1.5, cor_vertice=True, textura=None):
    if nome in MATERIAIS:
        return MATERIAIS[nome]
    mat = bpy.data.materials.new(nome)
    mat.use_nodes = True
    mat.use_backface_culling = False
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*cor, 1)
    bsdf.inputs["Roughness"].default_value = rugosidade
    bsdf.inputs["Metallic"].default_value = metalico
    if emissao is not None:
        bsdf.inputs["Emission Color"].default_value = (*emissao, 1)
        bsdf.inputs["Emission Strength"].default_value = forca
    if textura is not None:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = textura
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    elif cor_vertice:
        attr = nt.nodes.new("ShaderNodeVertexColor")
        attr.layer_name = "Pintura"
        nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    MATERIAIS[nome] = mat
    return mat


# ---------------------------------------------------------------------------
# "Pintura a mao": cor por canto de face com degrade vertical e variacao
# ---------------------------------------------------------------------------
def pintar(cor, pos, normal, variacao=0.06):
    """Degrade claro em cima / escuro embaixo, luz quente, sombra fria."""
    r, g, b = cor
    cima = max(-1.0, min(1.0, normal.z))
    altura = max(-1.0, min(1.0, (pos.z - 0.0) / 6.0))
    luz = 1.0 + 0.10 * cima + 0.05 * altura
    ruido = 1.0 + (random.random() - 0.5) * variacao
    k = luz * ruido
    # sombras puxando para o azul, luzes para o amarelo
    if cima < 0:
        r, g, b = r * (1 + 0.06 * cima), g * (1 + 0.03 * cima), b
    else:
        r, g, b = r, g, b * (1 - 0.04 * cima)
    return (min(r * k, 1), min(g * k, 1), min(b * k, 1), 1.0)


class Peca:
    """Acumula geometria low poly numa unica malha com cores por face."""

    def __init__(self, nome, pai, variacao=0.06, suave=False):
        self.nome = nome
        self.pai = pai
        self.bm = bmesh.new()
        self.cor_faces = {}
        self.variacao = variacao
        self.suave = suave

    # -- primitivas --------------------------------------------------------
    def _novas_faces(self, verts, cor):
        faces = set()
        for v in verts:
            faces.update(v.link_faces)
        for f in faces:
            self.cor_faces[f] = cor
        return faces

    def caixa(self, centro, tamanho, cor, rot=(0, 0, 0), chanfro=0.0):
        m = (Matrix.Translation(Vector(centro))
             @ Euler(rot).to_matrix().to_4x4()
             @ Matrix.Diagonal((*tamanho, 1)))
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        faces = self._novas_faces(res["verts"], cor)
        if chanfro > 0:
            arestas = {e for f in faces for e in f.edges}
            novo = bmesh.ops.bevel(self.bm, geom=list(arestas) + list(res["verts"]),
                                   offset=chanfro, segments=1, affect="EDGES",
                                   clamp_overlap=True)
            for f in novo["faces"]:
                self.cor_faces[f] = cor
        return faces

    def cilindro(self, centro, raio, altura, cor, eixo="Z", lados=8,
                 raio2=None, rot_extra=(0, 0, 0)):
        r2 = raio if raio2 is None else raio2
        giro = {"Z": (0, 0, 0), "X": (0, math.pi / 2, 0),
                "Y": (math.pi / 2, 0, 0)}[eixo]
        m = (Matrix.Translation(Vector(centro))
             @ Euler(rot_extra).to_matrix().to_4x4()
             @ Euler(giro).to_matrix().to_4x4())
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=lados,
                                    radius1=raio, radius2=r2, depth=altura,
                                    matrix=m)
        return self._novas_faces(res["verts"], cor)

    def esfera(self, centro, raios, cor, segs=8, aneis=5, rot=(0, 0, 0)):
        m = (Matrix.Translation(Vector(centro))
             @ Euler(rot).to_matrix().to_4x4()
             @ Matrix.Diagonal((*raios, 1)))
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=segs,
                                        v_segments=aneis, radius=1.0, matrix=m)
        return self._novas_faces(res["verts"], cor)

    def prisma(self, pontos_yz, x0, x1, cor):
        """Extruda um poligono (y, z) entre x0 e x1."""
        a = [self.bm.verts.new((x0, y, z)) for y, z in pontos_yz]
        b = [self.bm.verts.new((x1, y, z)) for y, z in pontos_yz]
        faces = [self.bm.faces.new(a), self.bm.faces.new(list(reversed(b)))]
        n = len(pontos_yz)
        for i in range(n):
            j = (i + 1) % n
            faces.append(self.bm.faces.new((a[i], a[j], b[j], b[i])))
        for f in faces:
            self.cor_faces[f] = cor
        return faces

    def malha(self, verts, faces_idx, cor):
        vs = [self.bm.verts.new(v) for v in verts]
        faces = []
        for idx in faces_idx:
            try:
                f = self.bm.faces.new([vs[i] for i in idx])
            except ValueError:
                continue
            self.cor_faces[f] = cor
            faces.append(f)
        return faces

    # -- finalizacao -------------------------------------------------------
    def criar(self, mat_nome="Pintado", recalcular=True):
        bm = self.bm
        if recalcular:
            bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.normal_update()
        camada = bm.loops.layers.color.new("Pintura")
        for f in bm.faces:
            cor = self.cor_faces.get(f, COR["branco"])
            c = f.calc_center_median()
            base = pintar(cor, c, f.normal, self.variacao)
            for loop in f.loops:
                # leve variacao por vertice: aspecto de pincelada
                j = 1.0 + (random.random() - 0.5) * self.variacao * 0.5
                loop[camada] = (min(base[0] * j, 1), min(base[1] * j, 1),
                                min(base[2] * j, 1), 1.0)
            f.smooth = self.suave
        me = bpy.data.meshes.new(self.nome)
        bm.to_mesh(me)
        bm.free()
        me.color_attributes.active_color_name = "Pintura"
        me.color_attributes.default_color_name = "Pintura"
        me.materials.append(material(mat_nome))
        obj = bpy.data.objects.new(self.nome, me)
        cena.collection.objects.link(obj)
        obj.parent = self.pai
        return obj


# ---------------------------------------------------------------------------
# Fuselagem: secao parametrica (meia-largura, topo, base)
# ---------------------------------------------------------------------------
def secao(x):
    if x > INICIO_NARIZ:      # nariz bem redondo, levemente "caido"
        t = min((x - INICIO_NARIZ) / (NARIZ_X - INICIO_NARIZ), 1.0)
        w = R * math.sqrt(max(0.0, 1 - t ** 2.0))
        zt = -0.35 + (R + 0.35) * math.sqrt(max(0.0, 1 - t ** 1.7))
        zb = -0.55 - (R - 0.55) * math.sqrt(max(0.0, 1 - t ** 2.4))
    elif x < INICIO_CAUDA:    # cauda curta e empinada
        s = min((INICIO_CAUDA - x) / (INICIO_CAUDA - CAUDA_X), 1.0)
        w = 0.35 + (R - 0.35) * (1 - s ** 1.4)
        zt = R - 0.35 * s ** 2
        zb = -R + (R + 1.0) * s ** 1.6
    else:
        w, zt, zb = R, R, -R
    return w, zt, zb


def ponto(x, th, encolher=0.0):
    w, zt, zb = secao(x)
    zc = (zt + zb) / 2
    h = (zt - zb) / 2
    return Vector((x, (w - encolher) * math.cos(th),
                   zc + (h - encolher) * math.sin(th)))


def meia_largura_interna(x, z, folga=0.25):
    """Meia-largura interna da fuselagem na altura z."""
    w, zt, zb = secao(x)
    zc = (zt + zb) / 2
    h = (zt - zb) / 2 - folga
    s = (z - zc) / h
    if abs(s) >= 1:
        return 0.0
    return (w - folga) * math.sqrt(1 - s * s)


# Estacoes: poucas (low poly), mais densas no nariz e na cauda
estacoes = sorted(set(
    [round(x, 3) for x in np.linspace(CAUDA_X + 0.25, INICIO_CAUDA, 9)]
    + [round(x, 3) for x in np.linspace(INICIO_CAUDA, INICIO_NARIZ, 9)]
    + [round(INICIO_NARIZ + (NARIZ_X - INICIO_NARIZ) * (1 - (1 - t) ** 1.6), 3)
       for t in np.linspace(0, 0.985, 9)]
    + [CORTE_X[0], CORTE_X[1]]
))
N_ANG = 20
angulos = [-math.pi / 2 + 2 * math.pi * j / N_ANG for j in range(N_ANG + 1)]


def angulo_no_corte(th):
    a = (th + math.pi) % (2 * math.pi) - math.pi
    return CORTE_ANG[0] <= a <= CORTE_ANG[1]


# ---------------------------------------------------------------------------
# Textura pintada a mao da fuselagem (pintura + janelas desenhadas)
# ---------------------------------------------------------------------------
TEX_L, TEX_A = 2048, 1024


def secao_np(x):
    w = np.full_like(x, R)
    zt = np.full_like(x, R)
    zb = np.full_like(x, -R)
    nar = x > INICIO_NARIZ
    t = np.clip((x - INICIO_NARIZ) / (NARIZ_X - INICIO_NARIZ), 0, 1)
    w = np.where(nar, R * np.sqrt(np.clip(1 - t ** 2.0, 0, None)), w)
    zt = np.where(nar, -0.35 + (R + 0.35) * np.sqrt(np.clip(1 - t ** 1.7, 0, None)), zt)
    zb = np.where(nar, -0.55 - (R - 0.55) * np.sqrt(np.clip(1 - t ** 2.4, 0, None)), zb)
    cau = x < INICIO_CAUDA
    s = np.clip((INICIO_CAUDA - x) / (INICIO_CAUDA - CAUDA_X), 0, 1)
    w = np.where(cau, 0.35 + (R - 0.35) * (1 - s ** 1.4), w)
    zt = np.where(cau, R - 0.35 * s ** 2, zt)
    zb = np.where(cau, -R + (R + 1.0) * s ** 1.6, zb)
    return w, zt, zb


def ruido_suave(alt, larg, escala, semente, escala_x=None):
    escala_x = escala if escala_x is None else escala_x
    g = np.random.default_rng(semente)
    pequeno = g.random((alt // escala + 2, larg // escala_x + 2))
    ys = np.linspace(0, pequeno.shape[0] - 1.001, alt)
    xs = np.linspace(0, pequeno.shape[1] - 1.001, larg)
    y0 = ys.astype(int)
    x0 = xs.astype(int)
    fy = (ys - y0)[:, None]
    fx = (xs - x0)[None, :]
    fy = fy * fy * (3 - 2 * fy)
    fx = fx * fx * (3 - 2 * fx)
    a = pequeno[y0][:, x0]
    b = pequeno[y0][:, x0 + 1]
    c = pequeno[y0 + 1][:, x0]
    d = pequeno[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def mistura(base, cor, mascara):
    m = np.clip(mascara, 0, 1)[..., None]
    return base * (1 - m) + np.array(cor)[None, None, :] * m


def borda(valor, largura):
    """Transicao curta (contorno pintado, nao serrilhado)."""
    return np.clip(0.5 + valor / largura, 0, 1)


def gerar_textura_fuselagem():
    u = (np.arange(TEX_L) + 0.5) / TEX_L
    v = (np.arange(TEX_A) + 0.5) / TEX_A
    U, V = np.meshgrid(u, v)
    X = CAUDA_X + U * (NARIZ_X - CAUDA_X)
    TH = -math.pi / 2 + V * 2 * math.pi
    w, zt, zb = secao_np(X)
    zc = (zt + zb) / 2
    h = (zt - zb) / 2
    S = np.sin(TH)
    C = np.cos(TH)
    Y = w * C
    Z = zc + h * S

    tremor = (ruido_suave(TEX_A, TEX_L, 48, 1) - 0.5) * 0.10   # linhas "a mao"
    img = np.ones((TEX_A, TEX_L, 3)) * np.array(COR["branco"])

    # Barriga azul que sobe e engole a cauda
    t = np.clip((-3.5 - X) / 8.0, 0, 1)
    limite = -0.42 + 1.95 * t ** 1.3 + tremor
    img = mistura(img, COR["azul"], borda(limite - S, 0.035))
    # Filete azul claro acima da barriga
    filete = 1 - np.abs(S - (limite + 0.09)) / 0.03
    img = mistura(img, COR["azul_claro"], np.clip(filete, 0, 1) * (X > -8.5))

    # Faixa branca "swoosh" atravessando a cauda azul
    centro_sw = 0.95 - 0.16 * (X + 15.5) + tremor * 0.5
    sw = np.clip(1 - np.abs(S - centro_sw) / 0.11, 0, 1)
    img = mistura(img, COR["branco"], borda(sw - 0.5, 0.15) * (X < -6.0))

    # Janelas redondas e grandes (cartunescas), dos dois lados
    janelas_x = np.arange(9.4, -6.0, -1.05)
    janelas_x = janelas_x[(np.abs(janelas_x - 0.2) > 0.6)]   # saida sobre a asa
    zj = 0.45
    jan = np.zeros_like(X)
    aro = np.zeros_like(X)
    for xj in janelas_x:
        d = np.sqrt(((X - xj) / 0.36) ** 2 + ((Z - zj) / 0.42) ** 2)
        jan = np.maximum(jan, borda(1 - d, 0.06))
        aro = np.maximum(aro, borda(1.22 - d, 0.06))
    lado_ok = np.abs(C) > 0.5
    img = mistura(img, COR["cinza"], aro * lado_ok)
    vidro = np.array(COR["vidro"])
    brilho = np.clip(1 - np.sqrt(((Z - zj - 0.2) / 0.25) ** 2), 0, 1) * 0.5
    img = mistura(img, vidro, jan * lado_ok)
    img = mistura(img, COR["azul_claro"], jan * lado_ok * brilho)

    # Para-brisa expressivo ("olhos" do aviao) com moldura
    nariz = (X > 12.6) & (X < 14.4)
    t_n = np.clip((X - 12.6) / 1.8, 0, 1)
    alto = 0.98 - 0.55 * t_n
    baixo = 0.32 - 0.05 * t_n
    janela_cab = borda(S - baixo, 0.03) * borda(alto - S, 0.03) * nariz
    divisoes = (np.abs(Y) < 0.06) | (np.abs(np.abs(Y) - 0.95) < 0.07)
    img = mistura(img, COR["preto"], janela_cab)
    img = mistura(img, COR["vidro"], janela_cab * (~divisoes) * 0.85)
    reflexo = borda(S - (alto - 0.12), 0.03) * janela_cab * (~divisoes)
    img = mistura(img, COR["azul_claro"], reflexo * 0.8)

    # Portas desenhadas com contorno
    def porta(x0, x1, z0, z1):
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        hx, hz = (x1 - x0) / 2, (z1 - z0) / 2
        d = np.maximum(np.abs(X - cx) / hx, np.abs(Z - cz) / hz)
        return np.clip(1 - np.abs(d - 1) / 0.035, 0, 1) * lado_ok
    for x0, x1, z0, z1 in [(10.5, 11.5, -0.7, 1.55), (-6.4, -5.5, -0.7, 1.5),
                           (-0.35, 0.75, -0.05, 1.05)]:
        img = mistura(img, COR["cinza_escuro"], porta(x0, x1, z0, z1))

    # Pinceladas e sujeirinha (aspecto pintado a mao)
    # pinceladas: ruido esticado no sentido do comprimento
    pinc = ruido_suave(TEX_A, TEX_L, 10, 3, escala_x=110)
    pinc2 = ruido_suave(TEX_A, TEX_L, 120, 4)
    img *= (0.965 + 0.05 * pinc[..., None]) * (0.96 + 0.06 * pinc2[..., None])
    # Luz de cima, sombra embaixo (pintado na textura)
    img *= (0.86 + 0.16 * (S[..., None] + 1) / 2)
    img = np.clip(img, 0, 1)

    imagem = bpy.data.images.new("Textura_Fuselagem", TEX_L, TEX_A, alpha=False)
    rgba = np.concatenate([img, np.ones((TEX_A, TEX_L, 1))], axis=2)
    imagem.pixels.foreach_set(rgba.astype(np.float32).ravel())
    imagem.file_format = "PNG"
    imagem.pack()
    return imagem


TEX_FUSELAGEM = gerar_textura_fuselagem()


def construir_casco(nome, pai, no_corte):
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    grade = {}
    for i, x in enumerate(estacoes):
        for j, th in enumerate(angulos):
            grade[i, j] = bm.verts.new(ponto(x, th))
    for i in range(len(estacoes) - 1):
        xc = (estacoes[i] + estacoes[i + 1]) / 2
        for j in range(N_ANG):
            thc = (angulos[j] + angulos[j + 1]) / 2
            dentro = (CORTE_X[0] <= xc <= CORTE_X[1]) and angulo_no_corte(thc)
            if dentro != no_corte:
                continue
            vs = [grade[i, j], grade[i, j + 1], grade[i + 1, j + 1], grade[i + 1, j]]
            f = bm.faces.new(vs)
            for loop, (ii, jj) in zip(f.loops, [(i, j), (i, j + 1), (i + 1, j + 1), (i + 1, j)]):
                loop[uv].uv = ((estacoes[ii] - CAUDA_X) / (NARIZ_X - CAUDA_X),
                               jj / N_ANG)
    if not no_corte:
        # tampa do nariz e da cauda (leque)
        for idx, ponta, u_val in [(len(estacoes) - 1, Vector((NARIZ_X, 0, -0.45)), 1.0),
                                  (0, None, 0.0)]:
            if ponta is None:
                x0 = estacoes[0]
                w, zt, zb = secao(x0)
                ponta = Vector((CAUDA_X, 0, (zt + zb) / 2))
            centro = bm.verts.new(ponta)
            for j in range(N_ANG):
                a, b = grade[idx, j], grade[idx, j + 1]
                f = bm.faces.new((a, b, centro) if idx else (b, a, centro))
                for loop in f.loops:
                    loop[uv].uv = (u_val, (j + 0.5) / N_ANG)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.normal_update()
    # garantir normais para fora
    for f in bm.faces:
        c = f.calc_center_median()
        w, zt, zb = secao(c.x)
        radial = Vector((0, c.y, c.z - (zt + zb) / 2))
        if f.normal.dot(radial) < 0 and abs(c.x) < 15.3:
            f.normal_flip()
    me = bpy.data.meshes.new(nome)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    me.materials.append(material("Fuselagem", textura=TEX_FUSELAGEM, cor_vertice=False))
    me.materials.append(material("Parede_Interna", COR["creme"], cor_vertice=False))
    me.materials.append(material("Borda_Corte", COR["cinza"], cor_vertice=False))
    obj = bpy.data.objects.new(nome, me)
    cena.collection.objects.link(obj)
    obj.parent = pai
    sol = obj.modifiers.new("Espessura", "SOLIDIFY")
    sol.thickness = ESPESSURA_CASCO
    sol.offset = -1
    sol.material_offset = 1
    sol.material_offset_rim = 2
    sol.use_rim = True
    return obj


construir_casco("Fuselagem", EXTERIOR, no_corte=False)
construir_casco("Fuselagem_Corte", CASCO_CORTE, no_corte=True)

# ---------------------------------------------------------------------------
# Superficies aerodinamicas (asas, estabilizadores, deriva) low poly
# ---------------------------------------------------------------------------
def perfil(n=6, espessura=0.16):
    """Perfil de asa simplificado (loop fechado, poucos pontos)."""
    us = [0.5 - 0.5 * math.cos(math.pi * k / n) for k in range(n + 1)]
    def meia(u):
        return 5 * espessura * (0.2969 * math.sqrt(u) - 0.126 * u - 0.3516 * u ** 2
                                + 0.2843 * u ** 3 - 0.1036 * u ** 4)
    cima = [(u, meia(u) + 0.03 * math.sin(math.pi * u)) for u in reversed(us)]
    baixo = [(u, -meia(u) * 0.75 + 0.03 * math.sin(math.pi * u)) for u in us[1:-1]]
    return cima + baixo


def loft(peca, secoes, cores, n=6, espessura=0.16, uv=None, uv_fixo=None):
    """secoes: lista de (bordo_ataque Vector, corda, eixo_espessura Vector).
    cores: cor por intervalo entre secoes.
    uv: camada UV opcional; u = fracao da corda, v = fracao da envergadura
    (ou uv_fixo para pintar tudo com um unico ponto da textura)."""
    pf = perfil(n, espessura)
    aneis = []
    for le, corda, eixo in secoes:
        aneis.append([peca.bm.verts.new(le + Vector((-u * corda, 0, 0)) + eixo * (v * corda))
                      for u, v in pf])
    m = len(pf)
    for k in range(len(aneis) - 1):
        for i in range(m):
            j = (i + 1) % m
            f = peca.bm.faces.new((aneis[k][i], aneis[k][j], aneis[k + 1][j], aneis[k + 1][i]))
            peca.cor_faces[f] = cores[k]
            if uv is not None:
                total = len(aneis) - 1
                for loop, (ii, kk) in zip(f.loops, ((i, k), (j, k), (j, k + 1), (i, k + 1))):
                    loop[uv].uv = uv_fixo or (pf[ii][0], kk / total)
    for anel, cor, v in ((aneis[0], cores[0], 0.0), (aneis[-1], cores[-1], 1.0)):
        f = peca.bm.faces.new(anel)
        peca.cor_faces[f] = cor
        if uv is not None:
            for loop, (u_p, _) in zip(f.loops, pf):
                loop[uv].uv = uv_fixo or (u_p, v)


def construir_asa(lado):
    s = lado
    peca = Peca(f"Asa_{'Esquerda' if s > 0 else 'Direita'}", EXTERIOR)
    up = Vector((0, 0, 1))
    def roll(phi):  # eixo da espessura girando para virar o winglet
        return Vector((0, -s * math.sin(phi), math.cos(phi)))
    secoes = [
        (Vector((3.2, s * 1.6, -1.45)), 6.4, up),
        (Vector((2.2, s * 5.0, -1.05)), 5.0, up),
        (Vector((0.6, s * 9.5, -0.45)), 3.4, up),
        (Vector((-0.6, s * 12.4, -0.05)), 2.3, up),
        (Vector((-1.1, s * 12.95, 0.35)), 1.8, roll(math.radians(55))),
        (Vector((-1.9, s * 13.15, 1.9)), 1.0, roll(math.radians(85))),
    ]
    branco = COR["branco"]
    loft(peca, secoes, [branco, branco, branco, COR["azul"], COR["azul"]],
         n=6, espessura=0.18)
    return peca.criar()


construir_asa(+1)
construir_asa(-1)


def construir_estabilizador(lado):
    s = lado
    peca = Peca(f"Estabilizador_{'Esquerdo' if s > 0 else 'Direito'}", EXTERIOR)
    up = Vector((0, 0, 1))
    secoes = [
        (Vector((-11.4, s * 0.3, 0.95)), 3.4, up),
        (Vector((-13.3, s * 4.8, 1.55)), 1.6, up),
        (Vector((-13.7, s * 5.3, 1.62)), 1.0, up),
    ]
    loft(peca, secoes, [COR["branco"], COR["azul"]], n=5, espessura=0.13)
    return peca.criar()


construir_estabilizador(+1)
construir_estabilizador(-1)


def gerar_textura_deriva(tam=512):
    """Azul com a faixa branca curva ("swoosh"), com cara de pintado a mao."""
    u = (np.arange(tam) + 0.5) / tam
    U, V = np.meshgrid(u, u)          # U = corda (0 = bordo de ataque), V = altura
    tremor = (ruido_suave(tam, tam, 64, 21) - 0.5) * 0.04
    centro = 0.1 + 0.98 * U ** 0.8 + tremor
    dist = np.abs(V - centro)
    img = np.ones((tam, tam, 3)) * np.array(COR["azul"])
    img = mistura(img, COR["azul_claro"], borda(0.155 - dist, 0.012))
    img = mistura(img, COR["branco"], borda(0.125 - dist, 0.012))
    img = mistura(img, COR["azul_escuro"], borda(0.12 - V, 0.05) * 0.6)  # raiz mais escura
    pinc = ruido_suave(tam, tam, 4, 22, escala_x=40)
    img *= (0.95 + 0.07 * pinc[..., None]) * (0.9 + 0.12 * V[..., None])
    img = np.clip(img, 0, 1)
    imagem = bpy.data.images.new("Textura_Deriva", tam, tam, alpha=False)
    rgba = np.concatenate([img, np.ones((tam, tam, 1))], axis=2)
    imagem.pixels.foreach_set(rgba.astype(np.float32).ravel())
    imagem.pack()
    return imagem


material("Deriva", textura=gerar_textura_deriva(), cor_vertice=False)
deriva = Peca("Deriva", EXTERIOR, variacao=0.04)
uv_deriva = deriva.bm.loops.layers.uv.new("UVMap")
lateral = Vector((0, 1, 0))
alturas = np.linspace(0, 1, 8)
secoes_deriva = []
for a in alturas:
    z = 1.6 + a * 5.6
    le_x = -7.0 - a * 4.4 - 1.2 * a ** 2
    corda = 5.6 - 3.6 * a ** 0.9
    secoes_deriva.append((Vector((le_x, 0, z)), corda, lateral))
loft(deriva, secoes_deriva, [COR["branco"]] * 7, n=7, espessura=0.13, uv=uv_deriva)
# Barbatana dorsal (filete entre fuselagem e deriva): ponto azul da textura
loft(deriva, [(Vector((-4.6, 0, 1.75)), 3.2, lateral),
              (Vector((-7.1, 0, 2.55)), 0.6, lateral)],
     [COR["branco"]], n=5, espessura=0.10, uv=uv_deriva, uv_fixo=(0.9, 0.3))
deriva.criar(mat_nome="Deriva")

# ---------------------------------------------------------------------------
# Motores gordinhos (cartunescos)
# ---------------------------------------------------------------------------
def torno(peca, perfil_xr, centro, cor_por_seg, lados=12):
    """Revolve (x_relativo, raio) em torno do eixo X, nariz para +X."""
    cx, cy, cz = centro
    aneis = []
    for xr, r in perfil_xr:
        anel = []
        for k in range(lados):
            a = 2 * math.pi * k / lados
            anel.append(peca.bm.verts.new((cx - xr, cy + r * math.cos(a), cz + r * math.sin(a))))
        aneis.append(anel)
    for i in range(len(aneis) - 1):
        for k in range(lados):
            k2 = (k + 1) % lados
            f = peca.bm.faces.new((aneis[i][k], aneis[i][k2], aneis[i + 1][k2], aneis[i + 1][k]))
            peca.cor_faces[f] = cor_por_seg[min(i, len(cor_por_seg) - 1)]
    return aneis


def construir_motor(lado):
    s = lado
    nome = f"Motor_{'Esquerdo' if s > 0 else 'Direito'}"
    peca = Peca(nome, EXTERIOR)
    c = (5.4, s * 5.1, -2.35)
    rmax = 1.35
    perfil_nacele = [(0.75, 1.0), (0.12, 1.05), (0.0, 1.2), (0.12, rmax),
                     (0.8, rmax + 0.08), (2.0, rmax + 0.05), (3.1, 1.15), (3.6, 0.95)]
    cores = [COR["cinza_escuro"], COR["metal"], COR["metal"], COR["branco"],
             COR["branco"], COR["branco"], COR["azul"]]
    torno(peca, perfil_nacele, c, cores, lados=12)
    # Ventoinha
    peca.cilindro((c[0] - 0.72, c[1], c[2]), 1.02, 0.06, COR["preto"], eixo="X", lados=12)
    for k in range(10):
        a = 2 * math.pi * k / 10
        peca.caixa((c[0] - 0.66, c[1] + 0.55 * math.cos(a), c[2] + 0.55 * math.sin(a)),
                   (0.06, 0.24, 0.85), COR["cinza_escuro"],
                   rot=(a - math.pi / 2, math.radians(30), 0))
    # Cone central em espiral (classico de desenho animado)
    torno(peca, [(0.25, 0.0), (0.4, 0.22), (0.68, 0.34)], (c[0], c[1], c[2]),
          [COR["branco"], COR["metal"]], lados=10)
    # Saida do nucleo
    torno(peca, [(3.4, 0.85), (3.9, 0.72), (4.4, 0.5), (4.75, 0.0)], c,
          [COR["metal"], COR["cinza"], COR["cinza_escuro"]], lados=10)
    # Pilone (liga o motor a asa)
    loft(peca, [(Vector((4.8, s * 5.1, -1.35)), 4.6, Vector((0, 1, 0))),
                (Vector((3.4, s * 5.1, -0.75)), 4.2, Vector((0, 1, 0)))],
         [COR["branco"]], n=4, espessura=0.10)
    return peca.criar(recalcular=False)


construir_motor(+1)
construir_motor(-1)

# ---------------------------------------------------------------------------
# Trem de pouso
# ---------------------------------------------------------------------------
trem = Peca("Trem_de_Pouso", EXTERIOR)
for s in (+1, -1):
    y = s * 3.2
    raio = 0.72
    eixo_z = CHAO_Z + raio
    trem.cilindro((-1.4, y, (eixo_z - 1.35) / 2), 0.14, -1.35 - eixo_z + 0.2,
                  COR["metal"], lados=8)
    for dy in (-0.42, 0.42):
        trem.cilindro((-1.4, y + dy, eixo_z), raio, 0.42, COR["preto"], eixo="Y", lados=10)
        trem.cilindro((-1.4, y + dy * 1.06, eixo_z), raio * 0.5, 0.44, COR["cinza"],
                      eixo="Y", lados=8)
# Bequilha (trem do nariz)
raio_n = 0.48
eixo_n = CHAO_Z + raio_n
trem.cilindro((11.6, 0, (eixo_n - 1.7) / 2), 0.11, -1.7 - eixo_n + 0.2, COR["metal"])
for dy in (-0.28, 0.28):
    trem.cilindro((11.6, dy, eixo_n), raio_n, 0.3, COR["preto"], eixo="Y", lados=10)
    trem.cilindro((11.6, dy * 1.08, eixo_n), raio_n * 0.5, 0.32, COR["cinza"], eixo="Y", lados=8)
trem.criar()

# Luzes de navegacao (vermelha a esquerda, verde a direita) e farol anticolisao
for nome, cor, pos in (("Luz_Vermelha", "vermelho", [(-1.2, 13.0, 0.3), (2.0, 0, R + 0.05)]),
                       ("Luz_Verde", "verde", [(-1.2, -13.0, 0.3)])):
    luz = Peca(nome, EXTERIOR, variacao=0.0)
    for p in pos:
        luz.esfera(p, (0.16, 0.16, 0.13), COR[cor], segs=6, aneis=4)
    obj = luz.criar()
    obj.data.materials[0] = material(nome, COR[cor], emissao=COR[cor], forca=1.2,
                                     cor_vertice=False)

# ---------------------------------------------------------------------------
# INTERIOR
# ---------------------------------------------------------------------------
# Piso acompanhando a largura da fuselagem
piso = Peca("Piso", INTERIOR, variacao=0.08)
xs_piso = np.linspace(14.4, X_ANIMAIS[1] - 0.05, 32)
for k in range(len(xs_piso) - 1):
    xa, xb = xs_piso[k], xs_piso[k + 1]
    wa = meia_largura_interna(xa, PISO_Z, 0.13)
    wb = meia_largura_interna(xb, PISO_Z, 0.13)
    xm = (xa + xb) / 2
    cor = COR["metal"] if xm < X_ANIMAIS[0] else COR["carpete"]
    if X_GALLEY[1] < xm < X_BANHEIROS[0]:
        cor = COR["cinza"]
    piso.malha([(xa, wa, PISO_Z), (xb, wb, PISO_Z), (xb, -wb, PISO_Z), (xa, -wa, PISO_Z)],
               [(0, 3, 2, 1)], cor)
    # corredor (tapete mais claro)
    if cor == COR["carpete"]:
        piso.malha([(xa, 0.32, PISO_Z + 0.01), (xb, 0.32, PISO_Z + 0.01),
                    (xb, -0.32, PISO_Z + 0.01), (xa, -0.32, PISO_Z + 0.01)],
                   [(0, 3, 2, 1)], COR["azul_claro"])
piso.criar(recalcular=False)


def anteparo(peca, x, cor, vao=0.45, espessura=0.08, topo=None):
    """Parede transversal acompanhando a secao, com porta central."""
    w, zt, zb = secao(x)
    zc = (zt + zb) / 2
    h = (zt - zb) / 2 - 0.16
    w = w - 0.16
    z_topo = zt - 0.16 if topo is None else topo
    for s in (+1, -1):
        pts = [(s * vao, PISO_Z)]
        th0 = math.asin(max(-1, min(1, (PISO_Z - zc) / h)))
        th1 = math.asin(max(-1, min(1, (z_topo - zc) / h)))
        for k in range(7):
            th = th0 + (th1 - th0) * k / 6
            pts.append((s * w * math.cos(th), zc + h * math.sin(th)))
        pts.append((s * vao, min(z_topo, zc + h * math.sin(th1))))
        if s < 0:
            pts.reverse()
        peca.prisma(pts, x + espessura / 2, x - espessura / 2, cor)


paredes = Peca("Paredes", INTERIOR, variacao=0.05)
anteparo(paredes, 12.05, COR["creme"], vao=0.42)
anteparo(paredes, 8.7, COR["creme"], vao=0.55, topo=0.9)
anteparo(paredes, 4.9, COR["creme"], vao=0.55, topo=0.9)
anteparo(paredes, X_GALLEY[0] + 0.1, COR["creme"], vao=0.5)
anteparo(paredes, X_ANIMAIS[0] + 0.1, COR["creme"], vao=0.5)
anteparo(paredes, X_ANIMAIS[1], COR["cinza"], vao=0.0)
paredes.criar(recalcular=False)

# Bagageiros: lado de la (-Y) visivel; lado de ca (+Y) some com o corte
bag_longe = Peca("Bagageiros", INTERIOR)
bag_perto = Peca("Bagageiros_Lado_Corte", CASCO_CORTE)
for xa, xb in ((11.7, 8.9), (8.5, 5.1), (4.7, X_ECONOMICA[1] + 0.1)):
    for s, peca in ((-1, bag_longe), (+1, bag_perto)):
        n = max(1, round((xa - xb) / 1.5))
        passo = (xa - xb) / n
        for k in range(n):
            xc = xa - passo * (k + 0.5)
            peca.caixa((xc, s * 1.45, 1.35), (passo - 0.06, 0.62, 0.5), COR["branco"],
                       rot=(s * math.radians(-18), 0, 0), chanfro=0.04)
            peca.caixa((xc, s * 1.16, 1.27), (passo * 0.5, 0.04, 0.06), COR["cinza"],
                       rot=(s * math.radians(-18), 0, 0))
bag_longe.criar()
bag_perto.criar()

# Faixa de luz no teto e janelas internas (lado de la)
luz_teto = Peca("Luz_Teto", INTERIOR, variacao=0.0)
for xa, xb in ((11.7, 8.9), (8.5, 5.1), (4.7, X_ECONOMICA[1] + 0.1)):
    luz_teto.caixa(((xa + xb) / 2, -1.08, 1.02), (xa - xb, 0.06, 0.08), COR["luz"])
    luz_teto.caixa(((xa + xb) / 2, 0, R - 0.22), (xa - xb, 0.5, 0.04), COR["luz"])
obj_luz = luz_teto.criar()
obj_luz.data.materials[0] = material("Luz_Interna", COR["luz"], emissao=COR["luz"], forca=2.0)

janelas_int = Peca("Janelas_Internas", INTERIOR, variacao=0.0)
for xj in np.arange(9.4, -6.0, -1.05):
    if abs(xj - 0.2) <= 0.6:
        continue
    y = -meia_largura_interna(xj, 0.45, 0.13) + 0.02
    janelas_int.cilindro((xj, y, 0.45), 0.3, 0.04, COR["azul_claro"], eixo="Y", lados=10)
obj_ji = janelas_int.criar()
obj_ji.data.materials[0] = material("Ceu_Janela", COR["azul_claro"], emissao=(0.55, 0.78, 1.0),
                                    forca=0.8)


# --- Assentos -------------------------------------------------------------
def assento(peca, x, y, largura, cor_estofado, cor_detalhe, profundidade=0.55,
            altura_encosto=0.85, luxo=False):
    zs = PISO_Z + 0.42
    peca.caixa((x, y, zs), (profundidade, largura - 0.06, 0.18), cor_estofado, chanfro=0.05)
    peca.caixa((x - profundidade / 2, y, zs + altura_encosto / 2 + 0.05),
               (0.18, largura - 0.08, altura_encosto), cor_estofado,
               rot=(0, math.radians(-12), 0), chanfro=0.05)
    # encosto de cabeca
    peca.caixa((x - profundidade / 2 - 0.08, y, zs + altura_encosto + 0.1),
               (0.2, largura * 0.6, 0.2), cor_detalhe, rot=(0, math.radians(-12), 0),
               chanfro=0.04)
    # bracos
    for dy in (-1, 1):
        peca.caixa((x - 0.05, y + dy * (largura / 2 - 0.03), zs + 0.18),
                   (profundidade * 0.9, 0.07, 0.07), cor_detalhe)
    # pe
    peca.caixa((x, y, PISO_Z + 0.16), (profundidade * 0.5, largura * 0.5, 0.32), COR["cinza_escuro"])
    if luxo:
        peca.caixa((x + profundidade * 0.65, y, PISO_Z + 0.3), (0.35, largura * 0.7, 0.18),
                   cor_estofado, chanfro=0.04)   # apoio de pernas


assentos = Peca("Assentos", INTERIOR, variacao=0.08)
lugares = []   # (x, y, classe) para sentar passageiros

# Primeira classe: poltronas enormes 1-1
for x in (11.0, 9.7):
    for y in (1.15, -1.15):
        assento(assentos, x, y, 0.95, COR["bege"], COR["marrom"], profundidade=0.75,
                altura_encosto=1.0, luxo=True)
        lugares.append((x, y, "primeira"))
        assentos.caixa((x + 0.2, y - math.copysign(0.65, y), PISO_Z + 0.35),
                       (0.6, 0.3, 0.7), COR["marrom"], chanfro=0.04)   # console

# Executiva: 2-2
for x in (7.9, 6.95, 6.0):
    for y in (1.25, 0.67, -0.67, -1.25):
        assento(assentos, x, y, 0.62, COR["azul_claro"], COR["azul_escuro"],
                profundidade=0.6, altura_encosto=0.95)
        lugares.append((x, y, "executiva"))

# Economica: 3-3 bem apertadinho
for x in np.arange(4.2, X_ECONOMICA[1] + 0.4, -0.82):
    for y in (1.42, 0.95, 0.48, -0.48, -0.95, -1.42):
        assento(assentos, x, y, 0.5, COR["azul"], COR["azul_escuro"])
        lugares.append((float(x), y, "economica"))
assentos.criar()

# --- Cockpit ----------------------------------------------------------------
cockpit = Peca("Cockpit", INTERIOR, variacao=0.06)
for y in (0.62, -0.62):
    assento(cockpit, 12.9, y, 0.7, COR["cinza_escuro"], COR["preto"], profundidade=0.6,
            altura_encosto=0.95)
# painel de instrumentos
cockpit.caixa((13.75, 0, PISO_Z + 0.55), (0.4, 2.2, 1.1), COR["cinza_escuro"], chanfro=0.05)
cockpit.caixa((13.6, 0, PISO_Z + 1.12), (0.55, 2.3, 0.1), COR["preto"])   # glareshield
cockpit.caixa((13.3, 0, PISO_Z + 0.4), (0.9, 0.45, 0.8), COR["cinza_escuro"], chanfro=0.05)
for y in (0.62, -0.62):
    cockpit.cilindro((13.45, y, PISO_Z + 0.75), 0.04, 0.4, COR["preto"],
                     rot_extra=(0, math.radians(-30), 0))
    cockpit.caixa((13.32, y, PISO_Z + 0.95), (0.05, 0.36, 0.05), COR["preto"])   # manche
cockpit.caixa((12.7, 0, R - 0.55), (1.0, 0.9, 0.12), COR["cinza_escuro"],
              rot=(0, math.radians(10), 0))   # painel do teto
cockpit.criar()

telas = Peca("Telas", INTERIOR, variacao=0.0)
for y in (-0.85, -0.4, 0.0, 0.4, 0.85):
    telas.caixa((13.54, y, PISO_Z + 0.72), (0.02, 0.32, 0.26), COR["tela"])
telas.caixa((13.3, 0, PISO_Z + 0.82), (0.5, 0.3, 0.02), COR["tela"])
obj_telas = telas.criar()
obj_telas.data.materials[0] = material("Telas", COR["tela"], emissao=COR["tela"], forca=1.6)

# --- Cozinha (galley) -------------------------------------------------------
galley = Peca("Cozinha", INTERIOR, variacao=0.07)
for s in (+1, -1):
    xm = sum(X_GALLEY) / 2
    comprimento = X_GALLEY[0] - X_GALLEY[1] - 0.2
    galley.caixa((xm, s * 1.15, PISO_Z + 0.5), (comprimento, 0.9, 1.0), COR["metal"],
                 chanfro=0.03)
    galley.caixa((xm, s * 1.15, PISO_Z + 1.03), (comprimento, 0.95, 0.06), COR["cinza_escuro"])
    galley.caixa((xm, s * 1.25, PISO_Z + 1.65), (comprimento, 0.7, 0.75), COR["metal"],
                 chanfro=0.03)
    # carrinhos e gavetas
    for k in range(3):
        xc = X_GALLEY[0] - 0.4 - k * 0.55
        galley.caixa((xc, s * 0.68, PISO_Z + 0.45), (0.46, 0.04, 0.82), COR["cinza"])
        galley.caixa((xc, s * 0.66, PISO_Z + 0.75), (0.2, 0.04, 0.05), COR["vermelho"])
        for zz in (1.45, 1.85):
            galley.caixa((xc, s * 0.88, PISO_Z + zz), (0.45, 0.04, 0.3), COR["cinza"])
    # cafeteira e xicaras (piada visual)
    galley.cilindro((X_GALLEY[0] - 0.6, s * 1.1, PISO_Z + 1.2), 0.12, 0.3, COR["preto"])
    for k in range(3):
        galley.cilindro((X_GALLEY[0] - 1.0 - k * 0.18, s * 1.0, PISO_Z + 1.1), 0.05, 0.08,
                        COR["branco"], lados=6)
galley.criar()

# --- Banheiros ----------------------------------------------------------------
banheiros = Peca("Banheiros", INTERIOR, variacao=0.06)
for s in (+1, -1):
    meio = sum(X_BANHEIROS) / 2
    for xa, xb in ((X_BANHEIROS[0] - 0.05, meio + 0.03), (meio - 0.03, X_BANHEIROS[1] + 0.05)):
        xm = (xa + xb) / 2
        largura = xa - xb
        # paredes laterais (entre cabines) e parede do corredor com porta
        banheiros.caixa((xb + 0.03, s * 1.05, PISO_Z + 1.05), (0.06, 1.0, 2.1), COR["creme"])
        banheiros.caixa((xm + largura * 0.3, s * 0.55, PISO_Z + 1.05), (largura * 0.4, 0.06, 2.1),
                        COR["creme"])
        # vaso sanitario
        banheiros.cilindro((xm - 0.15, s * 1.15, PISO_Z + 0.22), 0.2, 0.44, COR["branco"],
                           lados=10, raio2=0.24)
        banheiros.cilindro((xm - 0.15, s * 1.15, PISO_Z + 0.46), 0.25, 0.05, COR["branco"],
                           lados=10)
        banheiros.caixa((xm - 0.15, s * 1.42, PISO_Z + 0.7), (0.4, 0.15, 0.45), COR["branco"],
                        chanfro=0.04)
        # pia
        banheiros.caixa((xm + 0.3, s * 1.2, PISO_Z + 0.85), (0.45, 0.45, 0.12), COR["metal"])
        banheiros.cilindro((xm + 0.3, s * 1.2, PISO_Z + 0.9), 0.14, 0.04, COR["cinza"], lados=8)
        # espelho
        banheiros.caixa((xm + 0.3, s * 1.42, PISO_Z + 1.45), (0.45, 0.03, 0.5), COR["azul_claro"])
        # rolo de papel (eventualmente vai sair voando)
        banheiros.cilindro((xm - 0.5, s * 1.3, PISO_Z + 0.6), 0.08, 0.14, COR["branco"],
                           eixo="Y", lados=8)
banheiros.criar()

# --- Compartimento de animais -----------------------------------------------
animais = Peca("Compartimento_Animais", INTERIOR, variacao=0.06)
gaiolas_pos = []
for s in (+1, -1):
    for xc in np.linspace(X_ANIMAIS[0] - 0.55, X_ANIMAIS[1] + 0.55, 3):
        for andar in (0, 1):
            z0 = PISO_Z + andar * 0.82
            y_lim = min(meia_largura_interna(xc, z0 + 0.8, 0.2),
                        meia_largura_interna(xc, z0, 0.2))
            prof = max(0.5, min(0.8, y_lim - 0.3))
            yc = s * (0.3 + prof / 2)
            gaiolas_pos.append((xc, yc, z0, prof, s))
            larg = 0.85
            alt = 0.78
            # chao e teto da gaiola
            animais.caixa((xc, yc, z0 + 0.03), (larg, prof, 0.06), COR["cinza_escuro"])
            animais.caixa((xc, yc, z0 + alt), (larg, prof, 0.05), COR["metal"])
            # laterais
            for dx in (-1, 1):
                animais.caixa((xc + dx * larg / 2, yc, z0 + alt / 2), (0.05, prof, alt),
                              COR["metal"])
            # grades nos dois lados (corredor e parede)
            for dy in (-1, 1):
                yg = yc + dy * prof / 2
                for k in range(6):
                    animais.caixa((xc - larg / 2 + (k + 0.5) * larg / 6, yg, z0 + alt / 2),
                                  (0.03, 0.03, alt), COR["cinza"])
                animais.caixa((xc, yg, z0 + alt * 0.5), (larg, 0.04, 0.04), COR["cinza"])
            # etiqueta colorida
            animais.caixa((xc + larg / 2 - 0.12, s * 0.28, z0 + alt - 0.12), (0.12, 0.02, 0.08),
                          random.choice([COR["laranja"], COR["verde"], COR["vermelho"]]))
# trilhos de amarracao no piso
for y in (0.4, -0.4):
    animais.caixa((sum(X_ANIMAIS) / 2, y, PISO_Z + 0.02), (X_ANIMAIS[0] - X_ANIMAIS[1], 0.06, 0.03),
                  COR["cinza_escuro"])
animais.criar()


# Bichinhos cartunescos dentro das gaiolas
def cachorro(peca, x, y, z, s, cor, cor2):
    olhar = -s   # olham para o corredor
    peca.esfera((x, y, z + 0.2), (0.28, 0.18, 0.16), cor, segs=8, aneis=5)
    peca.esfera((x + 0.22, y + olhar * 0.02, z + 0.38), (0.15, 0.14, 0.14), cor, segs=8, aneis=5)
    peca.esfera((x + 0.34, y + olhar * 0.02, z + 0.34), (0.08, 0.07, 0.06), cor2, segs=6, aneis=4)
    peca.esfera((x + 0.41, y + olhar * 0.02, z + 0.35), (0.03, 0.03, 0.03), COR["preto"], segs=6, aneis=3)
    for dy in (-0.09, 0.09):
        peca.esfera((x + 0.18, y + dy, z + 0.45), (0.05, 0.03, 0.11), cor2, segs=6, aneis=3,
                    rot=(0, math.radians(25), 0))
        peca.esfera((x + 0.29, y + olhar * 0.09 + dy * 0.6, z + 0.43), (0.025, 0.025, 0.03),
                    COR["preto"], segs=5, aneis=3)
    peca.esfera((x - 0.3, y, z + 0.3), (0.12, 0.03, 0.03), cor, segs=5, aneis=3,
                rot=(0, math.radians(-40), 0))   # rabo
    for dx in (-0.15, 0.15):
        peca.cilindro((x + dx, y, z + 0.06), 0.05, 0.12, cor, lados=6)


def gato(peca, x, y, z, s, cor):
    olhar = -s
    peca.esfera((x, y, z + 0.16), (0.2, 0.13, 0.12), cor, segs=8, aneis=5)
    peca.esfera((x + 0.17, y + olhar * 0.02, z + 0.3), (0.11, 0.11, 0.1), cor, segs=8, aneis=5)
    for dy in (-0.06, 0.06):
        peca.cilindro((x + 0.17, y + dy, z + 0.42), 0.04, 0.08, cor, lados=4, raio2=0.0)
        peca.esfera((x + 0.25, y + olhar * 0.07 + dy * 0.6, z + 0.32), (0.02, 0.025, 0.03),
                    COR["amarelo"], segs=5, aneis=3)
    peca.cilindro((x - 0.22, y, z + 0.3), 0.025, 0.4, cor, lados=5,
                  rot_extra=(0, math.radians(-25), 0))


bichos = Peca("Animais", INTERIOR, variacao=0.1)
for idx, (xc, yc, z0, prof, s) in enumerate(gaiolas_pos):
    escolha = idx % 4
    if escolha in (0, 3):
        cachorro(bichos, xc - 0.05, yc, z0 + 0.06, s, COR["caramelo"], COR["marrom"])
    elif escolha == 1:
        gato(bichos, xc, yc, z0 + 0.06, s, COR["cinza"])
    else:
        cachorro(bichos, xc - 0.05, yc, z0 + 0.06, s, COR["branco"], COR["preto"])
bichos.criar()


# ---------------------------------------------------------------------------
# Passageiros "feijao" (prontos para a comedia fisica) - cada um e um objeto
# separado para o jogo poder sacudir, derrubar e arremessar
# ---------------------------------------------------------------------------
PASSAGEIROS = novo_vazio("Passageiros", INTERIOR)
cores_roupa = [COR["vermelho"], COR["laranja"], COR["verde"], COR["roxo"], COR["amarelo"],
               COR["rosa"], COR["azul_claro"]]


def feijao(nome, x, y, z, cor, pai, piloto=False):
    p = Peca(nome, pai, variacao=0.05)
    p.esfera((0, 0, 0.32), (0.22, 0.2, 0.34), cor, segs=8, aneis=6)        # corpo
    p.esfera((0.02, 0, 0.78), (0.19, 0.18, 0.19), COR["pele"], segs=8, aneis=6)  # cabeca
    for dy in (-0.07, 0.07):
        p.esfera((0.17, dy, 0.82), (0.035, 0.045, 0.06), COR["branco"], segs=6, aneis=4)
        p.esfera((0.2, dy, 0.82), (0.02, 0.025, 0.035), COR["preto"], segs=5, aneis=3)
    p.caixa((0.19, 0, 0.7), (0.02, 0.09, 0.025), COR["vermelho"])           # boca
    for dy in (-0.22, 0.22):
        p.esfera((0.08, dy, 0.38), (0.07, 0.06, 0.2), cor, segs=6, aneis=4,
                 rot=(0, math.radians(-30), 0))                             # bracos
    if piloto:
        p.cilindro((0.0, 0, 0.98), 0.2, 0.1, COR["azul_escuro"], lados=8)
        p.caixa((0.13, 0, 0.96), (0.16, 0.36, 0.03), COR["preto"])
    obj = p.criar()
    obj.location = (x, y, z)
    return obj


random.seed(11)
n = 0
for x, y, classe in lugares:
    if random.random() < 0.55:
        feijao(f"Passageiro_{n:02d}", x - 0.05, y, PISO_Z + 0.5,
               random.choice(cores_roupa), PASSAGEIROS)
        n += 1
for k, y in enumerate((0.62, -0.62)):
    feijao(f"Piloto_{k + 1}", 12.95, y, PISO_Z + 0.5, COR["branco"], PASSAGEIROS, piloto=True)
feijao("Comissaria", sum(X_GALLEY) / 2, 0.0, PISO_Z, COR["azul"], PASSAGEIROS)

# ---------------------------------------------------------------------------
# Malas soltas no bagageiro (para cair na turbulencia)
# ---------------------------------------------------------------------------
MALAS = novo_vazio("Malas", INTERIOR)
for k, x in enumerate((10.2, 7.2, 3.1, 0.5, -1.8)):
    m = Peca(f"Mala_{k}", MALAS, variacao=0.08)
    m.caixa((0, 0, 0), (0.55, 0.38, 0.28), random.choice(cores_roupa), chanfro=0.05)
    m.caixa((0, 0, 0.17), (0.2, 0.05, 0.06), COR["preto"])
    obj = m.criar()
    obj.location = (x, -1.45, 1.42)
    obj.rotation_euler = (math.radians(-18), 0, math.radians(random.uniform(-10, 10)))

# ---------------------------------------------------------------------------
# Rotulos (o viewer do Three.js mostra os nomes no modo corte)
# ---------------------------------------------------------------------------
for nome, x in [("Rotulo_Cockpit", 13.1), ("Rotulo_Primeira_Classe", 10.3),
                ("Rotulo_Classe_Executiva", 6.8), ("Rotulo_Classe_Economica", 0.0),
                ("Rotulo_Cozinha", sum(X_GALLEY) / 2), ("Rotulo_Banheiros", sum(X_BANHEIROS) / 2),
                ("Rotulo_Compartimento_Animais", sum(X_ANIMAIS) / 2)]:
    novo_vazio(nome, ROTULOS, loc=(x, 0, 3.6 if nome == "Rotulo_Banheiros" else 2.9))

# ---------------------------------------------------------------------------
# Luz e camera so para o .blend (nao vao para o glb)
# ---------------------------------------------------------------------------
sol = bpy.data.objects.new("Sol", bpy.data.lights.new("Sol", "SUN"))
sol.rotation_euler = (math.radians(50), math.radians(10), math.radians(140))
cena.collection.objects.link(sol)
cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
cam.location = (-22, 30, 12)
cam.rotation_euler = (math.radians(70), 0, math.radians(-145))
cena.collection.objects.link(cam)
cena.camera = cam

# ---------------------------------------------------------------------------
# Exportar
# ---------------------------------------------------------------------------
bpy.ops.export_scene.gltf(
    filepath=os.path.abspath(SAIDA_GLB),
    export_format="GLB",
    export_apply=True,
    export_yup=True,
    export_vertex_color="ACTIVE",
    export_all_vertex_colors=False,
    export_image_format="JPEG",
    export_jpeg_quality=88,
    export_lights=False,
    export_cameras=False,
    export_extras=False,
)
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(SAIDA_BLEND), compress=True)
print("OK ->", os.path.abspath(SAIDA_GLB))
