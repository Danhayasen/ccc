using SkyCrew.Core;
using SkyCrew.Domain.Content;
using SkyCrew.Domain.Interaction;
using SkyCrew.Domain.Tasks;
using SkyCrew.Player;
using UnityEngine;

namespace SkyCrew.Interaction
{
    /// <summary>
    /// Mancha de bebida no corredor. Limpa com o esfregao (E varias vezes). Quem passa
    /// correndo por cima escorrega (comedia fisica controlada: tropeco curto com recarga).
    /// </summary>
    public sealed class SpillZone : MonoBehaviour, IInteractable
    {
        private const float SlipCooldown = 2.5f;
        private const float SlipMinSpeed = 1.6f;

        private Transform _visual;
        private Vector3 _baseScale;
        private int _swipes;
        private float _nextSlip;

        public string SpillId { get; private set; }

        public void Configure(string spillId, Transform visual)
        {
            SpillId = spillId;
            _visual = visual;
            _baseScale = visual != null ? visual.localScale : Vector3.one;
        }

        public InteractionVerb GetVerb(Interactor interactor) => InteractionVerb.ApplyItem;
        public string GetPrompt(Interactor interactor) => "Esfregar a mancha";
        public string GetAcceptedItemType(Interactor interactor) => ItemTypesUnity.Mop;
        public bool IsAvailable(Interactor interactor) => _swipes < PrototypeScenario.CleanSwipes;
        public int Priority => 2;
        public Vector3 InteractionPoint => transform.position;

        public bool Execute(Interactor interactor)
        {
            _swipes++;
            GameBootstrap.Session?.Signal(GameplaySignal.Action(PrototypeScenario.ActionClean, SpillId, interactor.ActorId));
            float left = 1f - (float)_swipes / PrototypeScenario.CleanSwipes;
            if (_visual != null)
            {
                _visual.localScale = new Vector3(_baseScale.x * Mathf.Max(0.15f, left), _baseScale.y,
                                                 _baseScale.z * Mathf.Max(0.15f, left));
            }

            interactor.Feedback(left <= 0f ? "Brilhando!" : "Esfrega, esfrega...");
            return true;
        }

        private void OnTriggerStay(Collider other)
        {
            if (Time.time < _nextSlip || _swipes >= PrototypeScenario.CleanSwipes)
            {
                return;
            }

            var motor = other.GetComponent<PlayerMotor>();
            if (motor == null || motor.PlanarSpeed < SlipMinSpeed || motor.IsStumbling)
            {
                return;
            }

            _nextSlip = Time.time + SlipCooldown;
            motor.Stumble(motor.transform.forward * 3f, 0.7f);
            other.GetComponent<Interactor>()?.Feedback("Escorregou!");
        }
    }
}
