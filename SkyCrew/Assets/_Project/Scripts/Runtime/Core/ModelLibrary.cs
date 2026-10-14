using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkyCrew.Core
{
    /// <summary>
    /// Carrega os modelos gerados pelo Blender (FBX em pastas Resources/SkyCrew/...).
    /// Se um modelo faltar, usa uma alternativa provisoria (primitivas) e registra a ausencia,
    /// para o jogo continuar executando.
    /// </summary>
    public static class ModelLibrary
    {
        public const string Root = "SkyCrew/";
        private static readonly Dictionary<string, GameObject> Loaded = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> MissingSet = new HashSet<string>();

        public static IReadOnlyCollection<string> Missing => MissingSet;

        public static GameObject Load(string path)
        {
            if (Loaded.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var prefab = Resources.Load<GameObject>(Root + path);
            Loaded[path] = prefab;
            if (prefab == null && MissingSet.Add(path))
            {
                Debug.LogWarning($"[SkyCrew] Modelo '{Root + path}' nao encontrado. Usando geometria provisoria.");
            }

            return prefab;
        }

        public static GameObject Spawn(string path, Transform parent, Vector3 localPosition, Quaternion localRotation,
                                       Func<Transform, GameObject> fallback)
        {
            var prefab = Load(path);
            GameObject go;
            if (prefab != null)
            {
                go = UnityEngine.Object.Instantiate(prefab, parent);
                go.name = prefab.name;
            }
            else
            {
                go = fallback != null ? fallback(parent) : new GameObject(path);
                if (go.transform.parent != parent)
                {
                    go.transform.SetParent(parent, false);
                }
            }

            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            return go;
        }

        /// <summary>
        /// Remove colisores que vieram com o modelo (colisores sao definidos em codigo).
        /// Imediato: a NavMesh e gerada no mesmo quadro e nao pode enxergar estes colisores.
        /// </summary>
        public static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>())
            {
                UnityEngine.Object.DestroyImmediate(c);
            }
        }
    }

    /// <summary>Atalhos para geometria provisoria.</summary>
    public static class Primitives
    {
        public static GameObject Box(string name, Transform parent, Vector3 localCenter, Vector3 size, Color color,
                                     bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Get(color);
            if (!keepCollider)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            }

            return go;
        }

        /// <summary>Colisor invisivel (paredes, piso, bloqueios de navegacao).</summary>
        public static BoxCollider Collider(string name, Transform parent, Vector3 localCenter, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        public static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
                                       Vector3 scale, Color color, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Get(color);
            if (!keepCollider)
            {
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            }

            return go;
        }
    }
}
