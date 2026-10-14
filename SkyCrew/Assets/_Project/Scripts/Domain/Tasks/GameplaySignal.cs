namespace SkyCrew.Domain.Tasks
{
    public enum SignalType
    {
        /// <summary>Jogador entregou um item a um alvo (ex.: agua ao passageiro).</summary>
        ItemDelivered,

        /// <summary>Um item foi colocado numa zona (ex.: mala no bagageiro).</summary>
        ItemPlacedInZone,

        /// <summary>Um item foi descartado (ex.: lixo na lixeira).</summary>
        ItemDisposed,

        /// <summary>Jogador conversou com um alvo (ex.: acalmar passageiro).</summary>
        TalkedTo,

        /// <summary>Acao generica nomeada (ex.: "fechar_bagageiro").</summary>
        ActionPerformed,
    }

    /// <summary>
    /// Fato ocorrido no mundo, emitido pela camada Unity e consumido pelos objetivos.
    /// Mantem as tarefas desacopladas dos objetos fisicos.
    /// </summary>
    public readonly struct GameplaySignal
    {
        public GameplaySignal(SignalType type, string actorId = null, string targetId = null,
                              string itemType = null, string zoneId = null, string actionId = null)
        {
            Type = type;
            ActorId = actorId;
            TargetId = targetId;
            ItemType = itemType;
            ZoneId = zoneId;
            ActionId = actionId;
        }

        public SignalType Type { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string ItemType { get; }
        public string ZoneId { get; }
        public string ActionId { get; }

        public static GameplaySignal Delivered(string itemType, string targetId, string actorId = null)
            => new GameplaySignal(SignalType.ItemDelivered, actorId, targetId, itemType);

        /// <param name="itemId">Instancia do item (vai em TargetId).</param>
        public static GameplaySignal PlacedInZone(string itemType, string zoneId, string actorId = null, string itemId = null)
            => new GameplaySignal(SignalType.ItemPlacedInZone, actorId, itemId, itemType, zoneId);

        /// <param name="itemId">Instancia do item (vai em TargetId).</param>
        public static GameplaySignal Disposed(string itemType, string zoneId = null, string actorId = null, string itemId = null)
            => new GameplaySignal(SignalType.ItemDisposed, actorId, itemId, itemType, zoneId);

        public static GameplaySignal Talked(string targetId, string actorId = null)
            => new GameplaySignal(SignalType.TalkedTo, actorId, targetId);

        public static GameplaySignal Action(string actionId, string targetId = null, string actorId = null)
            => new GameplaySignal(SignalType.ActionPerformed, actorId, targetId, actionId: actionId);

        public override string ToString()
            => $"{Type}(item={ItemType}, alvo={TargetId}, zona={ZoneId}, acao={ActionId})";
    }
}
