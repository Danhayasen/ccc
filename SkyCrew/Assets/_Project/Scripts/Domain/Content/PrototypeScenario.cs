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

    public enum WorldRequestKind
    {
        /// <summary>Criar lixo (ItemIds) - perto do passageiro, se houver.</summary>
        Trash,

        /// <summary>Criar mancha no corredor (Id = id da mancha, alvo da acao "limpar").</summary>
        Spill,

        /// <summary>Largar o carrinho de servico no corredor (Id = id do carrinho).</summary>
        Cart,

        /// <summary>Uma mala cai do bagageiro (ItemIds[0] = id da mala).</summary>
        FallenBag,
    }

    /// <summary>Pedido do dominio para o mundo 3D criar algo (a posicao e decidida pela camada Unity).</summary>
    public sealed class WorldRequest
    {
        public WorldRequest(WorldRequestKind kind, string id, string taskId, string passengerId = null,
                            IReadOnlyList<string> itemIds = null)
        {
            Kind = kind;
            Id = id;
            TaskId = taskId;
            PassengerId = passengerId;
            ItemIds = itemIds ?? Array.Empty<string>();
        }

        public WorldRequestKind Kind { get; }
        public string Id { get; }
        public string TaskId { get; }
        public string PassengerId { get; }
        public IReadOnlyList<string> ItemIds { get; }
    }

    /// <summary>
    /// Conteudo do prototipo (Fases 1 e 2): 3 tarefas obrigatorias (guardar a mala, servir agua,
    /// acalmar na turbulencia), pedidos de servico vindos das necessidades e 7 eventos dinamicos,
    /// cada um gerando sua tarefa. Resolver a tarefa encerra o evento.
    /// </summary>
    public sealed class PrototypeScenario
    {
        // Tarefas
        public const string TaskStoreSuitcase = "guardar_mala";
        public const string TaskServeWater = "servir_agua";
        public const string TaskCalmPassenger = "acalmar_passageiro";
        public const string TaskCleanPrefix = "recolher_lixo";

        // Zonas, acoes e equipamentos
        public const string ZoneOverheadBin = "bagageiro";
        public const string ZoneGalley = "cozinha";
        public const string ActionClean = "limpar";
        public const string ActionRepair = "consertar";
        public const string EquipmentFridge = "geladeira";
        public const string ForgottenSuitcaseId = "mala_esquecida";

        // Eventos
        public const string EventTurbulence = "turbulencia_leve";
        public const string EventDroppedItem = "objeto_derrubado";
        public const string EventSpill = "derramamento";
        public const string EventBlockedAisle = "corredor_bloqueado";
        public const string EventBrokenEquipment = "equipamento_quebrado";
        public const string EventFallenBag = "mala_caida";
        public const string EventScaredPassenger = "passageiro_assustado";

        public const string TagAisle = "corredor";
        public const string TagEquipment = "equipamento";
        public const string TagPassenger = "passageiro";

        public const int TrashPerTurbulence = 2;
        public const int CleanSwipes = 3;
        public const int RepairHits = 3;
        public const float GuaranteedTurbulenceAt = 25f;

        public static readonly PassengerSpec[] DefaultPassengers =
        {
            new PassengerSpec("p_bia", "Bia", Personality.Exigente, "3C", true, thirstOverride: 0.035f),
            new PassengerSpec("p_otto", "Otto", Personality.Ansioso, "4D", true),
            new PassengerSpec("p_ze", "Zé", Personality.Calmo, "5A", true),
            new PassengerSpec("p_lia", "Lia", Personality.Desastrado, "2F", false),
            new PassengerSpec("p_rui", "Rui", Personality.Prestativo, "5D", true),
            new PassengerSpec("p_nina", "Nina", Personality.Impaciente, "1B", true),
        };

        private readonly MatchSession _session;
        private readonly IReadOnlyList<PassengerSpec> _specs;
        private readonly Dictionary<string, PassengerSpec> _specById = new Dictionary<string, PassengerSpec>();
        private readonly Dictionary<string, string> _eventByTask = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _serviceTaskPassenger = new Dictionary<string, string>();
        private readonly Dictionary<string, WorldRequest> _requestByEventInstance = new Dictionary<string, WorldRequest>();
        private int _counter;

        private PrototypeScenario(MatchSession session, IReadOnlyList<PassengerSpec> specs)
        {
            _session = session;
            _specs = specs;
            foreach (var s in specs)
            {
                _specById[s.Id] = s;
            }
        }

        /// <summary>O mundo 3D precisa criar algo (lixo, mancha, carrinho, mala).</summary>
        public event Action<WorldRequest> WorldRequested;

        /// <summary>O problema terminou (resolvido ou nao): o mundo 3D pode limpar o que criou.</summary>
        public event Action<WorldRequest> WorldCleared;

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
                MaxOpenRequests = 3,
                Events = new EventSchedulerConfig
                {
                    SelectionIntervalSeconds = 6f,
                    GlobalMinIntervalSeconds = 20f,
                    MaxActiveEvents = 2,
                    ChancePerRoll = 0.35f,
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
            _counter = 0;
            _eventByTask.Clear();
            _serviceTaskPassenger.Clear();
            _requestByEventInstance.Clear();
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
            _session.Events.Ended += OnEventEnded;
            _session.Tasks.StateChanged += OnTaskStateChanged;
            PassengersCreated?.Invoke(this);
        }

        private void RegisterContent()
        {
            _session.Tasks.Register(new TaskDefinition(
                TaskStoreSuitcase, "Guardar a mala",
                "Pegue a mala esquecida no corredor e guarde no bagageiro.",
                new ObjectiveDefinition[]
                {
                    new PlaceItemInZoneObjectiveDefinition("Mala no bagageiro", ItemTypes.Suitcase, ZoneOverheadBin,
                                                           itemId: ForgottenSuitcaseId),
                },
                TaskPriority.Normal, isMandatory: true, reward: 150));

            var ev = _session.Events;
            ev.Register(new EventDefinition(EventTurbulence, "Turbulência leve", 10f, weight: 1f, priority: 2,
                cooldownSeconds: 45f, intensity: 0.8f,
                tags: new[] { EventTags.Turbulence, EventTags.Cabin }, incompatibleTags: new[] { EventTags.Turbulence },
                conditions: new IEventCondition[] { new MinFlightTimeCondition(15f) }));
            ev.Register(new EventDefinition(EventDroppedItem, "Objeto derrubado", 4f, weight: 1.4f,
                cooldownSeconds: 20f, tags: new[] { EventTags.Cabin },
                conditions: new IEventCondition[] { new MinFlightTimeCondition(10f) }));
            ev.Register(new EventDefinition(EventSpill, "Derramamento de bebida", 46f, weight: 1f,
                cooldownSeconds: 40f, tags: new[] { TagAisle }, incompatibleTags: new[] { TagAisle },
                conditions: new IEventCondition[] { new MinFlightTimeCondition(20f) }));
            ev.Register(new EventDefinition(EventBlockedAisle, "Corredor bloqueado", 51f, weight: 0.8f,
                cooldownSeconds: 50f, tags: new[] { TagAisle }, incompatibleTags: new[] { TagAisle },
                conditions: new IEventCondition[] { new MinFlightTimeCondition(30f) }));
            ev.Register(new EventDefinition(EventBrokenEquipment, "Geladeira quebrada", 61f, weight: 0.7f,
                cooldownSeconds: 60f, maxOccurrences: 2, tags: new[] { TagEquipment },
                incompatibleTags: new[] { TagEquipment },
                conditions: new IEventCondition[] { new MinFlightTimeCondition(40f) }));
            ev.Register(new EventDefinition(EventFallenBag, "Mala caiu do bagageiro", 4f, weight: 0.8f,
                cooldownSeconds: 40f, tags: new[] { EventTags.Cabin },
                conditions: new IEventCondition[] { new MinFlightTimeCondition(15f) }));
            ev.Register(new EventDefinition(EventScaredPassenger, "Passageiro assustado", 3f, weight: 1f,
                cooldownSeconds: 30f, tags: new[] { TagPassenger },
                conditions: new IEventCondition[] { new MinFlightTimeCondition(20f) }));
            ev.AddScripted(GuaranteedTurbulenceAt, EventTurbulence);
        }

        private string Name(string passengerId) => GetSpec(passengerId)?.Name ?? passengerId;

        private string NextId(string prefix) => $"{prefix}_{++_counter}";

        private void Add(TaskDefinition def, string eventId = null)
        {
            _session.Tasks.Register(def);
            if (eventId != null)
            {
                _eventByTask[def.Id] = eventId;
            }

            _session.Tasks.TryActivate(def.Id, out _);
        }

        // --- Fluxo da partida ---------------------------------------------------------

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

        // --- Pedidos de servico ---------------------------------------------------------

        private static string ServiceVerb(ServiceKind kind)
        {
            switch (kind)
            {
                case ServiceKind.Water: return "água";
                case ServiceKind.Snack: return "lanche";
                case ServiceKind.Blanket: return "cobertor";
                default: return "algo";
            }
        }

        private void OnServiceRequested(PassengerBrain passenger, ServiceKind kind)
        {
            string itemType = ItemTypes.FromService(kind);
            if (itemType == null)
            {
                return;
            }

            string name = Name(passenger.Id);
            bool mandatory = kind == ServiceKind.Water && !_session.Tasks.IsRegistered(TaskServeWater);
            string id = mandatory ? TaskServeWater : NextId($"servir_{itemType}_{passenger.Id}");
            string where = kind == ServiceKind.Blanket ? "no armário da cozinha" : "na cozinha";
            _serviceTaskPassenger[id] = passenger.Id;
            Add(new TaskDefinition(
                id, $"Servir {ServiceVerb(kind)} para {name}",
                $"Pegue {ServiceVerb(kind)} {where} e entregue para {name}.",
                new ObjectiveDefinition[]
                {
                    new DeliverItemObjectiveDefinition($"Entregar {ServiceVerb(kind)}", itemType, passenger.Id),
                },
                mandatory ? TaskPriority.High : TaskPriority.Normal, isMandatory: mandatory,
                timeLimitSeconds: 75f, reward: mandatory ? 200 : 100, penalty: mandatory ? 100 : 40));
        }

        private void OnTaskStateChanged(TaskRuntime task, TaskState previous)
        {
            if (task.State == TaskState.Completed && _eventByTask.TryGetValue(task.Id, out string eventId))
            {
                _session.Events.TryEnd(eventId); // problema resolvido: encerra o evento
            }

            if (task.State == TaskState.Failed && _serviceTaskPassenger.TryGetValue(task.Id, out string pid))
            {
                _session.GetPassenger(pid)?.AbandonRequest(); // ninguem atendeu: desiste e fica mais impaciente
            }
        }

        // --- Eventos ---------------------------------------------------------------------

        private PassengerBrain PickPassenger(Func<PassengerBrain, float> weight)
        {
            float total = 0f;
            foreach (var p in _session.Passengers)
            {
                if (p.State != PassengerStateId.Disabled)
                {
                    total += Math.Max(0f, weight(p));
                }
            }

            if (total <= 0f)
            {
                return null;
            }

            double pick = _session.Random.NextDouble() * total;
            PassengerBrain last = null;
            foreach (var p in _session.Passengers)
            {
                if (p.State == PassengerStateId.Disabled)
                {
                    continue;
                }

                last = p;
                pick -= Math.Max(0f, weight(p));
                if (pick <= 0)
                {
                    return p;
                }
            }

            return last;
        }

        private void AddCalmTask(PassengerBrain target, string reason)
        {
            string name = Name(target.Id);
            bool first = !_session.Tasks.IsRegistered(TaskCalmPassenger);
            string id = first ? TaskCalmPassenger : NextId($"{TaskCalmPassenger}_{target.Id}");
            Add(new TaskDefinition(
                id, $"Acalmar {name}", $"{name} {reason}. Converse com {name}.",
                new ObjectiveDefinition[] { new TalkToObjectiveDefinition($"Conversar com {name}", target.Id) },
                TaskPriority.Urgent, isMandatory: first, timeLimitSeconds: first ? 60f : 45f,
                reward: first ? 200 : 100, penalty: first ? 100 : 30));
        }

        private void RequestTrash(string taskName, string taskDescription, int count, string passengerId,
                                  string eventInstanceId)
        {
            string taskId = NextId(TaskCleanPrefix);
            var ids = new string[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = $"{taskId}_item{i}";
            }

            Add(new TaskDefinition(taskId, taskName, taskDescription,
                new ObjectiveDefinition[] { new DisposeItemsObjectiveDefinition("Lixo recolhido", ItemTypes.Trash, count, ids) },
                TaskPriority.Low, reward: 40 * count));
            Emit(new WorldRequest(WorldRequestKind.Trash, taskId, taskId, passengerId, ids), eventInstanceId);
        }

        private void Emit(WorldRequest request, string eventInstanceId)
        {
            if (eventInstanceId != null)
            {
                _requestByEventInstance[eventInstanceId] = request;
            }

            WorldRequested?.Invoke(request);
        }

        private void OnEventStarted(ActiveEvent e)
        {
            switch (e.Definition.Id)
            {
                case EventTurbulence:
                {
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
                        AddCalmTask(target, "ficou com medo da turbulência");
                    }

                    RequestTrash("Recolher o lixo", "A turbulência derrubou lixo no corredor. Jogue na lixeira.",
                                 TrashPerTurbulence, null, null);
                    break;
                }

                case EventDroppedItem:
                {
                    // Desastrados derrubam coisas com muito mais frequencia.
                    var clumsy = PickPassenger(p => 0.05f + p.Profile.Clumsiness);
                    if (clumsy != null)
                    {
                        RequestTrash($"Recolher o que {Name(clumsy.Id)} derrubou",
                                     $"{Name(clumsy.Id)} derrubou o lanche no chão. Jogue na lixeira.", 1, clumsy.Id, null);
                    }

                    break;
                }

                case EventSpill:
                {
                    string spillId = e.InstanceId;
                    string taskId = NextId("limpar_derramamento");
                    Add(new TaskDefinition(taskId, "Limpar o derramamento",
                        "Alguém derramou suco no corredor. Pegue o esfregão (fundo da cabine) e limpe.",
                        new ObjectiveDefinition[]
                        {
                            new PerformActionObjectiveDefinition("Esfregar a mancha", ActionClean, spillId, CleanSwipes),
                        },
                        TaskPriority.High, timeLimitSeconds: 45f, reward: 120, penalty: 40), EventSpill);
                    Emit(new WorldRequest(WorldRequestKind.Spill, spillId, taskId), e.InstanceId);
                    break;
                }

                case EventBlockedAisle:
                {
                    string cartId = e.InstanceId;
                    string taskId = NextId("liberar_corredor");
                    Add(new TaskDefinition(taskId, "Liberar o corredor",
                        "O carrinho de serviço ficou no meio do corredor. Empurre de volta até a cozinha.",
                        new ObjectiveDefinition[]
                        {
                            new PlaceItemInZoneObjectiveDefinition("Carrinho na cozinha", ItemTypes.Cart, ZoneGalley,
                                                                   itemId: cartId),
                        },
                        TaskPriority.High, timeLimitSeconds: 50f, reward: 120, penalty: 50), EventBlockedAisle);
                    Emit(new WorldRequest(WorldRequestKind.Cart, cartId, taskId), e.InstanceId);
                    break;
                }

                case EventBrokenEquipment:
                {
                    _session.SetEquipmentBroken(EquipmentFridge, true);
                    string taskId = NextId("consertar_geladeira");
                    Add(new TaskDefinition(taskId, "Consertar a geladeira",
                        "A geladeira da cozinha travou e não sai água. Dê umas batidas nela (E).",
                        new ObjectiveDefinition[]
                        {
                            new PerformActionObjectiveDefinition("Bater na geladeira", ActionRepair, EquipmentFridge, RepairHits),
                        },
                        TaskPriority.High, timeLimitSeconds: 60f, reward: 100, penalty: 40), EventBrokenEquipment);
                    break;
                }

                case EventFallenBag:
                {
                    string bagId = NextId("mala_caida");
                    string taskId = NextId("guardar_mala");
                    Add(new TaskDefinition(taskId, "Guardar a mala que caiu",
                        "Uma mala despencou do bagageiro. Guarde de novo.",
                        new ObjectiveDefinition[]
                        {
                            new PlaceItemInZoneObjectiveDefinition("Mala no bagageiro", ItemTypes.Suitcase, ZoneOverheadBin,
                                                                   itemId: bagId),
                        },
                        TaskPriority.Normal, timeLimitSeconds: 60f, reward: 80, penalty: 30));
                    Emit(new WorldRequest(WorldRequestKind.FallenBag, bagId, taskId, null, new[] { bagId }), null);
                    break;
                }

                case EventScaredPassenger:
                {
                    // Qualquer um pode se assustar com "algo na janela"; ansiosos mais.
                    var target = PickPassenger(p => p.State == PassengerStateId.ReactingToEvent ? 0f : p.Profile.FearSensitivity);
                    if (target != null)
                    {
                        target.Scare(0.8f);
                        AddCalmTask(target, "viu algo estranho pela janela e entrou em pânico");
                    }

                    break;
                }
            }
        }

        private void OnEventEnded(ActiveEvent e, bool cancelled)
        {
            if (e.Definition.Id == EventBrokenEquipment)
            {
                _session.SetEquipmentBroken(EquipmentFridge, false);
            }

            if (_requestByEventInstance.TryGetValue(e.InstanceId, out var request))
            {
                _requestByEventInstance.Remove(e.InstanceId);
                WorldCleared?.Invoke(request);
            }
        }
    }
}
