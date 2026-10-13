using SkyCrew.Core;
using SkyCrew.Interaction;
using UnityEngine;

namespace SkyCrew.Physics
{
    /// <summary>Cria os objetos carregaveis do prototipo (modelo do Blender ou primitiva provisoria).</summary>
    public static class ItemFactory
    {
        public static CarryableItem Create(string itemType, Vector3 position, Quaternion rotation, string instanceId = null)
        {
            var item = CreateByType(itemType, position, rotation);
            if (item != null && !string.IsNullOrEmpty(instanceId))
            {
                item.InstanceId = instanceId;
                item.name = instanceId;
            }

            return item;
        }

        private static CarryableItem CreateByType(string itemType, Vector3 position, Quaternion rotation)
        {
            switch (itemType)
            {
                case ItemTypesUnity.Water:
                    return Build(itemType, "garrafa de água", "Props/Garrafa", position, rotation, PhysicsTuning.Light,
                        new Vector3(0f, -0.1f, 0f),
                        p => Primitives.Shape(PrimitiveType.Cylinder, "Garrafa", p, new Vector3(0f, 0.13f, 0f),
                                              new Vector3(0.09f, 0.13f, 0.09f), MaterialLibrary.AzulClaro));
                case ItemTypesUnity.Suitcase:
                    return Build(itemType, "mala", "Props/Mala", position, rotation, PhysicsTuning.Heavy,
                        new Vector3(0f, -0.25f, 0.1f),
                        p => Primitives.Shape(PrimitiveType.Cube, "Mala", p, new Vector3(0f, 0.18f, 0f),
                                              new Vector3(0.5f, 0.36f, 0.24f), MaterialLibrary.Vermelho));
                case ItemTypesUnity.Trash:
                    return Build(itemType, "lixo", "Props/Lixo", position, rotation, PhysicsTuning.Light,
                        new Vector3(0f, -0.05f, 0f),
                        p => Primitives.Shape(PrimitiveType.Sphere, "Lixo", p, new Vector3(0f, 0.07f, 0f),
                                              Vector3.one * 0.14f, MaterialLibrary.Branco));
                case ItemTypesUnity.Snack:
                    return Build(itemType, "lanche", "Props/Lanche", position, rotation, PhysicsTuning.Light,
                        new Vector3(0f, -0.05f, 0f),
                        p => Primitives.Shape(PrimitiveType.Cube, "Lanche", p, new Vector3(0f, 0.05f, 0f),
                                              new Vector3(0.2f, 0.1f, 0.14f), MaterialLibrary.Amarelo));
                case ItemTypesUnity.Blanket:
                    return Build(itemType, "cobertor", "Props/Cobertor", position, rotation, PhysicsTuning.Light,
                        new Vector3(0f, -0.08f, 0f),
                        p => Primitives.Shape(PrimitiveType.Cube, "Cobertor", p, new Vector3(0f, 0.06f, 0f),
                                              new Vector3(0.34f, 0.12f, 0.26f), MaterialLibrary.AzulClaro));
                case ItemTypesUnity.Mop:
                    return Build(itemType, "esfregão", "Props/Esfregao", position, rotation, PhysicsTuning.Light,
                        new Vector3(0.15f, -0.6f, 0.1f),
                        p => Primitives.Shape(PrimitiveType.Cylinder, "Esfregao", p, new Vector3(0f, 0.6f, 0f),
                                              new Vector3(0.05f, 0.6f, 0.05f), MaterialLibrary.Amarelo));
                default:
                    Debug.LogError($"[SkyCrew] Tipo de item desconhecido: {itemType}");
                    return null;
            }
        }

        private static CarryableItem Build(string itemType, string displayName, string modelPath, Vector3 position,
                                           Quaternion rotation, PhysicsTuning tuning, Vector3 holdOffset,
                                           System.Func<Transform, GameObject> fallback)
        {
            var root = new GameObject(displayName);
            root.transform.SetPositionAndRotation(position, rotation);
            var visual = ModelLibrary.Spawn(modelPath, root.transform, Vector3.zero, Quaternion.identity, fallback);
            ModelLibrary.StripColliders(visual);

            // Colisor simples ajustado aos limites do visual (nunca MeshCollider em objeto movel).
            var bounds = LocalBounds(root.transform);
            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.05f);

            root.AddComponent<Rigidbody>();
            var item = root.AddComponent<CarryableItem>();
            item.Configure(itemType, displayName, tuning, holdOffset);
            return item;
        }

        public static Bounds LocalBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.up * 0.1f, Vector3.one * 0.2f);
            }

            var world = renderers[0].bounds;
            foreach (var r in renderers)
            {
                world.Encapsulate(r.bounds);
            }

            // Converte para o espaco local (sem rotacao/escala na raiz no momento da criacao).
            var local = new Bounds(root.InverseTransformPoint(world.center), world.size);
            return local;
        }
    }
}
