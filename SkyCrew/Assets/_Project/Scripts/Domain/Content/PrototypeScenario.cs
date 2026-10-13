using System;
using System.Collections.Generic;
using SkyCrew.Domain.Events;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Domain.Tasks;

namespace SkyCrew.Domain.Content
{
    public sealed class PassengerSpec
    {
        public PassengerSpec(string id, string name, Personality personality, string seatId,
                             bool startsSeated, float? thirstOverride = null)
        {
            Id = id;
            Name = name;
            Personality = personality;
            SeatId = seatId;
            StartsSeated = startsSeated;
            ThirstOverride = thirstOverride;
        }

        public string Id { get; }
        public string Name { get; }
        public Personality Personality { get; }
        public string SeatId { get; }
        public bool StartsSeated { get; }
        public float? ThirstOverride { get; }
    }

    /// <summary>
    /// Conteudo da Fase 1: 3 tarefas obrigatorias (guardar mala, servir agua, acalmar passageiro),
    /// tarefa extra de limpeza e o evento de turbulencia. Liga pedidos e eventos a tarefas.
    /// </summary>
    public sealed class PrototypeScenario
    {
        public const string TaskStoreSuitcase = "guardar_mala";
        public const string TaskServeWater = "servir_agua";
        public const string TaskCalmPassenger = "acalmar_passageiro";
        public const string TaskCleanPrefix = "recolher_lixo";
        public const string ZoneOverheadBin = "bagageiro";
        public const string EventTurbulence = "turbulencia_leve";
        public const int TrashPerTurbulence = 2;
        public const float GuaranteedTurbulenceAt = 25f;

        public static readonly PassengerSpec[] DefaultPassengers =
        {
            new PassengerSpec("p_bia", "Bia", Personality.Exigente, "3C", true, thirstOverride: 0.035f),
            new PassengerSpec("p_otto", "Otto", Personality.Ansioso, "4D", true),
            new PassengerSpec("p_ze", "Zé", Personality.Calmo, "5A", true),
            new PassengerSpec("p_lia", "Lia", Personality.Desastrado, "2F", false),
        };

        private readonly MatchSession _session;
        private readonly IReadOnlyList<PassengerSpec> _specs;
        private readonly Dictionary<string, PassengerSpec> _specById = new Dictionary<string, PassengerSpec>();
        private int _extraTaskCounter;

        private PrototypeScenario(MatchSession session, IReadOnlyList<PassengerSpec> specs)
        {
            _session = session;
            _specs = specs;
            foreach (var s in specs)
            {
                _specById[s.Id] = s;
            }
        }

        /// <summary>Lixo a criar no mundo: (quantidade, id da tarefa).</summary>
        public event Action<int, string> TrashSpawnRequested;

        /// <summary>Disparado sempre que os passageiros sao (re)criados.</summary>
        public event Action<PrototypeScenario> PassengersCreated;

        public MatchSession Session => _session;
        public IReadOnlyList<PassengerSpec> Specs => _specs;

        public PassengerSpec GetSpec(string passengerId)
        {
            _specById.TryGetValue(passengerId, out var s);
            return s;
        }

        public static MatchConfig CreateConfig(ulong seed = 12345)
        {
            var config = new MatchConfig
            {
                Seed = seed,
                BoardingSeconds = 30f,
                FlightSeconds = 240f,
                FailSatisfactionThreshold = 0.2f,
                Events = new EventSchedulerConfig
                {
                    SelectionIntervalSeconds = 6f,
                    GlobalMinIntervalSeconds = 35f,
                    MaxActiveEvents = 1,
                    ChancePerRoll = 0.3f,
                },
            };
            config.RequiredTaskIds.Add(TaskStoreSuitcase);
            config.RequiredTaskIds.Add(TaskServeWater);
            config.RequiredTaskIds.Add(TaskCalmPassenger);
            return config;
        }

        public static PrototypeScenario Install(MatchSession session, IReadOnlyList<PassengerSpec> passengers = null)
        {
            var scenario = new PrototypeScenario(session, passengers ?? DefaultPassengers);
            scenario.Populate();
            session.State.Changed += scenario.OnMatchStateChanged;
            session.SessionReset += _ => scenario.Populate();
            return scenario;
        }

        private void Populate()
        {
            _extraTaskCounter = 0;
            RegisterContent();
            foreach (var spec in _specs)
            {
                var profile = PassengerProfiles.Get(spec.Personality);
                if (spec.ThirstOverride.HasValue)
                {
                    profile = profile.WithThirst(spec.ThirstOverride.Value);
                }

                var brain = new PassengerBrain(spec.Id, profile);
                brain.ServiceRequested += OnServiceRequested;
                _session.AddPassenger(brain);
                if (spec.StartsSeated)
                {
                    brain.SitImmediately();
                }
            }

            _session.Events.Started += OnEventStarted;
            PassengersCreated?.Invoke(this);
        }

        private void RegisterContent()
        {
            _session.Tasks.Register(new TaskDefinition(
                TaskStoreSuitcase, "Guardar a mala",
                "Pegue a mala esquecida no corredor e guarde no bagageiro.",
                new ObjectiveDefinition[]
                {
                    new PlaceItemInZoneObjectiveDefinition("Mala no bagageiro", ItemTypes.Suitcase, ZoneOverheadBin),
                },
                TaskPriority.Normal, isMandatory: true, reward: 150));

            _session.Events.Register(new EventDefinition(
                EventTurbulence, "Turbulência leve", durationSeconds: 10f, weight: 1f, priority: 1,
                cooldownSeconds: 45f, intensity: 0.8f,
                tags: new[] { EventTags.Turbulence, EventTags.Cabin },
                incompatibleTags: new[] { EventTags.Turbulence },
                conditions: new IEventCondition[] { new MinFlightTimeCondition(15f) }));
            _session.Events.AddScripted(GuaranteedTurbulenceAt, EventTurbulence);
        }

        private void OnMatchStateChanged(MatchState previous, MatchState next)
        {
            if (next != MatchState.Boarding)
            {
                return;
            }

            _session.Tasks.TryActivate(TaskStoreSuitcase, out _);
            foreach (var p in _session.Passengers)
            {
                p.BeginBoarding(); // so afeta quem ainda esta em Idle
            }
        }

        private void OnServiceRequested(PassengerBrain passenger, ServiceKind kind)
        {
            if (kind != ServiceKind.Water)
            {
                return;
            }

            string name = GetSpec(passenger.Id)?.Name ?? passenger.Id;
            bool first = !_session.Tasks.IsRegistered(TaskServeWater);
            string id = first ? TaskServeWater : $"{TaskServeWater}_{passenger.Id}_{++_extraTaskCounter}";
            _session.Tasks.Register(new TaskDefinition(
                id, $"Servir água para {name}",
                $"Pegue uma garrafa na cozinha e entregue para {name}.",
                new ObjectiveDefinition[]
                {
                    new DeliverItemObjectiveDefinition("Entregar água", ItemTypes.Water, passenger.Id),
                },
                first ? TaskPriority.High : TaskPriority.Normal, isMandatory: first,
                timeLimitSeconds: 75f, reward: first ? 200 : 100, penalty: 100));
            _session.Tasks.TryActivate(id, out _);
        }

        private void OnEventStarted(ActiveEvent e)
        {
            if (!e.Definition.HasTag(EventTags.Turbulence))
            {
                return;
            }

            // Quem mais vai se assustar: maior sensibilidade ao medo (desempate: medo atual).
            PassengerBrain target = null;
            float best = float.NegativeInfinity;
            foreach (var p in _session.Passengers)
            {
                if (p.State == PassengerStateId.Disabled)
                {
                    continue;
                }

                float score = p.Profile.FearSensitivity + p.Needs.Fear * 0.1f;
                if (score > best)
                {
                    best = score;
                    target = p;
                }
            }

            if (target != null)
            {
                string name = GetSpec(target.Id)?.Name ?? target.Id;
                bool first = !_session.Tasks.IsRegistered(TaskCalmPassenger);
                string id = first ? TaskCalmPassenger : $"{TaskCalmPassenger}_{target.Id}_{++_extraTaskCounter}";
                _session.Tasks.Register(new TaskDefinition(
                    id, $"Acalmar {name}", $"{name} ficou com medo da turbulência. Converse com {name}.",
                    new ObjectiveDefinition[] { new TalkToObjectiveDefinition($"Conversar com {name}", target.Id) },
                    TaskPriority.Urgent, isMandatory: first, timeLimitSeconds: 60f,
                    reward: first ? 200 : 100, penalty: 100));
                _session.Tasks.TryActivate(id, out _);
            }

            string cleanId = $"{TaskCleanPrefix}_{e.Occurrence}";
            _session.Tasks.Register(new TaskDefinition(
                cleanId, "Recolher o lixo", "A turbulência derrubou lixo no corredor. Jogue na lixeira.",
                new ObjectiveDefinition[]
                {
                    new DisposeItemsObjectiveDefinition("Lixo recolhido", ItemTypes.Trash, TrashPerTurbulence),
                },
                TaskPriority.Low, reward: 80));
            _session.Tasks.TryActivate(cleanId, out _);
            TrashSpawnRequested?.Invoke(TrashPerTurbulence, cleanId);
        }
    }
}
