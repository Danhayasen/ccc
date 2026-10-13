using System;
using System.Collections.Generic;

namespace SkyCrew.Domain.Scoring
{
    public readonly struct ScoreEntry
    {
        public ScoreEntry(string key, int amount, string reason)
        {
            Key = key;
            Amount = amount;
            Reason = reason;
        }

        public string Key { get; }
        public int Amount { get; }
        public string Reason { get; }
    }

    /// <summary>Quem recebe recompensas e penalidades (o ledger, ou um proxy de rede no futuro).</summary>
    public interface IScoreSink
    {
        /// <summary>Aplica o valor uma unica vez por chave. Retorna false se a chave ja foi usada.</summary>
        bool TryApply(string key, int amount, string reason);
    }

    /// <summary>
    /// Registro de pontos idempotente: cada recompensa/penalidade tem uma chave unica,
    /// entao eventos duplicados nao pontuam duas vezes.
    /// </summary>
    public sealed class ScoreLedger : IScoreSink
    {
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<ScoreEntry> _entries = new List<ScoreEntry>();

        public event Action<ScoreEntry> EntryAdded;

        public IReadOnlyList<ScoreEntry> Entries => _entries;

        public int Total { get; private set; }

        public int Rewards { get; private set; }

        public int Penalties { get; private set; }

        public bool Contains(string key) => _keys.Contains(key);

        public bool TryApply(string key, int amount, string reason)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Toda pontuacao precisa de uma chave unica", nameof(key));
            }

            if (!_keys.Add(key))
            {
                return false;
            }

            var entry = new ScoreEntry(key, amount, reason);
            _entries.Add(entry);
            Total += amount;
            if (amount >= 0)
            {
                Rewards += amount;
            }
            else
            {
                Penalties += -amount;
            }

            EntryAdded?.Invoke(entry);
            return true;
        }
    }

    public readonly struct ScoreBreakdown
    {
        public ScoreBreakdown(int taskRewards, int taskPenalties, int satisfactionBonus, int timeBonus, int total, int stars)
        {
            TaskRewards = taskRewards;
            TaskPenalties = taskPenalties;
            SatisfactionBonus = satisfactionBonus;
            TimeBonus = timeBonus;
            Total = total;
            Stars = stars;
        }

        public int TaskRewards { get; }
        public int TaskPenalties { get; }
        public int SatisfactionBonus { get; }
        public int TimeBonus { get; }
        public int Total { get; }
        public int Stars { get; }
    }

    /// <summary>
    /// Formula de pontuacao (documentada em Docs/ARCHITECTURE.md):
    /// total = recompensas - penalidades + satisfacao_media * 500 + segundos_restantes * 2 (max 300)
    /// Bonus de tempo so vale em vitoria. Estrelas: 1 >= 400, 2 >= 800, 3 >= 1200 (0 em derrota).
    /// </summary>
    public static class ScoreFormula
    {
        public const int SatisfactionWeight = 500;
        public const int PointsPerSecondLeft = 2;
        public const int MaxTimeBonus = 300;
        public static readonly int[] StarThresholds = { 400, 800, 1200 };

        public static ScoreBreakdown Compute(ScoreLedger ledger, float averageSatisfaction,
                                             float secondsLeft, bool victory)
        {
            float sat = averageSatisfaction < 0f ? 0f : (averageSatisfaction > 1f ? 1f : averageSatisfaction);
            int satisfactionBonus = (int)Math.Round(sat * SatisfactionWeight);
            int timeBonus = victory ? Math.Min(MaxTimeBonus, (int)Math.Max(0f, secondsLeft) * PointsPerSecondLeft) : 0;
            int total = Math.Max(0, ledger.Rewards - ledger.Penalties + satisfactionBonus + timeBonus);

            int stars = 0;
            if (victory)
            {
                foreach (int threshold in StarThresholds)
                {
                    if (total >= threshold)
                    {
                        stars++;
                    }
                }
            }

            return new ScoreBreakdown(ledger.Rewards, ledger.Penalties, satisfactionBonus, timeBonus, total, stars);
        }
    }
}
