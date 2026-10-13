using System;
using SkyCrew.Core;
using SkyCrew.Domain.Interaction;
using SkyCrew.Domain.Tasks;
using SkyCrew.Physics;
using UnityEngine;

namespace SkyCrew.Interaction
{
    /// <summary>
    /// Fonte de itens (geladeira, gaveta de lanches, armario de cobertores, esfregao).
    /// Se estiver ligada a um equipamento quebrado, a acao vira "Consertar" (bater).
    /// </summary>
    public sealed class ItemDispenser : MonoBehaviour, IInteractable
    {
        [SerializeField] private string _displayName = "água";
        [SerializeField] private int _maxLooseItems = 5;
        [SerializeField] private float _cooldown = 0.5f;

        private Func<CarryableItem> _factory;
        private string _equipmentId;
        private GameObject _brokenMarker;
        private int _loose;
        private float _readyAt;

        public void Configure(string displayName, Func<CarryableItem> factory, string equipmentId = null,
                              GameObject brokenMarker = null)
        {
            _displayName = displayName;
            _factory = factory;
            _equipmentId = equipmentId;
            _brokenMarker = brokenMarker;
        }

        public bool IsBroken => _equipmentId != null && GameBootstrap.Session != null
                                && !GameBootstrap.Session.IsEquipmentAvailable(_equipmentId);

        private void Update()
        {
            if (_brokenMarker != null)
            {
                _brokenMarker.SetActive(IsBroken && Mathf.Repeat(Time.time, 0.6f) < 0.35f);
            }
        }

        public InteractionVerb GetVerb(Interactor interactor) => IsBroken ? InteractionVerb.Use : InteractionVerb.Dispense;
        public string GetPrompt(Interactor interactor) => IsBroken ? "Consertar (dar uma batida)" : "Pegar " + _displayName;
        public string GetAcceptedItemType(Interactor interactor) => null;

        public bool IsAvailable(Interactor interactor)
            => IsBroken || (_factory != null && _loose < _maxLooseItems && Time.time >= _readyAt);

        public int Priority => 2;
        public Vector3 InteractionPoint => transform.position;

        public bool Execute(Interactor interactor)
        {
            if (IsBroken)
            {
                GameBootstrap.Session.Signal(GameplaySignal.Action(SkyCrew.Domain.Content.PrototypeScenario.ActionRepair,
                                                                   _equipmentId, interactor.ActorId));
                interactor.Feedback(GameBootstrap.Session.IsEquipmentAvailable(_equipmentId) ? "Voltou a funcionar!" : "BAM!");
                return true;
            }

            var item = _factory();
            if (item == null)
            {
                return false;
            }

            _loose++;
            _readyAt = Time.time + _cooldown;
            item.Despawned += _ => _loose--;
            if (!interactor.Hands.PickUp(item))
            {
                item.Despawn();
                return false;
            }

            return true;
        }
    }

    /// <summary>Lixeira: descarta o item segurado e avisa as tarefas.</summary>
    public sealed class DisposalBin : MonoBehaviour, IInteractable
    {
        [SerializeField] private string _acceptedItemType = ItemTypesUnity.Trash;
        [SerializeField] private string _zoneId = "lixeira";

        public InteractionVerb GetVerb(Interactor interactor) => InteractionVerb.Dispose;
        public string GetPrompt(Interactor interactor) => "Jogar no lixo";
        public string GetAcceptedItemType(Interactor interactor) => _acceptedItemType;
        public bool IsAvailable(Interactor interactor) => true;
        public int Priority => 2;
        public Vector3 InteractionPoint => transform.position;

        public bool Execute(Interactor interactor)
        {
            var item = interactor.Hands.Take();
            if (item == null)
            {
                return false;
            }

            GameBootstrap.Session?.Signal(GameplaySignal.Disposed(item.ItemType, _zoneId, interactor.ActorId, item.InstanceId));
            item.Despawn();
            return true;
        }
    }

    /// <summary>Zona onde um item e guardado (bagageiro). Cada vaga recebe um item.</summary>
    public sealed class ItemDropZone : MonoBehaviour, IInteractable
    {
        [SerializeField] private string _zoneId = "bagageiro";
        [SerializeField] private string _acceptedItemType = ItemTypesUnity.Suitcase;
        [SerializeField] private string _prompt = "Guardar no bagageiro";
        [SerializeField] private Transform[] _slots = Array.Empty<Transform>();

        private CarryableItem[] _stored = Array.Empty<CarryableItem>();

        public void Configure(string zoneId, string acceptedItemType, string prompt, Transform[] slots)
        {
            _zoneId = zoneId;
            _acceptedItemType = acceptedItemType;
            _prompt = prompt;
            _slots = slots;
            _stored = new CarryableItem[slots.Length];
        }

        public InteractionVerb GetVerb(Interactor interactor) => InteractionVerb.PlaceInZone;
        public string GetPrompt(Interactor interactor) => _prompt;
        public string GetAcceptedItemType(Interactor interactor) => _acceptedItemType;
        public bool IsAvailable(Interactor interactor) => FreeSlot() >= 0;
        public int Priority => 1;
        public Vector3 InteractionPoint => transform.position;

        private int FreeSlot()
        {
            if (_stored.Length != _slots.Length)
            {
                _stored = new CarryableItem[_slots.Length];
            }

            for (int i = 0; i < _stored.Length; i++)
            {
                if (_stored[i] == null)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool Execute(Interactor interactor)
        {
            int slot = FreeSlot();
            if (slot < 0)
            {
                return false;
            }

            var item = interactor.Hands.Take();
            if (item == null)
            {
                return false;
            }

            _stored[slot] = item;
            item.LockInto(_slots[slot]);
            GameBootstrap.Session?.Signal(GameplaySignal.PlacedInZone(item.ItemType, _zoneId, interactor.ActorId, item.InstanceId));
            return true;
        }
    }

    /// <summary>Espelho dos ids de item do dominio (para campos serializados).</summary>
    public static class ItemTypesUnity
    {
        public const string Water = SkyCrew.Domain.Match.ItemTypes.Water;
        public const string Suitcase = SkyCrew.Domain.Match.ItemTypes.Suitcase;
        public const string Trash = SkyCrew.Domain.Match.ItemTypes.Trash;
        public const string Snack = SkyCrew.Domain.Match.ItemTypes.Snack;
        public const string Blanket = SkyCrew.Domain.Match.ItemTypes.Blanket;
        public const string Mop = SkyCrew.Domain.Match.ItemTypes.Mop;
        public const string Cart = SkyCrew.Domain.Match.ItemTypes.Cart;
    }
}
