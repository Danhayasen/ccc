// Medidas da cabine (mesmos numeros de Runtime/Aircraft/CabinLayout.cs).
// A Unity usa coordenadas "mao esquerda"; os modelos GLB foram convertidos com o eixo X
// espelhado. Por isso toda posicao da Unity passa por U(x, y, z) -> (-x, y, z).
import * as THREE from 'three';

export const HalfWidth = 1.9;
export const CeilingHeight = 2.25;
export const FrontZ = 5.2;
export const RearZ = -5.2;
export const Rows = 6;
export const FirstRowZ = 2.0;
export const RowPitch = 0.9;
export const SeatWidth = 0.48;
export const AisleHalfWidth = 0.31;
export const GalleyStartZ = 3.0;
export const LavatoryStartZ = -3.4;
export const SeatLetters = ['A', 'B', 'C', 'D', 'E', 'F'];
export const SegmentLength = 2.08;

export const U = (x, y, z) => new THREE.Vector3(-x, y, z);
/** Rotacao em Y da Unity (graus) -> three.js (radianos). */
export const yawU = (deg) => -deg * Math.PI / 180;

export const rowZ = (row) => FirstRowZ - (row - 1) * RowPitch;

/** X (Unity) do centro da poltrona: A..C a esquerda, D..F a direita. */
export function seatXU(letterIndex) {
  const first = AisleHalfWidth + SeatWidth * 0.5;
  const side = letterIndex < 3 ? -1 : 1;
  const fromAisle = letterIndex < 3 ? 2 - letterIndex : letterIndex - 3;
  return side * (first + fromAisle * SeatWidth);
}

export function parseSeat(seatId) {
  const letter = SeatLetters.indexOf(seatId.slice(-1));
  const row = parseInt(seatId.slice(0, -1), 10);
  return { row, letter };
}

export const seatPosition = (row, letter) => U(seatXU(letter), 0, rowZ(row) - 0.05);
export const aislePointForRow = (row) => U(0, 0, rowZ(row));
export const BoardingDoor = U(0, 0, GalleyStartZ - 0.2);
export const PlayerSpawn = U(0, 0, 4.2);
export const LavatoryPoint = U(0, 0, -4.3);
export const ForgottenSuitcase = U(0.05, 0.35, 0.35);
export const GalleyZ = (FrontZ + GalleyStartZ) * 0.5 + 0.1;
