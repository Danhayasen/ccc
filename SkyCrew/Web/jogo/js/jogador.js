// Jogador: entrada (teclado/mouse), movimento (PlayerMotor.cs), camera em terceira pessoa
// (ThirdPersonCamera.cs) e escolha do alvo de interacao (Interactor.cs).
import * as THREE from 'three';
import { resolverCirculo } from './fisica.js';
import { Maos } from './itens.js';
import { Animador } from './personagem.js';

export class Entrada {
  constructor(alvo) {
    this.teclas = new Set();
    this.pressionadas = new Set();
    this.mouse = { x: 0, y: 0 };
    this.travado = false;
    addEventListener('keydown', e => {
      if (e.repeat) return;
      this.teclas.add(e.code);
      this.pressionadas.add(e.code);
      if (['Space', 'Tab', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(e.code)) e.preventDefault();
    });
    addEventListener('keyup', e => this.teclas.delete(e.code));
    addEventListener('blur', () => this.teclas.clear());
    addEventListener('mousemove', e => {
      if (!this.travado) return;
      this.mouse.x += e.movementX;
      this.mouse.y += e.movementY;
    });
    document.addEventListener('pointerlockchange', () => {
      this.travado = document.pointerLockElement === alvo;
    });
    this.alvo = alvo;
  }

  travar() {
    if (!this.travado) this.alvo.requestPointerLock?.()?.catch?.(() => {});
  }

  soltar() {
    if (document.pointerLockElement) document.exitPointerLock();
  }

  apertou(codigo) { return this.pressionadas.has(codigo); }
  segurando(codigo) { return this.teclas.has(codigo); }

  mover() {
    let x = 0, y = 0;
    if (this.segurando('KeyW') || this.segurando('ArrowUp')) y += 1;
    if (this.segurando('KeyS') || this.segurando('ArrowDown')) y -= 1;
    if (this.segurando('KeyD') || this.segurando('ArrowRight')) x += 1;
    if (this.segurando('KeyA') || this.segurando('ArrowLeft')) x -= 1;
    const m = Math.hypot(x, y);
    return m > 1 ? { x: x / m, y: y / m } : { x, y };
  }

  olhar() {
    const d = { x: this.mouse.x, y: this.mouse.y };
    this.mouse.x = this.mouse.y = 0;
    return d;
  }

  fimDoQuadro() { this.pressionadas.clear(); }
}

const ANDAR = 2.6, CORRER = 4.2, ACEL = 14, DESACEL = 18, GIRO = Math.PI * 4;
const GRAVIDADE = -20, PULO = 0.6, RAIO = 0.25, ALTURA = 1.6, DEGRAU = 0.25;

export class Jogador {
  constructor(mundo, visual) {
    this.mundo = mundo;
    this.obj = new THREE.Group();
    this.obj.name = 'Comissaria';
    mundo.cena.add(this.obj);
    const ancora = new THREE.Object3D();
    ancora.position.set(0, 1.05, 0.38);
    this.obj.add(ancora);
    this.maos = new Maos(this, ancora);
    this.velocidade = new THREE.Vector3();
    this.vy = 0;
    this.noChao = true;
    this.tropeco = 0;
    this.velTropeco = new THREE.Vector3();
    this.multiplicador = 1;
    this.trocarVisual(visual);
    this.camera = new CameraTerceiraPessoa(mundo, this);
    this.alvo = null;
    this.checagem = '';
    this.varredura = 0;
  }

  trocarVisual(visual) {
    if (this.visual) this.visual.removeFromParent();
    this.visual = visual;
    this.obj.add(visual);
    this.anim = new Animador(visual);
  }

  frente() {
    return new THREE.Vector3(Math.sin(this.obj.rotation.y), 0, Math.cos(this.obj.rotation.y));
  }

  get velocidadePlanar() { return Math.hypot(this.velocidade.x, this.velocidade.z); }
  get tropecando() { return this.tropeco > 0; }

  teleportar(pos, yaw) {
    this.obj.position.copy(pos);
    this.obj.rotation.y = yaw;
    this.velocidade.set(0, 0, 0);
    this.vy = 0;
    this.tropeco = 0;
    this.camera.yaw = yaw + Math.PI;
    this.camera.encaixar();
  }

  tropecar(empurrao, segundos) {
    empurrao.y = 0;
    if (empurrao.length() > 3) empurrao.setLength(3);
    this.velTropeco.copy(empurrao);
    this.tropeco = Math.min(1.5, Math.max(0, segundos));
    this.anim.escorregar();
    this.mundo.aoTropecar?.();
  }

  atualizar(dt, entrada, ativo) {
    if (ativo) {
      const o = entrada.olhar();
      this.camera.girar(o.x, o.y);
    } else entrada.olhar();
    const mov = ativo ? entrada.mover() : { x: 0, y: 0 };
    const correr = ativo && entrada.segurando('ShiftLeft');
    const pular = ativo && entrada.apertou('Space');

    // direcao relativa a camera
    const yaw = this.camera.yaw;
    const fx = -Math.sin(yaw), fz = -Math.cos(yaw); // frente da camera
    const rx = Math.cos(yaw), rz = -Math.sin(yaw);  // direita da camera
    const desejado = new THREE.Vector3(fx * mov.y + rx * mov.x, 0, fz * mov.y + rz * mov.x);
    const alvoVel = desejado.clone().multiplyScalar((correr ? CORRER : ANDAR) * Math.min(2, Math.max(0.1, this.multiplicador)));
    if (this.tropeco > 0) {
      this.tropeco -= dt;
      alvoVel.copy(this.velTropeco);
      const v = this.velTropeco.length();
      if (v > 0) this.velTropeco.setLength(Math.max(0, v - 6 * dt));
    }
    const plano = new THREE.Vector3(this.velocidade.x, 0, this.velocidade.z);
    const taxa = alvoVel.lengthSq() > plano.lengthSq() ? ACEL : DESACEL;
    const dif = alvoVel.clone().sub(plano);
    const maxPasso = taxa * dt;
    if (dif.length() > maxPasso) dif.setLength(maxPasso);
    plano.add(dif);
    this.velocidade.x = plano.x;
    this.velocidade.z = plano.z;

    if (desejado.lengthSq() > 0.001 && this.tropeco <= 0) {
      const alvoYaw = Math.atan2(desejado.x, desejado.z);
      let d = alvoYaw - this.obj.rotation.y;
      d = Math.atan2(Math.sin(d), Math.cos(d));
      const g = GIRO * dt;
      this.obj.rotation.y += Math.max(-g, Math.min(g, d));
    }

    if (this.noChao && this.vy < 0) this.vy = 0;
    if (this.noChao && pular && this.tropeco <= 0) this.vy = Math.sqrt(2 * -GRAVIDADE * PULO);
    this.vy += GRAVIDADE * dt;

    const p = this.obj.position;
    p.x += this.velocidade.x * dt;
    p.z += this.velocidade.z * dt;
    p.y += this.vy * dt;
    const estavaNoChao = this.noChao;
    this.noChao = false;
    if (p.y <= 0) { p.y = 0; this.vy = 0; this.noChao = true; }
    resolverCirculo(p, RAIO, p.y + DEGRAU, p.y + ALTURA, this.mundo.caixas);
    // passageiros e carrinho empurram o jogador
    for (const pas of this.mundo.passageiros) {
      const q = pas.obj.position;
      const dx = p.x - q.x, dz = p.z - q.z, d = Math.hypot(dx, dz), min = RAIO + 0.22;
      if (d < min && d > 1e-5 && !pas.sentado) { p.x = q.x + dx / d * min; p.z = q.z + dz / d * min; }
    }
    if (this.noChao && !estavaNoChao) this.anim.amassar(0.3);

    // chuta itens soltos no chao
    for (const item of this.mundo.itens) {
      if (item.seguro || item.travado) continue;
      const q = item.obj.position;
      if (q.y - item.base > p.y + 0.5) continue;
      const dx = q.x - p.x, dz = q.z - p.z, d = Math.hypot(dx, dz), min = RAIO + item.raio;
      if (d < min && d > 1e-5) {
        const v = this.velocidadePlanar;
        item.aplicarImpulso(new THREE.Vector3(dx / d, 0.05, dz / d).multiplyScalar(v * 1.5 * 0.4), null);
        item.obj.position.x = p.x + dx / d * min;
        item.obj.position.z = p.z + dz / d * min;
      }
    }

    // interacao
    this.varredura -= dt;
    if (this.varredura <= 0) { this.varredura = 0.1; this.escolherAlvo(); }
    if (ativo && entrada.apertou('KeyE')) {
      this.interagir();
      this.anim.falar();
    }

    this.anim.velocidade = this.velocidadePlanar;
    this.anim.carregando = this.maos.segurado != null;
    this.anim.atualizar(dt);
    this.camera.atualizar(dt);
  }

  get olho() { return this.obj.position.clone().add(new THREE.Vector3(0, 1, 0)); }

  /** Interactor.Scan: candidatos no alcance, validados pelas regras do dominio. */
  escolherAlvo() {
    const R = this.mundo.regras;
    this.alvo = null;
    this.checagem = '';
    const olho = this.olho;
    const frente = this.frente();
    const cands = [], alvos = [];
    for (const alvo of this.mundo.interativos()) {
      if (alvo === this.maos.segurado) continue;
      const dist = alvo.distancia(olho);
      if (dist > ALCANCE + 0.5) continue;
      const pt = alvo.ponto();
      const to = new THREE.Vector3(pt.x - this.obj.position.x, 0, pt.z - this.obj.position.z);
      const ang = to.lengthSq() < 1e-4 ? 0 : THREE.MathUtils.radToDeg(frente.angleTo(to.normalize()));
      const motivo = R.validar(alvo.verbo(this), dist, ALCANCE, this.maos.tipo, alvo.aceito(this), alvo.disponivel(this));
      cands.push({ distance: dist, angle: ang, priority: alvo.prioridade(this), valid: motivo === '' });
      alvos.push({ alvo, dist, motivo });
    }
    if (cands.length === 0) return;
    const i = R.melhorAlvo(cands);
    if (i < 0) return;
    this.alvo = alvos[i].alvo;
    this.checagem = alvos[i].motivo;
    this.distAlvo = alvos[i].dist;
  }

  interagir() {
    this.escolherAlvo();
    if (!this.alvo) {
      if (this.maos.segurado) this.maos.largar(this.mundo.caixas);
      return;
    }
    const R = this.mundo.regras;
    const alvo = this.alvo;
    const motivo = R.validar(alvo.verbo(this), this.distAlvo, ALCANCE, this.maos.tipo, alvo.aceito(this), alvo.disponivel(this));
    if (motivo) {
      this.mundo.hud.feedback(traduzir(motivo));
      return;
    }
    const verbo = alvo.verbo(this);
    if (alvo.executar(this)) this.mundo.aoInteragir?.(alvo, verbo);
    this.escolherAlvo();
  }
}

export const ALCANCE = 1.8;

/** Mensagens do dominio sao ASCII; aqui ganham acentos para a tela. */
export function traduzir(m) {
  return ({
    'Maos ocupadas': 'Mãos ocupadas', 'Nada nas maos': 'Nada nas mãos', 'Item errado': 'Item errado',
    'Fora de alcance': 'Fora de alcance', 'Indisponivel agora': 'Indisponível agora',
    'A partida nao esta em andamento': 'A viagem não está em andamento',
  })[m] ?? m;
}

class CameraTerceiraPessoa {
  constructor(mundo, alvo) {
    this.mundo = mundo;
    this.alvo = alvo;
    this.cam = mundo.camera;
    this.yaw = 0;
    this.pitch = THREE.MathUtils.degToRad(15);
    this.distancia = 2.6;
    this.atual = 2.6;
    this.tremor = 0;
    this.sensibilidade = 1;
    this.inverterY = false;
  }

  girar(dx, dy) {
    const k = 0.08 * Math.PI / 180 * this.sensibilidade;
    this.yaw -= dx * k;
    this.pitch += (this.inverterY ? -dy : dy) * k;
    this.pitch = Math.max(THREE.MathUtils.degToRad(-35), Math.min(THREE.MathUtils.degToRad(70), this.pitch));
  }

  tremer(q) { this.tremor = Math.min(0.6, this.tremor + q); }

  encaixar() { this.posicionar(1, 0); }

  atualizar(dt) {
    this.posicionar(1 - Math.exp(-18 * dt), dt);
    this.tremor = Math.max(0, this.tremor - dt * 0.8);
  }

  posicionar(mistura, dt) {
    const pivo = this.alvo.obj.position.clone().add(new THREE.Vector3(0, 1.45, 0));
    const tras = new THREE.Vector3(Math.sin(this.yaw) * Math.cos(this.pitch), Math.sin(this.pitch),
      Math.cos(this.yaw) * Math.cos(this.pitch));
    let quer = this.distancia;
    for (const c of this.mundo.caixas) {
      if (!c.ativa) continue;
      const t = c.raio(pivo, tras, this.distancia + 0.18);
      if (t < Infinity) quer = Math.min(quer, Math.max(0.35, t - 0.2));
    }
    // teto
    if (tras.y > 0) quer = Math.min(quer, Math.max(0.35, (2.17 - pivo.y) / tras.y));
    this.atual = quer < this.atual ? quer : this.atual + (quer - this.atual) * mistura;
    const pos = pivo.clone().addScaledVector(tras, this.atual);
    this.cam.position.copy(pos);
    this.cam.lookAt(pivo);
    if (this.tremor > 0) {
      const t = this.mundo.tempo * 25;
      this.cam.position.x += (Math.sin(t * 1.3) * 0.5) * this.tremor * 0.5;
      this.cam.position.y += (Math.sin(t * 1.7 + 1) * 0.5) * this.tremor * 0.5;
    }
  }
}
