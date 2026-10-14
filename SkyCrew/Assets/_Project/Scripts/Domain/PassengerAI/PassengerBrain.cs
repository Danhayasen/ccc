using System;
using System.Collections.Generic;

namespace SkyCrew.Domain.PassengerAI
{
    public enum PassengerStateId
    {
        Idle,
        Seated,
        Waiting,
        RequestingService,
        WalkingToDestination,
        Interacting,
        ReactingToEvent,
        Recovering,
        ReturningToSeat,
        Disabled,
    }

    public enum DestinationKind
    {
        None,
        Seat,
        Lavatory,
    }

    public enum NeedKind
    {
        Fear,
        Service,
        Lavatory,
    }

    public enum CommandKind
    {
        WalkTo,
        StopMoving,
        SnapToSeat,
        ShowRequest,
        ClearRequest,
        React,
        StopReacting,
    }

    /// <summary>Ordem para o "corpo" (camada Unity): andar, parar, mostrar balao, reagir...</summary>
    public readonly struct PassengerCommand
    {
        public PassengerCommand(CommandKind kind, DestinationKind destination = DestinationKind.None,
                                ServiceKind service = ServiceKind.None)
        {
            Kind = kind;
            Destination = destination;
            Service = service;
        }

        public CommandKind Kind { get; }
        public DestinationKind Destination { get; }
        public ServiceKind Service { get; }

        public override string ToString() => $"{Kind}({Destination}, {Service})";
    }

    /// <summary>O que o passageiro precisa saber do mundo (fornecido pela sessao).</summary>
    public interface IPassengerWorld
    {
        bool SeatbeltSignOn { get; }
        float TurbulenceIntensity { get; }
        bool CanRequestService { get; }
    }

    public sealed class PassengerBrainConfig
    {
        public float DecisionInterval = 0.25f;
        public float ThirstRequestThreshold = 0.7f;
        public float HungerRequestThreshold = 0.75f;
        public float ColdRequestThreshold = 0.8f;
        public float FearReactThreshold = 0.55f;
        public float FearCalmThreshold = 0.4f;
        public float LavatoryThreshold = 0.85f;
        public float LavatoryUseSeconds = 4f;
        public float RecoverSeconds = 1.5f;
        public int MaxNavigationRetries = 3;

        /// <summary>Ordem de prioridade das necessidades quando sentado (configuravel).</summary>
        public NeedKind[] Priorities = { NeedKind.Fear, NeedKind.Service, NeedKind.Lavatory };
    }

    /// <summary>
    /// Maquina de estados do passageiro. Decide em intervalos fixos (nao a cada quadro),
    /// emite comandos para o corpo e recebe callbacks de navegacao.
    /// </summary>
    public sealed class PassengerBrain
    {
        private static readonly Dictionary<PassengerStateId, PassengerStateId[]> Transitions =
            new Dictionary<PassengerStateId, PassengerStateId[]>
            {
                { PassengerStateId.Idle, new[] { PassengerStateId.WalkingToDestination, PassengerStateId.Seated, PassengerStateId.Disabled } },
                { PassengerStateId.Seated, new[] { PassengerStateId.RequestingService, PassengerStateId.WalkingToDestination, PassengerStateId.ReactingToEvent, PassengerStateId.Disabled } },
                { PassengerStateId.RequestingService, new[] { PassengerStateId.Waiting, PassengerStateId.ReactingToEvent, PassengerStateId.Disabled } },
                { PassengerStateId.Waiting, new[] { PassengerStateId.Seated, PassengerStateId.ReactingToEvent, PassengerStateId.Disabled } },
                { PassengerStateId.WalkingToDestination, new[] { PassengerStateId.Seated, PassengerStateId.Interacting, PassengerStateId.ReturningToSeat, PassengerStateId.ReactingToEvent, PassengerStateId.Recovering, PassengerStateId.Disabled } },
                { PassengerStateId.Interacting, new[] { PassengerStateId.ReturningToSeat, PassengerStateId.ReactingToEvent, PassengerStateId.Disabled } },
                { PassengerStateId.ReactingToEvent, new[] { PassengerStateId.Recovering, PassengerStateId.Disabled } },
                { PassengerStateId.Recovering, new[] { PassengerStateId.Seated, PassengerStateId.Waiting, PassengerStateId.ReturningToSeat, PassengerStateId.Disabled } },
                { PassengerStateId.ReturningToSeat, new[] { PassengerStateId.Seated, PassengerStateId.ReactingToEvent, PassengerStateId.Recovering, PassengerStateId.Disabled } },
                { PassengerStateId.Disabled, Array.Empty<PassengerStateId>() },
            };

        private const int HistorySize = 32;

        private readonly PassengerBrainConfig _config;
        private readonly Queue<PassengerCommand> _commands = new Queue<PassengerCommand>();
        private readonly Queue<string> _history = new Queue<string>();
        private float _decisionTimer;
        private int _navigationRetries;

        public PassengerBrain(string id, PassengerProfile profile, PassengerBrainConfig config = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Passageiro sem id", nameof(id));
            }

            Id = id;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _config = config ?? new PassengerBrainConfig();
            Needs = new PassengerNeeds(profile);
            State = PassengerStateId.Idle;
        }

        public string Id { get; }
        public PassengerProfile Profile { get; }
        public PassengerNeeds Needs { get; }
        public PassengerStateId State { get; private set; }
        public float TimeInState { get; private set; }
        public ServiceKind PendingService { get; private set; }
        public DestinationKind CurrentDestination { get; private set; }
        public bool IsAtSeat { get; private set; }
        public int TransitionCount { get; private set; }
        public IEnumerable<string> History => _history;

        /// <summary>(cerebro, estado anterior, estado novo)</summary>
        public event Action<PassengerBrain, PassengerStateId, PassengerStateId> StateChanged;

        public event Action<PassengerBrain, ServiceKind> ServiceRequested;

        public bool HasCommands => _commands.Count > 0;

        public bool TryDequeueCommand(out PassengerCommand command)
        {
            if (_commands.Count > 0)
            {
                command = _commands.Dequeue();
                return true;
            }

            command = default;
            return false;
        }

        public static bool IsTransitionAllowed(PassengerStateId from, PassengerStateId to)
            => Transitions.TryGetValue(from, out var t) && Array.IndexOf(t, to) >= 0;

        // --- Entradas da sessao / do corpo -----------------------------------

        /// <summary>Coloca o passageiro direto na poltrona (inicio ja sentado).</summary>
        public void SitImmediately()
        {
            if (TryEnter(PassengerStateId.Seated))
            {
                IsAtSeat = true;
                Emit(CommandKind.SnapToSeat);
            }
        }

        /// <summary>Embarque: caminhar ate a poltrona.</summary>
        public bool BeginBoarding()
        {
            if (State != PassengerStateId.Idle)
            {
                return false;
            }

            return StartWalking(DestinationKind.Seat);
        }

        public void OnArrived()
        {
            _navigationRetries = 0;
            switch (State)
            {
                case PassengerStateId.WalkingToDestination when CurrentDestination == DestinationKind.Lavatory:
                    IsAtSeat = false;
                    TryEnter(PassengerStateId.Interacting);
                    break;
                case PassengerStateId.WalkingToDestination when CurrentDestination == DestinationKind.Seat:
                case PassengerStateId.ReturningToSeat:
                    IsAtSeat = true;
                    CurrentDestination = DestinationKind.None;
                    if (TryEnter(PassengerStateId.Seated))
                    {
                        Emit(CommandKind.SnapToSeat);
                    }

                    break;
            }
        }

        public void OnNavigationFailed()
        {
            if (State != PassengerStateId.WalkingToDestination && State != PassengerStateId.ReturningToSeat)
            {
                return;
            }

            _navigationRetries++;
            if (_navigationRetries <= _config.MaxNavigationRetries)
            {
                Emit(CommandKind.WalkTo, CurrentDestination); // nova tentativa de rota
                return;
            }

            _navigationRetries = 0;
            if (CurrentDestination == DestinationKind.Lavatory)
            {
                // Desiste do banheiro e volta para a poltrona.
                StartReturning();
            }
            else
            {
                // Preso no caminho da poltrona: recuperacao forcada.
                IsAtSeat = true;
                CurrentDestination = DestinationKind.None;
                Emit(CommandKind.SnapToSeat);
                TryEnter(PassengerStateId.Recovering);
            }
        }

        /// <summary>Entrega de servico. So aceita o que foi pedido.</summary>
        public bool Serve(ServiceKind kind)
        {
            if (kind == ServiceKind.None || PendingService != kind || State == PassengerStateId.Disabled)
            {
                return false;
            }

            Needs.Serve(kind);
            PendingService = ServiceKind.None;
            Emit(CommandKind.ClearRequest);
            if (State == PassengerStateId.RequestingService)
            {
                TryEnter(PassengerStateId.Waiting);
            }

            if (State == PassengerStateId.Waiting)
            {
                TryEnter(PassengerStateId.Seated);
            }

            return true;
        }

        /// <summary>Conversa com o passageiro (acalma).</summary>
        public bool Calm()
        {
            if (State == PassengerStateId.Disabled)
            {
                return false;
            }

            Needs.Calm();
            if (State == PassengerStateId.ReactingToEvent && Needs.Fear < _config.FearCalmThreshold)
            {
                Emit(CommandKind.StopReacting);
                TryEnter(PassengerStateId.Recovering);
            }

            return true;
        }

        public void Disable()
        {
            if (State == PassengerStateId.Disabled)
            {
                return;
            }

            Emit(CommandKind.StopMoving);
            TryEnter(PassengerStateId.Disabled);
        }

        // --- Atualizacao ---------------------------------------------------------

        public void Tick(float dt, IPassengerWorld world)
        {
            if (State == PassengerStateId.Disabled || dt <= 0f)
            {
                return;
            }

            bool seated = State == PassengerStateId.Seated || State == PassengerStateId.Waiting
                          || State == PassengerStateId.RequestingService
                          || (State == PassengerStateId.ReactingToEvent && IsAtSeat)
                          || (State == PassengerStateId.Recovering && IsAtSeat);
            Needs.Tick(dt, new NeedsContext(PendingService != ServiceKind.None, world.TurbulenceIntensity, seated));

            TimeInState += dt;
            _decisionTimer += dt;
            if (_decisionTimer < _config.DecisionInterval)
            {
                return;
            }

            _decisionTimer = 0f;
            Decide(world);
        }

        private void Decide(IPassengerWorld world)
        {
            bool scared = Needs.Fear >= _config.FearReactThreshold;
            switch (State)
            {
                case PassengerStateId.Seated:
                    DecideWhileSeated(world, scared);
                    break;

                case PassengerStateId.RequestingService:
                    TryEnter(PassengerStateId.Waiting);
                    break;

                case PassengerStateId.Waiting:
                    if (scared)
                    {
                        StartReacting();
                    }

                    break;

                case PassengerStateId.WalkingToDestination:
                    if (scared)
                    {
                        StartReacting();
                    }
                    else if (CurrentDestination == DestinationKind.Lavatory && world.SeatbeltSignOn)
                    {
                        StartReturning(); // aviso de cinto aceso: volta para a poltrona
                    }

                    break;

                case PassengerStateId.Interacting:
                    if (TimeInState >= _config.LavatoryUseSeconds)
                    {
                        Needs.UseLavatory();
                        StartReturning();
                    }

                    break;

                case PassengerStateId.ReactingToEvent:
                    // Histerese: so sai quando o medo cai bem abaixo do limiar.
                    if (Needs.Fear < _config.FearCalmThreshold)
                    {
                        Emit(CommandKind.StopReacting);
                        TryEnter(PassengerStateId.Recovering);
                    }

                    break;

                case PassengerStateId.Recovering:
                    if (TimeInState >= _config.RecoverSeconds)
                    {
                        if (!IsAtSeat)
                        {
                            StartReturning();
                        }
                        else
                        {
                            TryEnter(PendingService != ServiceKind.None ? PassengerStateId.Waiting : PassengerStateId.Seated);
                        }
                    }

                    break;

                case PassengerStateId.ReturningToSeat:
                    if (scared)
                    {
                        StartReacting();
                    }

                    break;
            }
        }

        private void DecideWhileSeated(IPassengerWorld world, bool scared)
        {
            foreach (var need in _config.Priorities)
            {
                switch (need)
                {
                    case NeedKind.Fear when scared:
                        StartReacting();
                        return;

                    case NeedKind.Service when world.CanRequestService && PendingService == ServiceKind.None:
                        var wanted = MostUrgentService();
                        if (wanted == ServiceKind.None)
                        {
                            break; // nada urgente: avalia a proxima necessidade
                        }

                        PendingService = wanted;
                        if (TryEnter(PassengerStateId.RequestingService))
                        {
                            Emit(CommandKind.ShowRequest, service: wanted);
                            ServiceRequested?.Invoke(this, wanted);
                        }

                        return;

                    case NeedKind.Lavatory when !world.SeatbeltSignOn && Needs.Lavatory >= _config.LavatoryThreshold:
                        StartWalking(DestinationKind.Lavatory);
                        return;
                }
            }
        }

        /// <summary>Servico cuja necessidade mais passou do limite (ou None).</summary>
        public ServiceKind MostUrgentService()
        {
            ServiceKind best = ServiceKind.None;
            float bestExcess = 0f;
            Consider(Needs.Thirst - _config.ThirstRequestThreshold, ServiceKind.Water);
            Consider(Needs.Hunger - _config.HungerRequestThreshold, ServiceKind.Snack);
            Consider(Needs.Cold - _config.ColdRequestThreshold, ServiceKind.Blanket);
            return best;

            void Consider(float excess, ServiceKind kind)
            {
                if (excess >= 0f && (best == ServiceKind.None || excess > bestExcess))
                {
                    best = kind;
                    bestExcess = excess;
                }
            }
        }

        /// <summary>
        /// O passageiro desiste do pedido (ninguem atendeu a tempo): perde paciencia, apaga o
        /// balao e volta a ficar sentado. A necessidade continua e pode gerar outro pedido depois.
        /// </summary>
        public bool AbandonRequest(float patiencePenalty = 0.2f)
        {
            if (PendingService == ServiceKind.None || State == PassengerStateId.Disabled)
            {
                return false;
            }

            PendingService = ServiceKind.None;
            Needs.DebugSet(patience: Needs.Patience - patiencePenalty);
            Emit(CommandKind.ClearRequest);
            if (State == PassengerStateId.RequestingService)
            {
                TryEnter(PassengerStateId.Waiting);
            }

            if (State == PassengerStateId.Waiting)
            {
                TryEnter(PassengerStateId.Seated);
            }

            return true;
        }

        /// <summary>Susto vindo de evento (sem turbulencia).</summary>
        public void Scare(float amount)
        {
            if (State != PassengerStateId.Disabled)
            {
                Needs.Scare(amount);
            }
        }

        private bool StartWalking(DestinationKind destination)
        {
            if (!TryEnter(PassengerStateId.WalkingToDestination))
            {
                return false;
            }

            if (destination != DestinationKind.Seat)
            {
                IsAtSeat = false;
            }

            CurrentDestination = destination;
            _navigationRetries = 0;
            Emit(CommandKind.WalkTo, destination);
            return true;
        }

        private void StartReturning()
        {
            if (TryEnter(PassengerStateId.ReturningToSeat))
            {
                CurrentDestination = DestinationKind.Seat;
                _navigationRetries = 0;
                Emit(CommandKind.WalkTo, DestinationKind.Seat);
            }
        }

        private void StartReacting()
        {
            if (TryEnter(PassengerStateId.ReactingToEvent))
            {
                Emit(CommandKind.StopMoving);
                Emit(CommandKind.React);
            }
        }

        private bool TryEnter(PassengerStateId next)
        {
            if (!IsTransitionAllowed(State, next))
            {
                Log($"REJEITADA {State} -> {next}");
                return false;
            }

            var previous = State;
            State = next;
            TimeInState = 0f;
            TransitionCount++;
            Log($"{previous} -> {next}");
            StateChanged?.Invoke(this, previous, next);
            return true;
        }

        private void Emit(CommandKind kind, DestinationKind destination = DestinationKind.None,
                          ServiceKind service = ServiceKind.None)
        {
            _commands.Enqueue(new PassengerCommand(kind, destination, service));
        }

        private void Log(string line)
        {
            if (_history.Count >= HistorySize)
            {
                _history.Dequeue();
            }

            _history.Enqueue(line);
        }
    }
}
