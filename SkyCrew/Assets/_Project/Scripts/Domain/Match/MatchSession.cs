using System;
using System.Collections.Generic;
using SkyCrew.Domain.Common;
using SkyCrew.Domain.Events;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Domain.Scoring;
using SkyCrew.Domain.Tasks;

namespace SkyCrew.Domain.Match
{
    public sealed class MatchConfig
    {
        public float BoardingSeconds = 25f;
        public float FlightSeconds = 240f;

        /// <summary>Satisfacao media abaixo disso (por <see cref="SatisfactionGraceSeconds"/>) = derrota.</summary>
        public float FailSatisfactionThreshold = 0.2f;

        public float SatisfactionGraceSeconds = 3f;
        public ulong Seed = 12345;
        public float Difficulty = 1f;
        public EventSchedulerConfig Events = new EventSchedulerConfig();

        /// <summary>Tarefas que precisam ser concluidas para vencer.</summary>
        public List<string> RequiredTaskIds = new List<string>();
    }

    public sealed class MatchResults
    {
        public bool Victory;
        public string Reason;
        public ScoreBreakdown Score;
        public float AverageSatisfaction;
        public float FlightTime;
        public int TasksCompleted;
        public int TasksFailed;
        public readonly List<string> Lines = new List<string>();
    }

    /// <summary>
    /// Coordena uma viagem: estado, relogio, tarefas, eventos, passageiros e resultado.
    /// E a autoridade das regras; a camada Unity so envia sinais e le o estado.
    /// </summary>
    public sealed class MatchSession : IPassengerWorld
    {
        private readonly HashSet<string> _pendingInitSteps = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<PassengerBrain> _passengers = new List<PassengerBrain>();
        private readonly Dictionary<string, PassengerBrain> _passengersById = new Dictionary<string, PassengerBrain>();
        private float _lowSatisfactionTime;
        private bool _resultsGenerated;
        private bool _seatbeltManual;

        public MatchSession(MatchConfig config, IRandom random = null)
        {
            Config = config ?? new MatchConfig();
            Random = random ?? new DeterministicRandom(Config.Seed);
            State = new MatchStateMachine();
            State.Changed += OnStateChanged;
            CreateRuntimeSystems();
        }

        public MatchConfig Config { get; }
        public IRandom Random { get; }
        public MatchStateMachine State { get; }
        public TaskManager Tasks { get; private set; }
        public EventScheduler Events { get; private set; }
        public ScoreLedger Score { get; private set; }
        public MatchResults Results { get; private set; }

        /// <summary>Tempo no estado atual.</summary>
        public float StateTime { get; private set; }

        public float FlightTime { get; private set; }
        public float FlightTimeLeft => Math.Max(0f, Config.FlightSeconds - FlightTime);
        public float BoardingTimeLeft => State.Current == MatchState.Boarding ? Math.Max(0f, Config.BoardingSeconds - StateTime) : 0f;
        public IReadOnlyList<PassengerBrain> Passengers => _passengers;

        public event Action<MatchResults> ResultsReady;

        /// <summary>Disparado depois de Reset (para conteudos se reinstalarem).</summary>
        public event Action<MatchSession> SessionReset;

        // --- IPassengerWorld -------------------------------------------------------
        public bool SeatbeltSignOn => _seatbeltManual || Events.ActiveIntensityWithTag(EventTags.Turbulence) > 0f;
        public float TurbulenceIntensity => Events.ActiveIntensityWithTag(EventTags.Turbulence);
        public bool CanRequestService => State.Current == MatchState.InFlight;

        public void SetSeatbeltSign(bool on) => _seatbeltManual = on;

        private void CreateRuntimeSystems()
        {
            Score = new ScoreLedger();
            Tasks = new TaskManager(Score, () => State.IsPlayable);
            Events = new EventScheduler(Random, Config.Events);
        }

        // --- Inicializacao ---------------------------------------------------------

        /// <summary>Registra uma etapa que precisa terminar antes de Ready (ex.: "cena", "navmesh").</summary>
        public void RegisterInitStep(string step)
        {
            if (State.Current != MatchState.Initializing)
            {
                throw new InvalidOperationException("Etapas de inicializacao so podem ser registradas em Initializing");
            }

            _pendingInitSteps.Add(step);
        }

        public IReadOnlyCollection<string> PendingInitSteps => _pendingInitSteps;

        /// <summary>Marca a etapa como concluida; quando nao sobra nenhuma, vai para Ready.</summary>
        public bool CompleteInitStep(string step)
        {
            _pendingInitSteps.Remove(step);
            return TryBecomeReady(out _);
        }

        public bool TryBecomeReady(out string reason)
        {
            if (_pendingInitSteps.Count > 0)
            {
                reason = "Inicializacao incompleta: " + string.Join(", ", _pendingInitSteps);
                return false;
            }

            return State.TryTransition(MatchState.Ready, out reason);
        }

        public bool StartBoarding(out string reason) => State.TryTransition(MatchState.Boarding, out reason);

        public void AddPassenger(PassengerBrain brain)
        {
            if (_passengersById.ContainsKey(brain.Id))
            {
                throw new InvalidOperationException($"Passageiro '{brain.Id}' duplicado");
            }

            _passengers.Add(brain);
            _passengersById.Add(brain.Id, brain);
        }

        public PassengerBrain GetPassenger(string id)
        {
            _passengersById.TryGetValue(id, out var p);
            return p;
        }

        // --- Acoes validadas (ponto unico para o jogador e, no futuro, para a rede) ---

        public void Signal(in GameplaySignal signal)
        {
            if (!State.IsPlayable)
            {
                return;
            }

            Tasks.HandleSignal(signal);
        }

        /// <summary>Entrega um item a um passageiro. So funciona se ele pediu exatamente isso.</summary>
        public bool TryDeliverToPassenger(string passengerId, string itemType)
        {
            var p = GetPassenger(passengerId);
            if (p == null || !State.IsPlayable)
            {
                return false;
            }

            var kind = ItemTypes.ToService(itemType);
            if (kind == ServiceKind.None || !p.Serve(kind))
            {
                return false;
            }

            Signal(GameplaySignal.Delivered(itemType, passengerId));
            return true;
        }

        public bool CanDeliverToPassenger(string passengerId, string itemType)
        {
            var p = GetPassenger(passengerId);
            return p != null && State.IsPlayable && p.PendingService != ServiceKind.None
                   && p.PendingService == ItemTypes.ToService(itemType);
        }

        public bool TryTalkToPassenger(string passengerId)
        {
            var p = GetPassenger(passengerId);
            if (p == null || !State.IsPlayable || !p.Calm())
            {
                return false;
            }

            Signal(GameplaySignal.Talked(passengerId));
            return true;
        }

        // --- Atualizacao -----------------------------------------------------------

        public void Tick(float dt)
        {
            if (dt <= 0f || !State.IsPlayable)
            {
                return;
            }

            StateTime += dt;
            if (State.Current == MatchState.InFlight)
            {
                FlightTime += dt;
                Events.Tick(dt, new EventContext(State.Current, FlightTime, Config.Difficulty));
            }

            Tasks.Tick(dt);
            foreach (var p in _passengers)
            {
                p.Tick(dt, this);
            }

            if (State.Current == MatchState.Boarding && StateTime >= Config.BoardingSeconds)
            {
                State.TryTransition(MatchState.InFlight, out _);
                return;
            }

            if (State.Current == MatchState.InFlight)
            {
                EvaluateOutcome(dt);
            }
        }

        public float AverageSatisfaction
        {
            get
            {
                if (_passengers.Count == 0)
                {
                    return 1f;
                }

                float sum = 0f;
                foreach (var p in _passengers)
                {
                    sum += p.Needs.Satisfaction;
                }

                return sum / _passengers.Count;
            }
        }

        private void EvaluateOutcome(float dt)
        {
            bool allDone = Config.RequiredTaskIds.Count > 0;
            foreach (string id in Config.RequiredTaskIds)
            {
                var t = Tasks.Get(id);
                if (t != null && (t.State == TaskState.Failed || t.State == TaskState.Cancelled))
                {
                    Finish(false, $"Tarefa obrigatoria nao cumprida: {t.Definition.DisplayName}");
                    return;
                }

                if (t == null || t.State != TaskState.Completed)
                {
                    allDone = false;
                }
            }

            if (allDone)
            {
                Finish(true, "Todas as tarefas obrigatorias concluidas");
                return;
            }

            if (AverageSatisfaction < Config.FailSatisfactionThreshold)
            {
                _lowSatisfactionTime += dt;
                if (_lowSatisfactionTime >= Config.SatisfactionGraceSeconds)
                {
                    Finish(false, "Passageiros insatisfeitos demais");
                    return;
                }
            }
            else
            {
                _lowSatisfactionTime = 0f;
            }

            if (FlightTime >= Config.FlightSeconds)
            {
                Finish(false, "O voo terminou com tarefas pendentes");
            }
        }

        /// <summary>Encerra a partida. Os resultados sao gerados uma unica vez.</summary>
        public bool Finish(bool victory, string reason)
        {
            if (_resultsGenerated)
            {
                return false;
            }

            var target = victory ? MatchState.Results : MatchState.Failed;
            if (!State.TryTransition(target, out _))
            {
                return false;
            }

            _resultsGenerated = true;
            Events.CancelAll();
            foreach (var p in _passengers)
            {
                p.Disable();
            }

            var results = new MatchResults
            {
                Victory = victory,
                Reason = reason,
                AverageSatisfaction = AverageSatisfaction,
                FlightTime = FlightTime,
            };
            foreach (var t in Tasks.All)
            {
                if (t.State == TaskState.Completed) results.TasksCompleted++;
                if (t.State == TaskState.Failed) results.TasksFailed++;
            }

            results.Score = ScoreFormula.Compute(Score, results.AverageSatisfaction, FlightTimeLeft, victory);
            foreach (var e in Score.Entries)
            {
                results.Lines.Add($"{e.Reason}: {(e.Amount >= 0 ? "+" : "")}{e.Amount}");
            }

            Results = results;
            ResultsReady?.Invoke(results);
            return true;
        }

        /// <summary>
        /// Reinicia para Initializing com sistemas novos (nada da partida anterior e reaproveitado).
        /// Passageiros precisam ser registrados de novo pelo conteudo.
        /// </summary>
        public bool Reset(out string reason)
        {
            if (!State.TryTransition(MatchState.Initializing, out reason))
            {
                return false;
            }

            _passengers.Clear();
            _passengersById.Clear();
            _pendingInitSteps.Clear();
            _lowSatisfactionTime = 0f;
            _resultsGenerated = false;
            _seatbeltManual = false;
            Results = null;
            StateTime = 0f;
            FlightTime = 0f;
            CreateRuntimeSystems();
            SessionReset?.Invoke(this);
            return true;
        }

        private void OnStateChanged(MatchState previous, MatchState next)
        {
            StateTime = 0f;
        }
    }

    public static class EventTags
    {
        public const string Turbulence = "turbulencia";
        public const string Cabin = "cabine";
    }

    public static class ItemTypes
    {
        public const string Water = "agua";
        public const string Suitcase = "mala";
        public const string Trash = "lixo";

        public static ServiceKind ToService(string itemType)
        {
            return itemType == Water ? ServiceKind.Water : ServiceKind.None;
        }
    }
}
