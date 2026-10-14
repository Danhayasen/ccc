import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { OutlineEffect } from 'three/addons/effects/OutlineEffect.js';
import { CSS2DRenderer, CSS2DObject } from 'three/addons/renderers/CSS2DRenderer.js';
import { carregarAviao } from './aviao.js';

const CHAO_Y = -4.4;
const NOMES = {
  Rotulo_Cockpit: 'Cockpit',
  Rotulo_Primeira_Classe: 'Primeira classe',
  Rotulo_Classe_Executiva: 'Executiva',
  Rotulo_Classe_Economica: 'Econômica',
  Rotulo_Cozinha: 'Cozinha',
  Rotulo_Banheiros: 'Banheiros',
  Rotulo_Compartimento_Animais: 'Animais',
};

// Posicoes de camera [posicao, alvo]
const CAMERAS = {
  tres_quartos: [[24, 6, -26], [0, -0.5, 0]],
  lateral: [[0, 1, -38], [0, -0.5, 0]],
  frontal: [[34, 0, 0], [0, -0.5, 0]],
  traseira: [[-30, 7, 12], [0, 0, 0]],
  superior: [[0.1, 42, 0], [0, 0, 0]],
  corte: [[1, 9, -29], [0.5, -0.4, 0]],
};

const palco = document.getElementById('palco');
const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.setSize(innerWidth, innerHeight);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
palco.appendChild(renderer.domElement);

const efeito = new OutlineEffect(renderer, { defaultThickness: 0.0035, defaultColor: [0.05, 0.08, 0.16] });

const rotulosRenderer = new CSS2DRenderer();
rotulosRenderer.setSize(innerWidth, innerHeight);
Object.assign(rotulosRenderer.domElement.style, { position: 'fixed', inset: '0', pointerEvents: 'none' });
document.body.appendChild(rotulosRenderer.domElement);

const cena = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(40, innerWidth / innerHeight, 0.1, 500);
const controles = new OrbitControls(camera, renderer.domElement);
controles.enableDamping = true;
controles.maxPolarAngle = Math.PI * 0.495;
controles.minDistance = 6;
controles.maxDistance = 90;

cena.add(new THREE.HemisphereLight(0xdff3ff, 0xb59a74, 1.6));
const sol = new THREE.DirectionalLight(0xfff1d6, 2.4);
sol.position.set(18, 30, -14);
sol.castShadow = true;
sol.shadow.mapSize.set(2048, 2048);
Object.assign(sol.shadow.camera, { left: -24, right: 24, top: 24, bottom: -24, near: 1, far: 90 });
sol.shadow.bias = -0.0005;
cena.add(sol);

// Pista cartunesca
const chao = new THREE.Mesh(
  new THREE.CircleGeometry(60, 48),
  new THREE.MeshToonMaterial({ color: 0x9fd07f })
);
chao.rotation.x = -Math.PI / 2;
chao.position.y = CHAO_Y;
chao.receiveShadow = true;
cena.add(chao);
const pista = new THREE.Mesh(new THREE.PlaneGeometry(120, 16), new THREE.MeshToonMaterial({ color: 0x6b7280 }));
pista.rotation.x = -Math.PI / 2;
pista.position.y = CHAO_Y + 0.01;
pista.receiveShadow = true;
cena.add(pista);
for (let x = -54; x <= 54; x += 9) {
  const faixa = new THREE.Mesh(new THREE.PlaneGeometry(4.5, 0.5), new THREE.MeshToonMaterial({ color: 0xffffff }));
  faixa.rotation.x = -Math.PI / 2;
  faixa.position.set(x, CHAO_Y + 0.02, 0);
  cena.add(faixa);
}

const aviao = await carregarAviao();
cena.add(aviao.objeto);
document.getElementById('carregando').remove();

for (const r of aviao.rotulos) {
  const div = document.createElement('div');
  div.className = 'rotulo';
  div.textContent = NOMES[r.name] ?? r.name;
  const rotulo = new CSS2DObject(div);
  r.add(rotulo);
}
const atualizarRotulos = () => aviao.rotulos.forEach((r) => { r.visible = aviao.modoCorte; });
atualizarRotulos();

// --- camera animada entre vistas ---------------------------------------------
// Em telas estreitas (celular em pe) a camera se afasta para caber o aviao
function vista(nome) {
  const [pos, alvo] = CAMERAS[nome];
  const a = new THREE.Vector3(...alvo);
  const afastar = Math.max(1, 1.3 / camera.aspect);
  const p = new THREE.Vector3(...pos).sub(a).multiplyScalar(afastar).add(a);
  return [p, a];
}

let animCam = null;
function irPara(nome) {
  animCam = {
    t: 0,
    de: [camera.position.clone(), controles.target.clone()],
    para: vista(nome),
  };
  document.querySelectorAll('[data-camera]').forEach((b) => b.classList.toggle('ativo', b.dataset.camera === nome));
}
camera.position.copy(vista('tres_quartos')[0]);
controles.target.copy(vista('tres_quartos')[1]);

document.querySelectorAll('[data-camera]').forEach((b) => b.addEventListener('click', () => irPara(b.dataset.camera)));

const botaoCorte = document.getElementById('corte');
function alternarCorte(ativo = !aviao.modoCorte) {
  aviao.modoCorte = ativo;
  botaoCorte.classList.toggle('ativo', ativo);
  atualizarRotulos();
  if (ativo) irPara('corte');
}
botaoCorte.addEventListener('click', () => alternarCorte());
document.getElementById('turbulencia').addEventListener('click', () => aviao.turbulencia(1));
document.getElementById('guardar').addEventListener('click', () => aviao.guardarMalas());

// Atalhos: ?corte e ?vista=lateral na URL
const params = new URLSearchParams(location.search);
if (params.has('corte')) alternarCorte(true);
if (params.get('vista') in CAMERAS) {
  const [pos, alvo] = vista(params.get('vista'));
  camera.position.copy(pos);
  controles.target.copy(alvo);
  animCam = null;
}
if (params.has('turbulencia')) aviao.turbulencia(1.5);

addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
  rotulosRenderer.setSize(innerWidth, innerHeight);
});

const relogio = new THREE.Clock();
renderer.setAnimationLoop(() => {
  const dt = relogio.getDelta();
  if (animCam) {
    animCam.t = Math.min(1, animCam.t + dt * 1.4);
    const k = 1 - Math.pow(1 - animCam.t, 3);
    camera.position.lerpVectors(animCam.de[0], animCam.para[0], k);
    controles.target.lerpVectors(animCam.de[1], animCam.para[1], k);
    if (animCam.t >= 1) animCam = null;
  }
  controles.update();
  aviao.atualizar(dt);
  efeito.render(cena, camera);
  rotulosRenderer.render(cena, camera);
});
