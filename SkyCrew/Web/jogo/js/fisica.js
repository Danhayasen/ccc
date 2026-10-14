// Colisao simples e previsivel: caixas alinhadas aos eixos (AABB), como os colisores de caixa
// que o PrototypeLevelBuilder da Unity monta em codigo.
import * as THREE from 'three';

export class Caixa {
  constructor(centro, tamanho, nome = '') {
    this.min = new THREE.Vector3(centro.x - Math.abs(tamanho.x) / 2, centro.y - tamanho.y / 2, centro.z - tamanho.z / 2);
    this.max = new THREE.Vector3(centro.x + Math.abs(tamanho.x) / 2, centro.y + tamanho.y / 2, centro.z + tamanho.z / 2);
    this.nome = nome;
    this.ativa = true;
  }

  pontoMaisProximo(p, saida = new THREE.Vector3()) {
    return saida.set(
      Math.min(Math.max(p.x, this.min.x), this.max.x),
      Math.min(Math.max(p.y, this.min.y), this.max.y),
      Math.min(Math.max(p.z, this.min.z), this.max.z));
  }

  contem(p, margem = 0) {
    return p.x >= this.min.x - margem && p.x <= this.max.x + margem && p.y >= this.min.y - margem
      && p.y <= this.max.y + margem && p.z >= this.min.z - margem && p.z <= this.max.z + margem;
  }

  /** Distancia ao longo do raio (origem, direcao normalizada) ou Infinity. */
  raio(origem, dir, maximo) {
    let tmin = 0, tmax = maximo;
    for (const eixo of ['x', 'y', 'z']) {
      const o = origem[eixo], d = dir[eixo];
      if (Math.abs(d) < 1e-8) {
        if (o < this.min[eixo] || o > this.max[eixo]) return Infinity;
      } else {
        let t1 = (this.min[eixo] - o) / d, t2 = (this.max[eixo] - o) / d;
        if (t1 > t2) [t1, t2] = [t2, t1];
        tmin = Math.max(tmin, t1);
        tmax = Math.min(tmax, t2);
        if (tmin > tmax) return Infinity;
      }
    }
    return tmin;
  }
}

/**
 * Empurra um circulo (pos.x, pos.z, raio) para fora das caixas que cruzam a faixa de altura
 * [baixo, alto]. Retorna true se encostou em algo.
 */
export function resolverCirculo(pos, raio, baixo, alto, caixas) {
  let tocou = false;
  for (const c of caixas) {
    if (!c.ativa || c.max.y <= baixo || c.min.y >= alto) continue;
    const px = Math.min(Math.max(pos.x, c.min.x), c.max.x);
    const pz = Math.min(Math.max(pos.z, c.min.z), c.max.z);
    let dx = pos.x - px, dz = pos.z - pz;
    const d2 = dx * dx + dz * dz;
    if (d2 >= raio * raio) continue;
    tocou = true;
    if (d2 > 1e-10) {
      const d = Math.sqrt(d2);
      pos.x = px + dx / d * raio;
      pos.z = pz + dz / d * raio;
    } else {
      // centro dentro da caixa: sai pelo lado mais proximo
      const saidas = [
        [c.min.x - raio - pos.x, 'x'], [c.max.x + raio - pos.x, 'x'],
        [c.min.z - raio - pos.z, 'z'], [c.max.z + raio - pos.z, 'z']];
      saidas.sort((a, b) => Math.abs(a[0]) - Math.abs(b[0]));
      pos[saidas[0][1]] += saidas[0][0];
    }
  }
  return tocou;
}
