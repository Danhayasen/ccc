// Itens carregaveis (Runtime/Physics/CarryableItem.cs + ItemFactory.cs + CarrySocket.cs).
// Fisica propria e simples: gravidade, quique, atrito e impulsos limitados.
import * as THREE from 'three';
import { instanciar } from './modelos.js';
import { CeilingHeight } from './layout.js';

const LEVE = { massa: 0.4, amortecimento: 0.2, velMax: 6, impulsoMax: 1.2 };
const PESADO = { massa: 9, amortecimento: 0.6, velMax: 4, impulsoMax: 25 };

// segurar = deslocamento na mao (Unity, com X espelhado)
export const TIPOS = {
  agua: { nome: 'garrafa de água', modelo: 'Props/Garrafa', ajuste: LEVE, segurar: [0, -0.1, 0] },
  mala: { nome: 'mala', modelo: 'Props/Mala', ajuste: PESADO, segurar: [0, -0.25, 0.1] },
  lixo: { nome: 'lixo', modelo: 'Props/Lixo', ajuste: LEVE, segurar: [0, -0.05, 0] },
  lanche: { nome: 'lanche', modelo: 'Props/Lanche', ajuste: LEVE, segurar: [0, -0.05, 0] },
  cobertor: { nome: 'cobertor', modelo: 'Props/Cobertor', ajuste: LEVE, segurar: [0, -0.08, 0] },
  esfregao: { nome: 'esfregão', modelo: 'Props/Esfregao', ajuste: LEVE, segurar: [-0.15, -0.6, 0.1] },
};

let contador = 0;
const tmp = new THREE.Vector3();

export class Item {
  constructor(mundo, tipo, posicao, rotacaoY = 0, id = null) {
    const def = TIPOS[tipo];
    this.mundo = mundo;
    this.tipo = tipo;
    this.nome = def.nome;
    this.ajuste = def.ajuste;
    this.segurar = new THREE.Vector3(...def.segurar);
    this.id = id ?? `${tipo}_${++contador}`;
    this.obj = new THREE.Group();
    this.obj.name = this.id;
    this.visual = instanciar(def.modelo);
    this.obj.add(this.visual);
    const caixa = new THREE.Box3().setFromObject(this.visual);
    const tam = caixa.getSize(new THREE.Vector3());
    this.base = -caixa.min.y; // distancia da origem ate o fundo
    this.altura = Math.max(0.05, tam.y);
    this.raio = Math.min(0.35, Math.max(0.06, Math.max(tam.x, tam.z) * 0.5));
    this.obj.position.copy(posicao);
    this.obj.rotation.y = rotacaoY;
    this.vel = new THREE.Vector3();
    this.giro = new THREE.Vector3();
    this.seguro = false;
    this.travado = false;
    this.parado = false;
    this.aoSumir = [];
    this.item = this; // interativo
    mundo.cena.add(this.obj);
    mundo.itens.add(this);
  }

  get posicao() { return this.obj.getWorldPosition(new THREE.Vector3()); }

  // --- Interativo (pegar) ---
  verbo() { return 'PickUp'; }
  texto() { return 'Pegar ' + this.nome; }
  aceito() { return null; }
  disponivel() { return !this.seguro && !this.travado; }
  prioridade() { return 1; }
  ponto() { return this.posicao; }
  distancia(olho) {
    const p = this.posicao;
    return Math.hypot(p.x - olho.x, p.z - olho.z);
  }
  executar(jogador) { return jogador.maos.pegar(this); }

  aplicarImpulso(impulso, torque = null) {
    if (this.seguro || this.travado) return;
    tmp.copy(impulso);
    if (tmp.length() > this.ajuste.impulsoMax) tmp.setLength(this.ajuste.impulsoMax);
    this.vel.addScaledVector(tmp, 1 / this.ajuste.massa);
    if (torque) this.giro.addScaledVector(torque, 1 / this.ajuste.massa);
    this.parado = false;
  }

  prender(ancora) {
    this.seguro = true;
    this.vel.set(0, 0, 0);
    this.giro.set(0, 0, 0);
    ancora.add(this.obj);
    this.obj.position.copy(this.segurar);
    this.obj.rotation.set(0, 0, 0);
  }

  soltar(posicao, velocidade) {
    this.seguro = false;
    this.mundo.cena.add(this.obj);
    this.obj.position.copy(posicao);
    this.vel.copy(velocidade ?? tmp.set(0, 0, 0));
    this.parado = false;
  }

  travarEm(vaga) {
    this.seguro = false;
    this.travado = true;
    vaga.add(this.obj);
    this.obj.position.set(0, 0, 0);
    this.obj.rotation.set(0, 0, 0);
  }

  sumir() {
    if (this.sumiu) return;
    this.sumiu = true;
    this.obj.removeFromParent();
    this.mundo.itens.delete(this);
    for (const f of this.aoSumir) f(this);
  }

  atualizar(dt, caixas) {
    if (this.seguro || this.travado || this.parado) return;
    const p = this.obj.position;
    const a = this.ajuste;
    this.vel.y -= 9.81 * dt;
    const amort = Math.max(0, 1 - a.amortecimento * dt);
    this.vel.x *= amort; this.vel.z *= amort;
    if (this.vel.length() > a.velMax) this.vel.setLength(a.velMax);
    const fundoAntes = p.y - this.base;
    p.addScaledVector(this.vel, dt);

    let apoiado = false;
    // piso
    if (p.y - this.base <= 0) {
      p.y = this.base;
      if (this.vel.y < -1) this.mundo.aoImpacto?.(p, -this.vel.y * a.massa);
      this.vel.y = this.vel.y < -0.6 ? -this.vel.y * 0.25 : 0;
      apoiado = true;
    }
    // teto
    if (p.y - this.base + this.altura > CeilingHeight) {
      p.y = CeilingHeight - this.altura + this.base;
      if (this.vel.y > 0) this.vel.y = 0;
    }
    // caixas: pousa em cima ou e empurrado para o lado
    for (const c of caixas) {
      if (!c.ativa) continue;
      const fundo = p.y - this.base, topo = fundo + this.altura;
      if (topo <= c.min.y || fundo >= c.max.y) continue;
      const px = Math.min(Math.max(p.x, c.min.x), c.max.x);
      const pz = Math.min(Math.max(p.z, c.min.z), c.max.z);
      const dx = p.x - px, dz = p.z - pz;
      if (dx * dx + dz * dz >= this.raio * this.raio) continue;
      if (fundoAntes >= c.max.y - 0.03) {
        p.y = c.max.y + this.base;
        if (this.vel.y < -1) this.mundo.aoImpacto?.(p, -this.vel.y * a.massa);
        this.vel.y = this.vel.y < -0.6 ? -this.vel.y * 0.25 : 0;
        apoiado = true;
      } else {
        const antesX = p.x, antesZ = p.z;
        const pos = { x: p.x, z: p.z };
        const lista = [c];
        import_resolver(pos, this.raio, lista);
        p.x = pos.x; p.z = pos.z;
        const nx = p.x - antesX, nz = p.z - antesZ;
        const n = Math.hypot(nx, nz);
        if (n > 1e-6) {
          const vn = (this.vel.x * nx + this.vel.z * nz) / n;
          if (vn < 0) {
            this.vel.x -= 1.4 * vn * nx / n;
            this.vel.z -= 1.4 * vn * nz / n;
          }
        }
      }
    }

    // giro (comedia) no ar; no chao volta a ficar de pe
    if (apoiado) {
      this.vel.x *= Math.max(0, 1 - 6 * dt);
      this.vel.z *= Math.max(0, 1 - 6 * dt);
      this.giro.multiplyScalar(Math.max(0, 1 - 8 * dt));
      this.obj.rotation.x *= Math.max(0, 1 - 10 * dt);
      this.obj.rotation.z *= Math.max(0, 1 - 10 * dt);
      if (this.vel.lengthSq() < 0.0004 && Math.abs(this.obj.rotation.x) + Math.abs(this.obj.rotation.z) < 0.01) {
        this.vel.set(0, 0, 0);
        this.parado = true;
      }
    } else {
      this.obj.rotation.x += this.giro.x * dt;
      this.obj.rotation.y += this.giro.y * dt;
      this.obj.rotation.z += this.giro.z * dt;
    }
    if (p.y < -3) { p.set(0, 0.5, 3.6); this.vel.set(0, 0, 0); }
  }

  acordar() { this.parado = false; }
}

// empurra o circulo para fora de uma caixa (sem depender da faixa de altura, ja testada)
function import_resolver(pos, raio, lista) {
  for (const c of lista) {
    const px = Math.min(Math.max(pos.x, c.min.x), c.max.x);
    const pz = Math.min(Math.max(pos.z, c.min.z), c.max.z);
    const dx = pos.x - px, dz = pos.z - pz;
    const d2 = dx * dx + dz * dz;
    if (d2 > 1e-10) {
      const d = Math.sqrt(d2);
      pos.x = px + dx / d * raio;
      pos.z = pz + dz / d * raio;
    } else {
      const op = [[c.min.x - raio - pos.x, 'x'], [c.max.x + raio - pos.x, 'x'], [c.min.z - raio - pos.z, 'z'], [c.max.z + raio - pos.z, 'z']];
      op.sort((a, b) => Math.abs(a[0]) - Math.abs(b[0]));
      pos[op[0][1]] += op[0][0];
    }
  }
}

/** Maos do jogador (CarrySocket). */
export class Maos {
  constructor(jogador, ancora) {
    this.jogador = jogador;
    this.ancora = ancora;
    this.segurado = null;
  }

  get tipo() { return this.segurado ? this.segurado.tipo : null; }

  pegar(item) {
    if (this.segurado || !item || item.seguro || item.travado) return false;
    this.segurado = item;
    item.prender(this.ancora);
    this._aoSumir = () => { if (this.segurado === item) this.segurado = null; };
    item.aoSumir.push(this._aoSumir);
    return true;
  }

  tirar() {
    const item = this.segurado;
    if (!item) return null;
    item.aoSumir = item.aoSumir.filter(f => f !== this._aoSumir);
    this.segurado = null;
    return item;
  }

  largar(caixas) {
    const item = this.tirar();
    if (!item) return;
    const j = this.jogador;
    const origem = j.obj.position.clone().add(new THREE.Vector3(0, 0.9, 0));
    const frente = j.frente();
    let dist = 0.6;
    for (const c of caixas) {
      if (!c.ativa) continue;
      const t = c.raio(origem, frente, 0.6 + 0.12);
      if (t < Infinity) dist = Math.min(dist, Math.max(0, t - 0.15));
    }
    item.soltar(origem.addScaledVector(frente, dist), j.velocidade.clone());
  }
}
