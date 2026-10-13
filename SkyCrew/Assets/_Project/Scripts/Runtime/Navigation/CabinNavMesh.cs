using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SkyCrew.Navigation
{
    /// <summary>
    /// Gera a malha de navegacao em tempo de execucao a partir dos COLISORES do cenario
    /// (NavMeshBuilder do modulo AI nativo; dispensa o pacote AI Navigation nesta fase).
    /// O raio do agente e reduzido para caber no corredor estreito (~0,62 m).
    /// </summary>
    public sealed class CabinNavMesh : MonoBehaviour
    {
        public const float AgentRadius = 0.22f;
        public const float AgentHeight = 1.6f;

        private NavMeshDataInstance _instance;
        private NavMeshData _data;

        public bool IsBuilt { get; private set; }
        public int TriangleCount { get; private set; }

        public bool Build(Bounds bounds, int layerMask = ~0)
        {
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, layerMask, NavMeshCollectGeometry.PhysicsColliders, 0,
                                          new List<NavMeshBuildMarkup>(), sources);

            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = AgentRadius;
            settings.agentHeight = AgentHeight;
            settings.agentClimb = 0.2f;
            settings.agentSlope = 30f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = AgentRadius / 3f;

            _data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (_data == null)
            {
                Debug.LogError("[SkyCrew] Falha ao gerar NavMesh");
                return false;
            }

            _instance = NavMesh.AddNavMeshData(_data);
            var tri = NavMesh.CalculateTriangulation();
            TriangleCount = tri.indices.Length / 3;
            IsBuilt = TriangleCount > 0;
            if (!IsBuilt)
            {
                Debug.LogError("[SkyCrew] NavMesh vazia: verifique os colisores do piso");
            }

            return IsBuilt;
        }

        /// <summary>Confere se ha rota completa entre dois pontos (usado no diagnostico).</summary>
        public static bool HasCompletePath(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(from, out var a, 0.6f, NavMesh.AllAreas)
                || !NavMesh.SamplePosition(to, out var b, 0.6f, NavMesh.AllAreas))
            {
                return false;
            }

            return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path)
                   && path.status == NavMeshPathStatus.PathComplete;
        }

        private void OnDestroy()
        {
            if (_instance.valid)
            {
                NavMesh.RemoveNavMeshData(_instance);
            }
        }
    }

    /// <summary>
    /// Controle do corredor estreito: limita quantos passageiros andam ao mesmo tempo.
    /// Quem nao recebe a vez espera parado (fila por ordem de pedido).
    /// </summary>
    public sealed class AisleTrafficCoordinator
    {
        private readonly int _maxWalkers;
        private readonly List<object> _walking = new List<object>();
        private readonly List<object> _queue = new List<object>();

        public AisleTrafficCoordinator(int maxWalkers = 2)
        {
            _maxWalkers = Mathf.Max(1, maxWalkers);
        }

        public int Walking => _walking.Count;
        public int Waiting => _queue.Count;

        /// <summary>Pede passagem; retorna true quando liberado (chamar a cada quadro enquanto espera).</summary>
        public bool Request(object who)
        {
            if (_walking.Contains(who))
            {
                return true;
            }

            if (!_queue.Contains(who))
            {
                _queue.Add(who);
            }

            if (_walking.Count < _maxWalkers && _queue[0] == who)
            {
                _queue.RemoveAt(0);
                _walking.Add(who);
                return true;
            }

            return false;
        }

        public void Release(object who)
        {
            _walking.Remove(who);
            _queue.Remove(who);
        }
    }
}
