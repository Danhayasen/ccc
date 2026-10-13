using System;
using System.Collections.Generic;
using SkyCrew.Domain.Common;
using SkyCrew.Domain.Match;

namespace SkyCrew.Domain.Events
{
    /// <summary>Informacoes que as condicoes de evento podem consultar.</summary>
    public readonly struct EventContext
    {
        public EventContext(MatchState state, float flightTime, float difficulty = 1f)
        {
            State = state;
            FlightTime = flightTime;
            Difficulty = difficulty;
        }

        public MatchState State { get; }
        public float FlightTime { get; }
        public float Difficulty { get; }
    }

    public interface IEventCondition
    {
        bool IsSatisfied(in EventContext context);
        string Describe();
    }

    public sealed class MinFlightTimeCondition : IEventCondition
    {
        private readonly float _seconds;

        public MinFlightTimeCondition(float seconds)
        {
            _seconds = seconds;
        }

        public bool IsSatisfied(in EventContext context) => context.FlightTime >= _seconds;

        public string Describe() => $"voo >= {_seconds}s";
    }

    /// <summary>Definicao imutavel de um evento dinamico.</summary>
    public sealed class EventDefinition
    {
        public EventDefinition(string id, string displayName, float durationSeconds,
                               float weight = 1f, int priority = 0, float cooldownSeconds = 30f,
                               float intensity = 1f, int maxOccurrences = 0,
                               string[] tags = null, string[] incompatibleTags = null,
                               MatchState[] allowedStates = null, IEventCondition[] conditions = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Evento sem id", nameof(id));
            }

            Id = id;
            DisplayName = displayName;
            DurationSeconds = Math.Max(0.1f, durationSeconds);
            Weight = Math.Max(0f, weight);
            Priority = priority;
            CooldownSeconds = Math.Max(0f, cooldownSeconds);
            Intensity = Math.Max(0f, intensity);
            MaxOccurrences = Math.Max(0, maxOccurrences);
            Tags = tags ?? Array.Empty<string>();
            IncompatibleTags = incompatibleTags ?? Array.Empty<string>();
            AllowedStates = allowedStates ?? new[] { MatchState.InFlight };
            Conditions = conditions ?? Array.Empty<IEventCondition>();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public float DurationSeconds { get; }
        public float Weight { get; }
        public int Priority { get; }
        public float CooldownSeconds { get; }
        public float Intensity { get; }

        /// <summary>0 = ilimitado.</summary>
        public int MaxOccurrences { get; }

        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyList<string> IncompatibleTags { get; }
        public IReadOnlyList<MatchState> AllowedStates { get; }
        public IReadOnlyList<IEventCondition> Conditions { get; }

        public bool HasTag(string tag)
        {
            foreach (string t in Tags)
            {
                if (t == tag)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class ActiveEvent
    {
        internal ActiveEvent(EventDefinition definition, int occurrence, float intensity)
        {
            Definition = definition;
            Occurrence = occurrence;
            Intensity = intensity;
            InstanceId = $"{definition.Id}#{occurrence}";
        }

        public EventDefinition Definition { get; }
        public string InstanceId { get; }
        public int Occurrence { get; }
        public float Intensity { get; }
        public float Elapsed { get; internal set; }
        public float Remaining => Math.Max(0f, Definition.DurationSeconds - Elapsed);
    }

    public sealed class EventSchedulerConfig
    {
        /// <summary>De quanto em quanto tempo o sorteio acontece.</summary>
        public float SelectionIntervalSeconds = 5f;

        /// <summary>Intervalo minimo entre o inicio de quaisquer dois eventos.</summary>
        public float GlobalMinIntervalSeconds = 15f;

        public int MaxActiveEvents = 1;

        /// <summary>Chance de iniciar algo em cada sorteio elegivel.</summary>
        public float ChancePerRoll = 0.35f;

        /// <summary>Multiplicador de intensidade (dificuldade).</summary>
        public float IntensityScale = 1f;
    }

    /// <summary>
    /// Escolhe e controla eventos dinamicos respeitando estado da partida, condicoes,
    /// cooldown, limite de ativos, compatibilidade por tags e repeticao. Deterministico
    /// dado o mesmo IRandom (sementes iguais = mesma sequencia).
    /// </summary>
    public sealed class EventScheduler
    {
        private readonly IRandom _random;
        private readonly EventSchedulerConfig _config;
        private readonly List<EventDefinition> _definitions = new List<EventDefinition>();
        private readonly List<ActiveEvent> _active = new List<ActiveEvent>();
        private readonly List<ActiveEvent> _ended = new List<ActiveEvent>();
        private readonly List<EventDefinition> _eligible = new List<EventDefinition>();
        private readonly Dictionary<string, int> _occurrences = new Dictionary<string, int>();
        private readonly Dictionary<string, float> _lastStart = new Dictionary<string, float>();
        private readonly List<KeyValuePair<float, string>> _scripted = new List<KeyValuePair<float, string>>();
        private float _rollTimer;
        private float _lastAnyStart = float.NegativeInfinity;
        private float _clock;

        public EventScheduler(IRandom random, EventSchedulerConfig config = null)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _config = config ?? new EventSchedulerConfig();
        }

        public event Action<ActiveEvent> Started;

        /// <summary>(evento, foi cancelado?)</summary>
        public event Action<ActiveEvent, bool> Ended;

        public IReadOnlyList<ActiveEvent> Active => _active;
        public IReadOnlyList<EventDefinition> Definitions => _definitions;

        public void Register(EventDefinition definition)
        {
            foreach (var d in _definitions)
            {
                if (d.Id == definition.Id)
                {
                    throw new InvalidOperationException($"Evento '{definition.Id}' ja registrado");
                }
            }

            _definitions.Add(definition);
        }

        /// <summary>Agenda um evento garantido para um tempo de voo (tenta de novo se bloqueado).</summary>
        public void AddScripted(float flightTime, string eventId)
        {
            _scripted.Add(new KeyValuePair<float, string>(flightTime, eventId));
            _scripted.Sort((a, b) => a.Key.CompareTo(b.Key));
        }

        public int OccurrencesOf(string id) => _occurrences.TryGetValue(id, out int n) ? n : 0;

        public bool IsActive(string id)
        {
            foreach (var a in _active)
            {
                if (a.Definition.Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        public float ActiveIntensityWithTag(string tag)
        {
            float sum = 0f;
            foreach (var a in _active)
            {
                if (a.Definition.HasTag(tag))
                {
                    sum += a.Intensity;
                }
            }

            return sum;
        }

        public bool IsEligible(EventDefinition def, in EventContext ctx, bool ignoreGlobalInterval, out string reason)
        {
            bool stateOk = false;
            foreach (var s in def.AllowedStates)
            {
                if (s == ctx.State)
                {
                    stateOk = true;
                    break;
                }
            }

            if (!stateOk)
            {
                reason = $"estado {ctx.State} nao permitido";
                return false;
            }

            if (IsActive(def.Id))
            {
                reason = "ja ativo";
                return false;
            }

            if (_active.Count >= _config.MaxActiveEvents)
            {
                reason = "limite de eventos ativos";
                return false;
            }

            if (def.MaxOccurrences > 0 && OccurrencesOf(def.Id) >= def.MaxOccurrences)
            {
                reason = "limite de repeticoes";
                return false;
            }

            if (_lastStart.TryGetValue(def.Id, out float last) && _clock - last < def.CooldownSeconds)
            {
                reason = "em cooldown";
                return false;
            }

            if (!ignoreGlobalInterval && _clock - _lastAnyStart < _config.GlobalMinIntervalSeconds)
            {
                reason = "intervalo minimo global";
                return false;
            }

            foreach (var active in _active)
            {
                if (Conflicts(def, active.Definition))
                {
                    reason = $"incompativel com '{active.Definition.Id}'";
                    return false;
                }
            }

            foreach (var condition in def.Conditions)
            {
                if (!condition.IsSatisfied(ctx))
                {
                    reason = "condicao nao atendida: " + condition.Describe();
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private static bool Conflicts(EventDefinition a, EventDefinition b)
        {
            foreach (string t in a.IncompatibleTags)
            {
                if (b.HasTag(t))
                {
                    return true;
                }
            }

            foreach (string t in b.IncompatibleTags)
            {
                if (a.HasTag(t))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryStart(string id, in EventContext ctx, out string reason, bool ignoreGlobalInterval = false)
        {
            EventDefinition def = null;
            foreach (var d in _definitions)
            {
                if (d.Id == id)
                {
                    def = d;
                    break;
                }
            }

            if (def == null)
            {
                reason = $"evento '{id}' nao registrado";
                return false;
            }

            if (!IsEligible(def, ctx, ignoreGlobalInterval, out reason))
            {
                return false;
            }

            Start(def, ctx);
            return true;
        }

        private void Start(EventDefinition def, in EventContext ctx)
        {
            int occurrence = OccurrencesOf(def.Id) + 1;
            _occurrences[def.Id] = occurrence;
            _lastStart[def.Id] = _clock;
            _lastAnyStart = _clock;
            var active = new ActiveEvent(def, occurrence, def.Intensity * _config.IntensityScale * ctx.Difficulty);
            _active.Add(active);
            Started?.Invoke(active);
        }

        public void Tick(float dt, in EventContext ctx)
        {
            if (dt <= 0f)
            {
                return;
            }

            _clock += dt;

            // 1) Avanca e encerra eventos ativos.
            _ended.Clear();
            foreach (var a in _active)
            {
                a.Elapsed += dt;
                if (a.Elapsed >= a.Definition.DurationSeconds)
                {
                    _ended.Add(a);
                }
            }

            foreach (var a in _ended)
            {
                _active.Remove(a);
                Ended?.Invoke(a, false);
            }

            // 2) Eventos roteirizados (garantidos).
            for (int i = 0; i < _scripted.Count; i++)
            {
                var entry = _scripted[i];
                if (entry.Key > ctx.FlightTime)
                {
                    break;
                }

                if (TryStart(entry.Value, ctx, out _, ignoreGlobalInterval: true))
                {
                    _scripted.RemoveAt(i);
                    i--;
                }
            }

            // 3) Sorteio controlado.
            _rollTimer += dt;
            if (_rollTimer < _config.SelectionIntervalSeconds)
            {
                return;
            }

            _rollTimer = 0f;
            _eligible.Clear();
            float totalWeight = 0f;
            foreach (var def in _definitions)
            {
                if (def.Weight > 0f && IsEligible(def, ctx, false, out _))
                {
                    _eligible.Add(def);
                    totalWeight += def.Weight;
                }
            }

            if (_eligible.Count == 0 || _random.NextDouble() >= _config.ChancePerRoll)
            {
                return;
            }

            double pick = _random.NextDouble() * totalWeight;
            foreach (var def in _eligible)
            {
                pick -= def.Weight;
                if (pick <= 0)
                {
                    Start(def, ctx);
                    return;
                }
            }

            Start(_eligible[_eligible.Count - 1], ctx);
        }

        /// <summary>Encerra tudo (ex.: fim da partida). Handlers restauram estados temporarios.</summary>
        public void CancelAll()
        {
            var copy = _active.ToArray();
            _active.Clear();
            foreach (var a in copy)
            {
                Ended?.Invoke(a, true);
            }
        }
    }
}
