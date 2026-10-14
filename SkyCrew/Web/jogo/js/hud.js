// Interface (Runtime/UI/GameHud.cs + MenuScreens.cs) em HTML sobre o canvas.
import * as THREE from 'three';
import { traduzir } from './jogador.js';

const $ = (sel, raiz = document) => raiz.querySelector(sel);
const relogio = (s) => { const t = Math.ceil(Math.max(0, s)); return `${Math.floor(t / 60)}:${String(t % 60).padStart(2, '0')}`; };
const PEDIDO = { Water: 'Água!', Snack: 'Lanche!', Blanket: 'Cobertor!' };
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);

export class Hud {
  constructor(jogo) {
    this.jogo = jogo;
    this.raiz = $('#hud');
    this.avisos = [];
    this.ultimoFeedback = { texto: '', ate: 0 };
    this.baloes = new Map();
    this.tela = null;
  }

  feedback(texto) {
    this.ultimoFeedback = { texto, ate: performance.now() + 1800 };
  }

  aviso(texto, cor = 'info') {
    const el = document.createElement('div');
    el.className = 'aviso ' + cor;
    el.textContent = texto;
    $('#avisos').prepend(el);
    setTimeout(() => el.classList.add('saindo'), 3200);
    setTimeout(() => el.remove(), 3700);
    while ($('#avisos').children.length > 5) $('#avisos').lastChild.remove();
  }

  centro(texto) {
    const el = $('#centro');
    el.textContent = texto ?? '';
    el.hidden = !texto;
  }

  atualizar(estado, jogador, camera) {
    if (!estado) return;
    const emJogo = estado.state === 'Boarding' || estado.state === 'InFlight';
    this.raiz.hidden = !(emJogo || estado.state === 'Results' || estado.state === 'Failed');

    $('#fase').textContent = estado.state === 'Boarding' ? `Embarque  ${relogio(estado.boardingLeft)}`
      : estado.state === 'InFlight' ? `Em voo  ${relogio(estado.flightLeft)}` : 'Pousou';
    const sat = Math.max(0, Math.min(1, estado.satisfaction));
    const barra = $('#satisfacao i');
    barra.style.width = (sat * 100).toFixed(0) + '%';
    barra.style.background = sat > 0.6 ? 'var(--verde)' : sat > 0.35 ? 'var(--amarelo)' : 'var(--vermelho)';
    $('#pontos').textContent = `Pontos: ${estado.score}`;
    $('#cinto').hidden = !estado.seatbelt;
    $('#geladeira').hidden = estado.fridgeOk;
    const voo = estado.flightTotal > 0 ? estado.flightTime / estado.flightTotal : 0;
    $('#rota i').style.width = (Math.min(1, voo) * 100).toFixed(1) + '%';

    // Tarefas: ativas sempre; Tab mostra tambem as encerradas
    const todas = this.jogo.entrada.segurando('Tab');
    const ordem = { Urgent: 0, High: 1, Normal: 2, Low: 3 };
    const lista = estado.tasks
      .filter(t => todas || t.state === 'Active')
      .sort((a, b) => (a.state === 'Active' ? 0 : 1) - (b.state === 'Active' ? 0 : 1) || ordem[a.priority] - ordem[b.priority]);
    $('#tarefas').hidden = !emJogo;
    $('#tarefas').innerHTML = `<h3>Tarefas ${todas ? '' : '<small>(Tab: todas)</small>'}</h3>` + (lista.length ? lista.map(t => {
      const marca = t.state === 'Completed' ? '✔' : t.state === 'Failed' ? '✖' : t.state === 'Cancelled' ? '–' : '•';
      const tempo = t.state === 'Active' && t.timeLimit > 0 ? `<b class="${t.timeLeft < 10 ? 'urgente' : ''}">${Math.ceil(t.timeLeft)}s</b>` : '';
      const extra = t.mandatory ? '<em>obrigatória</em>' : '';
      return `<div class="tarefa ${t.state} p-${t.priority}"><span>${marca} ${esc(t.name)} ${extra}</span>${tempo}
        ${t.state === 'Active' ? `<p>${esc(t.description)}</p>` : ''}</div>`;
    }).join('') : '<div class="vazio">Nada pendente. Fique de olho na cabine!</div>');

    // Acao atual
    const acao = $('#acao');
    if (emJogo && jogador.alvo) {
      const ok = jogador.checagem === '';
      acao.innerHTML = ok ? `<kbd>E</kbd> ${esc(jogador.alvo.texto(jogador))}`
        : `${esc(jogador.alvo.texto(jogador))} — <span>${esc(traduzir(jogador.checagem))}</span>`;
      acao.className = ok ? '' : 'negado';
      acao.hidden = false;
    } else if (emJogo && jogador.maos.segurado) {
      acao.innerHTML = `<kbd>E</kbd> Largar ${esc(jogador.maos.segurado.nome)}`;
      acao.className = '';
      acao.hidden = false;
    } else acao.hidden = true;

    const fb = $('#feedback');
    const agora = performance.now();
    fb.textContent = agora < this.ultimoFeedback.ate ? this.ultimoFeedback.texto : '';
    fb.hidden = agora >= this.ultimoFeedback.ate;

    if (emJogo) this.atualizarBaloes(estado, camera); else this.limparBaloes();
  }

  atualizarBaloes(estado, camera) {
    const caixa = $('#baloes');
    const w = innerWidth, h = innerHeight;
    const v = new THREE.Vector3();
    const vivos = new Set();
    for (const p of this.jogo.mundo.passageiros) {
      const e = p.estado;
      let texto = null, classe = '';
      if (p.reagindo) { texto = '!!'; classe = 'medo'; }
      else if (p.pedido !== 'None') { texto = PEDIDO[p.pedido]; classe = 'pedido'; }
      if (!texto || !e) continue;
      v.copy(p.obj.position); v.y += p.sentado ? 1.55 : 1.95;
      v.project(camera);
      if (v.z > 1) continue;
      let el = this.baloes.get(p);
      if (!el) { el = document.createElement('div'); caixa.appendChild(el); this.baloes.set(p, el); }
      el.className = 'balao ' + classe;
      el.textContent = texto;
      el.style.transform = `translate(${((v.x + 1) / 2 * w).toFixed(0)}px, ${((1 - v.y) / 2 * h).toFixed(0)}px) translate(-50%, -100%)`;
      vivos.add(p);
    }
    for (const [p, el] of this.baloes) if (!vivos.has(p)) { el.remove(); this.baloes.delete(p); }
  }

  limparBaloes() {
    for (const el of this.baloes.values()) el.remove();
    this.baloes.clear();
  }

  // --- Telas ------------------------------------------------------------------------

  mostrar(id) {
    for (const el of document.querySelectorAll('.tela')) el.hidden = el.id !== id;
    this.tela = id;
  }

  esconderTelas() { this.mostrar(null); }

  menuPrincipal(perfil) {
    $('#estatisticas').textContent =
      `★ ${perfil.totalStars} estrelas · ${perfil.flightsWon}/${perfil.flightsPlayed} voos · recorde ${perfil.bestScore}`;
    this.mostrar('menu');
  }

  configuracoes(perfil, voltar) {
    const s = perfil.settings;
    const campos = [
      ['master', 'Volume geral'], ['music', 'Música'], ['ambience', 'Ambiente (motor, turbulência)'],
      ['sfx', 'Efeitos e interface'], ['voice', 'Vozes'],
    ];
    $('#config-campos').innerHTML = campos.map(([k, nome]) =>
      `<label>${nome} <output>${Math.round(s[k] * 100)}%</output><input type="range" min="0" max="1" step="0.05" name="${k}" value="${s[k]}"></label>`).join('')
      + `<label>Sensibilidade do mouse <output>${s.mouse.toFixed(2)}</output><input type="range" min="0.2" max="3" step="0.05" name="mouse" value="${s.mouse}"></label>`
      + `<label class="caixa"><input type="checkbox" name="invertY" ${s.invertY ? 'checked' : ''}> Inverter eixo vertical da câmera</label>`;
    for (const r of document.querySelectorAll('#config-campos input[type=range]')) {
      r.oninput = () => { r.previousElementSibling.textContent = r.name === 'mouse' ? Number(r.value).toFixed(2) : Math.round(r.value * 100) + '%'; };
    }
    $('#config-salvar').onclick = () => {
      const c = {};
      for (const r of document.querySelectorAll('#config-campos input')) c[r.name] = r.type === 'checkbox' ? r.checked : Number(r.value);
      this.jogo.salvarConfiguracoes(c);
      voltar();
    };
    this.mostrar('config');
  }

  guardaRoupa(perfil, voltar) {
    const usando = (id) => id === perfil.uniform || id === perfil.accessory;
    $('#roupas-resumo').textContent = `Você tem ★ ${perfil.totalStars} e ${perfil.flightsWon} vitórias.`;
    $('#roupas-lista').innerHTML = perfil.catalog.map(u => {
      const vestivel = u.category === 'Uniforme' || u.category === 'Acessorio';
      const req = u.unlocked ? '' : ` <small>(precisa ★${u.stars}${u.wins ? ` e ${u.wins} vitórias` : ''})</small>`;
      const botao = vestivel ? `<button data-id="${u.id}" ${!u.unlocked || usando(u.id) ? 'disabled' : ''}>${usando(u.id) ? 'Em uso' : 'Usar'}</button>` : '';
      return `<div class="linha ${u.unlocked ? '' : 'bloqueado'}"><span>${u.unlocked ? '✔' : '🔒'} ${esc(u.name)} <i>${u.category}</i>${req}</span>${botao}</div>`;
    }).join('') + (perfil.accessory ? '<div class="linha"><span>Sem acessório</span><button data-id="">Tirar acessório</button></div>' : '');
    for (const b of document.querySelectorAll('#roupas-lista button')) {
      b.onclick = () => { this.jogo.equipar(b.dataset.id); this.guardaRoupa(this.jogo.regras.perfil(), voltar); };
    }
    $('#roupas-voltar').onclick = voltar;
    this.mostrar('roupas');
  }

  resultado(r, progresso, perfil) {
    $('#res-titulo').textContent = r.victory ? 'Viagem concluída!' : 'Voo complicado...';
    $('#res-estrelas').innerHTML = [0, 1, 2].map(i => `<span class="${i < r.stars ? 'cheia' : ''}">★</span>`).join('');
    const motivo = ({
      'Pouso tranquilo: todas as tarefas obrigatorias concluidas': 'Pouso tranquilo: todas as tarefas obrigatórias concluídas',
      'Passageiros insatisfeitos demais': 'Passageiros insatisfeitos demais',
    })[r.reason] ?? r.reason.replace('obrigatoria nao cumprida', 'obrigatória não cumprida');
    const novos = (progresso?.unlocks ?? []).map(u => `<p class="desbloqueio">Desbloqueado: ${esc(u.name)}</p>`).join('');
    $('#res-detalhes').innerHTML = `
      <p>${esc(motivo)}</p>
      <p>Satisfação média: ${Math.round(r.satisfaction * 100)}% (+${r.satisfactionBonus})</p>
      <p>Tarefas (com bônus de resposta rápida): +${r.rewards} · Penalidades: -${r.penalties}</p>
      <p>Tarefas concluídas: ${r.tasksCompleted} · Falhas: ${r.tasksFailed}</p>
      <p class="total">Pontuação final: ${r.total}</p>
      <p class="pequeno">Estrelas no total: ${perfil.totalStars} · Voos: ${perfil.flightsWon}/${perfil.flightsPlayed} · Recorde: ${perfil.bestScore}${progresso?.newBest ? ' — novo recorde!' : ''}</p>
      ${novos}`;
    this.mostrar('resultado');
  }
}
