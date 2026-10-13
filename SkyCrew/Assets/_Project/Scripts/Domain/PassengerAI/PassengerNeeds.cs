using SkyCrew.Domain.Common;

namespace SkyCrew.Domain.PassengerAI
{
    public enum ServiceKind
    {
        None,
        Water,
    }

    /// <summary>Condicoes externas que alteram as necessidades.</summary>
    public readonly struct NeedsContext
    {
        public NeedsContext(bool waitingForService, float turbulenceIntensity, bool seated)
        {
            WaitingForService = waitingForService;
            TurbulenceIntensity = turbulenceIntensity < 0f ? 0f : turbulenceIntensity;
            Seated = seated;
        }

        public bool WaitingForService { get; }
        public float TurbulenceIntensity { get; }
        public bool Seated { get; }
    }

    /// <summary>
    /// Necessidades normalizadas [0, 1]. Mudam por taxas e eventos (nunca aleatoriamente por quadro).
    /// Paciencia e conforto: 1 = otimo. Medo, sede e banheiro: 0 = otimo.
    /// </summary>
    public sealed class PassengerNeeds
    {
        /// <summary>Medo ganho por segundo com turbulencia de intensidade 1 e sensibilidade 1.</summary>
        public const float FearGainPerSecond = 0.12f;

        /// <summary>Paciencia perdida por segundo em pe no corredor durante turbulencia.</summary>
        public const float ComfortLossStandingInTurbulence = 0.02f;

        private readonly PassengerProfile _profile;

        public PassengerNeeds(PassengerProfile profile)
        {
            _profile = profile;
            Patience = 1f;
            Comfort = 1f;
        }

        public float Patience { get; private set; }
        public float Comfort { get; private set; }
        public float Fear { get; private set; }
        public float Thirst { get; private set; }
        public float Lavatory { get; private set; }

        /// <summary>Satisfacao geral [0, 1] usada na pontuacao e na condicao de falha.</summary>
        public float Satisfaction => 0.45f * Patience + 0.30f * Comfort + 0.25f * (1f - Fear);

        public void Tick(float dt, in NeedsContext ctx)
        {
            if (dt <= 0f)
            {
                return;
            }

            Thirst = MathUtil.Clamp01(Thirst + _profile.ThirstPerSecond * dt);
            Lavatory = MathUtil.Clamp01(Lavatory + _profile.LavatoryNeedPerSecond * dt);

            if (ctx.WaitingForService)
            {
                Patience = MathUtil.Clamp01(Patience - _profile.PatienceDecayWhileWaiting * dt);
            }

            if (ctx.TurbulenceIntensity > 0f)
            {
                Fear = MathUtil.Clamp01(Fear + ctx.TurbulenceIntensity * _profile.FearSensitivity * FearGainPerSecond * dt);
                if (!ctx.Seated)
                {
                    Comfort = MathUtil.Clamp01(Comfort - ComfortLossStandingInTurbulence * dt);
                }
            }
            else
            {
                Fear = MathUtil.Clamp01(Fear - _profile.FearRecoveryPerSecond * dt);
            }

            Comfort = MathUtil.Clamp01(Comfort - _profile.ComfortDecayPerSecond * (1f + ctx.TurbulenceIntensity) * dt);
        }

        public void Serve(ServiceKind kind)
        {
            if (kind == ServiceKind.Water)
            {
                Thirst = 0f;
            }

            Patience = MathUtil.Clamp01(Patience + 0.25f);
            Comfort = MathUtil.Clamp01(Comfort + 0.10f);
        }

        public void Calm()
        {
            Fear = MathUtil.Clamp01(Fear - 0.6f);
            Patience = MathUtil.Clamp01(Patience + 0.05f);
        }

        public void UseLavatory()
        {
            Lavatory = 0f;
            Comfort = MathUtil.Clamp01(Comfort + 0.15f);
        }

        /// <summary>Somente para testes e ferramentas de diagnostico.</summary>
        public void DebugSet(float? patience = null, float? comfort = null, float? fear = null,
                             float? thirst = null, float? lavatory = null)
        {
            if (patience.HasValue) Patience = MathUtil.Clamp01(patience.Value);
            if (comfort.HasValue) Comfort = MathUtil.Clamp01(comfort.Value);
            if (fear.HasValue) Fear = MathUtil.Clamp01(fear.Value);
            if (thirst.HasValue) Thirst = MathUtil.Clamp01(thirst.Value);
            if (lavatory.HasValue) Lavatory = MathUtil.Clamp01(lavatory.Value);
        }
    }
}
