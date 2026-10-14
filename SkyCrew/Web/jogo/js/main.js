// Ponto de entrada da versao de navegador (equivale ao GameBootstrap.cs da Unity):
// carrega regras e modelos, monta a cabine, cria jogador e passageiros e conduz a partida.
import * as THREE from 'three';
import { carregarRegras } from './regras.js';
import { carregarTudo } from './modelos.js';
import { construirCabine } from './cabine.js';
import { Item } from './itens.js';
import { Entrada, Jogador } from './jogador.js';
import { vestir } from './personagem.js';
import { Passageiro, TrafegoCorredor } from './passageiros.js';
import { DiretorMundo, Turbulencia } from './mundo.js';
import { Hud } from './hud.js';
import { Audio } from './audio.js';
import * as L from './layout.js';

const CHAVE_SAVE = 'skycrew.save';
const $ = (s) => document.querySelector(s);

function lerSave() { try { return localStorage.getItem(CHAVE_SAVE) ?? ''; } catch { return ''; } }
function gravarSave(texto) { try { localStorage.setItem(CHAVE_SAVE, texto); } catch { /* modo privado: so na memoria */ } }

class Jogo {
  async iniciar() {
    const progresso = (k, texto) => {
      $('#carregando i').style.width = Math.round(k * 100) + '%';
      if (texto) $('#carregando p').textContent = texto;
    };

    const canvas = $('#palco');
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.NeutralToneMapping;
    this.renderer.toneMappingExposure = 1.0;
    this.cena = new THREE.Scene();
    this.cena.background = new THREE.Color(0x1b356e);
    this.camera = new THREE.PerspectiveCamera(65, 1, 0.05, 60);
    this.redimensionar();
    addEventListener('resize', () => this.redimensionar());

    progresso(0.05, 'Carregando as regras do jogo...');
    this.regras = await carregarRegras();
    const status = this.regras.carregarSave(lerSave());
    if (status === 'Corrupted' || status === 'FromNewerVersion') {
      this.avisoSave = 'O progresso salvo estava danificado: começando do zero.';
    }

    progresso(0.15, 'Carregando a cabine e a tripulação...');
    await carregarTudo(k => progresso(0.15 + 0.8 * k));

    this.entrada = new Entrada(canvas);
    this.hud = new Hud(this);
    this.audio = new Audio(this.regras);
    this.mundo = {
      cena: this.cena, camera: this.camera, regras: this.regras, hud: this.hud, tempo: 0,
      caixas: [], itens: new Set(), passageiros: [], manchas: new Set(), carrinhos: new Set(),
      trafego: new TrafegoCorredor(2), ClasseItem: Item,
      interativos: () => [...this.mundo.nivel.interativos, ...this.mundo.itens, ...this.mundo.passageiros,
        ...this.mundo.manchas, ...this.mundo.carrinhos],
      aoImpacto: (pos, forca) => this.audio.tocar('impacto', Math.min(1, forca / 4)),
      aoTropecar: () => this.audio.tocar('escorregao'),
      aoInteragir: (alvo, verbo) => this.somInteracao(verbo),
    };
    this.mundo.nivel = construirCabine(this.mundo);
    const perfil = this.regras.perfil();
    this.jogador = new Jogador(this.mundo, vestir(perfil.uniform, perfil.accessory));
    this.mundo.jogador = this.jogador;
    this.aplicarCamera(perfil.settings);
    this.diretor = new DiretorMundo(this.mundo);
    this.turbulencia = new Turbulencia(this.mundo);
    this.configurarBotoes();

    this.novaPartida(false);
    progresso(1, 'Pronto!');
    $('#carregando').hidden = true;
    this.relogio = new THREE.Clock();
    this.renderer.setAnimationLoop(() => this.quadro());
    window.SKYCREW = this; // ajuda nos testes automatizados
  }

  redimensionar() {
    const w = innerWidth, h = innerHeight;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  aplicarCamera(s) {
    this.jogador.camera.sensibilidade = s.mouse;
    this.jogador.camera.inverterY = s.invertY;
  }

  // --- Fluxo da partida -------------------------------------------------------------

  novaPartida(autoIniciar) {
    const R = this.regras;
    const atual = this.estado;
    if (atual && (atual.state === 'Boarding' || atual.state === 'InFlight')) {
      R.abandonar(); // sair no meio do voo conta como derrota (como na Unity)
      gravarSave(R.textoSave());
    }
    this.ultimoResultado = null;
    this.hud.esconderTelas();
    this.hud.limparBaloes();
    this.pausado = false;
    this.resultadoMostrado = false;

    const segurado = this.jogador.maos.tirar();
    segurado?.sumir();
    for (const item of [...this.mundo.itens]) item.sumir();
    this.diretor.limparTudo();
    for (const p of this.mundo.passageiros) p.remover();
    this.mundo.passageiros.length = 0;
    this.mundo.trafego.limpar();
    for (const bin of this.mundo.nivel.bins) bin.guardados.fill(null);
    this.jogador.multiplicador = 1;

    R.novaPartida((Math.random() * 0x7fffffff) | 0);
    this.estado = R.estado();
    this.estado.passengers.forEach((dados, i) => this.mundo.passageiros.push(new Passageiro(this.mundo, dados, i)));
    // mala esquecida no corredor (a tarefa "guardar a mala" exige esta instancia)
    new Item(this.mundo, 'mala', L.ForgottenSuitcase, L.yawU(90), 'mala_esquecida');
    this.processarEventos();
    this.jogador.teleportar(L.PlayerSpawn, Math.PI);
    this.cinto = false;
    this.autoIniciar = autoIniciar;
    this.esperaPronto = 0;
    if (autoIniciar) this.hud.centro('Prepare-se para o embarque!');
    else this.abrirMenu();
  }

  abrirMenu() {
    this.entrada.soltar();
    this.hud.centro(null);
    this.hud.menuPrincipal(this.regras.perfil());
    if (this.avisoSave) { this.hud.aviso(this.avisoSave, 'falha'); this.avisoSave = null; }
  }

  jogar() {
    this.audio.ligar();
    this.hud.esconderTelas();
    this.entrada.travar();
    this.regras.iniciarEmbarque();
    this.processarEventos();
  }

  get emJogo() { return this.estado && (this.estado.state === 'Boarding' || this.estado.state === 'InFlight'); }

  pausar(sim) {
    if (sim && !this.emJogo) return;
    this.pausado = sim;
    if (sim) { this.entrada.soltar(); this.hud.mostrar('pausa'); }
    else { this.hud.esconderTelas(); this.entrada.travar(); this.audio.ligar(); }
  }

  configurarBotoes() {
    const clique = (sel, f) => { $(sel).onclick = f; };
    clique('#btn-jogar', () => this.jogar());
    clique('#btn-roupas', () => this.hud.guardaRoupa(this.regras.perfil(), () => this.abrirMenu()));
    clique('#btn-config', () => this.hud.configuracoes(this.regras.perfil(), () => this.abrirMenu()));
    clique('#btn-ajuda', () => this.hud.mostrar('ajuda'));
    clique('#ajuda-voltar', () => this.abrirMenu());
    clique('#btn-continuar', () => this.pausar(false));
    clique('#btn-reiniciar', () => { this.novaPartida(true); this.entrada.travar(); this.audio.ligar(); });
    clique('#btn-pausa-config', () => this.hud.configuracoes(this.regras.perfil(), () => this.hud.mostrar('pausa')));
    clique('#btn-pausa-menu', () => this.novaPartida(false));
    clique('#btn-de-novo', () => { this.novaPartida(true); this.entrada.travar(); this.audio.ligar(); });
    clique('#btn-res-menu', () => this.novaPartida(false));
    $('#palco').addEventListener('click', () => {
      this.audio.ligar();
      if (this.emJogo && !this.pausado && !this.hud.tela) this.entrada.travar();
    });
    document.addEventListener('pointerlockchange', () => {
      if (!document.pointerLockElement && this.emJogo && !this.pausado && !this.hud.tela) this.pausar(true);
    });
    addEventListener('keydown', e => {
      if (e.code === 'Escape' && this.pausado && this.hud.tela === 'pausa') this.pausar(false);
      else if (e.code === 'Escape' && this.emJogo && !this.pausado && !document.pointerLockElement) this.pausar(true);
    });
  }

  salvarConfiguracoes(c) {
    this.regras.configurar(c);
    this.aplicarCamera(this.regras.perfil().settings);
    gravarSave(this.regras.textoSave());
  }

  equipar(id) {
    if (!this.regras.equipar(id)) return;
    const p = this.regras.perfil();
    this.jogador.trocarVisual(vestir(p.uniform, p.accessory));
    gravarSave(this.regras.textoSave());
  }

  // --- Eventos das regras --------------------------------------------------------------

  processarEventos() {
    for (const e of this.regras.eventos()) {
      switch (e.type) {
        case 'command': this.mundo.passageiros.find(p => p.id === e.passenger)?.comando(e); break;
        case 'world': this.diretor.pedido(e); break;
        case 'worldCleared': this.diretor.limpo(e); break;
        case 'request': this.audio.tocar('pedido'); break;
        case 'eventStarted': this.audio.tocar('alerta_evento'); break;
        case 'task':
          if (e.state === 'Active') { this.hud.aviso('Nova tarefa: ' + e.name, e.mandatory ? 'importante' : 'info'); this.audio.tocar('nova_tarefa'); }
          else if (e.state === 'Completed') { this.hud.aviso(`+${e.reward}  ${e.name}`, 'sucesso'); this.audio.tocar('tarefa_concluida'); }
          else if (e.state === 'Failed') { this.hud.aviso('Falhou: ' + e.name, 'falha'); this.audio.tocar('tarefa_falhou'); }
          break;
        case 'state':
          if (e.to === 'Boarding') this.hud.centro(null);
          if (e.to === 'InFlight') this.hud.aviso('Decolamos! Atenção aos passageiros.', 'importante');
          break;
        case 'results':
          gravarSave(this.regras.textoSave());
          this.audio.tocar(e.results.victory ? 'vitoria' : 'derrota');
          this.ultimoResultado = e;
          break;
      }
    }
  }

  somInteracao(verbo) {
    const som = { PickUp: 'pegar', Dispense: 'pegar', PlaceInZone: 'guardar', Dispose: 'guardar', Deliver: 'guardar',
      Talk: 'conversa', ApplyItem: 'esfregar', Use: 'batida' }[verbo];
    if (som) this.audio.tocar(som);
  }

  // --- Quadro --------------------------------------------------------------------------

  quadro() {
    this.passo(Math.min(0.05, this.relogio.getDelta()));
    this.renderer.render(this.cena, this.camera);
    this.entrada.fimDoQuadro();
  }

  /** Logica de um quadro (sem desenhar): usado pelo laco e pelos testes automatizados. */
  passo(dt) {
    const R = this.regras;
    const m = this.mundo;
    if (!this.pausado) {
      if (this.estado.state === 'Ready' && this.autoIniciar) {
        this.esperaPronto += dt;
        if (this.esperaPronto >= 2) { this.autoIniciar = false; this.hud.centro(null); R.iniciarEmbarque(); }
      }
      R.tick(dt);
      this.processarEventos();
      this.estado = R.estado();
      for (const p of m.passageiros) p.estado = this.estado.passengers.find(x => x.id === p.id);
      m.tempo += dt;

      this.jogador.atualizar(dt, this.entrada, this.emJogo);
      for (const p of m.passageiros) p.atualizar(dt);
      for (const item of m.itens) item.atualizar(dt, m.caixas);
      for (const s of m.manchas) s.atualizar(dt);
      for (const c of m.carrinhos) c.atualizar(dt);
      for (const f of Object.values(m.nivel.fontes)) f.atualizar(dt);
      this.turbulencia.atualizar(dt, this.estado.turbulence);

      if (this.estado.seatbelt && !this.cinto) this.audio.tocar('aviso_cinto');
      this.cinto = this.estado.seatbelt;

      if ((this.estado.state === 'Results' || this.estado.state === 'Failed') && !this.resultadoMostrado && this.ultimoResultado) {
        this.resultadoMostrado = true;
        setTimeout(() => {
          this.entrada.soltar();
          this.hud.resultado(this.ultimoResultado.results, this.ultimoResultado.progress, R.perfil());
        }, 1200);
      }
    }
    this.hud.atualizar(this.estado, this.jogador, this.camera);
    this.audio.atualizar(this.estado);
  }
}

new Jogo().iniciar().catch(err => {
  console.error(err);
  $('#carregando p').textContent = 'Não foi possível iniciar o jogo: ' + (err?.message ?? err);
  $('#carregando').classList.add('erro');
});
