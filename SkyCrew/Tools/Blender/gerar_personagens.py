"""
SkyCrew - personagens com ESQUELETO e ANIMACOES (Fase 3).

- Esqueleto com nomes no padrao Humanoid da Unity (Hips, Spine, Chest, Neck, Head,
  LeftUpperLeg, LeftLowerLeg, LeftFoot, LeftUpperArm, LeftLowerArm, LeftHand, ... e Right*).
- Skinning rigido por segmento (cada vertice tem peso 1 em um unico osso): visual de boneco
  articulado, sem deformacao "bala de goma" nas juntas.
- Personagens: Art/Characters/Resources/SkyCrew/Personagens/<Nome>.fbx (malha + esqueleto)
- Animacoes:  Art/Animations/SkyCrew_Animacoes.fbx (so esqueleto + todas as acoes)
- Validacao: pesos, ossos, acoes, loops, direcao do passo e reimportacao dos FBX.

Uso: blender --background --python Tools/Blender/gerar_personagens.py
"""
import importlib.util
import math
import os
import sys

import bpy  # antes de bmesh/mathutils
from mathutils import Matrix, Quaternion, Vector

AQUI = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("gerar_modulos", os.path.join(AQUI, "gerar_modulos.py"))
G = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(G)
U = G.U

PASTA_PERS = G.PASTAS["Personagens"]
PASTA_ANIM = os.path.join(G.ASSETS, "Animations")
FPS = 24

# ---------------------------------------------------------------------------
# Esqueleto (coordenadas da Unity: frente +Z, esquerda do personagem = -X)
# ---------------------------------------------------------------------------
OSSOS = [
    # nome, pai, cabeca(Unity), cauda(Unity)
    ("Hips", None, (0, 0.56, 0), (0, 0.72, 0)),
    ("Spine", "Hips", (0, 0.72, 0), (0, 0.95, 0)),
    ("Chest", "Spine", (0, 0.95, 0), (0, 1.12, 0)),
    ("Neck", "Chest", (0, 1.12, 0), (0, 1.2, 0)),
    ("Head", "Neck", (0, 1.2, 0), (0, 1.68, 0)),
]
for lado, s in (("Left", -1), ("Right", 1)):
    OSSOS += [
        (f"{lado}UpperLeg", "Hips", (s * 0.11, 0.56, 0), (s * 0.11, 0.33, 0)),
        (f"{lado}LowerLeg", f"{lado}UpperLeg", (s * 0.11, 0.33, 0), (s * 0.11, 0.09, 0)),
        (f"{lado}Foot", f"{lado}LowerLeg", (s * 0.11, 0.09, 0), (s * 0.11, 0.03, 0.15)),
        (f"{lado}UpperArm", "Chest", (s * 0.25, 1.06, 0), (s * 0.29, 0.87, 0)),
        (f"{lado}LowerArm", f"{lado}UpperArm", (s * 0.29, 0.87, 0), (s * 0.3, 0.7, 0.02)),
        (f"{lado}Hand", f"{lado}LowerArm", (s * 0.3, 0.7, 0.02), (s * 0.3, 0.6, 0.03)),
    ]
NOMES_OSSOS = [o[0] for o in OSSOS]


def criar_esqueleto():
    dados = bpy.data.armatures.new("Armature")
    arm = bpy.data.objects.new("Armature", dados)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for nome, pai, cabeca, cauda in OSSOS:
        eb = dados.edit_bones.new(nome)
        eb.head = U(*cabeca)
        eb.tail = U(*cauda)
        eb.roll = 0.0
        if pai:
            eb.parent = dados.edit_bones[pai]
            eb.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return arm


# ---------------------------------------------------------------------------
# Malha unica com partes presas a ossos
# ---------------------------------------------------------------------------
class MalhaRigida(G.Malha):
    """Malha da paleta onde cada primitiva fica marcada com o osso que a move."""

    def __init__(self, nome):
        super().__init__(nome)
        self.osso_camada = self.bm.verts.layers.int.new("osso")

    def no_osso(self, osso, _faces=None):
        """Prende ao osso todos os vertices ainda sem dono (a peca recem-criada, inclusive o chanfro).
        Valor guardado = indice + 1; 0 = sem osso."""
        idx = NOMES_OSSOS.index(osso) + 1
        for v in self.bm.verts:
            if v[self.osso_camada] == 0:
                v[self.osso_camada] = idx


def construir_personagem(nome, pele, roupa, detalhe, calca, cabelo, estilo, acessorio=None, sapato="preto"):
    arm = criar_esqueleto()
    m = MalhaRigida(nome + "_Malha")
    b = m.no_osso
    # tronco e quadril
    b("Spine", m.esfera((0, 0.87, 0), (0.25, 0.3, 0.19), roupa, segs=10, aneis=7))
    b("Hips", m.esfera((0, 0.6, 0), (0.2, 0.1, 0.16), calca, segs=10, aneis=5))
    b("Chest", m.caixa((0, 1.12, 0.12), (0.18, 0.1, 0.04), detalhe, rot=(25, 0, 0)))
    b("Neck", m.cilindro((0, 1.17, 0), 0.08, 0.1, pele, lados=8))
    if acessorio == "lenco":
        b("Chest", m.caixa((0.05, 1.05, 0.17), (0.07, 0.16, 0.03), "amarelo", rot=(0, 0, 15)))
        b("Chest", m.caixa((0.14, 0.98, 0.17), (0.08, 0.05, 0.02), "dourado"))
    if acessorio == "bolsa":
        b("Spine", m.caixa((-0.2, 0.75, 0.1), (0.06, 0.18, 0.14), "marrom", chanfro=0.02))
    # cabeca
    H = "Head"
    b(H, m.esfera((0, 1.43, 0), (0.25, 0.25, 0.23), pele, segs=12, aneis=8))
    for s in (-1, 1):
        b(H, m.esfera((s * 0.085, 1.47, 0.19), (0.07, 0.085, 0.05), "olho", segs=8, aneis=6))
        b(H, m.esfera((s * 0.08, 1.46, 0.235), (0.032, 0.042, 0.02), "pupila", segs=6, aneis=4))
        b(H, m.esfera((s * 0.075, 1.48, 0.248), (0.011, 0.011, 0.006), "olho", segs=5, aneis=3))
        b(H, m.caixa((s * 0.09, 1.575, 0.2), (0.09, 0.022, 0.02), cabelo, rot=(0, 0, s * -8)))
        b(H, m.esfera((s * 0.245, 1.42, 0.0), (0.035, 0.06, 0.045), pele, segs=6, aneis=4))
    b(H, m.esfera((0, 1.40, 0.235), (0.035, 0.03, 0.035), pele, segs=6, aneis=4))
    b(H, m.caixa((0, 1.33, 0.21), (0.1, 0.025, 0.02), "boca", chanfro=0.008))
    if estilo == "coque":
        b(H, m.esfera((0, 1.55, -0.03), (0.26, 0.17, 0.24), cabelo, segs=10, aneis=6))
        b(H, m.esfera((0, 1.53, -0.25), (0.1, 0.1, 0.09), cabelo, segs=8, aneis=5))
    elif estilo == "curto":
        b(H, m.esfera((0, 1.57, -0.02), (0.26, 0.15, 0.245), cabelo, segs=10, aneis=6))
    elif estilo == "volumoso":
        b(H, m.esfera((0, 1.53, -0.04), (0.31, 0.24, 0.28), cabelo, segs=10, aneis=6))
    elif estilo == "careca":
        b(H, m.esfera((0, 1.42, -0.12), (0.255, 0.12, 0.16), cabelo, segs=10, aneis=5))
    if acessorio == "lenco":
        b(H, m.cilindro((0, 1.66, 0.0), 0.17, 0.1, "azul_escuro", lados=12, raio2=0.19))
        b(H, m.cilindro((0, 1.62, 0.0), 0.175, 0.025, "dourado", lados=12))
        b(H, m.esfera((0, 1.66, 0.17), (0.04, 0.03, 0.01), "dourado", segs=6, aneis=4))
    if acessorio == "oculos":
        for s in (-1, 1):
            b(H, m.cilindro((s * 0.085, 1.47, 0.245), 0.06, 0.012, "preto", eixo="Z", lados=10))
        b(H, m.caixa((0, 1.48, 0.25), (0.06, 0.015, 0.01), "preto"))
    if acessorio == "bone":
        b(H, m.esfera((0, 1.6, -0.02), (0.26, 0.12, 0.25), "vermelho", segs=10, aneis=5))
        b(H, m.caixa((0, 1.6, 0.25), (0.22, 0.025, 0.16), "vermelho", chanfro=0.01))
    # bracos e pernas em segmentos
    for lado, s in (("Left", -1), ("Right", 1)):
        b(f"{lado}UpperArm", m.esfera((s * 0.27, 0.965, 0), (0.07, 0.11, 0.07), roupa, segs=8, aneis=5))
        b(f"{lado}LowerArm", m.esfera((s * 0.295, 0.785, 0.01), (0.062, 0.1, 0.062), roupa, segs=8, aneis=5))
        b(f"{lado}Hand", m.esfera((s * 0.3, 0.66, 0.025), (0.065, 0.065, 0.065), pele, segs=8, aneis=5))
        b(f"{lado}UpperLeg", m.esfera((s * 0.11, 0.445, 0), (0.09, 0.14, 0.09), calca, segs=8, aneis=5))
        b(f"{lado}LowerLeg", m.esfera((s * 0.11, 0.2, 0), (0.08, 0.13, 0.08), calca, segs=8, aneis=5))
        b(f"{lado}Foot", m.esfera((s * 0.11, 0.05, 0.05), (0.08, 0.055, 0.13), sapato, segs=8, aneis=5))

    # vertex groups a partir da marcacao por vertice
    bm = m.bm
    osso_por_vertice = [v[m.osso_camada] - 1 for v in bm.verts]
    assert min(osso_por_vertice) >= 0, "ha vertices sem osso"
    # criar() recalcula normais e pode reordenar? Nao: bmesh preserva a ordem dos vertices em to_mesh.
    obj = m.criar(None)
    obj.name = nome
    for nome_osso in NOMES_OSSOS:
        obj.vertex_groups.new(name=nome_osso)
    for i, idx in enumerate(osso_por_vertice):
        obj.vertex_groups[NOMES_OSSOS[idx]].add([i], 1.0, "REPLACE")
    obj.parent = arm
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return arm, obj


# ---------------------------------------------------------------------------
# Animacoes (rotacoes descritas em termos do personagem, convertidas para cada osso)
# ---------------------------------------------------------------------------
LATERAL = Vector((1, 0, 0))   # eixo X do Blender (= -X da Unity): giro para frente/tras
FRENTE = Vector((0, 1, 0))    # eixo Y do Blender (= -Z da Unity): giro para os lados
VERTICAL = Vector((0, 0, 1))


def _local(pb, eixo_mundo, graus):
    """Rotacao em torno de um eixo do mundo, expressa no espaco local do osso em repouso."""
    m = pb.bone.matrix_local.to_3x3()
    r = Matrix.Rotation(math.radians(graus), 3, eixo_mundo)
    return (m.inverted() @ r @ m).to_quaternion()


def aponta_para_baixo(pb):
    return pb.bone.tail_local.z < pb.bone.head_local.z


def balanco(pb, graus):
    """Ponta do osso vai para a FRENTE do personagem (+Z da Unity) com graus positivos."""
    sinal = -1.0 if aponta_para_baixo(pb) else 1.0
    return _local(pb, LATERAL, sinal * graus)


def abrir(pb, graus):
    """Ponta do osso se afasta do corpo para o lado com graus positivos."""
    lado_esquerdo = pb.name.startswith("Left")   # esquerda do personagem = +X do Blender
    return _local(pb, FRENTE, (-graus) if lado_esquerdo else graus)


def girar(pb, graus):
    return _local(pb, VERTICAL, graus)


def pose(arm, quadro, **rot):
    """rot: osso -> lista de quaternions (combinadas). Ossos ausentes voltam ao repouso."""
    for pb in arm.pose.bones:
        q = Quaternion()
        for extra in rot.get(pb.name, []):
            q = extra @ q
        pb.rotation_quaternion = q
        pb.keyframe_insert("rotation_quaternion", frame=quadro)
        if pb.name == "Hips":
            pb.keyframe_insert("location", frame=quadro)


def nova_acao(arm, nome):
    acao = bpy.data.actions.new(nome)
    acao.use_fake_user = True
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = acao
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0)
    return acao


def P(arm, nome):
    return arm.pose.bones[nome]


def ciclo_andar(arm, nome, frames, perna, joelho, braco, cotovelo, inclinacao, carregando=False):
    nova_acao(arm, nome)
    for k in range(frames + 1):
        fase = 2 * math.pi * k / frames
        s = math.sin(fase)
        rot = {
            "LeftUpperLeg": [balanco(P(arm, "LeftUpperLeg"), perna * s)],
            "RightUpperLeg": [balanco(P(arm, "RightUpperLeg"), -perna * s)],
            # joelho dobra quando a perna vai para tras
            "LeftLowerLeg": [balanco(P(arm, "LeftLowerLeg"), -joelho * max(0.0, -s))],
            "RightLowerLeg": [balanco(P(arm, "RightLowerLeg"), -joelho * max(0.0, s))],
            "Spine": [balanco(P(arm, "Spine"), inclinacao), girar(P(arm, "Spine"), 6 * s)],
            "Head": [girar(P(arm, "Head"), -4 * s)],
        }
        if carregando:
            for lado in ("Left", "Right"):
                rot[f"{lado}UpperArm"] = [balanco(P(arm, f"{lado}UpperArm"), 60), abrir(P(arm, f"{lado}UpperArm"), -8)]
                rot[f"{lado}LowerArm"] = [balanco(P(arm, f"{lado}LowerArm"), 30)]
        else:
            rot["LeftUpperArm"] = [balanco(P(arm, "LeftUpperArm"), -braco * s), abrir(P(arm, "LeftUpperArm"), 8)]
            rot["RightUpperArm"] = [balanco(P(arm, "RightUpperArm"), braco * s), abrir(P(arm, "RightUpperArm"), 8)]
            rot["LeftLowerArm"] = [balanco(P(arm, "LeftLowerArm"), cotovelo)]
            rot["RightLowerArm"] = [balanco(P(arm, "RightLowerArm"), cotovelo)]
        P(arm, "Hips").location = (0, 0.03 * abs(math.cos(fase)), 0)  # sobe e desce (eixo do osso = cima)
        pose(arm, k + 1, **rot)


def criar_animacoes(arm):
    # Parado: respiracao
    nova_acao(arm, "Parado")
    for k, f in enumerate((1, 13, 25, 37, 49)):
        t = math.sin(2 * math.pi * k / 4)
        P(arm, "Hips").location = (0, 0, 0)
        pose(arm, f,
             Chest=[balanco(P(arm, "Chest"), -2 * t)],
             Head=[balanco(P(arm, "Head"), 2 * t)],
             LeftUpperArm=[abrir(P(arm, "LeftUpperArm"), 6 + 2 * t)],
             RightUpperArm=[abrir(P(arm, "RightUpperArm"), 6 + 2 * t)])

    ciclo_andar(arm, "Andar", 24, perna=28, joelho=35, braco=25, cotovelo=10, inclinacao=3)
    ciclo_andar(arm, "Correr", 16, perna=45, joelho=70, braco=45, cotovelo=60, inclinacao=12)
    ciclo_andar(arm, "Carregar", 24, perna=24, joelho=30, braco=0, cotovelo=0, inclinacao=0, carregando=True)

    nova_acao(arm, "CarregarParado")
    for f in (1, 25):
        P(arm, "Hips").location = (0, 0, 0)
        rot = {}
        for lado in ("Left", "Right"):
            rot[f"{lado}UpperArm"] = [balanco(P(arm, f"{lado}UpperArm"), 60), abrir(P(arm, f"{lado}UpperArm"), -8)]
            rot[f"{lado}LowerArm"] = [balanco(P(arm, f"{lado}LowerArm"), 30)]
        pose(arm, f, **rot)

    # Sentado: quadril desce 0,1 m; coxas na horizontal, canelas na vertical, maos no colo
    nova_acao(arm, "Sentado")
    for k, f in enumerate((1, 25, 49)):
        t = math.sin(math.pi * k)
        P(arm, "Hips").location = (0, -0.1, 0)
        rot = {"Chest": [balanco(P(arm, "Chest"), -2 * t)]}
        for lado in ("Left", "Right"):
            rot[f"{lado}UpperLeg"] = [balanco(P(arm, f"{lado}UpperLeg"), 88)]
            rot[f"{lado}LowerLeg"] = [balanco(P(arm, f"{lado}LowerLeg"), -88)]
            rot[f"{lado}UpperArm"] = [balanco(P(arm, f"{lado}UpperArm"), 25)]
            rot[f"{lado}LowerArm"] = [balanco(P(arm, f"{lado}LowerArm"), 45)]
        pose(arm, f, **rot)

    # Panico: bracos para cima sacudindo, cabeca balancando
    nova_acao(arm, "Panico")
    for k in range(9):
        t = math.sin(2 * math.pi * k / 8)
        P(arm, "Hips").location = (0, 0.03 * abs(t), 0)
        pose(arm, k * 2 + 1,
             LeftUpperArm=[abrir(P(arm, "LeftUpperArm"), 160 + 12 * t)],
             RightUpperArm=[abrir(P(arm, "RightUpperArm"), 160 - 12 * t)],
             LeftLowerArm=[abrir(P(arm, "LeftLowerArm"), 20 * t)],
             RightLowerArm=[abrir(P(arm, "RightLowerArm"), -20 * t)],
             Head=[girar(P(arm, "Head"), 18 * t)],
             LeftUpperLeg=[balanco(P(arm, "LeftUpperLeg"), 10 * max(0.0, t))],
             RightUpperLeg=[balanco(P(arm, "RightUpperLeg"), 10 * max(0.0, -t))])

    # Escorregar (uma vez): pernas para frente, corpo para tras, bracos abertos, volta
    nova_acao(arm, "Escorregar")
    for f, intensidade in ((1, 0.0), (6, 1.0), (16, 1.0), (30, 0.0)):
        i = intensidade
        P(arm, "Hips").location = (0, -0.08 * i, 0)
        pose(arm, f,
             LeftUpperLeg=[balanco(P(arm, "LeftUpperLeg"), 55 * i)],
             RightUpperLeg=[balanco(P(arm, "RightUpperLeg"), 30 * i)],
             Spine=[balanco(P(arm, "Spine"), -30 * i)],
             LeftUpperArm=[abrir(P(arm, "LeftUpperArm"), 85 * i)],
             RightUpperArm=[abrir(P(arm, "RightUpperArm"), 85 * i)],
             Head=[balanco(P(arm, "Head"), 15 * i)])

    # Falar (uma vez): gesto com a mao direita e aceno de cabeca
    nova_acao(arm, "Falar")
    for f, g in ((1, 0.0), (7, 1.0), (13, 0.6), (19, 1.0), (25, 0.0)):
        P(arm, "Hips").location = (0, 0, 0)
        pose(arm, f,
             RightUpperArm=[balanco(P(arm, "RightUpperArm"), 35 * g)],
             RightLowerArm=[balanco(P(arm, "RightLowerArm"), 55 * g)],
             Head=[balanco(P(arm, "Head"), 8 * math.sin(g * math.pi))])

    for acao in bpy.data.actions:
        for curva in acao.fcurves if hasattr(acao, "fcurves") else []:
            for ponto in curva.keyframe_points:
                ponto.interpolation = "BEZIER"


ACOES_EM_LOOP = {"Parado", "Andar", "Correr", "Carregar", "CarregarParado", "Sentado", "Panico"}


# ---------------------------------------------------------------------------
# Exportacao e validacao
# ---------------------------------------------------------------------------
def limpar_cena():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    for a in list(bpy.data.armatures):
        bpy.data.armatures.remove(a)


def exportar(objs, caminho, com_animacoes):
    os.makedirs(os.path.dirname(caminho), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=caminho, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_space_transform=False,  # "Apply Transform" nao e confiavel com esqueletos
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
        mesh_smooth_type="FACE", path_mode="STRIP", embed_textures=False,
        bake_anim=com_animacoes, bake_anim_use_all_actions=com_animacoes,
        bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
    )


def para_unity(v):
    return Vector((-v.x, v.z, -v.y))


def validar_personagem(caminho, altura_esperada):
    limpar_cena()
    bpy.ops.import_scene.fbx(filepath=caminho, axis_forward="-Z", axis_up="Y")
    bpy.context.view_layer.update()
    arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    malhas = [o for o in bpy.data.objects if o.type == "MESH"]
    erros = []
    if len(arms) != 1 or len(malhas) != 1:
        erros.append(f"esperado 1 esqueleto e 1 malha, veio {len(arms)}/{len(malhas)}")
        return erros, 0
    arm, malha = arms[0], malhas[0]
    nomes = {b.name for b in arm.data.bones}
    faltando = set(NOMES_OSSOS) - nomes
    if faltando:
        erros.append("ossos faltando: " + ", ".join(sorted(faltando)))
    grupos = {g.index: g.name for g in malha.vertex_groups}
    sem_peso = 0
    for v in malha.data.vertices:
        pesos = [g.weight for g in v.groups if g.weight > 0]
        if not pesos:
            sem_peso += 1
        elif abs(sum(pesos) - 1.0) > 1e-3 or len(pesos) > 4:
            erros.append("peso invalido num vertice")
            break
    if sem_peso:
        erros.append(f"{sem_peso} vertices sem peso")
    if not any(m.type == "ARMATURE" for m in malha.modifiers):
        erros.append("malha nao esta ligada ao esqueleto")
    pts = [para_unity(malha.matrix_world @ v.co) for v in malha.data.vertices]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    altura = max(ys) - min(ys)
    if abs(altura - altura_esperada) > 0.15 or min(ys) < -0.05:
        erros.append(f"altura {altura:.2f} (esperado {altura_esperada:.2f}), base {min(ys):.2f}")
    # o rosto (pupilas, nariz) fica na frente: mais vertices em +Z na altura da cabeca
    frente = sum(1 for p in pts if p.y > 1.35 and p.z > 0.2)
    tras = sum(1 for p in pts if p.y > 1.35 and p.z < -0.2)
    if frente <= 0:
        erros.append("rosto nao esta virado para +Z")
    malha.data.calc_loop_triangles()
    return erros, len(malha.data.loop_triangles)


def validar_animacoes(arm_original):
    """Confere as acoes no esqueleto original (posicoes das pontas dos ossos em coords Unity)."""
    erros = []
    cena = bpy.context.scene

    def ponta(nome):
        return para_unity(arm_original.matrix_world @ arm_original.pose.bones[nome].tail)

    for acao in bpy.data.actions:
        arm_original.animation_data.action = acao
        inicio, fim = [int(x) for x in acao.frame_range]
        for f in range(inicio, fim + 1):
            cena.frame_set(f)
            for pb in arm_original.pose.bones:
                q = pb.rotation_quaternion
                if any(math.isnan(c) for c in q):
                    erros.append(f"{acao.name}: NaN no quadro {f}")
            for nome in ("LeftFoot", "RightFoot", "LeftHand", "RightHand", "Head"):
                p = ponta(nome)
                if p.y < -0.25 or p.y > 2.4 or abs(p.x) > 1.0 or abs(p.z) > 1.0:
                    erros.append(f"{acao.name}: {nome} fora do lugar no quadro {f}: {tuple(round(c, 2) for c in p)}")
        if acao.name in ACOES_EM_LOOP:
            cena.frame_set(inicio)
            a = [pb.rotation_quaternion.copy() for pb in arm_original.pose.bones]
            cena.frame_set(fim)
            b = [pb.rotation_quaternion.copy() for pb in arm_original.pose.bones]
            if any(qa.rotation_difference(qb).angle > 0.02 for qa, qb in zip(a, b)):
                erros.append(f"{acao.name}: loop nao fecha (primeiro != ultimo quadro)")

    # Andar: no quadro 7 (1/4 do ciclo) o pe esquerdo esta a frente do direito
    arm_original.animation_data.action = bpy.data.actions["Andar"]
    cena.frame_set(7)
    if not ponta("LeftFoot").z > ponta("RightFoot").z + 0.1:
        erros.append("Andar: o pe esquerdo deveria estar a frente no quadro 7")
    # e o braco direito acompanha (oposto da perna)
    if not ponta("RightHand").z > ponta("LeftHand").z:
        erros.append("Andar: o braco direito deveria ir a frente junto com a perna esquerda")
    # Sentado: joelhos a frente do quadril e na altura do quadril
    arm_original.animation_data.action = bpy.data.actions["Sentado"]
    cena.frame_set(1)
    joelho = para_unity(arm_original.matrix_world @ arm_original.pose.bones["LeftUpperLeg"].tail)
    quadril = para_unity(arm_original.matrix_world @ arm_original.pose.bones["LeftUpperLeg"].head)
    if not (joelho.z > quadril.z + 0.15 and abs(joelho.y - quadril.y) < 0.08):
        erros.append(f"Sentado: coxa deveria estar na horizontal para frente (joelho {joelho}, quadril {quadril})")
    # Panico: as duas maos acima dos ombros em TODOS os quadros
    acao = bpy.data.actions["Panico"]
    arm_original.animation_data.action = acao
    for f in range(int(acao.frame_range[0]), int(acao.frame_range[1]) + 1):
        cena.frame_set(f)
        if not (ponta("LeftHand").y > 1.4 and ponta("RightHand").y > 1.4):
            erros.append(f"Panico: maos abaixo dos ombros no quadro {f}")
            break
    arm_original.animation_data.action = None
    return erros


def validar_fbx_animacoes(caminho):
    limpar_cena()
    bpy.ops.import_scene.fbx(filepath=caminho, axis_forward="-Z", axis_up="Y")
    nomes = sorted(a.name for a in bpy.data.actions)
    esperadas = {"Parado", "Andar", "Correr", "Carregar", "CarregarParado", "Sentado", "Panico", "Escorregar", "Falar"}
    achadas = {n.split("|")[-1] for n in nomes}
    faltando = esperadas - achadas
    return ([f"acoes faltando no FBX: {sorted(faltando)}"] if faltando else []), nomes


PERSONAGENS = [
    ("Comissaria", ("pele_1", "azul_escuro", "branco", "azul_escuro", "cabelo_castanho", "coque", "lenco"), 1.76),
] + [(n, (pele, roupa, det, calca, cab, est, acc), 1.72) for n, pele, roupa, det, calca, cab, est, acc in G.VARIANTES]


def principal():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    G.MATERIAL = G.material_paleta(G.gerar_paleta())

    falhas = 0
    caminhos = []
    for nome, args, altura in PERSONAGENS:
        limpar_cena()
        arm, malha = construir_personagem(nome, *args[:6], acessorio=args[6])
        caminho = os.path.join(PASTA_PERS, nome + ".fbx")
        exportar([arm, malha], caminho, com_animacoes=False)
        caminhos.append((nome, caminho, altura))

    # Animacoes: esqueleto puro com todas as acoes
    limpar_cena()
    arm = criar_esqueleto()
    criar_animacoes(arm)
    erros_anim = validar_animacoes(arm)
    caminho_anim = os.path.join(PASTA_ANIM, "SkyCrew_Animacoes.fbx")
    exportar([arm], caminho_anim, com_animacoes=True)

    print("\nVALIDACAO DOS PERSONAGENS (reimportando os FBX):")
    for nome, caminho, altura in caminhos:
        erros, tris = validar_personagem(caminho, altura)
        print(f"  {'OK ' if not erros else 'ERRO'} {nome:14s} {tris:5d} tri  {'; '.join(erros)}")
        falhas += len(erros)
    print("VALIDACAO DAS ANIMACOES:")
    for e in erros_anim:
        print("  ERRO", e)
    falhas += len(erros_anim)
    erros_fbx, nomes = validar_fbx_animacoes(caminho_anim)
    for e in erros_fbx:
        print("  ERRO", e)
    falhas += len(erros_fbx)
    print("  acoes no FBX:", ", ".join(nomes))
    if falhas:
        print(f"{falhas} problema(s)")
        sys.exit(1)
    print("Tudo OK")


if __name__ == "__main__" and sys.argv[-1] != "previa":
    principal()


def previa_poses():
    """Renderiza uma pose de cada animacao lado a lado (Tools/Blender/previas/poses.png)."""
    spec = importlib.util.spec_from_file_location("previa", os.path.join(AQUI, "renderizar_previa.py"))
    R = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(R)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    G.MATERIAL = G.material_paleta(G.gerar_paleta())
    R.preparar_render(1600, 520, amostras=32)
    arm, malha = construir_personagem("Comissaria", *PERSONAGENS[0][1][:6], acessorio=PERSONAGENS[0][1][6])
    criar_animacoes(arm)
    poses = [("Parado", 1), ("Andar", 7), ("Correr", 5), ("Carregar", 7), ("Sentado", 1),
             ("Panico", 3), ("Escorregar", 10), ("Falar", 7)]
    deps = bpy.context.evaluated_depsgraph_get()
    for i, (acao, quadro) in enumerate(poses):
        arm.animation_data.action = bpy.data.actions[acao]
        bpy.context.scene.frame_set(quadro)
        deps = bpy.context.evaluated_depsgraph_get()
        avaliado = malha.evaluated_get(deps)
        copia = bpy.data.objects.new(f"Pose_{acao}", bpy.data.meshes.new_from_object(avaliado))
        bpy.context.scene.collection.objects.link(copia)
        copia.matrix_world = malha.matrix_world.copy()
        copia.location = U((i - 3.5) * 0.95, 0, 0)
        copia.rotation_euler = (0, 0, math.radians(-35))   # 3/4 para ver o passo
    malha.hide_render = True
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0, 0, 0))
    chao = bpy.context.active_object
    mat = bpy.data.materials.new("Chao")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.82, 0.86, 0.95, 1)
    chao.data.materials.append(mat)
    R.luz((1.5, 4, 4), energia=500, tamanho=4)
    R.luz((-3, 3, 2), energia=150, tamanho=3)
    R.camera((0, 1.1, 6.2), (0, 0.8, 0), lente=30)
    R.renderizar("poses.png")


if __name__ == "__main__" and sys.argv[-1] == "previa":
    previa_poses()
