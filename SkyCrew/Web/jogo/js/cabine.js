// Monta a secao de cabine (Runtime/Aircraft/PrototypeLevelBuilder.cs): modulos do Blender
// para o visual e caixas simples para colisao, independentes entre si.
import * as THREE from 'three';
import { Caixa } from './fisica.js';
import { instanciar } from './modelos.js';
import * as L from './layout.js';

export function construirCabine(mundo) {
  const raiz = new THREE.Group();
  raiz.name = 'Cabine';
  mundo.cena.add(raiz);
  const caixas = mundo.caixas;
  const colisor = (nome, cx, cy, cz, sx, sy, sz) => {
    const c = new Caixa(L.U(cx, cy, cz), new THREE.Vector3(sx, sy, sz), nome);
    caixas.push(c);
    return c;
  };
  const por = (modelo, pos, yawGraus = 0) => {
    const o = instanciar(modelo);
    o.position.copy(pos);
    o.rotation.y = L.yawU(yawGraus);
    raiz.add(o);
    return o;
  };

  // Fuselagem
  const n = Math.ceil((L.FrontZ - L.RearZ) / L.SegmentLength);
  for (let i = 0; i < n; i++) por('Cabine/Segmento', L.U(0, 0, L.RearZ + L.SegmentLength * (i + 0.5)));
  por('Cabine/Parede_Frente', L.U(0, 0, L.FrontZ));
  por('Cabine/Parede_Fundo', L.U(0, 0, L.RearZ));

  // Colisores da casca
  const w = L.HalfWidth, comp = L.FrontZ - L.RearZ, meio = (L.FrontZ + L.RearZ) / 2;
  colisor('Parede_E', -w - 0.05, 1.2, meio, 0.1, 2.6, comp + 0.4);
  colisor('Parede_D', w + 0.05, 1.2, meio, 0.1, 2.6, comp + 0.4);
  colisor('Parede_Frente', 0, 1.2, L.FrontZ + 0.05, w * 2, 2.6, 0.1);
  colisor('Parede_Fundo', 0, 1.2, L.RearZ - 0.05, w * 2, 2.6, 0.1);

  // Poltronas e blocos por fileira (impedem andar por cima das poltronas)
  const largura = L.SeatWidth * 3, bx = L.AisleHalfWidth + largura / 2;
  for (let row = 1; row <= L.Rows; row++) {
    for (let letra = 0; letra < 6; letra++) por('Cabine/Poltrona', L.U(L.seatXU(letra), 0, L.rowZ(row)));
    const z = L.rowZ(row) - 0.08;
    colisor(`Fileira_${row}_E`, -bx, 0.5, z, largura, 1.0, 0.62);
    colisor(`Fileira_${row}_D`, bx, 0.5, z, largura, 1.0, 0.62);
  }

  // Bagageiros (zonas onde a mala e guardada; 3 vagas cada)
  const bins = [];
  for (const lado of [-1, 1]) {
    [L.rowZ(2) + 0.45, L.rowZ(5) + 0.45].forEach((cz, i) => {
      const pos = L.U(lado * 1.55, 1.75, cz);
      const visual = por('Cabine/Bagageiro', pos, lado > 0 ? 180 : 0);
      const caixa = colisor(`Bagageiro_${lado < 0 ? 'E' : 'D'}${i + 1}`, lado * 1.55, 1.97, cz, 0.65, 0.44, 2.6);
      const vagas = [0, 1, 2].map(s => {
        const v = new THREE.Object3D();
        v.position.copy(pos).add(new THREE.Vector3(0, 0.05, (s - 1) * 0.8));
        v.rotation.y = L.yawU(90);
        raiz.add(v);
        return v;
      });
      bins.push(new ZonaGuardar(mundo, caixa, vagas, visual));
    });
  }

  // Cozinha
  const gz = L.GalleyZ;
  por('Cabine/Cozinha', L.U(0, 0, gz));
  for (const lado of [-1, 1]) colisor('Balcao', lado * 1.575, 0.5, gz, 0.65, 1.0, 2.0);
  const geladeira = new Fonte(mundo, 'água', 'agua', L.U(-1.2, 1.05, gz + 0.3), 0x6fa3dc, 'geladeira');
  const lanches = new Fonte(mundo, 'lanche', 'lanche', L.U(1.2, 1.05, gz + 0.5), 0xf5c842);
  const cobertores = new Fonte(mundo, 'cobertor', 'cobertor', L.U(1.2, 1.75, gz - 0.4), 0x6fa3dc);
  for (const f of [geladeira, lanches, cobertores]) raiz.add(f.marcador);
  raiz.add(geladeira.luzDefeito);

  const zonaCarrinho = new Caixa(L.U(0, 0.6, gz - 0.2), new THREE.Vector3(2.2, 1.2, 1.6), 'Zona_Carrinho');

  const lixeiraVisual = por('Props/Lixeira', L.U(1.1, 0, L.GalleyStartZ + 0.35));
  const lixeiraCaixa = colisor('Lixeira', 1.1, 0.35, L.GalleyStartZ + 0.35, 0.4, 0.7, 0.4);
  const lixeira = new Lixeira(mundo, lixeiraCaixa, lixeiraVisual);

  // Banheiro
  const z0 = L.LavatoryStartZ, z1 = L.RearZ, mz = (z0 + z1) / 2, prof = z0 - z1;
  por('Cabine/Banheiro', L.U(0, 0, mz));
  const creme = new THREE.MeshStandardMaterial({ color: 0xefe3c8, roughness: 0.9 });
  const cinza = new THREE.MeshStandardMaterial({ color: 0xa9b0ba, roughness: 0.9 });
  const bloco = (nome, cx, cy, cz, sx, sy, sz, mat) => {
    const m = new THREE.Mesh(new THREE.BoxGeometry(sx, sy, sz), mat);
    m.position.copy(L.U(cx, cy, cz));
    raiz.add(m);
    colisor(nome, cx, cy, cz, sx, sy, sz);
  };
  for (const lado of [-1, 1]) {
    bloco('Parede_Lateral', lado * 0.72, 1.1, mz, 0.06, 2.2, prof, creme);
    bloco('Parede_Porta', lado * 0.58, 1.1, z0, 0.28, 2.2, 0.06, creme);
    bloco('Armario_Fundo', lado * 1.32, 0.9, mz, 1.15, 1.8, prof, cinza);
  }
  const batente = new THREE.Mesh(new THREE.BoxGeometry(0.9, 0.2, 0.06), creme);
  batente.position.copy(L.U(0, 2.15, z0));
  raiz.add(batente);
  const esfregoes = new Fonte(mundo, 'esfregão', 'esfregao', L.U(-0.85, 1.0, z0 - 0.15), 0xf5c842);
  raiz.add(esfregoes.marcador);

  // Luzes (pontuais quentes no teto + ambiente em tres tons)
  const luzes = [];
  for (const z of [4.0, 1.4, -1.2, -3.8]) {
    const luz = new THREE.PointLight(0xfff2d9, 2.4, 7, 1.0);
    luz.position.copy(L.U(0, L.CeilingHeight - 0.35, z));
    raiz.add(luz);
    luzes.push(luz);
  }
  const ambiente = new THREE.HemisphereLight(0xbfcce6, 0x4d5261, 1.9);
  raiz.add(ambiente);

  return {
    raiz, bins, lixeira, zonaCarrinho, luzes,
    fontes: { geladeira, lanches, cobertores, esfregoes },
    interativos: [...bins, lixeira, geladeira, lanches, cobertores, esfregoes],
  };
}

/** Fonte de itens (ItemDispenser): geladeira, gaveta de lanches, armario, esfregao. */
export class Fonte {
  constructor(mundo, nome, tipo, pos, cor, equipamento = null) {
    this.mundo = mundo;
    this.nome = nome;
    this.tipo = tipo;
    this.pos = pos;
    this.equipamento = equipamento;
    this.soltos = 0;
    this.prontoEm = 0;
    this.caixa = new Caixa(pos, new THREE.Vector3(0.4, 0.4, 0.6));
    this.marcador = new THREE.Mesh(new THREE.BoxGeometry(0.12, 0.28, 0.48),
      new THREE.MeshStandardMaterial({ color: cor, emissive: cor, emissiveIntensity: 0.35 }));
    this.marcador.position.copy(pos);
    this.luzDefeito = new THREE.Mesh(new THREE.SphereGeometry(0.06, 12, 8),
      new THREE.MeshStandardMaterial({ color: 0xe0574f, emissive: 0xff2a1a, emissiveIntensity: 3 }));
    this.luzDefeito.position.copy(pos).add(L.U(0.05, 0.3, 0));
    this.luzDefeito.visible = false;
  }

  get quebrada() { return this.equipamento != null && !this.mundo.regras.equipamentoOk(this.equipamento); }
  verbo() { return this.quebrada ? 'Use' : 'Dispense'; }
  texto() { return this.quebrada ? 'Consertar (dar uma batida)' : 'Pegar ' + this.nome; }
  aceito() { return null; }
  disponivel() { return this.quebrada || (this.soltos < 5 && this.mundo.tempo >= this.prontoEm); }
  prioridade() { return 2; }
  ponto() { return this.pos; }
  distancia(olho) { return distanciaCaixa(this.caixa, olho); }

  executar(jogador) {
    const R = this.mundo.regras;
    if (this.quebrada) {
      R.acao('consertar', this.equipamento);
      this.mundo.hud.feedback(R.equipamentoOk(this.equipamento) ? 'Voltou a funcionar!' : 'BAM!');
      return true;
    }
    const item = new (this.mundo.ClasseItem)(this.mundo, this.tipo, this.pos.clone());
    this.soltos++;
    this.prontoEm = this.mundo.tempo + 0.5;
    item.aoSumir.push(() => this.soltos--);
    if (!jogador.maos.pegar(item)) {
      item.sumir();
      return false;
    }
    return true;
  }

  atualizar() {
    this.luzDefeito.visible = this.quebrada && (this.mundo.tempo % 0.6) < 0.35;
  }
}

/** Bagageiro (ItemDropZone): cada vaga guarda uma mala. */
export class ZonaGuardar {
  constructor(mundo, caixa, vagas, visual) {
    this.mundo = mundo;
    this.caixa = caixa;
    this.vagas = vagas;
    this.guardados = vagas.map(() => null);
    this.visual = visual;
  }

  verbo() { return 'PlaceInZone'; }
  texto() { return 'Guardar no bagageiro'; }
  aceito() { return 'mala'; }
  disponivel() { return this.guardados.some(g => g == null); }
  prioridade() { return 1; }
  ponto() { return this.caixa.pontoMaisProximo(this.mundo.jogador.obj.position); }
  distancia(olho) { return distanciaCaixa(this.caixa, olho); }

  executar(jogador) {
    const vaga = this.guardados.findIndex(g => g == null);
    if (vaga < 0) return false;
    const item = jogador.maos.tirar();
    if (!item) return false;
    this.guardados[vaga] = item;
    item.travarEm(this.vagas[vaga]);
    this.mundo.regras.guardado(item.tipo, 'bagageiro', item.id);
    return true;
  }
}

/** Lixeira (DisposalBin). */
export class Lixeira {
  constructor(mundo, caixa, visual) {
    this.mundo = mundo;
    this.caixa = caixa;
    this.visual = visual;
  }

  verbo() { return 'Dispose'; }
  texto() { return 'Jogar no lixo'; }
  aceito() { return 'lixo'; }
  disponivel() { return true; }
  prioridade() { return 2; }
  ponto() { return this.caixa.pontoMaisProximo(this.mundo.jogador.obj.position); }
  distancia(olho) { return distanciaCaixa(this.caixa, olho); }

  executar(jogador) {
    const item = jogador.maos.tirar();
    if (!item) return false;
    this.mundo.regras.descartado(item.tipo, item.id);
    item.sumir();
    return true;
  }
}

/** Distancia horizontal do olho ate o ponto mais proximo da caixa (Interactor.DistanceTo). */
export function distanciaCaixa(caixa, olho) {
  const p = caixa.pontoMaisProximo(olho);
  return Math.hypot(p.x - olho.x, p.z - olho.z);
}
