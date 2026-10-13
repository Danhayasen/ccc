using SkyCrew.Core;
using SkyCrew.Domain.Content;
using SkyCrew.Domain.Interaction;
using SkyCrew.Domain.Tasks;
using SkyCrew.Interaction;
using SkyCrew.Player;
using UnityEngine;
using UnityEngine.AI;

namespace SkyCrew.Physics
{
    /// <summary>
    /// Carrinho de servico. Pesado, empurravel e tambem "puxavel": E segura a alca e o carrinho
    /// mantem a mesma posicao relativa ao jogador (deslocamento fixo no mundo, medido ao pegar),
    /// entao da para puxar ou empurrar em linha reta no corredor sem ele girar por dentro do
    /// jogador ao virar. E de novo solta. Bloqueia a navegacao dos passageiros (carving).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ServiceCart : MonoBehaviour, IInteractable
    {
        private const float MinHoldDistance = 0.7f;
        private const float MaxHoldDistance = 1.0f;
        private const float DragSpeedMultiplier = 0.6f;
        private const float MaxSpeed = 2.5f;

        private Rigidbody _body;
        private PlayerMotor _holder;
        private Vector3 _grabOffset;
        private bool _returned;

        public string CartId { get; private set; }
        public bool IsHeld => _holder != null;
        public bool IsReturned => _returned;

        public void Configure(string cartId)
        {
            CartId = cartId;
            _body = GetComponent<Rigidbody>();
            _body.mass = 40f;
#if UNITY_6000_0_OR_NEWER
            _body.linearDamping = 3f;
            _body.angularDamping = 6f;
#else
            _body.drag = 3f;
            _body.angularDrag = 6f;
#endif
            _body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _body.interpolation = RigidbodyInterpolation.Interpolate;

            var obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            var box = GetComponent<BoxCollider>();
            obstacle.center = box != null ? box.center : Vector3.up * 0.5f;
            obstacle.size = box != null ? box.size : new Vector3(0.4f, 1f, 0.8f);
            obstacle.carving = true;
        }

        public InteractionVerb GetVerb(Interactor interactor) => InteractionVerb.Use;
        public string GetPrompt(Interactor interactor) => IsHeld ? "Soltar o carrinho" : "Puxar o carrinho";
        public string GetAcceptedItemType(Interactor interactor) => null;
        public bool IsAvailable(Interactor interactor) => !_returned && (interactor.Hands == null || interactor.Hands.Held == null);
        public int Priority => 2;
        public Vector3 InteractionPoint => transform.position + Vector3.up * 0.5f;

        public bool Execute(Interactor interactor)
        {
            if (IsHeld)
            {
                Release();
                return true;
            }

            _holder = interactor.GetComponent<PlayerMotor>();
            if (_holder == null)
            {
                return false;
            }

            Vector3 offset = transform.position - _holder.transform.position;
            offset.y = 0f;
            float distance = Mathf.Clamp(offset.magnitude, MinHoldDistance, MaxHoldDistance);
            _grabOffset = offset.sqrMagnitude > 1e-4f ? offset.normalized * distance : _holder.transform.forward * distance;
            _holder.SpeedMultiplier = DragSpeedMultiplier;
            _body.isKinematic = true;
            return true;
        }

        public void Release()
        {
            if (_holder != null)
            {
                _holder.SpeedMultiplier = 1f;
            }

            _holder = null;
            if (!_returned)
            {
                _body.isKinematic = false;
            }
        }

        /// <summary>Empurrao vindo de colisao (limitado).</summary>
        public void Push(Vector3 force)
        {
            if (IsHeld || _returned || _body.isKinematic)
            {
                return;
            }

            force.y = 0f;
            _body.AddForce(Vector3.ClampMagnitude(force, 300f), ForceMode.Force);
        }

        private void FixedUpdate()
        {
            if (_holder != null)
            {
                // Mantem o deslocamento do momento em que foi pego, sem atravessar paredes.
                Vector3 target = _holder.transform.position + _grabOffset;
                target.y = transform.position.y;
                Vector3 step = Vector3.ClampMagnitude(target - _body.position, MaxSpeed * Time.fixedDeltaTime);
                if (!UnityEngine.Physics.BoxCast(_body.position + Vector3.up * 0.5f, new Vector3(0.18f, 0.4f, 0.38f),
                                                 step.normalized, transform.rotation, step.magnitude + 0.02f, ~0,
                                                 QueryTriggerInteraction.Ignore)
                    || step.sqrMagnitude < 1e-6f)
                {
                    _body.MovePosition(_body.position + step);
                }

                // Se o jogador se afastar demais (ficou preso), solta sozinho.
                Vector3 gap = _holder.transform.position + _grabOffset - _body.position;
                gap.y = 0f;
                if (gap.magnitude > 1.2f)
                {
                    Release();
                }

                return;
            }

            if (transform.position.y < -3f)
            {
                transform.position = CabinLayoutPoint();
            }
        }

        private static Vector3 CabinLayoutPoint() => new Vector3(0f, 0.05f, SkyCrew.Aircraft.CabinLayout.GalleyStartZ + 0.6f);

        /// <summary>Chamado pela zona da cozinha. Avisa a tarefa uma unica vez e trava o carrinho.</summary>
        public void MarkReturned()
        {
            if (_returned)
            {
                return;
            }

            _returned = true;
            Release();
            _body.isKinematic = true;
            GameBootstrap.Session?.Signal(GameplaySignal.PlacedInZone(ItemTypesUnity.Cart, PrototypeScenario.ZoneGalley,
                                                                      "jogador", CartId));
        }

        private void OnDestroy()
        {
            if (_holder != null)
            {
                _holder.SpeedMultiplier = 1f;
            }
        }
    }

    /// <summary>Area da cozinha onde o carrinho deve voltar.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CartReturnZone : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            other.GetComponentInParent<ServiceCart>()?.MarkReturned();
        }
    }
}
