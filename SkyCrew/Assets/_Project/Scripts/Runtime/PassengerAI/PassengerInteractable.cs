using SkyCrew.Core;
using SkyCrew.Domain.Interaction;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Interaction;
using UnityEngine;

namespace SkyCrew.PassengerAI
{
    /// <summary>
    /// Interacao com passageiro: com item na mao = entregar; sem item = conversar (acalmar).
    /// A regra (aceitar ou nao) e decidida pela sessao do dominio.
    /// </summary>
    [RequireComponent(typeof(PassengerController))]
    public sealed class PassengerInteractable : MonoBehaviour, IInteractable
    {
        private PassengerController _passenger;

        private void Awake()
        {
            _passenger = GetComponent<PassengerController>();
        }

        private static bool Holding(Interactor i) => i.Hands != null && i.Hands.Held != null;

        public InteractionVerb GetVerb(Interactor interactor)
            => Holding(interactor) ? InteractionVerb.Deliver : InteractionVerb.Talk;

        public string GetPrompt(Interactor interactor)
        {
            string name = _passenger.DisplayName;
            if (Holding(interactor))
            {
                return $"Entregar {interactor.Hands.Held.DisplayName} para {name}";
            }

            return _passenger.IsReacting ? $"Acalmar {name}" : $"Conversar com {name}";
        }

        public string GetAcceptedItemType(Interactor interactor)
            => (_passenger.Brain != null ? SkyCrew.Domain.Match.ItemTypes.FromService(_passenger.Brain.PendingService) : null)
               ?? "__nada__";

        public bool IsAvailable(Interactor interactor)
        {
            if (_passenger.Brain == null || _passenger.Brain.State == PassengerStateId.Disabled)
            {
                return false;
            }

            if (Holding(interactor))
            {
                return GameBootstrap.Session != null
                       && GameBootstrap.Session.CanDeliverToPassenger(_passenger.Brain.Id, interactor.Hands.HeldItemType);
            }

            return true;
        }

        public int Priority => 3;
        public Vector3 InteractionPoint => transform.position + Vector3.up * 0.5f;

        public bool Execute(Interactor interactor)
        {
            var session = GameBootstrap.Session;
            if (session == null)
            {
                return false;
            }

            var animator = GetComponent<SkyCrew.Characters.ICharacterAnimation>();
            if (Holding(interactor))
            {
                var item = interactor.Hands.Take();
                if (session.TryDeliverToPassenger(_passenger.Brain.Id, item.ItemType))
                {
                    item.Despawn();
                    animator?.Talk();
                    interactor.Feedback($"{_passenger.DisplayName}: Obrigado!");
                    return true;
                }

                interactor.Hands.PickUp(item); // recusado: volta para a mao
                interactor.Feedback($"{_passenger.DisplayName} não quer isso");
                return false;
            }

            if (session.TryTalkToPassenger(_passenger.Brain.Id))
            {
                animator?.Talk();
                interactor.Feedback($"Você conversou com {_passenger.DisplayName}");
                return true;
            }

            return false;
        }
    }
}
