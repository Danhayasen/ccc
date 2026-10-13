using System;
using System.Collections.Generic;

namespace SkyCrew.Domain.Persistence
{
    /// <summary>Configuracoes do jogador (persistentes).</summary>
    public sealed class GameSettings
    {
        public float MasterVolume = 1f;
        public float MusicVolume = 0.8f;
        public float SfxVolume = 1f;
        public float MouseSensitivity = 1f;
        public bool InvertY;

        public GameSettings Clone() => (GameSettings)MemberwiseClone();
    }

    /// <summary>Estatisticas permanentes (somente crescem).</summary>
    public sealed class PlayerStats
    {
        public int FlightsPlayed;
        public int FlightsWon;
        public int TasksCompleted;
        public int TasksFailed;
        public int TotalStars;
        public int BestScore;
        public long TotalScore;

        public PlayerStats Clone() => (PlayerStats)MemberwiseClone();
    }

    /// <summary>
    /// Tudo que e salvo em disco. NAO contem estado temporario de partida nem credenciais.
    /// </summary>
    public sealed class SaveData
    {
        /// <summary>Versao atual do formato. Mudou o formato? Incremente e escreva a migracao.</summary>
        public const int CurrentVersion = 2;

        /// <summary>Quantas partidas recentes guardamos para evitar recompensa duplicada.</summary>
        public const int ProcessedMatchesKept = 32;

        public int Version = CurrentVersion;
        public GameSettings Settings = new GameSettings();
        public PlayerStats Stats = new PlayerStats();
        public readonly SortedSet<string> Unlocks = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>Partidas ja contabilizadas (mais antiga primeiro).</summary>
        public readonly List<string> ProcessedMatches = new List<string>();

        /// <summary>Uniforme/acessorio equipado (deve estar desbloqueado).</summary>
        public string EquippedOutfit = "uniforme_azul";

        public static SaveData CreateDefault()
        {
            var d = new SaveData();
            d.Unlocks.Add("uniforme_azul");
            d.Unlocks.Add("aviao_prototipo");
            return d;
        }

        public SaveData Clone()
        {
            var c = new SaveData
            {
                Version = Version,
                Settings = Settings.Clone(),
                Stats = Stats.Clone(),
                EquippedOutfit = EquippedOutfit,
            };
            foreach (string u in Unlocks)
            {
                c.Unlocks.Add(u);
            }

            c.ProcessedMatches.AddRange(ProcessedMatches);
            return c;
        }
    }
}
