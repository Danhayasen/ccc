// Modelos GLB (convertidos dos FBX da Unity por Tools/Blender/exportar_glb.py).
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { clone as cloneEsqueleto } from 'three/addons/utils/SkeletonUtils.js';

const loader = new GLTFLoader();
const cache = new Map();
export let clipes = [];

export const LISTA = [
  'Cabine/Segmento', 'Cabine/Parede_Frente', 'Cabine/Parede_Fundo', 'Cabine/Poltrona', 'Cabine/Bagageiro',
  'Cabine/Cozinha', 'Cabine/Banheiro',
  'Props/Garrafa', 'Props/Mala', 'Props/Lixo', 'Props/Lanche', 'Props/Cobertor', 'Props/Esfregao',
  'Props/Lixeira', 'Props/Mancha', 'Props/Carrinho',
  'Personagens/Comissaria', 'Personagens/Comissaria_Vermelho', 'Personagens/Comissaria_Verde',
  'Personagens/Passageiro_1', 'Personagens/Passageiro_2', 'Personagens/Passageiro_3', 'Personagens/Passageiro_4',
  'Acessorios/Lenco_Amarelo', 'Acessorios/Quepe_Dourado',
];

function prepararMateriais(raiz) {
  raiz.traverse(o => {
    if (o.isMesh) {
      o.castShadow = false;
      o.receiveShadow = false;
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      for (const m of mats) {
        if (m.map) {
          m.map.colorSpace = THREE.SRGBColorSpace;
          m.map.magFilter = THREE.NearestFilter; // paleta: cores chapadas, sem borrar entre amostras
          m.map.minFilter = THREE.NearestFilter;
          m.map.generateMipmaps = false;
        }
        m.roughness = 0.85;
        m.metalness = 0.0;
      }
      if (o.isSkinnedMesh) o.frustumCulled = false;
    }
  });
}

export async function carregarTudo(aoProgredir) {
  let feitos = 0;
  const total = LISTA.length + 1;
  const passo = () => aoProgredir?.(++feitos / total);
  await Promise.all(LISTA.map(async nome => {
    const gltf = await loader.loadAsync(`modelos/${nome}.glb`);
    prepararMateriais(gltf.scene);
    cache.set(nome, gltf.scene);
    passo();
  }));
  const anim = await loader.loadAsync('modelos/Animacoes.glb');
  clipes = anim.animations;
  passo();
}

/** Copia independente do modelo (com esqueleto proprio quando houver). */
export function instanciar(nome) {
  const base = cache.get(nome);
  if (!base) throw new Error('Modelo nao carregado: ' + nome);
  const copia = nome.startsWith('Personagens/') ? cloneEsqueleto(base) : base.clone(true);
  return copia;
}
