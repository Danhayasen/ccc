// Sons PROVISORIOS sintetizados (mesmos algoritmos de Runtime/Audio/ProceduralClips.cs) e
// tocados pelo AudioRouter do dominio (volume por canal, limite de repeticao e de instancias).

let ctx = null;
const buffers = new Map();
let ruidoSemente = 7;
const N = () => { ruidoSemente = (ruidoSemente * 1103515245 + 12345) & 0x7fffffff; return ruidoSemente / 0x3fffffff - 1; };

function envelope(t, dur, ataque = 0.01) {
  if (t < ataque) return t / ataque;
  return Math.min(1, Math.max(0, 1 - (t - ataque) / Math.max(0.001, dur - ataque)));
}

function criar(dados) {
  const b = ctx.createBuffer(1, dados.length, ctx.sampleRate);
  b.copyToChannel(dados, 0);
  return b;
}

function notas(freqs, dur, vol, sino = false) {
  const R = ctx.sampleRate, porNota = Math.floor(R * dur);
  const d = new Float32Array(porNota * freqs.length);
  freqs.forEach((f, n) => {
    for (let i = 0; i < porNota; i++) {
      const t = i / R;
      const env = envelope(t, dur) * (sino ? Math.exp(-t * 4) * 1.5 : 1);
      d[n * porNota + i] = (Math.sin(2 * Math.PI * f * t) + 0.3 * Math.sin(4 * Math.PI * f * t)) * env * vol * 0.7;
    }
  });
  return criar(d);
}

function zumbido(f, dur) {
  const R = ctx.sampleRate, d = new Float32Array(Math.floor(R * dur));
  for (let i = 0; i < d.length; i++) { const t = i / R; d[i] = Math.sign(Math.sin(2 * Math.PI * f * t)) * envelope(t, dur) * 0.25; }
  return criar(d);
}

function baque(dur, f) {
  const R = ctx.sampleRate, d = new Float32Array(Math.floor(R * dur));
  let grave = 0;
  for (let i = 0; i < d.length; i++) {
    const t = i / R;
    grave += (N() - grave) * 0.08;
    d[i] = (Math.sin(2 * Math.PI * f * t * (1 - t)) * 0.7 + grave * 2) * Math.exp(-t * 18) * 0.8;
  }
  return criar(d);
}

function deslize(dur) {
  const R = ctx.sampleRate, d = new Float32Array(Math.floor(R * dur));
  let fase = 0;
  for (let i = 0; i < d.length; i++) {
    const t = i / R;
    fase += 2 * Math.PI * (900 + (200 - 900) * t / dur) / R;
    d[i] = Math.sin(fase) * envelope(t, dur, 0.02) * 0.4;
  }
  return criar(d);
}

function blabla(dur) {
  const R = ctx.sampleRate, d = new Float32Array(Math.floor(R * dur));
  for (let i = 0; i < d.length; i++) {
    const t = i / R;
    const silaba = Math.abs(Math.sin(2 * Math.PI * 7 * t));
    const f = 220 + 60 * Math.sin(2 * Math.PI * 3 * t);
    d[i] = (Math.sin(2 * Math.PI * f * t) + 0.5 * Math.sin(2 * Math.PI * 2.7 * f * t)) * silaba * envelope(t, dur, 0.03) * 0.35;
  }
  return criar(d);
}

function esfregar(dur) {
  const R = ctx.sampleRate, d = new Float32Array(Math.floor(R * dur));
  let hp = 0, ant = 0;
  for (let i = 0; i < d.length; i++) {
    const t = i / R, n = N();
    hp = 0.9 * (hp + n - ant); ant = n;
    d[i] = hp * (0.5 + 0.5 * Math.sin(2 * Math.PI * 9 * t)) * envelope(t, dur, 0.02) * 0.3;
  }
  return criar(d);
}

function loopMotor(dur) {
  const R = ctx.sampleRate, d = new Float32Array(Math.floor(R * dur));
  let grave = 0;
  for (let i = 0; i < d.length; i++) { const t = i / R; grave += (N() - grave) * 0.02; d[i] = grave * 0.6 + Math.sin(2 * Math.PI * 90 * t) * 0.05; }
  return criar(cruzarPontas(d));
}

function loopRonco(dur) {
  const R = ctx.sampleRate, d = new Float32Array(Math.floor(R * dur));
  let grave = 0;
  for (let i = 0; i < d.length; i++) { const t = i / R; grave += (N() - grave) * 0.01; d[i] = grave * 3 * (0.6 + 0.4 * Math.sin(2 * Math.PI * 2 * t)); }
  return criar(cruzarPontas(d));
}

function loopMusica() {
  const escala = [392, 440, 523, 587, 659, 784];
  const padrao = [0, 2, 4, 2, 1, 3, 5, 3, 0, 2, 4, 5, 4, 2, 1, 0];
  const R = ctx.sampleRate, passo = 0.5, d = new Float32Array(Math.floor(R * passo * padrao.length));
  padrao.forEach((nota, s) => {
    const f = escala[nota], inicio = Math.floor(s * passo * R);
    for (let i = 0; i < passo * R; i++) {
      const t = i / R, env = Math.exp(-t * 3);
      d[inicio + i] += (Math.sin(2 * Math.PI * f * t) + 0.2 * Math.sin(2 * Math.PI * f * 2 * t)) * env * 0.18;
      if (s % 4 === 0) d[inicio + i] += Math.sin(2 * Math.PI * f / 2 * t) * env * 0.12;
    }
  });
  return criar(d);
}

function cruzarPontas(d) {
  const fade = Math.min(Math.floor(ctx.sampleRate / 10), Math.floor(d.length / 4));
  for (let i = 0; i < fade; i++) { const k = i / fade; d[i] = d[i] * k + d[d.length - fade + i] * (1 - k); }
  return d.slice(0, d.length - fade);
}

const SINTESE = {
  pedido: () => notas([880, 1175], 0.18, 0.5),
  nova_tarefa: () => notas([660], 0.2, 0.4),
  tarefa_concluida: () => notas([784, 988, 1319], 0.13, 0.5),
  tarefa_falhou: () => zumbido(140, 0.6),
  alerta_evento: () => notas([523, 392], 0.3, 0.6),
  aviso_cinto: () => notas([1046, 784], 0.45, 0.4, true),
  impacto: () => baque(0.22, 180),
  escorregao: () => deslize(0.55),
  conversa: () => blabla(0.45),
  pegar: () => notas([520], 0.08, 0.35),
  guardar: () => baque(0.2, 120),
  batida: () => baque(0.18, 320),
  esfregar: () => esfregar(0.32),
  vitoria: () => notas([523, 659, 784, 1046], 0.28, 0.5),
  derrota: () => notas([392, 330, 262], 0.4, 0.5),
  loop_motor: () => loopMotor(4),
  loop_turbulencia: () => loopRonco(3),
  loop_musica: () => loopMusica(),
};

function buffer(id) {
  if (!buffers.has(id)) buffers.set(id, SINTESE[id]());
  return buffers.get(id);
}

export class Audio {
  constructor(regras) {
    this.regras = regras;
    this.loops = {};
  }

  /** Navegadores so liberam o som depois de um clique. */
  ligar() {
    if (!ctx) {
      ctx = new (window.AudioContext || window.webkitAudioContext)();
      this.saida = ctx.createGain();
      this.saida.connect(ctx.destination);
      for (const id of ['loop_motor', 'loop_turbulencia', 'loop_musica']) {
        const g = ctx.createGain();
        g.gain.value = 0;
        g.connect(this.saida);
        const src = ctx.createBufferSource();
        src.buffer = buffer(id);
        src.loop = true;
        src.connect(g);
        src.start();
        this.loops[id] = { src, g };
      }
    }
    if (ctx.state === 'suspended') ctx.resume();
  }

  tocar(cue, intensidade = 1) {
    if (!ctx) return;
    const r = this.regras.som(cue, ctx.currentTime, intensidade);
    if (!r || r.volume <= 0) return;
    const src = ctx.createBufferSource();
    src.buffer = buffer(cue);
    src.playbackRate.value = r.pitch;
    const g = ctx.createGain();
    g.gain.value = r.volume;
    src.connect(g).connect(this.saida);
    src.start();
  }

  /** Loops: motor e turbulencia (Ambience) e musica (Music). */
  atualizar(estado) {
    if (!ctx || !this.loops.loop_motor) return;
    const voando = estado?.state === 'InFlight';
    const turb = estado?.turbulence ?? 0;
    const amb = this.regras.volumeCanal('Ambience');
    const mus = this.regras.volumeCanal('Music');
    const t = ctx.currentTime;
    this.loops.loop_motor.g.gain.setTargetAtTime(amb * (voando ? 0.6 : 0.25), t, 0.3);
    this.loops.loop_motor.src.playbackRate.setTargetAtTime(voando ? 1 + 0.05 * turb : 0.85, t, 0.5);
    this.loops.loop_turbulencia.g.gain.setTargetAtTime(amb * Math.min(1, turb), t, 0.2);
    const calmo = !voando || turb <= 0;
    this.loops.loop_musica.g.gain.setTargetAtTime(mus * (calmo ? 0.5 : 0.15), t, 0.8);
  }
}
