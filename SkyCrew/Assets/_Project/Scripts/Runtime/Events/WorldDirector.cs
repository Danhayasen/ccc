using System.Collections.Generic;
using SkyCrew.Aircraft;
using SkyCrew.Core;
using SkyCrew.Domain.Content;
using SkyCrew.Interaction;
using SkyCrew.PassengerAI;
using SkyCrew.Physics;
using UnityEngine;

namespace SkyCrew.Events
{
    /// <summary>
    /// Materializa no mundo 3D o que o cenario pede (lixo, mancha, carrinho, mala caindo) e
    /// limpa quando o problema termina. Posicoes sao decididas aqui; regras ficam no dominio.
    /// </summary>
    public sealed class WorldDirector : MonoBehaviour
    {
        private readonly Dictionary<string, GameObject> _spawned = new Dictionary<string, GameObject>();
        private GameBootstrap _game;

        public IReadOnlyDictionary<string, GameObject> Spawned => _spawned;

        public void Bind(GameBootstrap game, PrototypeScenario scenario)
        {
            _game = game;
            scenario.WorldRequested += OnRequested;
            scenario.WorldCleared += OnCleared;
        }

        private static float RandomAisleZ(int firstRow = 2, int lastRow = 5)
            => Random.Range(CabinLayout.RowZ(lastRow), CabinLayout.RowZ(firstRow));

        private Vector3? PassengerPosition(string passengerId)
        {
            if (passengerId == null || _game == null)
            {
                return null;
            }

            foreach (var p in _game.Passengers)
            {
                if (p != null && p.Brain != null && p.Brain.Id == passengerId)
                {
                    return p.transform.position;
                }
            }

            return null;
        }

        private void OnRequested(WorldRequest r)
        {
            switch (r.Kind)
            {
                case WorldRequestKind.Trash:
                {
                    Vector3? near = PassengerPosition(r.PassengerId);
                    for (int i = 0; i < r.ItemIds.Count; i++)
                    {
                        Vector3 pos = near.HasValue
                            ? new Vector3(near.Value.x * 0.5f, 1.0f, near.Value.z + Random.Range(-0.1f, 0.1f))
                            : new Vector3(Random.Range(-0.15f, 0.15f), 1.6f, RandomAisleZ(1, CabinLayout.Rows));
                        var item = ItemFactory.Create(ItemTypesUnity.Trash, pos, Random.rotation, r.ItemIds[i]);
                        // sai voando um pouquinho (comedia), com impulso limitado
                        item?.ApplyImpulse(new Vector3(-Mathf.Sign(pos.x + 0.001f) * 0.4f, 0.3f, 0f), Random.insideUnitSphere * 0.05f);
                    }

                    break;
                }

                case WorldRequestKind.Spill:
                    _spawned[r.Id] = CreateSpill(r.Id, new Vector3(0f, 0.005f, RandomAisleZ()));
                    break;

                case WorldRequestKind.Cart:
                    _spawned[r.Id] = CreateCart(r.Id, new Vector3(0f, 0.02f, RandomAisleZ(2, 4)));
                    break;

                case WorldRequestKind.FallenBag:
                {
                    int side = Random.value < 0.5f ? -1 : 1;
                    var pos = new Vector3(side * 1.05f, 1.9f, RandomAisleZ(1, CabinLayout.Rows));
                    var bag = ItemFactory.Create(ItemTypesUnity.Suitcase, pos, Quaternion.Euler(0f, 90f, 0f), r.ItemIds[0]);
                    bag?.ApplyImpulse(new Vector3(-side * 15f, 5f, 0f), new Vector3(0f, 0f, side * 4f)); // despenca no corredor
                    break;
                }
            }
        }

        private void OnCleared(WorldRequest r)
        {
            if (!_spawned.TryGetValue(r.Id, out var go))
            {
                return;
            }

            _spawned.Remove(r.Id);
            if (go != null)
            {
                Destroy(go);
            }
        }

        private static GameObject CreateSpill(string id, Vector3 position)
        {
            var root = new GameObject("Mancha_" + id);
            root.transform.position = position;
            var visual = ModelLibrary.Spawn("Props/Mancha", root.transform, Vector3.zero, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                p => Primitives.Shape(PrimitiveType.Cylinder, "Mancha", p, Vector3.zero, new Vector3(0.9f, 0.004f, 0.6f),
                                      MaterialLibrary.Hex(0xE89A3C)));
            ModelLibrary.StripColliders(visual);
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.3f, 0f);
            trigger.size = new Vector3(0.7f, 0.6f, 0.9f);
            root.AddComponent<SpillZone>().Configure(id, visual.transform);
            return root;
        }

        private static GameObject CreateCart(string id, Vector3 position)
        {
            var root = new GameObject("Carrinho_" + id);
            root.transform.position = position;
            var visual = ModelLibrary.Spawn("Props/Carrinho", root.transform, Vector3.zero, Quaternion.identity,
                p => Primitives.Shape(PrimitiveType.Cube, "Carrinho", p, new Vector3(0f, 0.5f, 0f), new Vector3(0.4f, 1.0f, 0.8f),
                                      MaterialLibrary.Cinza));
            ModelLibrary.StripColliders(visual);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.5f, 0f);
            box.size = new Vector3(0.4f, 1.0f, 0.8f);
            root.AddComponent<Rigidbody>();
            root.AddComponent<ServiceCart>().Configure(id);
            return root;
        }

        /// <summary>Remove tudo que foi criado (reinicio da partida).</summary>
        public void ClearAll()
        {
            foreach (var go in _spawned.Values)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _spawned.Clear();
        }
    }
}
