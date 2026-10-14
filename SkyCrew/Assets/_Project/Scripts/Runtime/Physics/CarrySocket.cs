using System;
using UnityEngine;

namespace SkyCrew.Physics
{
    /// <summary>Maos do personagem: segura no maximo um objeto por vez.</summary>
    public sealed class CarrySocket : MonoBehaviour
    {
        [SerializeField] private Transform _anchor;
        [SerializeField] private float _dropDistance = 0.6f;

        public CarryableItem Held { get; private set; }
        public string HeldItemType => Held != null ? Held.ItemType : null;

        public event Action<CarryableItem> Changed;

        public void SetAnchor(Transform anchor) => _anchor = anchor;

        public bool PickUp(CarryableItem item)
        {
            if (Held != null || item == null || item.IsHeld || item.IsLocked)
            {
                return false;
            }

            Held = item;
            item.AttachTo(_anchor != null ? _anchor : transform);
            item.Despawned += OnHeldDespawned;
            Changed?.Invoke(item);
            return true;
        }

        /// <summary>Tira o objeto da mao sem solta-lo no mundo (entrega, descarte, guardar).</summary>
        public CarryableItem Take()
        {
            var item = Held;
            if (item == null)
            {
                return null;
            }

            item.Despawned -= OnHeldDespawned;
            Held = null;
            Changed?.Invoke(null);
            return item;
        }

        /// <summary>Solta a frente do personagem, evitando posicoes dentro de paredes.</summary>
        public void Drop(Vector3 inheritedVelocity = default)
        {
            var item = Take();
            if (item == null)
            {
                return;
            }

            Vector3 origin = transform.position + Vector3.up * 0.9f;
            Vector3 target = origin + transform.forward * _dropDistance;
            if (UnityEngine.Physics.SphereCast(origin, 0.12f, transform.forward, out var hit, _dropDistance,
                                               ~0, QueryTriggerInteraction.Ignore))
            {
                target = origin + transform.forward * Mathf.Max(0f, hit.distance - 0.15f);
            }

            item.Release(target, inheritedVelocity);
        }

        private void OnHeldDespawned(CarryableItem item)
        {
            if (Held == item)
            {
                Held = null;
                Changed?.Invoke(null);
            }
        }
    }
}
