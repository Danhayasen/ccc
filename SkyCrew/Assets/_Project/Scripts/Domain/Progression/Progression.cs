using System;
using System.Collections.Generic;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.Persistence;

namespace SkyCrew.Domain.Progression
{
    public enum UnlockCategory
    {
        Uniforme,
        Acessorio,
        Aviao,
        Modificador,
    }

    /// <summary>Item desbloqueavel e sua exigencia (estrelas totais e/ou voos vencidos).</summary>
    public sealed class UnlockDefinition
    {
        public UnlockDefinition(string id, string displayName, UnlockCategory category, int starsRequired, int winsRequired = 0)
        {
            Id = id;
            DisplayName = displayName;
            Category = category;
            StarsRequired = Math.Max(0, starsRequired);
            WinsRequired = Math.Max(0, winsRequired);
        }

        public string Id { get; }
        public string DisplayName { get; }
        public UnlockCategory Category { get; }
        public int StarsRequired { get; }
        public int WinsRequired { get; }

        public bool IsMet(PlayerStats stats) => stats.TotalStars >= StarsRequired && stats.FlightsWon >= WinsRequired;
    }

    public sealed class ProgressionReport
    {
        public bool Applied;
        public string Reason;
        public int StarsGained;
        public bool NewBestScore;
        public readonly List<UnlockDefinition> NewUnlocks = new List<UnlockDefinition>();
    }

    /// <summary>
    /// Aplica o resultado de uma partida ao progresso permanente. Idempotente por id de partida:
    /// a mesma partida nunca rende recompensa duas vezes (reinicio, evento duplicado, rede).
    /// A progressao nao e necessaria para jogar: tudo comeca com o basico desbloqueado.
    /// </summary>
    public static class ProgressionService
    {
        public static readonly UnlockDefinition[] Catalog =
        {
            new UnlockDefinition("uniforme_azul", "Uniforme azul", UnlockCategory.Uniforme, 0),
            new UnlockDefinition("aviao_prototipo", "Avião SkyCrew 1", UnlockCategory.Aviao, 0),
            new UnlockDefinition("lenco_amarelo", "Lenço amarelo", UnlockCategory.Acessorio, 2),
            new UnlockDefinition("uniforme_vermelho", "Uniforme vermelho", UnlockCategory.Uniforme, 5),
            new UnlockDefinition("quepe_dourado", "Quepe dourado", UnlockCategory.Acessorio, 9),
            new UnlockDefinition("modo_turbulencia", "Modificador: turbulência dobrada", UnlockCategory.Modificador, 12, 3),
            new UnlockDefinition("uniforme_verde", "Uniforme verde", UnlockCategory.Uniforme, 15),
            new UnlockDefinition("aviao_jumbo", "Avião SkyCrew 2 (em breve)", UnlockCategory.Aviao, 24, 6),
        };

        public static UnlockDefinition Find(string id)
        {
            foreach (var u in Catalog)
            {
                if (u.Id == id)
                {
                    return u;
                }
            }

            return null;
        }

        public static ProgressionReport Apply(SaveData save, MatchResults results, string matchId)
        {
            var report = new ProgressionReport();
            if (save == null || results == null || string.IsNullOrEmpty(matchId))
            {
                report.Reason = "dados ausentes";
                return report;
            }

            if (save.ProcessedMatches.Contains(matchId))
            {
                report.Reason = "partida ja contabilizada";
                return report;
            }

            var stats = save.Stats;
            stats.FlightsPlayed++;
            if (results.Victory)
            {
                stats.FlightsWon++;
                report.StarsGained = Math.Max(0, Math.Min(3, results.Score.Stars));
                stats.TotalStars += report.StarsGained;
            }

            stats.TasksCompleted += Math.Max(0, results.TasksCompleted);
            stats.TasksFailed += Math.Max(0, results.TasksFailed);
            stats.TotalScore += Math.Max(0, results.Score.Total);
            if (results.Score.Total > stats.BestScore)
            {
                stats.BestScore = results.Score.Total;
                report.NewBestScore = true;
            }

            foreach (var u in Catalog)
            {
                if (!save.Unlocks.Contains(u.Id) && u.IsMet(stats))
                {
                    save.Unlocks.Add(u.Id);
                    report.NewUnlocks.Add(u);
                }
            }

            save.ProcessedMatches.Add(matchId);
            while (save.ProcessedMatches.Count > SaveData.ProcessedMatchesKept)
            {
                save.ProcessedMatches.RemoveAt(0);
            }

            report.Applied = true;
            return report;
        }

        /// <summary>
        /// Equipa um uniforme ou acessorio, somente se ja estiver desbloqueado.
        /// "" desequipa o acessorio. Avioes e modificadores nao sao equipaveis aqui.
        /// </summary>
        public static bool TryEquip(SaveData save, string unlockId)
        {
            if (unlockId == "")
            {
                save.EquippedAccessory = "";
                return true;
            }

            var def = Find(unlockId);
            if (def == null || !save.Unlocks.Contains(unlockId))
            {
                return false;
            }

            switch (def.Category)
            {
                case UnlockCategory.Uniforme:
                    save.EquippedUniform = unlockId;
                    return true;
                case UnlockCategory.Acessorio:
                    save.EquippedAccessory = unlockId;
                    return true;
                default:
                    return false;
            }
        }
    }
}
