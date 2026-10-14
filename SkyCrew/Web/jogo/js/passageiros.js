// "Corpo" dos passageiros (Runtime/PassengerAI/PassengerController.cs + PassengerInteractable.cs).
// O cerebro (PassengerBrain) roda nas regras; aqui so executamos os comandos: andar pelo
// corredor, entrar/sair da poltrona, mostrar pedidos e reagir. O corredor e uma linha reta,
// entao a "navegacao" e andar em Z por x = 0, respeitando o carrinho e o limite de pessoas.
import * as THREE from 'three';
import { instanciar } from './modelos.js';
import { Animador } from './personagem.js';
import * as L from './layout.js';

const DESLIZE = 0.6, TRAVADO = 3;
const VARIANTES = ['Personagens/Passageiro_1', 'Personagens/Passageiro_2', 'Personagens/Passageiro_3', 'Personagens/Passageiro_4'];

/** AisleTrafficCoordinator: no maximo N passageiros andando no corredor ao mesmo tempo. */
export class TrafegoCorredor {
  constructor(max = 2) { this.max = max; this.andando = new Set(); }
  pedir(p) { if (this.andando.has(p)) return true; if (this.andando.size >= this.max) return false; this.andando.add(p); return true; }
  liberar(p) { this.andando.delete(p); }
  limpar() { this.andando.clear(); }
}

export class Passageiro {
  constructor(mundo, dados, variante) {
    this.mundo = mundo;
    this.id = dados.id;
    this.nome = dados.name;
    this.assento = dados.seat;
    this.velocidade = dados.walkSpeed;
    const { row, letter } = L.parseSeat(dados.seat);
    this.posAssento = L.seatPosition(row, letter);
    this.pontoCorredor = L.aislePointForRow(row);
    this.obj = new THREE.Group();
    this.obj.name = 'Passageiro_' + this.nome;
    this.visual = instanciar(VARIANTES[variante % VARIANTES.length]);
    this.obj.add(this.visual);
    this.anim = new Animador(this.visual);
    mundo.cena.add(this.obj);
    this.modo = 'EmPe';
    this.destino = 'None';
    this.pedido = 'None';
    this.reagindo = false;
    this.obj.position.copy(dados.startsSeated ? this.posAssento : L.BoardingDoor);
    this.obj.rotation.y = dados.startsSeated ? 0 : Math.PI;
    this.velAtual = 0;
  }

  get sentado() { return this.modo === 'Sentado'; }

  comando(c) {
    const R = this.mundo.regras;
    switch (c.kind) {
      case 'WalkTo':
        this.destino = c.destination;
        if (this.modo === 'Sentado') this.deslizar(this.posAssento, this.pontoCorredor, 'Saindo');
        else this.modo = 'Esperando';
        break;
      case 'StopMoving':
        this.mundo.trafego.liberar(this);
        if (this.modo !== 'Sentado') this.modo = 'EmPe';
        break;
      case 'SnapToSeat':
        this.mundo.trafego.liberar(this);
        this.obj.position.copy(this.posAssento);
        this.obj.rotation.y = 0;
        this.modo = 'Sentado';
        break;
      case 'ShowRequest':
        this.pedido = c.service;
        break;
      case 'ClearRequest':
        this.pedido = 'None';
        this.anim.falar();
        break;
      case 'React':
        this.reagindo = true;
        this.anim.amassar(0.2);
        break;
      case 'StopReacting':
        this.reagindo = false;
        break;
    }
    void R;
  }

  deslizar(de, para, modo) {
    this.de = de.clone();
    this.para = para.clone();
    this.t = 0;
    this.modo = modo;
  }

  atualizar(dt) {
    const R = this.mundo.regras;
    this.velAtual = 0;
    switch (this.modo) {
      case 'Saindo':
      case 'Entrando': {
        this.t += dt / DESLIZE;
        const k = THREE.MathUtils.smoothstep(Math.min(1, this.t), 0, 1);
        this.obj.position.lerpVectors(this.de, this.para, k);
        if (this.t >= 1) {
          if (this.modo === 'Saindo') this.modo = 'Esperando';
          else {
            this.modo = 'Sentado';
            this.obj.rotation.y = 0;
            R.chegou(this.id);
          }
        }
        break;
      }
      case 'Esperando':
        if (this.mundo.trafego.pedir(this)) this.comecarAndar();
        break;
      case 'Andando':
        this.andar(dt);
        break;
    }
    this.anim.velocidade = this.velAtual;
    this.anim.sentado = this.modo === 'Sentado';
    this.anim.reagindo = this.reagindo;
    this.anim.atualizar(dt);
  }

  comecarAndar() {
    this.alvo = this.destino === 'Lavatory' ? L.LavatoryPoint.clone() : this.pontoCorredor.clone();
    this.modo = 'Andando';
    this.travado = 0;
    this.ultimaPos = this.obj.position.clone();
  }

  andar(dt) {
    const p = this.obj.position;
    // primeiro volta ao eixo do corredor, depois anda em Z
    const alvo = Math.abs(p.x) > 0.05 ? new THREE.Vector3(0, 0, p.z) : this.alvo;
    const dir = alvo.clone().sub(p);
    dir.y = 0;
    const dist = dir.length();
    if (dist <= 0.15 && alvo === this.alvo) {
      this.mundo.trafego.liberar(this);
      if (this.destino === 'Seat') this.deslizar(p, this.posAssento, 'Entrando');
      else { this.modo = 'EmPe'; this.mundo.regras.chegou(this.id); }
      return;
    }
    dir.normalize();
    let passo = Math.min(dist, this.velocidade * dt);
    // o carrinho fora do lugar bloqueia o corredor (NavMeshObstacle com "carving" na Unity)
    for (const carrinho of this.mundo.carrinhos) {
      if (carrinho.devolvido) continue;
      const c = carrinho.obj.position;
      const frenteZ = (c.z - p.z) * Math.sign(dir.z || 1);
      if (Math.abs(c.x - p.x) < 0.55 && frenteZ > 0 && frenteZ < 0.75) passo = 0;
    }
    // nao atravessa outro passageiro de pe no corredor
    for (const outro of this.mundo.passageiros) {
      if (outro === this || outro.sentado || outro.modo === 'Saindo' || outro.modo === 'Entrando') continue;
      const q = outro.obj.position;
      const frente = (q.z - p.z) * Math.sign(dir.z || 1);
      if (Math.abs(q.x - p.x) < 0.3 && frente > 0 && frente < 0.5) passo = Math.min(passo, 0);
    }
    p.addScaledVector(dir, passo);
    this.velAtual = passo / Math.max(dt, 1e-4);
    if (passo > 0) this.obj.rotation.y = Math.atan2(dir.x, dir.z);

    this.travado += dt;
    if (this.travado >= TRAVADO) {
      const moveu = p.distanceToSquared(this.ultimaPos) > 0.05 * 0.05;
      this.travado = 0;
      this.ultimaPos.copy(p);
      if (!moveu) {
        this.mundo.trafego.liberar(this);
        this.modo = 'EmPe';
        this.mundo.regras.falhouRota(this.id);
      }
    }
  }

  // --- Interativo: entregar (com item) ou conversar (maos vazias) ---
  verbo(j) { return j.maos.segurado ? 'Deliver' : 'Talk'; }
  texto(j) {
    if (j.maos.segurado) return `Entregar ${j.maos.segurado.nome} para ${this.nome}`;
    return this.reagindo ? `Acalmar ${this.nome}` : `Conversar com ${this.nome}`;
  }
  aceito() {
    return ({ Water: 'agua', Snack: 'lanche', Blanket: 'cobertor' })[this.estado?.pending] ?? '__nada__';
  }
  disponivel(j) {
    if (!this.estado || this.estado.state === 'Disabled') return false;
    if (j.maos.segurado) return this.mundo.regras.podeEntregar(this.id, j.maos.tipo);
    return true;
  }
  prioridade(j) { return this.mundo.regras.prioridadePassageiro(this.reagindo, !!j.maos.segurado && this.disponivel(j)); }
  ponto() { return this.obj.position.clone().add(new THREE.Vector3(0, 0.5, 0)); }
  distancia(olho) { const p = this.obj.position; return Math.max(0, Math.hypot(p.x - olho.x, p.z - olho.z) - 0.24); }

  executar(j) {
    const R = this.mundo.regras;
    const hud = this.mundo.hud;
    if (j.maos.segurado) {
      const item = j.maos.tirar();
      if (R.entregar(this.id, item.tipo)) {
        item.sumir();
        this.anim.falar();
        hud.feedback(`${this.nome}: Obrigado!`);
        return true;
      }
      j.maos.pegar(item);
      hud.feedback(`${this.nome} não quer isso`);
      return false;
    }
    if (R.conversar(this.id)) {
      this.anim.falar();
      hud.feedback(`Você conversou com ${this.nome}`);
      return true;
    }
    return false;
  }

  remover() {
    this.mundo.trafego.liberar(this);
    this.obj.removeFromParent();
  }
}
