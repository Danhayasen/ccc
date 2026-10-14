// Modulo do aviao para o jogo (Three.js).
// Carrega aviao.glb (gerado pelo Blender), aplica o visual cartunesco (toon)
// e oferece controles: modo corte, turbulencia e malas caindo.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const GRAVIDADE = -9.8;
const PISO_Y = -0.75;          // altura do piso da cabine (em metros)

// Degrade de 3 tons para o sombreamento "cel shading"
function criarGradiente() {
  const dados = new Uint8Array([90, 170, 255]);
  const tex = new THREE.DataTexture(dados, dados.length, 1, THREE.RedFormat);
  tex.minFilter = THREE.NearestFilter;
  tex.magFilter = THREE.NearestFilter;
  tex.needsUpdate = true;
  return tex;
}

function paraToon(mat, gradiente) {
  const toon = new THREE.MeshToonMaterial({
    name: mat.name,
    color: mat.color,
    map: mat.map,
    vertexColors: mat.vertexColors,
    emissive: mat.emissive,
    emissiveIntensity: mat.emissiveIntensity,
    emissiveMap: mat.emissiveMap,
    gradientMap: gradiente,
    side: THREE.DoubleSide,
  });
  if (toon.map) toon.map.colorSpace = THREE.SRGBColorSpace;
  return toon;
}

export async function carregarAviao(url = new URL('./aviao.glb', import.meta.url).href) {
  const gltf = await new GLTFLoader().loadAsync(url);
  const raiz = gltf.scene;
  const gradiente = criarGradiente();
  const cache = new Map();

  raiz.traverse((obj) => {
    if (!obj.isMesh) return;
    obj.castShadow = true;
    obj.receiveShadow = true;
    const original = obj.material;
    if (!cache.has(original)) cache.set(original, paraToon(original, gradiente));
    obj.material = cache.get(original);
  });

  const no = (nome) => raiz.getObjectByName(nome);
  const cascoCorte = no('Casco_Corte');
  const rotulos = [];
  no('Rotulos')?.children.forEach((r) => rotulos.push(r));

  // --- comedia fisica: passageiros pulando, malas caindo -------------------
  const corpos = [];
  no('Passageiros')?.children.forEach((p) => {
    corpos.push({ obj: p, tipo: 'passageiro', base: p.position.clone(),
                  baseRot: p.rotation.clone(), vel: new THREE.Vector3(), giro: 0 });
  });
  no('Malas')?.children.forEach((m) => {
    corpos.push({ obj: m, tipo: 'mala', base: m.position.clone(),
                  baseRot: m.rotation.clone(), vel: new THREE.Vector3(),
                  giro: new THREE.Vector3(), solta: false });
  });
  const bichos = no('Animais');
  const bichosBase = bichos ? bichos.position.clone() : null;

  let tremor = 0;          // intensidade atual da turbulencia
  let tempo = 0;

  function turbulencia(forca = 1) {
    tremor = Math.min(1.5, tremor + forca);
    for (const c of corpos) {
      if (c.tipo === 'passageiro') {
        c.vel.y += 1.5 + Math.random() * 2.5 * forca;
        c.giro = (Math.random() - 0.5) * 6;
      } else if (!c.solta && Math.random() < 0.8) {
        c.solta = true;     // a mala escapa do bagageiro
        c.vel.set((Math.random() - 0.5) * 0.6, 1.5, -1.2 - Math.random());
        c.giro.set(Math.random() * 6, Math.random() * 4, Math.random() * 6);
      }
    }
  }

  function guardarMalas() {
    for (const c of corpos) {
      if (c.tipo !== 'mala') continue;
      c.solta = false;
      c.vel.set(0, 0, 0);
      c.obj.position.copy(c.base);
      c.obj.rotation.copy(c.baseRot);
    }
  }

  function atualizar(dt) {
    dt = Math.min(dt, 1 / 30);
    tempo += dt;
    tremor = Math.max(0, tremor - dt * 0.6);

    // o aviao inteiro chacoalha
    const t = tremor;
    raiz.rotation.x = Math.sin(tempo * 23) * 0.012 * t + Math.sin(tempo * 7) * 0.02 * t;
    raiz.rotation.z = Math.sin(tempo * 17 + 1) * 0.01 * t;
    raiz.position.y = Math.sin(tempo * 31) * 0.05 * t;

    for (const c of corpos) {
      const o = c.obj;
      if (c.tipo === 'passageiro') {
        c.vel.y += GRAVIDADE * dt;
        o.position.y += c.vel.y * dt;
        if (o.position.y <= c.base.y) {           // caiu de volta na poltrona
          o.position.y = c.base.y;
          if (c.vel.y < -1.5) c.vel.y *= -0.35;    // quiquinho
          else c.vel.y = 0;
        }
        const noAr = o.position.y - c.base.y;
        // estica no ar, achata ao cair (squash & stretch)
        const estica = THREE.MathUtils.clamp(1 + c.vel.y * 0.06, 0.75, 1.3);
        o.scale.set(1 / Math.sqrt(estica), estica, 1 / Math.sqrt(estica));
        o.rotation.x = c.baseRot.x + Math.sin(tempo * 9 + c.base.x) * Math.min(noAr, 0.3) * c.giro * 0.3;
      } else if (c.solta) {
        c.vel.y += GRAVIDADE * dt;
        o.position.addScaledVector(c.vel, dt);
        o.rotation.x += c.giro.x * dt;
        o.rotation.y += c.giro.y * dt;
        o.rotation.z += c.giro.z * dt;
        // corredor (|z| < 0.35) = piso; fora dele cai sobre as poltronas
        const chao = Math.abs(o.position.z) < 0.35 ? PISO_Y + 0.15 : PISO_Y + 0.75;
        if (o.position.y < chao) {
          o.position.y = chao;
          c.vel.y = Math.abs(c.vel.y) > 1 ? -c.vel.y * 0.4 : 0;
          c.vel.x *= 0.6;
          c.vel.z *= 0.6;
          c.giro.multiplyScalar(0.5);
        }
        if (Math.abs(o.position.z) < 0.2) c.vel.z *= 0.9;
      }
    }

    if (bichos) {   // bichos pulando nas gaiolas
      bichos.position.y = bichosBase.y + Math.abs(Math.sin(tempo * 14)) * 0.08 * Math.min(t, 1);
    }
  }

  return {
    objeto: raiz,
    rotulos,
    set modoCorte(ativo) { if (cascoCorte) cascoCorte.visible = !ativo; },
    get modoCorte() { return cascoCorte ? !cascoCorte.visible : false; },
    turbulencia,
    guardarMalas,
    atualizar,
  };
}
