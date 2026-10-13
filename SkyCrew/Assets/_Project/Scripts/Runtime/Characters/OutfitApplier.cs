using SkyCrew.Core;
using UnityEngine;

namespace SkyCrew.Characters
{
    /// <summary>
    /// Veste o personagem: troca o modelo pelo uniforme equipado e encaixa o acessorio no osso
    /// certo (quepe na cabeca, lenco no peito). Acessorios sao modelados no espaco da raiz do
    /// personagem, entao basta alinha-los a raiz na pose de repouso e prende-los ao osso.
    /// </summary>
    public static class OutfitApplier
    {
        public static string UniformModel(string uniformId)
        {
            switch (uniformId)
            {
                case "uniforme_vermelho": return CharacterFactory.Crew + "_Vermelho";
                case "uniforme_verde": return CharacterFactory.Crew + "_Verde";
                default: return CharacterFactory.Crew;
            }
        }

        /// <summary>Osso onde cada acessorio e preso.</summary>
        public static string AccessoryBone(string accessoryId)
            => accessoryId == "lenco_amarelo" ? "Chest" : "Head";

        public static string AccessoryModel(string accessoryId)
        {
            switch (accessoryId)
            {
                case "lenco_amarelo": return "Acessorios/Lenco_Amarelo";
                case "quepe_dourado": return "Acessorios/Quepe_Dourado";
                default: return null;
            }
        }

        /// <summary>Cria o visual do uniforme como filho de <paramref name="owner"/> e encaixa o acessorio.</summary>
        public static Transform Dress(Transform owner, string uniformId, string accessoryId)
        {
            var visual = CharacterFactory.SpawnVisual(UniformModel(uniformId), owner);
            AttachAccessory(visual, accessoryId);
            return visual;
        }

        public static GameObject AttachAccessory(Transform visual, string accessoryId)
        {
            string model = AccessoryModel(accessoryId);
            if (model == null || ModelLibrary.Load(model) == null)
            {
                return null;
            }

            var bone = FindDeep(visual, AccessoryBone(accessoryId)) ?? visual;
            // Alinha a raiz do personagem (ainda na pose de repouso) e prende ao osso mantendo a posicao.
            var accessory = ModelLibrary.Spawn(model, visual, Vector3.zero, Quaternion.identity, null);
            ModelLibrary.StripColliders(accessory);
            accessory.name = "Acessorio_" + accessoryId;
            accessory.transform.SetParent(bone, true);
            return accessory;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
