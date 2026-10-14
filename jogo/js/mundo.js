// Diretor do mundo (Runtime/Events/WorldDirector.cs + TurbulenceDirector.cs) e os objetos
// que os eventos criam: mancha (SpillZone.cs) e carrinho de servico (ServiceCart.cs).
import * as THREE from 'three';
import { Caixa } from './fisica.js';
import { Item } from './itens.js';
import { instanciar } from './modelos.js';
import * as L from './layout.js';

const aleatorio = (a, b) => a + Math.random() * (b - a);
const zCorredor = (primeira = 2, ultima = 5) => aleatorio(L.rowZ(ultima), L.rowZ(primeira));

export class Mancha {
  constructor(mundo, id, pos) {
    this.mundo = mundo;
    this.id = id;
    this.obj = new THREE.Group();
    this.obj.position.copy(pos);
    this.visual = instanciar('Props/Mancha');
    this.visual.rotation.y = aleatorio(0, Math.PI * 2);
    this.obj.add(this.visual);
    this.escala = this.visual.scale.clone();
    mundo.cena.add(this.obj);
    this.caixa = new Caixa(pos.clone().add(new THREE.Vector3(0, 0.3, 0)), new THREE.Vector3(0.7, 0.6, 0.9));
    this.esfregadas = 0;
    this.proximoEscorregao = 0;
  }

  verbo() { return 'ApplyItem'; }
  texto() { return 'Esfregar a mancha'; }
  aceito() { return 'esfregao'; }
  disponivel() { return this.esfregadas < 3; }
  prioridade() { return 2; }
  ponto() { return this.obj.position; }
  distancia(olho) { const p = this.caixa.pontoMaisProximo(olho); return Math.hypot(p.x - olho.x, p.z - olho.z); }

  executar() {
    this.esfregadas++;
    this.mundo.regras.acao('limpar', this.id);
    const resta = 1 - this.esfregadas / 3;
    const k = Math.max(0.15, resta);
    this.visual.scale.set(this.escala.x * k, this.escala.y, this.escala.z * k);
    this.mundo.hud.feedback(resta <= 0 ? 'Brilhando!' : 'Esfrega, esfrega...');
    return true;
  }

  atualizar() {
    const j = this.mundo.jogador;
    if (this.mundo.tempo < this.proximoEscorregao || this.esfregadas >= 3) return;
    const p = j.obj.position;
    if (!this.caixa.contem(new THREE.Vector3(p.x, 0.3, p.z), 0.1)) return;
    if (j.velocidadePlanar < 1.6 || j.tropecando) return;
    this.proximoEscorregao = this.mundo.tempo + 2.5;
    j.tropecar(j.frente().multiplyScalar(3), 0.7);
    this.mundo.hud.feedback('Escorregou!');
  }

  remover() { this.obj.removeFromParent(); }
}

export class Carrinho {
  constructor(mundo, id, pos) {
    this.mundo = mundo;
    this.id = id;
    this.obj = instanciar('Props/Carrinho');
    this.obj.position.copy(pos);
    mundo.cena.add(this.obj);
    this.caixa = new Caixa(pos.clone().add(new THREE.Vector3(0, 0.5, 0)), new THREE.Vector3(0.4, 1.0, 0.8), 'Carrinho');
    mundo.caixas.push(this.caixa);
    this.segurando = false;
    this.devolvido = false;
    this.deslocamento = new THREE.Vector3();
    this.vel = new THREE.Vector3();
  }

  verbo() { return 'Use'; }
  texto() { return this.segurando ? 'Soltar o carrinho' : 'Puxar o carrinho'; }
  aceito() { return null; }
  disponivel(j) { return !this.devolvido && !j.maos.segurado; }
  prioridade() { return 2; }
  ponto() { return this.obj.position.clone().add(new THREE.Vector3(0, 0.5, 0)); }
  distancia(olho) { const p = this.caixa.pontoMaisProximo(olho); return Math.hypot(p.x - olho.x, p.z - olho.z); }

  executar(j) {
    if (this.segurando) { this.soltar(); return true; }
    const d = this.obj.position.clone().sub(j.obj.position);
    d.y = 0;
    const dist = Math.min(1.0, Math.max(0.7, d.length()));
    this.deslocamento = d.lengthSq() > 1e-4 ? d.normalize().multiplyScalar(dist) : j.frente().multiplyScalar(dist);
    this.segurando = true;
    j.multiplicador = 0.6;
    return true;
  }

  soltar() {
    if (this.segurando) this.mundo.jogador.multiplicador = 1;
    this.segurando = false;
  }

  atualizar(dt) {
    if (this.devolvido) return;
    const p = this.obj.position;
    const j = this.mundo.jogador;
    let passo = new THREE.Vector3();
    if (this.segurando) {
      const alvo = j.obj.position.clone().add(this.deslocamento);
      passo = alvo.sub(p);
      passo.y = 0;
      const max = 2.5 * dt;
      if (passo.length() > max) passo.setLength(max);
      const lacuna = j.obj.position.clone().add(this.deslocamento).sub(p);
      lacuna.y = 0;
      if (lacuna.length() > 1.2) this.soltar();
    } else {
      // empurrado pelo jogador ao encostar
      const q = j.obj.position;
      const dx = p.x - q.x, dz = p.z - q.z;
      const perto = Math.abs(dx) < 0.2 + 0.25 && Math.abs(dz) < 0.4 + 0.25;
      if (perto && j.velocidadePlanar > 0.1) this.vel.add(new THREE.Vector3(j.velocidade.x, 0, j.velocidade.z).multiplyScalar(dt * 4));
      this.vel.multiplyScalar(Math.max(0, 1 - 3 * dt));
      if (this.vel.length() > 2.5) this.vel.setLength(2.5);
      passo = this.vel.clone().multiplyScalar(dt);
    }
    // nao atravessa poltronas e paredes
    this.caixa.ativa = false;
    const novo = p.clone().add(passo);
    const meia = new THREE.Vector3(0.2, 0.5, 0.4);
    const bate = this.mundo.caixas.some(c => c.ativa && c.max.y > 0.05 &&
      novo.x + meia.x > c.min.x && novo.x - meia.x < c.max.x && novo.z + meia.z > c.min.z && novo.z - meia.z < c.max.z);
    if (!bate) p.copy(novo); else this.vel.set(0, 0, 0);
    this.caixa.ativa = true;
    this.caixa.min.set(p.x - 0.2, p.y, p.z - 0.4);
    this.caixa.max.set(p.x + 0.2, p.y + 1.0, p.z + 0.4);
    if (this.mundo.nivel.zonaCarrinho.contem(new THREE.Vector3(p.x, 0.6, p.z))) this.marcarDevolvido();
  }

  marcarDevolvido() {
    if (this.devolvido) return;
    this.devolvido = true;
    this.soltar();
    this.mundo.regras.guardado('carrinho', 'cozinha', this.id);
  }

  remover() {
    this.soltar();
    this.obj.removeFromParent();
    const i = this.mundo.caixas.indexOf(this.caixa);
    if (i >= 0) this.mundo.caixas.splice(i, 1);
  }
}

/** Materializa os pedidos do cenario e limpa quando o problema termina. */
export class DiretorMundo {
  constructor(mundo) {
    this.mundo = mundo;
    this.criados = new Map();
  }

  pedido(r) {
    const m = this.mundo;
    switch (r.kind) {
      case 'Trash': {
        const perto = r.passenger ? m.passageiros.find(p => p.id === r.passenger)?.obj.position : null;
        for (const id of r.items) {
          const pos = perto
            ? new THREE.Vector3(perto.x * 0.5, 1.0, perto.z + aleatorio(-0.1, 0.1))
            : L.U(aleatorio(-0.15, 0.15), 1.6, zCorredor(1, L.Rows));
          const item = new Item(m, 'lixo', pos, aleatorio(0, Math.PI * 2), id);
          item.aplicarImpulso(new THREE.Vector3(-Math.sign(pos.x + 0.001) * 0.4, 0.3, 0),
            new THREE.Vector3(aleatorio(-4, 4), aleatorio(-4, 4), aleatorio(-4, 4)));
        }
        break;
      }
      case 'Spill': {
        const mancha = new Mancha(m, r.id, L.U(0, 0.005, zCorredor()));
        this.criados.set(r.id, mancha);
        m.manchas.add(mancha);
        break;
      }
      case 'Cart': {
        const carrinho = new Carrinho(m, r.id, L.U(0, 0.02, zCorredor(2, 4)));
        this.criados.set(r.id, carrinho);
        m.carrinhos.add(carrinho);
        break;
      }
      case 'FallenBag': {
        const lado = Math.random() < 0.5 ? -1 : 1;
        const pos = L.U(lado * 1.05, 1.9, zCorredor(1, L.Rows));
        const mala = new Item(m, 'mala', pos, L.yawU(90), r.items[0]);
        // despenca no corredor
        mala.aplicarImpulso(L.U(-lado * 15, 5, 0), new THREE.Vector3(0, 0, lado * 4));
        break;
      }
    }
  }

  limpo(r) {
    const o = this.criados.get(r.id);
    if (!o) return;
    this.criados.delete(r.id);
    o.remover();
    this.mundo.manchas.delete(o);
    this.mundo.carrinhos.delete(o);
  }

  limparTudo() {
    for (const o of this.criados.values()) o.remover();
    this.criados.clear();
    this.mundo.manchas.clear();
    this.mundo.carrinhos.clear();
  }
}

/** Efeitos da turbulencia: tremor de camera, luzes piscando, objetos sacudindo e tropecos. */
export class Turbulencia {
  constructor(mundo) {
    this.mundo = mundo;
    this.base = mundo.nivel.luzes.map(l => l.intensity);
    this.pulso = 0;
    this.ativa = false;
  }

  atualizar(dt, intensidade) {
    const m = this.mundo;
    if (intensidade <= 0) {
      if (this.ativa) {
        this.ativa = false;
        m.nivel.luzes.forEach((l, i) => { l.intensity = this.base[i]; });
      }
      return;
    }
    this.ativa = true;
    m.jogador.camera.tremer(intensidade * dt * 1.5);
    m.nivel.luzes.forEach((l, i) => {
      const f = 0.5 + 0.5 * Math.sin(m.tempo * 9 + i * 3.1) * Math.sin(m.tempo * 5.3 + i);
      l.intensity = this.base[i] * THREE.MathUtils.lerp(0.55, 1.05, f);
    });
    this.pulso -= dt;
    if (this.pulso > 0) return;
    this.pulso = aleatorio(0.5, 1.1);
    const tranco = new THREE.Vector3(aleatorio(-1, 1), aleatorio(0.6, 1.4), aleatorio(-0.5, 0.5)).multiplyScalar(intensidade);
    for (const item of m.itens) {
      const massa = item.ajuste.massa;
      item.aplicarImpulso(tranco.clone().multiplyScalar(massa * 1.2),
        new THREE.Vector3(aleatorio(-1, 1), aleatorio(-1, 1), aleatorio(-1, 1)).multiplyScalar(massa * 0.3 * 8));
    }
    m.jogador.camera.tremer(0.15 * intensidade);
    if (!m.jogador.tropecando && Math.random() < 0.25 * intensidade) {
      m.jogador.tropecar(new THREE.Vector3(tranco.x, 0, tranco.z).multiplyScalar(2.5), 0.45);
    }
  }
}
