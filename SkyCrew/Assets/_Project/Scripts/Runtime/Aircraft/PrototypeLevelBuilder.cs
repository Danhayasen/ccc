using System.Collections.Generic;
using SkyCrew.Core;
using SkyCrew.Interaction;
using UnityEngine;

namespace SkyCrew.Aircraft
{
    /// <summary>Referencias do cenario construido.</summary>
    public sealed class LevelRefs
    {
        public Transform Root;
        public readonly List<ItemDropZone> Bins = new List<ItemDropZone>();
        public readonly List<Light> Lights = new List<Light>();
        public ItemDispenser Dispenser;
        public DisposalBin TrashBin;
    }

    /// <summary>
    /// Monta a secao de cabine do prototipo com modulos (FBX do Blender) e colisores simples
    /// definidos em codigo. Visual e colisao sao independentes: trocar um modulo nao quebra
    /// a navegacao nem a fisica.
    /// </summary>
    public static class PrototypeLevelBuilder
    {
        public const float SegmentLength = 2.08f;

        public static LevelRefs Build(Transform parent)
        {
            var refs = new LevelRefs();
            var root = new GameObject("Cabine").transform;
            root.SetParent(parent, false);
            refs.Root = root;

            BuildShell(root);
            BuildColliders(root);
            BuildSeats(root);
            BuildBins(root, refs);
            BuildGalley(root, refs);
            BuildLavatory(root);
            BuildLights(root, refs);
            return refs;
        }

        private static void BuildShell(Transform root)
        {
            var shell = new GameObject("Fuselagem").transform;
            shell.SetParent(root, false);
            int count = Mathf.CeilToInt((CabinLayout.FrontZ - CabinLayout.RearZ) / SegmentLength);
            for (int i = 0; i < count; i++)
            {
                float z = CabinLayout.RearZ + SegmentLength * (i + 0.5f);
                var seg = ModelLibrary.Spawn("Cabine/Segmento", shell, new Vector3(0f, 0f, z), Quaternion.identity,
                                             p => FallbackSegment(p));
                ModelLibrary.StripColliders(seg);
            }

            // Paredes das pontas (cabine de comando e fundo)
            ModelLibrary.Spawn("Cabine/Parede_Frente", shell, new Vector3(0f, 0f, CabinLayout.FrontZ), Quaternion.identity,
                p => Primitives.Box("Parede_Frente", p, new Vector3(0f, 1.15f, 0f), new Vector3(3.8f, 2.3f, 0.1f), MaterialLibrary.Creme, false));
            ModelLibrary.Spawn("Cabine/Parede_Fundo", shell, new Vector3(0f, 0f, CabinLayout.RearZ), Quaternion.identity,
                p => Primitives.Box("Parede_Fundo", p, new Vector3(0f, 1.15f, 0f), new Vector3(3.8f, 2.3f, 0.1f), MaterialLibrary.Creme, false));
        }

        private static GameObject FallbackSegment(Transform parent)
        {
            var seg = new GameObject("Segmento_Provisorio");
            seg.transform.SetParent(parent, false);
            var t = seg.transform;
            float w = CabinLayout.HalfWidth;
            Primitives.Box("Piso", t, new Vector3(0f, -0.05f, 0f), new Vector3(w * 2f, 0.1f, SegmentLength), MaterialLibrary.Carpete, false);
            Primitives.Box("Corredor", t, new Vector3(0f, 0.001f, 0f), new Vector3(0.6f, 0.01f, SegmentLength), MaterialLibrary.AzulClaro, false);
            Primitives.Box("Parede_E", t, new Vector3(-w - 0.05f, 1.1f, 0f), new Vector3(0.1f, 2.2f, SegmentLength), MaterialLibrary.Creme, false);
            Primitives.Box("Parede_D", t, new Vector3(w + 0.05f, 1.1f, 0f), new Vector3(0.1f, 2.2f, SegmentLength), MaterialLibrary.Creme, false);
            Primitives.Box("Teto", t, new Vector3(0f, CabinLayout.CeilingHeight + 0.05f, 0f), new Vector3(w * 2f, 0.1f, SegmentLength), MaterialLibrary.Branco, false);
            return seg;
        }

        private static void BuildColliders(Transform root)
        {
            var col = new GameObject("Colisores").transform;
            col.SetParent(root, false);
            float w = CabinLayout.HalfWidth;
            float length = CabinLayout.FrontZ - CabinLayout.RearZ;
            float midZ = (CabinLayout.FrontZ + CabinLayout.RearZ) * 0.5f;
            Primitives.Collider("Piso", col, new Vector3(0f, -0.05f, midZ), new Vector3(w * 2f + 0.4f, 0.1f, length + 0.4f));
            Primitives.Collider("Teto", col, new Vector3(0f, CabinLayout.CeilingHeight + 0.05f, midZ), new Vector3(w * 2f + 0.4f, 0.1f, length + 0.4f));
            Primitives.Collider("Parede_E", col, new Vector3(-w - 0.05f, 1.2f, midZ), new Vector3(0.1f, 2.6f, length + 0.4f));
            Primitives.Collider("Parede_D", col, new Vector3(w + 0.05f, 1.2f, midZ), new Vector3(0.1f, 2.6f, length + 0.4f));
            Primitives.Collider("Parede_Frente", col, new Vector3(0f, 1.2f, CabinLayout.FrontZ + 0.05f), new Vector3(w * 2f, 2.6f, 0.1f));
            Primitives.Collider("Parede_Fundo", col, new Vector3(0f, 1.2f, CabinLayout.RearZ - 0.05f), new Vector3(w * 2f, 2.6f, 0.1f));

            // Bloco por fileira e lado: impede navegar por cima das poltronas.
            float blockWidth = CabinLayout.SeatWidth * 3f;
            float blockX = CabinLayout.AisleHalfWidth + blockWidth * 0.5f;
            for (int row = 1; row <= CabinLayout.Rows; row++)
            {
                float z = CabinLayout.RowZ(row) - 0.08f;
                Primitives.Collider($"Fileira_{row}_E", col, new Vector3(-blockX, 0.5f, z), new Vector3(blockWidth, 1.0f, 0.62f));
                Primitives.Collider($"Fileira_{row}_D", col, new Vector3(blockX, 0.5f, z), new Vector3(blockWidth, 1.0f, 0.62f));
            }
        }

        private static void BuildSeats(Transform root)
        {
            var seats = new GameObject("Poltronas").transform;
            seats.SetParent(root, false);
            for (int row = 1; row <= CabinLayout.Rows; row++)
            {
                for (int letter = 0; letter < 6; letter++)
                {
                    var pos = new Vector3(CabinLayout.SeatX(letter), 0f, CabinLayout.RowZ(row));
                    var seat = ModelLibrary.Spawn("Cabine/Poltrona", seats, pos, Quaternion.identity, FallbackSeat);
                    seat.name = "Poltrona_" + CabinLayout.SeatId(row, letter);
                    ModelLibrary.StripColliders(seat);
                }
            }
        }

        private static GameObject FallbackSeat(Transform parent)
        {
            var seat = new GameObject("Poltrona_Provisoria");
            seat.transform.SetParent(parent, false);
            var t = seat.transform;
            Primitives.Box("Assento", t, new Vector3(0f, 0.4f, 0f), new Vector3(0.44f, 0.12f, 0.5f), MaterialLibrary.Azul, false);
            Primitives.Box("Encosto", t, new Vector3(0f, 0.8f, -0.25f), new Vector3(0.44f, 0.7f, 0.1f), MaterialLibrary.Azul, false);
            Primitives.Box("Pe", t, new Vector3(0f, 0.17f, 0f), new Vector3(0.2f, 0.34f, 0.3f), MaterialLibrary.CinzaEscuro, false);
            return seat;
        }

        private static void BuildBins(Transform root, LevelRefs refs)
        {
            var bins = new GameObject("Bagageiros").transform;
            bins.SetParent(root, false);
            float[] centers = { CabinLayout.RowZ(2) + 0.45f, CabinLayout.RowZ(5) + 0.45f };
            const float length = 2.6f;
            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 0; i < centers.Length; i++)
                {
                    var pos = new Vector3(side * 1.55f, 1.75f, centers[i]);
                    var bin = ModelLibrary.Spawn("Cabine/Bagageiro", bins, pos, Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f),
                        p => Primitives.Box("Bagageiro", p, new Vector3(0f, 0.22f, 0f), new Vector3(0.65f, 0.44f, length), MaterialLibrary.Branco, false));
                    bin.name = $"Bagageiro_{(side < 0 ? "E" : "D")}{i + 1}";
                    ModelLibrary.StripColliders(bin);
                    var box = bin.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.22f, 0f);
                    box.size = new Vector3(0.65f, 0.44f, length);

                    var slots = new Transform[3];
                    for (int s = 0; s < slots.Length; s++)
                    {
                        slots[s] = new GameObject("Vaga_" + s).transform;
                        slots[s].SetParent(bin.transform, false);
                        slots[s].localPosition = new Vector3(0f, 0.05f, (s - 1) * 0.8f);
                        slots[s].localRotation = Quaternion.Euler(0f, 90f, 0f);
                    }

                    var zone = bin.AddComponent<ItemDropZone>();
                    zone.Configure(SkyCrew.Domain.Content.PrototypeScenario.ZoneOverheadBin, ItemTypesUnity.Suitcase,
                                   "Guardar no bagageiro", slots);
                    refs.Bins.Add(zone);
                }
            }
        }

        private static void BuildGalley(Transform root, LevelRefs refs)
        {
            var galley = new GameObject("Cozinha").transform;
            galley.SetParent(root, false);
            float z = (CabinLayout.FrontZ + CabinLayout.GalleyStartZ) * 0.5f + 0.1f;
            var visual = ModelLibrary.Spawn("Cabine/Cozinha", galley, new Vector3(0f, 0f, z), Quaternion.identity, p =>
            {
                var g = new GameObject("Cozinha_Provisoria");
                g.transform.SetParent(p, false);
                foreach (int side in new[] { -1, 1 })
                {
                    Primitives.Box("Balcao", g.transform, new Vector3(side * 1.575f, 0.5f, 0f), new Vector3(0.65f, 1.0f, 2.0f), MaterialLibrary.Cinza, false);
                    Primitives.Box("Armario", g.transform, new Vector3(side * 1.6f, 1.9f, 0f), new Vector3(0.6f, 0.6f, 2.0f), MaterialLibrary.Cinza, false);
                }

                return g;
            });
            ModelLibrary.StripColliders(visual);
            foreach (int side in new[] { -1, 1 })
            {
                Primitives.Collider("Balcao_Colisor", galley, new Vector3(side * 1.575f, 0.5f, z), new Vector3(0.65f, 1.0f, 2.0f));
            }

            // Geladeira com garrafas de agua
            var dispenser = new GameObject("Geladeira_Agua");
            dispenser.transform.SetParent(galley, false);
            dispenser.transform.localPosition = new Vector3(-1.2f, 1.05f, z + 0.3f);
            var trigger = dispenser.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.4f, 0.4f, 0.6f);
            Primitives.Shape(PrimitiveType.Cube, "Marcador_Agua", dispenser.transform, Vector3.zero,
                             new Vector3(0.12f, 0.3f, 0.5f), MaterialLibrary.AzulClaro);
            refs.Dispenser = dispenser.AddComponent<ItemDispenser>();

            // Lixeira
            var bin = ModelLibrary.Spawn("Props/Lixeira", galley, new Vector3(1.1f, 0f, CabinLayout.GalleyStartZ + 0.35f), Quaternion.identity,
                p => Primitives.Shape(PrimitiveType.Cylinder, "Lixeira", p, new Vector3(0f, 0.35f, 0f), new Vector3(0.4f, 0.35f, 0.4f), MaterialLibrary.CinzaEscuro));
            bin.name = "Lixeira";
            ModelLibrary.StripColliders(bin);
            var binCol = bin.AddComponent<BoxCollider>();
            binCol.center = new Vector3(0f, 0.35f, 0f);
            binCol.size = new Vector3(0.4f, 0.7f, 0.4f);
            refs.TrashBin = bin.AddComponent<DisposalBin>();
        }

        private static void BuildLavatory(Transform root)
        {
            var lav = new GameObject("Banheiro").transform;
            lav.SetParent(root, false);
            float z0 = CabinLayout.LavatoryStartZ;
            float z1 = CabinLayout.RearZ;
            float midZ = (z0 + z1) * 0.5f;
            var visual = ModelLibrary.Spawn("Cabine/Banheiro", lav, new Vector3(0f, 0f, midZ), Quaternion.identity, p =>
            {
                var g = new GameObject("Banheiro_Provisorio");
                g.transform.SetParent(p, false);
                Primitives.Shape(PrimitiveType.Cylinder, "Vaso", g.transform, new Vector3(0f, 0.22f, -0.5f), new Vector3(0.4f, 0.22f, 0.45f), MaterialLibrary.Branco);
                return g;
            });
            ModelLibrary.StripColliders(visual);

            float depth = z0 - z1;
            foreach (int side in new[] { -1, 1 })
            {
                Primitives.Box("Parede_Lateral", lav, new Vector3(side * 0.72f, 1.1f, midZ), new Vector3(0.06f, 2.2f, depth), MaterialLibrary.Creme);
                Primitives.Box("Parede_Porta", lav, new Vector3(side * 0.58f, 1.1f, z0), new Vector3(0.28f, 2.2f, 0.06f), MaterialLibrary.Creme);
                Primitives.Box("Armario_Fundo", lav, new Vector3(side * 1.32f, 0.9f, midZ), new Vector3(1.15f, 1.8f, depth), MaterialLibrary.Cinza);
            }

            Primitives.Box("Batente", lav, new Vector3(0f, 2.15f, z0), new Vector3(0.9f, 0.2f, 0.06f), MaterialLibrary.Creme);
        }

        private static void BuildLights(Transform root, LevelRefs refs)
        {
            var lights = new GameObject("Luzes").transform;
            lights.SetParent(root, false);
            foreach (float z in new[] { 4.0f, 1.4f, -1.2f, -3.8f })
            {
                var go = new GameObject("Luz_Teto");
                go.transform.SetParent(lights, false);
                go.transform.localPosition = new Vector3(0f, CabinLayout.CeilingHeight - 0.15f, z);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 5.5f;
                light.intensity = 1.6f;
                light.color = new Color(1f, 0.95f, 0.85f);
                light.shadows = z > 0f && z < 2f ? LightShadows.Soft : LightShadows.None; // uma sombra so (custo)
                refs.Lights.Add(light);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.75f, 0.8f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.6f, 0.58f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.32f, 0.38f);
        }
    }
}
