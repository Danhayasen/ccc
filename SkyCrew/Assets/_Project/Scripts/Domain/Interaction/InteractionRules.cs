using System.Collections.Generic;
using SkyCrew.Domain.Match;

namespace SkyCrew.Domain.Interaction
{
    public enum InteractionVerb
    {
        PickUp,
        Deliver,
        Talk,
        Use,
        Dispense,
        Dispose,
        PlaceInZone,
    }

    /// <summary>Tudo o que e preciso para decidir se uma interacao e valida (sem Unity).</summary>
    public readonly struct InteractionQuery
    {
        public InteractionQuery(InteractionVerb verb, float distance, float maxRange, MatchState state,
                                string heldItemType, string acceptedItemType = null, bool targetAvailable = true)
        {
            Verb = verb;
            Distance = distance;
            MaxRange = maxRange;
            State = state;
            HeldItemType = heldItemType;
            AcceptedItemType = acceptedItemType;
            TargetAvailable = targetAvailable;
        }

        public InteractionVerb Verb { get; }
        public float Distance { get; }
        public float MaxRange { get; }
        public MatchState State { get; }

        /// <summary>null = maos vazias.</summary>
        public string HeldItemType { get; }

        /// <summary>Tipo aceito pelo alvo (Deliver/Dispose/PlaceInZone). null = qualquer.</summary>
        public string AcceptedItemType { get; }

        public bool TargetAvailable { get; }
    }

    public readonly struct InteractionCheck
    {
        private InteractionCheck(bool allowed, string reason)
        {
            Allowed = allowed;
            Reason = reason;
        }

        public bool Allowed { get; }
        public string Reason { get; }

        public static InteractionCheck Ok() => new InteractionCheck(true, null);
        public static InteractionCheck Deny(string reason) => new InteractionCheck(false, reason);
    }

    /// <summary>
    /// Regras de validacao de interacao. Usadas ao exibir a acao e de novo ao executar
    /// (no multiplayer, o servidor chamara a mesma funcao).
    /// </summary>
    public static class InteractionRules
    {
        public static InteractionCheck Validate(in InteractionQuery q)
        {
            if (q.State != MatchState.Boarding && q.State != MatchState.InFlight)
            {
                return InteractionCheck.Deny("A partida nao esta em andamento");
            }

            if (q.Distance > q.MaxRange)
            {
                return InteractionCheck.Deny("Fora de alcance");
            }

            if (!q.TargetAvailable)
            {
                return InteractionCheck.Deny("Indisponivel agora");
            }

            bool handsEmpty = string.IsNullOrEmpty(q.HeldItemType);
            switch (q.Verb)
            {
                case InteractionVerb.PickUp:
                case InteractionVerb.Dispense:
                    return handsEmpty ? InteractionCheck.Ok() : InteractionCheck.Deny("Maos ocupadas");

                case InteractionVerb.Deliver:
                case InteractionVerb.Dispose:
                case InteractionVerb.PlaceInZone:
                    if (handsEmpty)
                    {
                        return InteractionCheck.Deny("Nada nas maos");
                    }

                    if (!string.IsNullOrEmpty(q.AcceptedItemType) && q.AcceptedItemType != q.HeldItemType)
                    {
                        return InteractionCheck.Deny("Item errado");
                    }

                    return InteractionCheck.Ok();

                case InteractionVerb.Talk:
                case InteractionVerb.Use:
                    return InteractionCheck.Ok();

                default:
                    return InteractionCheck.Deny("Acao desconhecida");
            }
        }
    }

    public readonly struct InteractionCandidate
    {
        public InteractionCandidate(float distance, float angleDegrees, int priority, bool valid)
        {
            Distance = distance;
            AngleDegrees = angleDegrees;
            Priority = priority;
            Valid = valid;
        }

        public float Distance { get; }

        /// <summary>Angulo entre a frente do jogador e o alvo.</summary>
        public float AngleDegrees { get; }

        public int Priority { get; }
        public bool Valid { get; }
    }

    public static class InteractionTargetSelector
    {
        /// <summary>
        /// Indice do melhor alvo ou -1. Prefere alvos validos, depois prioridade,
        /// depois o que esta mais a frente e mais perto.
        /// </summary>
        public static int SelectBest(IReadOnlyList<InteractionCandidate> candidates, float maxAngleDegrees = 75f)
        {
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.AngleDegrees > maxAngleDegrees)
                {
                    continue;
                }

                float score = (c.Valid ? 1000f : 0f) + c.Priority * 10f - c.Distance * 2f - c.AngleDegrees / 30f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }
    }
}
