using SkyCrew.Core;
using UnityEngine;

namespace SkyCrew.Characters
{
    /// <summary>Instancia os personagens (modelos do Blender ou boneco provisorio de primitivas).</summary>
    public static class CharacterFactory
    {
        public const string Crew = "Personagens/Comissaria";
        public static readonly string[] PassengerVariants =
        {
            "Personagens/Passageiro_1", "Personagens/Passageiro_2", "Personagens/Passageiro_3", "Personagens/Passageiro_4",
        };

        private static readonly Color[] FallbackShirts =
        {
            MaterialLibrary.Vermelho, MaterialLibrary.Verde, MaterialLibrary.Amarelo, MaterialLibrary.Marrom,
        };

        /// <summary>Cria o visual como filho de <paramref name="parent"/>, com pes na origem e frente em +Z.</summary>
        public static Transform SpawnVisual(string modelPath, Transform parent, int variant = 0)
        {
            var go = ModelLibrary.Spawn(modelPath, parent, Vector3.zero, Quaternion.identity,
                                        p => BuildFallback(p, modelPath == Crew ? MaterialLibrary.AzulEscuro
                                                                                : FallbackShirts[variant % FallbackShirts.Length]));
            ModelLibrary.StripColliders(go);
            go.name = "Visual";
            return go.transform;
        }

        /// <summary>Boneco provisorio com as mesmas partes nomeadas do modelo final.</summary>
        private static GameObject BuildFallback(Transform parent, Color shirt)
        {
            var root = new GameObject("Boneco_Provisorio");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            Primitives.Shape(PrimitiveType.Capsule, "Corpo", t, new Vector3(0f, 0.85f, 0f), new Vector3(0.5f, 0.38f, 0.38f), shirt);
            var head = new GameObject("Cabeca").transform;
            head.SetParent(t, false);
            head.localPosition = new Vector3(0f, 1.25f, 0f);
            Primitives.Shape(PrimitiveType.Sphere, "Cabeca_Malha", head, new Vector3(0f, 0.2f, 0f), Vector3.one * 0.46f, MaterialLibrary.Hex(0xF2C39B));
            Primitives.Shape(PrimitiveType.Sphere, "Olho_E", head, new Vector3(-0.09f, 0.25f, 0.2f), Vector3.one * 0.1f, Color.white);
            Primitives.Shape(PrimitiveType.Sphere, "Olho_D", head, new Vector3(0.09f, 0.25f, 0.2f), Vector3.one * 0.1f, Color.white);
            foreach (var (name, x) in new[] { ("Braco_E", -0.3f), ("Braco_D", 0.3f) })
            {
                var arm = new GameObject(name).transform;
                arm.SetParent(t, false);
                arm.localPosition = new Vector3(x, 1.1f, 0f);
                Primitives.Shape(PrimitiveType.Capsule, name + "_Malha", arm, new Vector3(0f, -0.25f, 0f), new Vector3(0.14f, 0.25f, 0.14f), shirt);
            }

            foreach (var (name, x) in new[] { ("Perna_E", -0.12f), ("Perna_D", 0.12f) })
            {
                var leg = new GameObject(name).transform;
                leg.SetParent(t, false);
                leg.localPosition = new Vector3(x, 0.5f, 0f);
                Primitives.Shape(PrimitiveType.Capsule, name + "_Malha", leg, new Vector3(0f, -0.25f, 0f), new Vector3(0.16f, 0.25f, 0.16f), MaterialLibrary.CinzaEscuro);
            }

            return root;
        }
    }
}
