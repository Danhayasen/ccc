using System.Collections.Generic;
using SkyCrew.Core;
using SkyCrew.Domain.Interaction;
using SkyCrew.Physics;
using UnityEngine;

namespace SkyCrew.Interaction
{
    /// <summary>
    /// Descobre alvos proximos, escolhe o melhor, mostra a acao e executa ao apertar E.
    /// A acao e validada ao exibir E de novo ao executar.
    /// </summary>
    public sealed class Interactor : MonoBehaviour
    {
        [SerializeField] private float _range = 1.8f;
        [SerializeField] private float _scanInterval = 0.1f;
        [SerializeField] private LayerMask _mask = ~0;

        private readonly Collider[] _hits = new Collider[32];
        private readonly List<IInteractable> _found = new List<IInteractable>();
        private readonly List<Collider> _foundColliders = new List<Collider>();
        private readonly List<InteractionCandidate> _candidates = new List<InteractionCandidate>();
        private readonly List<IInteractable> _candidateTargets = new List<IInteractable>();
        private float _scanTimer;

        public CarrySocket Hands { get; private set; }
        public IInteractable Current { get; private set; }
        public InteractionCheck CurrentCheck { get; private set; }
        public float Range => _range;
        public string ActorId { get; set; } = "jogador";

        /// <summary>Ultima mensagem de feedback ("Maos ocupadas"...).</summary>
        public string LastFeedback { get; private set; }

        public float LastFeedbackTime { get; private set; }

        private void Awake()
        {
            Hands = GetComponent<CarrySocket>();
        }

        private void Update()
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer > 0f)
            {
                return;
            }

            _scanTimer = _scanInterval;
            Scan();
        }

        private Vector3 Eye => transform.position + Vector3.up * 1.0f;

        private void Scan()
        {
            var session = GameBootstrap.Session;
            Current = null;
            if (session == null)
            {
                return;
            }

            int count = UnityEngine.Physics.OverlapSphereNonAlloc(Eye, _range + 0.5f, _hits, _mask, QueryTriggerInteraction.Collide);
            _found.Clear();
            _foundColliders.Clear();
            for (int i = 0; i < count; i++)
            {
                var interactable = _hits[i].GetComponentInParent<IInteractable>();
                if (interactable == null || _found.Contains(interactable))
                {
                    continue;
                }

                if (Hands != null && Hands.Held != null && ReferenceEquals(interactable, Hands.Held))
                {
                    continue; // nao interage com o que ja esta na mao
                }

                _found.Add(interactable);
                _foundColliders.Add(_hits[i]);
            }

            _candidates.Clear();
            _candidateTargets.Clear();
            Vector3 forward = transform.forward;
            forward.y = 0f;
            for (int i = 0; i < _found.Count; i++)
            {
                var target = _found[i];
                float distance = DistanceTo(target, _foundColliders[i]);
                Vector3 to = target.InteractionPoint - transform.position;
                to.y = 0f;
                float angle = to.sqrMagnitude < 0.0001f ? 0f : Vector3.Angle(forward, to);
                var check = Validate(target, distance);
                _candidates.Add(new InteractionCandidate(distance, angle, target.Priority, check.Allowed));
                _candidateTargets.Add(target);
            }

            int best = InteractionTargetSelector.SelectBest(_candidates);
            if (best < 0)
            {
                return;
            }

            Current = _candidateTargets[best];
            CurrentCheck = Validate(Current, _candidates[best].Distance);
        }

        /// <summary>
        /// Distancia horizontal ate o ponto mais proximo do colisor (objetos compridos, como
        /// bagageiros, sao alcancaveis pelas pontas, nao so pelo centro).
        /// </summary>
        private float DistanceTo(IInteractable target, Collider collider)
        {
            Vector3 point = collider != null && collider.enabled ? collider.ClosestPoint(Eye) : target.InteractionPoint;
            Vector3 to = point - transform.position;
            return new Vector2(to.x, to.z).magnitude;
        }

        private InteractionCheck Validate(IInteractable target, float distance)
        {
            var session = GameBootstrap.Session;
            var query = new InteractionQuery(
                target.GetVerb(this), distance, _range, session.State.Current,
                Hands != null ? Hands.HeldItemType : null,
                target.GetAcceptedItemType(this), target.IsAvailable(this));
            return InteractionRules.Validate(query);
        }

        /// <summary>Chamado pelo controle do jogador ao apertar Interagir.</summary>
        public void TryInteract()
        {
            Scan(); // estado fresco
            if (Current == null)
            {
                if (Hands != null && Hands.Held != null)
                {
                    Hands.Drop(); // sem alvo: solta o que esta segurando
                }

                return;
            }

            int index = _found.IndexOf(Current);
            float distance = index >= 0 ? DistanceTo(Current, _foundColliders[index]) : float.MaxValue;
            var check = Validate(Current, distance); // revalida
            if (!check.Allowed)
            {
                Feedback(check.Reason);
                return;
            }

            Current.Execute(this);
            Scan();
        }

        public void Feedback(string message)
        {
            LastFeedback = message;
            LastFeedbackTime = Time.time;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position + Vector3.up, _range);
        }
    }
}
