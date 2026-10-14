// Robo de teste da versao web: joga uma partida inteira usando as mesmas interacoes do jogador
// (jogador.interagir()). Move a comissaria pelo corredor sem teclado e deixa a fisica resolver colisoes.
window.bot = (() => {
  const j = () => window.SKYCREW;
  const log = [];
  const DT = 1 / 30;
  const sim = (seg) => { for (let t = 0; t < seg; t += DT) j().passo(DT); };
  const estado = () => j().estado;
  const tarefasAtivas = () => estado().tasks.filter(t => t.state === 'Active');
  const pos = () => j().jogador.obj.position;
  const fim = () => ['Results', 'Failed'].includes(estado().state);

  function andarAte(x, z, vel = 4.0) {
    const segs = [];
    if (Math.abs(pos().x) > 0.1 && Math.abs(pos().z - z) > 0.3) segs.push([0, pos().z]);
    if (Math.abs(x) > 0.1 && Math.abs(pos().z - z) > 0.3) segs.push([0, z]);
    segs.push([x, z]);
    for (const [tx, tz] of segs) {
      for (let k = 0; k < 400 && !fim(); k++) {
        const p = pos();
        const dx = tx - p.x, dz = tz - p.z, d = Math.hypot(dx, dz);
        if (d < 0.05) break;
        const passo = Math.min(d, vel * DT);
        p.x += dx / d * passo; p.z += dz / d * passo;
        j().jogador.obj.rotation.y = Math.atan2(dx, dz);
        j().passo(DT);
      }
    }
  }

  function olharPara(x, z) {
    const p = pos();
    j().jogador.obj.rotation.y = Math.atan2(x - p.x, z - p.z);
    j().passo(DT); j().passo(DT); j().passo(DT); j().passo(DT);
  }

  function interagir(rotulo) {
    const jog = j().jogador;
    jog.escolherAlvo();
    const alvo = jog.alvo ? jog.alvo.texto(jog) : 'nenhum';
    jog.interagir();
    j().passo(DT);
    log.push(`${Math.round(estado().flightTime)}s ${rotulo}: alvo="${alvo}" ${jog.checagem ? '(' + jog.checagem + ')' : ''} maos=${jog.maos.tipo}`);
  }

  const fonte = (n) => j().mundo.nivel.fontes[n];
  const pegarDaFonte = (n, rotulo) => {
    const f = fonte(n);
    andarAte(0, f.pos.z);
    olharPara(f.pos.x, f.pos.z);
    if (f.pos.x !== 0) andarAte(Math.sign(f.pos.x) * 0.5, f.pos.z);
    olharPara(f.pos.x, f.pos.z);
    interagir(rotulo);
  };

  function passageiro(id) { return j().mundo.passageiros.find(p => p.id === id); }

  function irAoPassageiro(p) {
    const q = p.obj.position;
    if (p.sentado) { andarAte(0, q.z + 0.05); olharPara(q.x, q.z); }
    else { andarAte(q.x, q.z + (q.z > pos().z ? -0.6 : 0.6)); olharPara(q.x, q.z); }
  }

  function largarSeSegurando() {
    if (j().jogador.maos.segurado) { j().jogador.maos.largar(j().mundo.caixas); j().passo(DT); }
  }

  function guardarMala(item) {
    largarSeSegurando();
    const q = item.obj.position;
    andarAte(Math.max(-0.25, Math.min(0.25, q.x)), q.z + 0.45);
    olharPara(q.x, q.z);
    interagir('pegar mala ' + item.id);
    if (j().jogador.maos.tipo !== 'mala') return;
    // bagageiro livre mais proximo
    const bin = j().mundo.nivel.bins.filter(b => b.disponivel()).sort((a, b) =>
      Math.abs((a.caixa.min.z + a.caixa.max.z) / 2 - pos().z) - Math.abs((b.caixa.min.z + b.caixa.max.z) / 2 - pos().z))[0];
    const bz = (bin.caixa.min.z + bin.caixa.max.z) / 2, bx = (bin.caixa.min.x + bin.caixa.max.x) / 2;
    andarAte(0, bz);
    olharPara(bx, bz);
    interagir('guardar mala');
  }

  function servir(t) {
    const m = t.name.match(/Servir (água|lanche|cobertor) para (.+)/);
    if (!m) return;
    const p = j().mundo.passageiros.find(x => x.nome === m[2]);
    const n = { 'água': 'geladeira', lanche: 'lanches', cobertor: 'cobertores' }[m[1]];
    largarSeSegurando();
    if (n === 'geladeira' && !estado().fridgeOk) { consertar(); }
    pegarDaFonte(n, 'pegar ' + m[1]);
    irAoPassageiro(p);
    interagir('entregar para ' + p.nome);
    largarSeSegurando();
  }

  function consertar() {
    largarSeSegurando();
    for (let i = 0; i < 4 && !estado().fridgeOk; i++) pegarDaFonte('geladeira', 'bater na geladeira');
  }

  function acalmar(t) {
    const nome = t.name.replace('Acalmar ', '');
    const p = j().mundo.passageiros.find(x => x.nome === nome);
    largarSeSegurando();
    irAoPassageiro(p);
    interagir('conversar com ' + nome);
  }

  function recolherLixo() {
    for (const item of [...j().mundo.itens].filter(i => i.tipo === 'lixo' && !i.seguro && !i.travado)) {
      largarSeSegurando();
      const q = item.obj.position;
      andarAte(Math.max(-0.2, Math.min(0.2, q.x)), q.z + 0.4);
      olharPara(q.x, q.z);
      interagir('pegar lixo (' + q.toArray().map(v => v.toFixed(1)) + ')');
      if (j().jogador.maos.tipo !== 'lixo') continue;
      const l = j().mundo.nivel.lixeira.caixa;
      andarAte(-0.45, 3.1);
      olharPara((l.min.x + l.max.x) / 2, (l.min.z + l.max.z) / 2);
      interagir('lixeira');
    }
  }

  function limparMancha(mancha) {
    largarSeSegurando();
    pegarDaFonte('esfregoes', 'pegar esfregão');
    const q = mancha.obj.position;
    andarAte(0, q.z + 0.7);
    olharPara(q.x, q.z);
    for (let i = 0; i < 3; i++) interagir('esfregar');
    largarSeSegurando();
  }

  function devolverCarrinho(c) {
    log.push(`carrinho em z=${c.obj.position.z.toFixed(2)} jogador z=${pos().z.toFixed(2)}`);
    largarSeSegurando();
    log.push(`carrinho em z=${c.obj.position.z.toFixed(2)}`);
    const q = c.obj.position;
    andarAte(0, q.z + 0.85);
    olharPara(q.x, q.z);
    interagir('puxar carrinho');
    // puxa para a frente (cozinha)
    andarAte(0, 4.6, 1.5);
    log.push(`carrinho devolvido=${c.devolvido} z=${c.obj.position.z.toFixed(2)}`);
    if (c.segurando) interagir('soltar carrinho');
  }

  return {
    log,
    iniciar() { j().renderer.setAnimationLoop(null); j().jogar(); sim(0.5); return estado().state; },
    testarTeclado() {
      const z0 = pos().z;
      j().entrada.teclas.add('KeyW'); sim(1.0); j().entrada.teclas.delete('KeyW'); sim(0.3);
      return `W por 1s: z ${z0.toFixed(2)} -> ${pos().z.toFixed(2)}`;
    },
    rodada() {
      // resolve o que estiver pendente, na ordem de urgencia
      if (fim()) return false;
      log.push(`-- rodada t=${Math.round(estado().flightTime)} carrinhos=${j().mundo.carrinhos.size} ativas=${tarefasAtivas().map(t=>t.id).join(',')}`);
      for (const c of [...j().mundo.carrinhos]) if (!c.devolvido && !fim()) devolverCarrinho(c);
      const mala = [...j().mundo.itens].find(i => i.tipo === 'mala' && !i.travado && !i.seguro);
      if (mala) guardarMala(mala);
      for (const t of tarefasAtivas()) {
        if (fim()) break;
        if (t.name.startsWith('Acalmar')) acalmar(t);
      }
      if (!estado().fridgeOk) consertar();
      for (const t of tarefasAtivas()) {
        if (fim()) break;
        if (t.name.startsWith('Servir')) servir(t);
      }
      for (const m of [...j().mundo.manchas]) if (!fim()) limparMancha(m);
      for (const c of [...j().mundo.carrinhos]) if (!c.devolvido && !fim()) devolverCarrinho(c);
      if (!fim()) recolherLixo();
      if (!fim()) { andarAte(0, 1.0); sim(1.0); }
      return !fim();
    },
    resumo() {
      const e = estado();
      return JSON.stringify({ estado: e.state, voo: Math.round(e.flightTime), pontos: e.score, sat: +e.satisfaction.toFixed(2),
        tarefas: e.tasks.map(t => `${t.id}:${t.state}`), resultado: e.results ?? null });
    },
  };
})();
window.botEventos = () => {
  const j = window.SKYCREW, R = j.regras, out = [];
  const sim = (s) => { for (let t = 0; t < s; t += 1 / 30) j.passo(1 / 30); };
  sim(31 + 45); bot.rodada(); // fim do embarque + 45 s de voo
  for (const ev of ['corredor_bloqueado', 'equipamento_quebrado', 'mala_caida', 'derramamento', 'passageiro_assustado']) {
    const r = R.forcarEvento(ev);
    sim(0.5);
    out.push(`${ev}: ${r || 'ok'} | carrinhos=${j.mundo.carrinhos.size} manchas=${j.mundo.manchas.size} geladeira=${j.estado.fridgeOk}`);
    bot.rodada();
    out.push('  -> ' + j.estado.tasks.filter(t => t.state !== 'Completed').map(t => `${t.id}:${t.state}`).join(', '));
  }
  out.push('turbulencia: ' + (R.forcarEvento('turbulencia_leve') || 'ok'));
  sim(3);
  out.push(`  cinto=${j.estado.seatbelt} turb=${j.estado.turbulence} itens=${j.mundo.itens.size}`);
  return out.join('\n');
};
