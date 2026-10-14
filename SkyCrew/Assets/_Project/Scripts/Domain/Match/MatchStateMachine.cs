using System;
using System.Collections.Generic;

namespace SkyCrew.Domain.Match
{
    public enum MatchState
    {
        Initializing,
        Ready,
        Boarding,
        InFlight,
        Results,
        Failed,
        ReturningToMenu,
    }

    /// <summary>
    /// Fonte de verdade do estado da partida. Toda mudanca passa pela tabela
    /// de transicoes permitidas; transicoes invalidas sao rejeitadas.
    /// </summary>
    public sealed class MatchStateMachine
    {
        private static readonly Dictionary<MatchState, MatchState[]> Allowed =
            new Dictionary<MatchState, MatchState[]>
            {
                { MatchState.Initializing, new[] { MatchState.Ready, MatchState.Failed } },
                { MatchState.Ready, new[] { MatchState.Boarding, MatchState.ReturningToMenu } },
                { MatchState.Boarding, new[] { MatchState.InFlight, MatchState.Failed, MatchState.ReturningToMenu } },
                { MatchState.InFlight, new[] { MatchState.Results, MatchState.Failed, MatchState.ReturningToMenu } },
                { MatchState.Results, new[] { MatchState.ReturningToMenu, MatchState.Initializing } },
                { MatchState.Failed, new[] { MatchState.ReturningToMenu, MatchState.Initializing } },
                { MatchState.ReturningToMenu, new[] { MatchState.Initializing } },
            };

        private readonly List<string> _history = new List<string>();

        public MatchStateMachine()
        {
            Current = MatchState.Initializing;
        }

        public MatchState Current { get; private set; }

        /// <summary>(anterior, novo)</summary>
        public event Action<MatchState, MatchState> Changed;

        public IReadOnlyList<string> History => _history;

        public static bool IsAllowed(MatchState from, MatchState to)
        {
            return Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;
        }

        public bool CanTransition(MatchState to)
        {
            return IsAllowed(Current, to);
        }

        public bool TryTransition(MatchState to, out string reason)
        {
            if (!CanTransition(to))
            {
                reason = $"Transicao invalida: {Current} -> {to}";
                _history.Add("REJEITADA " + reason);
                return false;
            }

            var previous = Current;
            Current = to;
            reason = null;
            _history.Add($"{previous} -> {to}");
            Changed?.Invoke(previous, to);
            return true;
        }

        public bool IsPlayable => Current == MatchState.Boarding || Current == MatchState.InFlight;
    }
}
