using SkyCrew.Domain.Content;
using SkyCrew.Domain.Events;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.PassengerAI;
using UnityEngine;

namespace SkyCrew.Data
{
    /// <summary>
    /// Configuracao da partida editavel no Inspector. So dados: o estado de execucao
    /// fica no MatchSession (nunca neste asset).
    /// </summary>
    [CreateAssetMenu(menuName = "SkyCrew/Configuração da Partida", fileName = "ConfiguracaoPartida")]
    public sealed class MatchConfigAsset : ScriptableObject
    {
        [Min(5f)] public float BoardingSeconds = 30f;
        [Min(30f)] public float FlightSeconds = 240f;
        [Range(0f, 1f)] public float FailSatisfactionThreshold = 0.2f;
        [Min(0f)] public float Difficulty = 1f;
        public long Seed = 12345;

        [Header("Eventos")]
        [Min(1f)] public float SelectionIntervalSeconds = 6f;
        [Min(0f)] public float GlobalMinIntervalSeconds = 35f;
        [Min(1)] public int MaxActiveEvents = 1;
        [Range(0f, 1f)] public float ChancePerRoll = 0.3f;

        public MatchConfig ToDomain()
        {
            var c = PrototypeScenario.CreateConfig((ulong)Seed);
            c.BoardingSeconds = BoardingSeconds;
            c.FlightSeconds = FlightSeconds;
            c.FailSatisfactionThreshold = FailSatisfactionThreshold;
            c.Difficulty = Difficulty;
            c.Events = new EventSchedulerConfig
            {
                SelectionIntervalSeconds = SelectionIntervalSeconds,
                GlobalMinIntervalSeconds = GlobalMinIntervalSeconds,
                MaxActiveEvents = MaxActiveEvents,
                ChancePerRoll = ChancePerRoll,
            };
            return c;
        }
    }

    /// <summary>Perfil de personalidade editavel (convertido para o PassengerProfile imutavel).</summary>
    [CreateAssetMenu(menuName = "SkyCrew/Perfil de Passageiro", fileName = "PerfilPassageiro")]
    public sealed class PassengerProfileAsset : ScriptableObject
    {
        public Personality Personality = Personality.Calmo;
        public string DisplayName = "Calmo";
        [Range(0f, 0.1f)] public float PatienceDecayWhileWaiting = 0.006f;
        [Range(0f, 0.1f)] public float ThirstPerSecond = 0.004f;
        [Range(0f, 3f)] public float FearSensitivity = 0.5f;
        [Range(0f, 0.5f)] public float FearRecoveryPerSecond = 0.08f;
        [Range(0f, 0.05f)] public float ComfortDecayPerSecond = 0.001f;
        [Range(0f, 0.05f)] public float LavatoryNeedPerSecond = 0.002f;
        [Range(0.3f, 3f)] public float WalkSpeed = 1.2f;
        [Range(0f, 1f)] public float Helpfulness = 0.4f;
        [Range(0f, 1f)] public float Clumsiness = 0.1f;
        [Range(0f, 1f)] public float ComplainTendency = 0.1f;

        public PassengerProfile ToDomain()
        {
            return new PassengerProfile(Personality, DisplayName, PatienceDecayWhileWaiting, ThirstPerSecond,
                                        FearSensitivity, FearRecoveryPerSecond, ComfortDecayPerSecond,
                                        LavatoryNeedPerSecond, WalkSpeed, Helpfulness, Clumsiness, ComplainTendency);
        }

        public void CopyFrom(PassengerProfile p)
        {
            Personality = p.Personality;
            DisplayName = p.DisplayName;
            PatienceDecayWhileWaiting = p.PatienceDecayWhileWaiting;
            ThirstPerSecond = p.ThirstPerSecond;
            FearSensitivity = p.FearSensitivity;
            FearRecoveryPerSecond = p.FearRecoveryPerSecond;
            ComfortDecayPerSecond = p.ComfortDecayPerSecond;
            LavatoryNeedPerSecond = p.LavatoryNeedPerSecond;
            WalkSpeed = p.WalkSpeed;
            Helpfulness = p.Helpfulness;
            Clumsiness = p.Clumsiness;
            ComplainTendency = p.ComplainTendency;
        }
    }
}
