// Animacao dos personagens: mesmo comportamento do Animator gerado pelo menu "SkyCrew > 5"
// (Editor/SkyCrewAnimatorBuilder.cs): blend de locomocao Parado/Andar/Correr (0 / 1,4 / 4 m/s),
// carregando, sentado, panico (em pe ou sentado) e os disparos Falar e Escorregar.
import * as THREE from 'three';
import { clipes, instanciar } from './modelos.js';

const LOOP = new Set(['Parado', 'Andar', 'Correr', 'Carregar', 'CarregarParado', 'Sentado', 'Panico', 'PanicoSentado']);

export class Animador {
  constructor(visual) {
    this.visual = visual;
    this.escalaBase = visual.scale.clone();
    this.mixer = new THREE.AnimationMixer(visual);
    this.acoes = {};
    for (const clip of clipes) {
      const a = this.mixer.clipAction(clip);
      a.setLoop(LOOP.has(clip.name) ? THREE.LoopRepeat : THREE.LoopOnce, Infinity);
      a.clampWhenFinished = true;
      a.enabled = true;
      a.setEffectiveWeight(0);
      a.play();
      this.acoes[clip.name] = a;
    }
    this.pesos = Object.fromEntries(Object.keys(this.acoes).map(n => [n, 0]));
    this.velocidade = 0;
    this.velSuave = 0;
    this.sentado = false;
    this.carregando = false;
    this.reagindo = false;
    this.disparo = null; // { nome, t }
    this.amasso = 0;
  }

  falar() { if (!this.sentado && !this.reagindo) this._disparar('Falar'); }
  escorregar() { this._disparar('Escorregar'); this.amassar(0.2); }
  amassar(q = 0.25) { this.amasso = Math.max(this.amasso, q); }

  _disparar(nome) {
    const a = this.acoes[nome];
    if (!a) return;
    a.reset();
    a.play();
    this.disparo = { nome, duracao: a.getClip().duration, t: 0 };
  }

  atualizar(dt) {
    // suavizacao do parametro de velocidade (SetFloat com damping 0,1 s)
    this.velSuave += (this.velocidade - this.velSuave) * Math.min(1, dt / 0.1);
    const s = Math.max(0, this.velSuave);
    const alvo = Object.fromEntries(Object.keys(this.acoes).map(n => [n, 0]));
    let transicao = 0.25;

    if (this.disparo) {
      this.disparo.t += dt;
      if (this.disparo.t >= this.disparo.duracao * 0.9) this.disparo = null;
    }

    if (this.disparo) {
      alvo[this.disparo.nome] = 1;
      transicao = this.disparo.nome === 'Escorregar' ? 0.05 : 0.1;
    } else if (this.reagindo) {
      alvo[this.sentado ? 'PanicoSentado' : 'Panico'] = 1;
      transicao = 0.15;
    } else if (this.sentado) {
      alvo.Sentado = 1;
      transicao = 0.3;
    } else if (this.carregando) {
      const k = Math.min(1, s / 1.4);
      alvo.CarregarParado = 1 - k;
      alvo.Carregar = k;
      transicao = 0.15;
    } else if (s <= 1.4) {
      const k = s / 1.4;
      alvo.Parado = 1 - k;
      alvo.Andar = k;
    } else {
      const k = Math.min(1, (s - 1.4) / 2.6);
      alvo.Andar = 1 - k;
      alvo.Correr = k;
    }

    // Andar e Correr com a mesma fase (como num blend tree)
    const andar = this.acoes.Andar, correr = this.acoes.Correr;
    if (andar && correr) {
      const fase = andar.time / andar.getClip().duration;
      correr.time = fase * correr.getClip().duration;
    }

    const passo = dt / Math.max(0.01, transicao);
    for (const nome in this.acoes) {
      const atual = this.pesos[nome];
      const meta = alvo[nome];
      const novo = atual < meta ? Math.min(meta, atual + passo) : Math.max(meta, atual - passo);
      this.pesos[nome] = novo;
      this.acoes[nome].setEffectiveWeight(novo);
    }
    this.mixer.update(dt);

    this.amasso = Math.max(0, this.amasso - dt * 1.5);
    const k = 1 - this.amasso;
    this.visual.scale.set(this.escalaBase.x / Math.sqrt(k), this.escalaBase.y * k, this.escalaBase.z / Math.sqrt(k));
  }
}

const UNIFORMES = {
  uniforme_vermelho: 'Personagens/Comissaria_Vermelho',
  uniforme_verde: 'Personagens/Comissaria_Verde',
};
const ACESSORIOS = {
  lenco_amarelo: { modelo: 'Acessorios/Lenco_Amarelo', osso: 'Chest' },
  quepe_dourado: { modelo: 'Acessorios/Quepe_Dourado', osso: 'Head' },
};

/** Visual da comissaria com uniforme e acessorio (OutfitApplier.cs). */
export function vestir(uniformeId, acessorioId) {
  const visual = instanciar(UNIFORMES[uniformeId] ?? 'Personagens/Comissaria');
  const def = ACESSORIOS[acessorioId];
  if (def) {
    // Acessorios sao modelados no espaco da raiz do personagem: alinha na pose de repouso e
    // prende ao osso mantendo a posicao no mundo.
    const acessorio = instanciar(def.modelo);
    visual.add(acessorio);
    visual.updateMatrixWorld(true);
    const osso = visual.getObjectByName(def.osso);
    if (osso) osso.attach(acessorio);
  }
  return visual;
}
