using System;
using System.Collections.Generic;

namespace SkyCrew.Domain.Tasks
{
    /// <summary>Estado de execucao de um objetivo. Novos tipos sao adicionados sem mudar o TaskManager.</summary>
    public interface ITaskObjective
    {
        string Description { get; }

        /// <summary>Progresso em [0, 1].</summary>
        float Progress { get; }

        bool IsComplete { get; }

        /// <summary>Processa um sinal; retorna true se o progresso mudou.</summary>
        bool Handle(in GameplaySignal signal);
    }

    /// <summary>Definicao imutavel de um objetivo; cria um estado novo a cada ativacao.</summary>
    public abstract class ObjectiveDefinition
    {
        protected ObjectiveDefinition(string description)
        {
            Description = description;
        }

        public string Description { get; }

        public abstract ITaskObjective CreateRuntime();
    }

    internal static class FieldMatch
    {
        /// <summary>null/vazio na definicao significa "qualquer".</summary>
        public static bool Field(string expected, string actual)
        {
            return string.IsNullOrEmpty(expected) || string.Equals(expected, actual, StringComparison.Ordinal);
        }
    }

    /// <summary>Base para objetivos que contam ocorrencias de sinais que batem com um filtro.</summary>
    public abstract class CountingObjective : ITaskObjective
    {
        private readonly int _required;
        private int _count;

        protected CountingObjective(string description, int required)
        {
            Description = description;
            _required = Math.Max(1, required);
        }

        public string Description { get; }
        public int Count => _count;
        public int Required => _required;
        public float Progress => (float)_count / _required;
        public bool IsComplete => _count >= _required;

        protected abstract bool Matches(in GameplaySignal signal);

        public bool Handle(in GameplaySignal signal)
        {
            if (IsComplete || !Matches(signal))
            {
                return false;
            }

            _count++;
            return true;
        }
    }

    // --- Entregar um item a um alvo ------------------------------------------
    public sealed class DeliverItemObjectiveDefinition : ObjectiveDefinition
    {
        public DeliverItemObjectiveDefinition(string description, string itemType, string targetId, int count = 1)
            : base(description)
        {
            ItemType = itemType;
            TargetId = targetId;
            Count = count;
        }

        public string ItemType { get; }
        public string TargetId { get; }
        public int Count { get; }

        public override ITaskObjective CreateRuntime() => new Runtime(this);

        private sealed class Runtime : CountingObjective
        {
            private readonly DeliverItemObjectiveDefinition _def;

            public Runtime(DeliverItemObjectiveDefinition def) : base(def.Description, def.Count)
            {
                _def = def;
            }

            protected override bool Matches(in GameplaySignal s)
                => s.Type == SignalType.ItemDelivered && FieldMatch.Field(_def.ItemType, s.ItemType)
                   && FieldMatch.Field(_def.TargetId, s.TargetId);
        }
    }

    // --- Levar um objeto a um destino ----------------------------------------
    public sealed class PlaceItemInZoneObjectiveDefinition : ObjectiveDefinition
    {
        public PlaceItemInZoneObjectiveDefinition(string description, string itemType, string zoneId, int count = 1)
            : base(description)
        {
            ItemType = itemType;
            ZoneId = zoneId;
            Count = count;
        }

        public string ItemType { get; }
        public string ZoneId { get; }
        public int Count { get; }

        public override ITaskObjective CreateRuntime() => new Runtime(this);

        private sealed class Runtime : CountingObjective
        {
            private readonly PlaceItemInZoneObjectiveDefinition _def;

            public Runtime(PlaceItemInZoneObjectiveDefinition def) : base(def.Description, def.Count)
            {
                _def = def;
            }

            protected override bool Matches(in GameplaySignal s)
                => s.Type == SignalType.ItemPlacedInZone && FieldMatch.Field(_def.ItemType, s.ItemType)
                   && FieldMatch.Field(_def.ZoneId, s.ZoneId);
        }
    }

    // --- Recolher / limpar (descartar N itens) --------------------------------
    public sealed class DisposeItemsObjectiveDefinition : ObjectiveDefinition
    {
        public DisposeItemsObjectiveDefinition(string description, string itemType, int count)
            : base(description)
        {
            ItemType = itemType;
            Count = count;
        }

        public string ItemType { get; }
        public int Count { get; }

        public override ITaskObjective CreateRuntime() => new Runtime(this);

        private sealed class Runtime : CountingObjective
        {
            private readonly DisposeItemsObjectiveDefinition _def;

            public Runtime(DisposeItemsObjectiveDefinition def) : base(def.Description, def.Count)
            {
                _def = def;
            }

            protected override bool Matches(in GameplaySignal s)
                => s.Type == SignalType.ItemDisposed && FieldMatch.Field(_def.ItemType, s.ItemType);
        }
    }

    // --- Conversar com um alvo -------------------------------------------------
    public sealed class TalkToObjectiveDefinition : ObjectiveDefinition
    {
        public TalkToObjectiveDefinition(string description, string targetId) : base(description)
        {
            TargetId = targetId;
        }

        public string TargetId { get; }

        public override ITaskObjective CreateRuntime() => new Runtime(this);

        private sealed class Runtime : CountingObjective
        {
            private readonly TalkToObjectiveDefinition _def;

            public Runtime(TalkToObjectiveDefinition def) : base(def.Description, 1)
            {
                _def = def;
            }

            protected override bool Matches(in GameplaySignal s)
                => s.Type == SignalType.TalkedTo && FieldMatch.Field(_def.TargetId, s.TargetId);
        }
    }

    // --- Acao nomeada (ex.: "abrir_bagageiro") ---------------------------------
    public sealed class PerformActionObjectiveDefinition : ObjectiveDefinition
    {
        public PerformActionObjectiveDefinition(string description, string actionId, string targetId = null)
            : base(description)
        {
            ActionId = actionId;
            TargetId = targetId;
        }

        public string ActionId { get; }
        public string TargetId { get; }

        public override ITaskObjective CreateRuntime() => new Runtime(this);

        private sealed class Runtime : CountingObjective
        {
            private readonly PerformActionObjectiveDefinition _def;

            public Runtime(PerformActionObjectiveDefinition def) : base(def.Description, 1)
            {
                _def = def;
            }

            protected override bool Matches(in GameplaySignal s)
                => s.Type == SignalType.ActionPerformed && FieldMatch.Field(_def.ActionId, s.ActionId)
                   && FieldMatch.Field(_def.TargetId, s.TargetId);
        }
    }

    // --- Sequencia: etapas precisam acontecer em ordem -------------------------
    public sealed class SequenceObjectiveDefinition : ObjectiveDefinition
    {
        private readonly ObjectiveDefinition[] _steps;

        public SequenceObjectiveDefinition(string description, params ObjectiveDefinition[] steps)
            : base(description)
        {
            if (steps == null || steps.Length == 0)
            {
                throw new ArgumentException("A sequencia precisa de pelo menos uma etapa", nameof(steps));
            }

            _steps = (ObjectiveDefinition[])steps.Clone();
        }

        public IReadOnlyList<ObjectiveDefinition> Steps => _steps;

        public override ITaskObjective CreateRuntime() => new Runtime(this);

        private sealed class Runtime : ITaskObjective
        {
            private readonly ITaskObjective[] _steps;
            private int _index;

            public Runtime(SequenceObjectiveDefinition def)
            {
                Description = def.Description;
                _steps = new ITaskObjective[def._steps.Length];
                for (int i = 0; i < _steps.Length; i++)
                {
                    _steps[i] = def._steps[i].CreateRuntime();
                }
            }

            public string Description { get; }
            public bool IsComplete => _index >= _steps.Length;

            public float Progress
            {
                get
                {
                    if (IsComplete)
                    {
                        return 1f;
                    }

                    return (_index + _steps[_index].Progress) / _steps.Length;
                }
            }

            public bool Handle(in GameplaySignal signal)
            {
                if (IsComplete)
                {
                    return false;
                }

                // Somente a etapa atual escuta sinais: fora de ordem nao conta.
                bool changed = _steps[_index].Handle(signal);
                if (_steps[_index].IsComplete)
                {
                    _index++;
                }

                return changed;
            }
        }
    }
}
