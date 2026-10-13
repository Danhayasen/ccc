using System;
using SkyCrew.Domain.Interaction;
using SkyCrew.Interaction;
using UnityEngine;

namespace SkyCrew.Physics
{
    /// <summary>Parametros fisicos por tipo de objeto (nada de "um ajuste para todos").</summary>
    [Serializable]
    public struct PhysicsTuning
    {
        public float Mass;
        public float LinearDamping;
        public float AngularDamping;
        public float MaxSpeed;
        public float MaxAngularSpeed;
        public float MaxImpulse;

        public static PhysicsTuning Light => new PhysicsTuning
        {
            Mass = 0.4f, LinearDamping = 0.2f, AngularDamping = 0.3f, MaxSpeed = 6f, MaxAngularSpeed = 18f, MaxImpulse = 1.2f,
        };

        public static PhysicsTuning Heavy => new PhysicsTuning
        {
            Mass = 9f, LinearDamping = 0.6f, AngularDamping = 1.2f, MaxSpeed = 4f, MaxAngularSpeed = 8f, MaxImpulse = 25f,
        };
    }

    /// <summary>
    /// Objeto que o jogador pega, carrega, solta e entrega. Enquanto esta na mao fica
    /// cinematico e sem colisao (nao atravessa nem empurra o corpo); ao soltar a fisica volta
    /// com velocidade limitada. Objetos que caem para fora do mundo voltam ao ponto de origem.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarryableItem : MonoBehaviour, IInteractable
    {
        [SerializeField] private string _itemType = "item";
        [SerializeField] private string _displayName = "objeto";
        [SerializeField] private PhysicsTuning _tuning = PhysicsTuning.Light;
        [SerializeField] private Vector3 _holdOffset = Vector3.zero;

        private Rigidbody _body;
        private Collider[] _colliders;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private bool _locked;

        public string ItemType => _itemType;
        public string DisplayName => _displayName;
        public bool IsHeld { get; private set; }
        public bool IsLocked => _locked;
        public Rigidbody Body => _body;

        /// <summary>Chamado ao sair de cena (consumido ou descartado).</summary>
        public event Action<CarryableItem> Despawned;

        public void Configure(string itemType, string displayName, PhysicsTuning tuning, Vector3 holdOffset)
        {
            _itemType = itemType;
            _displayName = displayName;
            _tuning = tuning;
            _holdOffset = holdOffset;
            ApplyTuning();
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>();
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            ApplyTuning();
            MarkSpawn();
        }

        private void ApplyTuning()
        {
            if (_body == null)
            {
                return;
            }

            _body.mass = _tuning.Mass;
#if UNITY_6000_0_OR_NEWER
            _body.linearDamping = _tuning.LinearDamping;
            _body.angularDamping = _tuning.AngularDamping;
#else
            _body.drag = _tuning.LinearDamping;
            _body.angularDrag = _tuning.AngularDamping;
#endif
            _body.maxAngularVelocity = _tuning.MaxAngularSpeed;
        }

        public void MarkSpawn()
        {
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        // --- IInteractable --------------------------------------------------------
        public InteractionVerb GetVerb(Interactor interactor) => InteractionVerb.PickUp;
        public string GetPrompt(Interactor interactor) => "Pegar " + _displayName;
        public string GetAcceptedItemType(Interactor interactor) => null;
        public bool IsAvailable(Interactor interactor) => !IsHeld && !_locked;
        public int Priority => 1;
        public Vector3 InteractionPoint => transform.position;

        public bool Execute(Interactor interactor)
        {
            return interactor.Hands != null && interactor.Hands.PickUp(this);
        }

        // --- Ciclo de manipulacao ---------------------------------------------------
        internal void AttachTo(Transform anchor)
        {
            IsHeld = true;
            SetCollidersEnabled(false);
            SetVelocity(Vector3.zero, Vector3.zero);
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.None;
            transform.SetParent(anchor, false);
            transform.localPosition = _holdOffset;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>Solta no mundo, restaurando a fisica sem forcas residuais exageradas.</summary>
        internal void Release(Vector3 position, Vector3 velocity)
        {
            transform.SetParent(null, true);
            transform.position = position;
            IsHeld = false;
            _body.isKinematic = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            SetCollidersEnabled(true);
            SetVelocity(Vector3.ClampMagnitude(velocity, _tuning.MaxSpeed * 0.5f), Vector3.zero);
        }

        /// <summary>Prende num lugar (ex.: dentro do bagageiro). Deixa de ser interativo.</summary>
        public void LockInto(Transform slot)
        {
            IsHeld = false;
            _locked = true;
            _body.isKinematic = true;
            SetCollidersEnabled(false);
            transform.SetParent(slot, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        public void Despawn()
        {
            Despawned?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>Impulso limitado (turbulencia, empurroes).</summary>
        public void ApplyImpulse(Vector3 impulse, Vector3 torque)
        {
            if (IsHeld || _locked || _body.isKinematic)
            {
                return;
            }

            _body.AddForce(Vector3.ClampMagnitude(impulse, _tuning.MaxImpulse), ForceMode.Impulse);
            _body.AddTorque(Vector3.ClampMagnitude(torque, _tuning.MaxImpulse), ForceMode.Impulse);
        }

        public void ResetToSpawn()
        {
            if (IsHeld || _locked)
            {
                return;
            }

            SetVelocity(Vector3.zero, Vector3.zero);
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
        }

        private void FixedUpdate()
        {
            if (IsHeld || _locked || _body.isKinematic)
            {
                return;
            }

            // Sem explosoes de forca: limita velocidades.
            Vector3 v = GetVelocity();
            if (v.sqrMagnitude > _tuning.MaxSpeed * _tuning.MaxSpeed)
            {
                SetVelocity(v.normalized * _tuning.MaxSpeed, _body.angularVelocity);
            }

            if (transform.position.y < -3f)
            {
                ResetToSpawn(); // caiu para fora do mundo
            }
        }

        private Vector3 GetVelocity()
        {
#if UNITY_6000_0_OR_NEWER
            return _body.linearVelocity;
#else
            return _body.velocity;
#endif
        }

        private void SetVelocity(Vector3 linear, Vector3 angular)
        {
            if (_body.isKinematic)
            {
                return;
            }

#if UNITY_6000_0_OR_NEWER
            _body.linearVelocity = linear;
#else
            _body.velocity = linear;
#endif
            _body.angularVelocity = angular;
        }

        private void SetCollidersEnabled(bool enabled)
        {
            foreach (var c in _colliders)
            {
                if (c != null)
                {
                    c.enabled = enabled;
                }
            }
        }
    }
}
