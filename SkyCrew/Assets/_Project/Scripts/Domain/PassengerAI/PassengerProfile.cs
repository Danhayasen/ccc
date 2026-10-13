using System;

namespace SkyCrew.Domain.PassengerAI
{
    public enum Personality
    {
        Calmo,
        Impaciente,
        Ansioso,
        Exigente,
        Distraido,
        Prestativo,
        Desastrado,
    }

    /// <summary>
    /// Parametros de personalidade (imutaveis). Taxas sao "por segundo";
    /// valores normalizados ficam em [0, 1].
    /// </summary>
    public sealed class PassengerProfile
    {
        public PassengerProfile(Personality personality, string displayName,
                                float patienceDecayWhileWaiting, float thirstPerSecond,
                                float fearSensitivity, float fearRecoveryPerSecond,
                                float comfortDecayPerSecond, float lavatoryNeedPerSecond,
                                float walkSpeed, float helpfulness, float clumsiness,
                                float complainTendency)
        {
            Personality = personality;
            DisplayName = displayName;
            PatienceDecayWhileWaiting = Math.Max(0f, patienceDecayWhileWaiting);
            ThirstPerSecond = Math.Max(0f, thirstPerSecond);
            FearSensitivity = Math.Max(0f, fearSensitivity);
            FearRecoveryPerSecond = Math.Max(0f, fearRecoveryPerSecond);
            ComfortDecayPerSecond = Math.Max(0f, comfortDecayPerSecond);
            LavatoryNeedPerSecond = Math.Max(0f, lavatoryNeedPerSecond);
            WalkSpeed = Math.Max(0.1f, walkSpeed);
            Helpfulness = Common.MathUtil.Clamp01(helpfulness);
            Clumsiness = Common.MathUtil.Clamp01(clumsiness);
            ComplainTendency = Common.MathUtil.Clamp01(complainTendency);
        }

        public Personality Personality { get; }
        public string DisplayName { get; }
        public float PatienceDecayWhileWaiting { get; }
        public float ThirstPerSecond { get; }
        public float FearSensitivity { get; }
        public float FearRecoveryPerSecond { get; }
        public float ComfortDecayPerSecond { get; }
        public float LavatoryNeedPerSecond { get; }
        public float WalkSpeed { get; }
        public float Helpfulness { get; }
        public float Clumsiness { get; }
        public float ComplainTendency { get; }

        /// <summary>Copia com outra taxa de sede (usado para criar o passageiro "sedento" do prototipo).</summary>
        public PassengerProfile WithThirst(float thirstPerSecond)
        {
            return new PassengerProfile(Personality, DisplayName, PatienceDecayWhileWaiting, thirstPerSecond,
                                        FearSensitivity, FearRecoveryPerSecond, ComfortDecayPerSecond,
                                        LavatoryNeedPerSecond, WalkSpeed, Helpfulness, Clumsiness,
                                        ComplainTendency);
        }
    }

    /// <summary>Perfis padrao das 7 personalidades. Ajustaveis via ScriptableObject na camada Unity.</summary>
    public static class PassengerProfiles
    {
        public static PassengerProfile Get(Personality p)
        {
            switch (p)
            {
                //                                             espera  sede    medo  recup conf    banh    vel  ajuda desast recl
                case Personality.Calmo:
                    return new PassengerProfile(p, "Calmo", 0.006f, 0.004f, 0.5f, 0.08f, 0.0010f, 0.002f, 1.2f, 0.4f, 0.1f, 0.1f);
                case Personality.Impaciente:
                    return new PassengerProfile(p, "Impaciente", 0.020f, 0.006f, 0.8f, 0.05f, 0.0020f, 0.003f, 1.6f, 0.1f, 0.2f, 0.7f);
                case Personality.Ansioso:
                    return new PassengerProfile(p, "Ansioso", 0.012f, 0.005f, 1.8f, 0.02f, 0.0020f, 0.004f, 1.3f, 0.2f, 0.3f, 0.4f);
                case Personality.Exigente:
                    return new PassengerProfile(p, "Exigente", 0.016f, 0.010f, 0.9f, 0.05f, 0.0030f, 0.002f, 1.3f, 0.1f, 0.1f, 0.9f);
                case Personality.Distraido:
                    return new PassengerProfile(p, "Distraído", 0.008f, 0.005f, 0.6f, 0.06f, 0.0010f, 0.003f, 1.0f, 0.3f, 0.5f, 0.2f);
                case Personality.Prestativo:
                    return new PassengerProfile(p, "Prestativo", 0.007f, 0.004f, 0.6f, 0.07f, 0.0010f, 0.002f, 1.3f, 0.9f, 0.1f, 0.1f);
                case Personality.Desastrado:
                    return new PassengerProfile(p, "Desastrado", 0.010f, 0.006f, 1.0f, 0.05f, 0.0015f, 0.003f, 1.4f, 0.5f, 0.9f, 0.3f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(p), p, null);
            }
        }
    }
}
