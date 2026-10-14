using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SkyCrew.Domain.Common;
using SkyCrew.Domain.Events;
using SkyCrew.Domain.Interaction;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.PassengerAI;

namespace SkyCrew.Tests.EditMode
{
    internal sealed class FakeWorld : IPassengerWorld
    {
        public bool SeatbeltSignOn { get; set; }
        public float TurbulenceIntensity { get; set; }
        public bool CanRequestService { get; set; } = true;
    }

    public class PassengerTests
    {
        private static void Run(PassengerBrain b, IPassengerWorld w, float seconds, float step = 0.05f)
        {
            for (float t = 0; t < seconds; t += step)
            {
                b.Tick(step, w);
            }
        }

        private static List<PassengerCommand> Drain(PassengerBrain b)
        {
            var list = new List<PassengerCommand>();
            while (b.TryDequeueCommand(out var c))
            {
                list.Add(c);
            }

            return list;
        }

        [Test]
        public void PersonalidadesSaoDiferentes()
        {
            var calmo = PassengerProfiles.Get(Personality.Calmo);
            var ansioso = PassengerProfiles.Get(Personality.Ansioso);
            var impaciente = PassengerProfiles.Get(Personality.Impaciente);
            Assert.Greater(ansioso.FearSensitivity, calmo.FearSensitivity);
            Assert.Greater(impaciente.PatienceDecayWhileWaiting, calmo.PatienceDecayWhileWaiting);
            foreach (Personality p in System.Enum.GetValues(typeof(Personality)))
            {
                Assert.DoesNotThrow(() => PassengerProfiles.Get(p));
            }
        }

        [Test]
        public void SedeGeraPedidoEEntregaResolve()
        {
            var b = new PassengerBrain("p1", PassengerProfiles.Get(Personality.Exigente));
            var w = new FakeWorld();
            b.SitImmediately();
            ServiceKind pedido = ServiceKind.None;
            b.ServiceRequested += (_, k) => pedido = k;
            b.Needs.DebugSet(thirst: 0.75f);
            Run(b, w, 1f);
            Assert.AreEqual(ServiceKind.Water, pedido);
            Assert.AreEqual(PassengerStateId.Waiting, b.State);
            Assert.IsTrue(Drain(b).Any(c => c.Kind == CommandKind.ShowRequest));

            float paciencia = b.Needs.Patience;
            Run(b, w, 5f);
            Assert.Less(b.Needs.Patience, paciencia, "paciencia cai esperando");

            Assert.IsTrue(b.Serve(ServiceKind.Water));
            Assert.AreEqual(PassengerStateId.Seated, b.State);
            Assert.AreEqual(0f, b.Needs.Thirst);
            Assert.IsFalse(b.Serve(ServiceKind.Water), "nao aceita o que nao pediu");
        }

        [Test]
        public void SemPedidoQuandoServicoNaoPermitido()
        {
            var b = new PassengerBrain("p1", PassengerProfiles.Get(Personality.Exigente));
            var w = new FakeWorld { CanRequestService = false };
            b.SitImmediately();
            b.Needs.DebugSet(thirst: 0.9f);
            Run(b, w, 2f);
            Assert.AreEqual(PassengerStateId.Seated, b.State);
        }

        [Test]
        public void TurbulenciaAssustaEConversaAcalma()
        {
            var b = new PassengerBrain("p1", PassengerProfiles.Get(Personality.Ansioso));
            var w = new FakeWorld { TurbulenceIntensity = 1f };
            b.SitImmediately();
            Run(b, w, 5f);
            Assert.AreEqual(PassengerStateId.ReactingToEvent, b.State);
            Assert.IsTrue(b.Calm());
            Assert.AreEqual(PassengerStateId.Recovering, b.State);
            w.TurbulenceIntensity = 0f;
            Run(b, w, 3f);
            Assert.AreEqual(PassengerStateId.Seated, b.State);
        }

        [Test]
        public void CalmoAguentaTurbulenciaCurta()
        {
            var b = new PassengerBrain("p1", PassengerProfiles.Get(Personality.Calmo));
            var w = new FakeWorld { TurbulenceIntensity = 0.8f };
            b.SitImmediately();
            Run(b, w, 5f);
            Assert.AreEqual(PassengerStateId.Seated, b.State);
        }

        [Test]
        public void AvisoDeCintoImpedeIrAoBanheiro()
        {
            var b = new PassengerBrain("p1", PassengerProfiles.Get(Personality.Calmo));
            var w = new FakeWorld { SeatbeltSignOn = true };
            b.SitImmediately();
            b.Needs.DebugSet(lavatory: 0.95f);
            Run(b, w, 2f);
            Assert.AreEqual(PassengerStateId.Seated, b.State);

            w.SeatbeltSignOn = false;
            Run(b, w, 0.5f);
            Assert.AreEqual(PassengerStateId.WalkingToDestination, b.State);
            Assert.AreEqual(DestinationKind.Lavatory, b.CurrentDestination);
        }

        [Test]
        public void CintoAcesoNoCaminhoFazVoltar()
        {
            var b = new PassengerBrain("p1", PassengerProfiles.Get(Personality.Calmo));
            var w = new FakeWorld();
            b.SitImmediately();
            b.Needs.DebugSet(lavatory: 0.95f);
            Run(b, w, 0.5f);
            Assert.AreEqual(PassengerStateId.WalkingToDestination, b.State);
            w.SeatbeltSignOn = true;
            Run(b, w, 0.5f);
            Assert.AreEqual(PassengerStateId.ReturningToSeat, b.State);
            b.OnArrived();
            Assert.AreEqual(PassengerStateId.Seated, b.State);
        }

        [Test]
        public void CicloCompletoDoBanheiro()
        {
            var b = new PassengerBrain("p1", PassengerProfiles.Get(Personality.Calmo));
            var w = new FakeWorld();
            b.SitImmediately();
            b.Needs.DebugSet(lavatory: 0.95f);
            Run(b, w, 0.5f);
            b.OnArrived();
            Assert.AreEqual(PassengerStateId.Interacting, b.State);
            Run(b, w, 5f);
            Assert.AreEqual(PassengerStateId.ReturningToSeat, b.State);
            Assert.AreEqual(0f, b.Needs.Lavatory, 0.01f);
            b.OnArrived();
            Assert.AreEqual(PassengerStateId.Seated, b.State);
        }

        [Test]
        public void FalhaDeRotaTentaDeNovoEDepoisRecupera()
        {
            var cfg = new PassengerBrainConfig { MaxNavigationRetries = 2 };
            var b = new PassengerBrain("p1", PassengerProfiles.Get(Personality.Calmo), cfg);
            Assert.IsTrue(b.BeginBoarding());
            Drain(b);
            b.OnNavigationFailed();
            b.OnNavigationFailed();
            var retries = Drain(b);
            Assert.AreEqual(2, retries.Count(c => c.Kind == CommandKind.WalkTo));
            Assert.AreEqual(PassengerStateId.WalkingToDestination, b.State);

            b.OnNavigationFailed();
            Assert.AreEqual(PassengerStateId.Recovering, b.State);
            Assert.IsTrue(Drain(b).Any(c => c.Kind == CommandKind.SnapToSeat), "preso: teletransporta para a poltrona");
            Run(b, new FakeWorld(), 2f);
            Assert.AreEqual(PassengerStateId.Seated, b.State);
        }

        [Test]
        public void SemCiclosSemProgressoNumVooCalmo()
        {
            var b = new PassengerBrain("p1", PassengerProfiles.Get(Personality.Impaciente));
            var w = new FakeWorld { CanRequestService = false };
            b.SitImmediately();
            Run(b, w, 60f);
            Assert.LessOrEqual(b.TransitionCount, 3, string.Join("\n", b.History));
        }

        [Test]
        public void DesabilitadoIgnoraTudo()
        {
            var b = new PassengerBrain("p1", PassengerProfiles.Get(Personality.Ansioso));
            b.SitImmediately();
            b.Disable();
            Run(b, new FakeWorld { TurbulenceIntensity = 1f }, 5f);
            Assert.AreEqual(PassengerStateId.Disabled, b.State);
            Assert.IsFalse(b.Calm());
        }

        [Test]
        public void TabelaDeTransicoes()
        {
            Assert.IsTrue(PassengerBrain.IsTransitionAllowed(PassengerStateId.Seated, PassengerStateId.RequestingService));
            Assert.IsFalse(PassengerBrain.IsTransitionAllowed(PassengerStateId.Disabled, PassengerStateId.Seated));
            Assert.IsFalse(PassengerBrain.IsTransitionAllowed(PassengerStateId.Waiting, PassengerStateId.WalkingToDestination));
        }
    }

    public class EventSchedulerTests
    {
        private static EventDefinition Turb(string id = "turb", float cooldown = 30f, int max = 0)
            => new EventDefinition(id, "Turbulencia", 10f, cooldownSeconds: cooldown, maxOccurrences: max,
                tags: new[] { "turbulencia" }, incompatibleTags: new[] { "turbulencia" });

        private static EventContext Flight(float t) => new EventContext(MatchState.InFlight, t);

        [Test]
        public void SoNoEstadoPermitido()
        {
            var s = new EventScheduler(new DeterministicRandom(1));
            s.Register(Turb());
            Assert.IsFalse(s.TryStart("turb", new EventContext(MatchState.Boarding, 100f), out string reason));
            StringAssert.Contains("Boarding", reason);
            Assert.IsTrue(s.TryStart("turb", Flight(100f), out _));
        }

        [Test]
        public void CondicaoDeTempoMinimo()
        {
            var s = new EventScheduler(new DeterministicRandom(1));
            s.Register(new EventDefinition("e", "E", 5f, conditions: new IEventCondition[] { new MinFlightTimeCondition(20f) }));
            Assert.IsFalse(s.TryStart("e", Flight(10f), out _));
            Assert.IsTrue(s.TryStart("e", Flight(25f), out _));
        }

        [Test]
        public void IncompativeisNaoSimultaneos()
        {
            var s = new EventScheduler(new DeterministicRandom(1), new EventSchedulerConfig { MaxActiveEvents = 3 });
            s.Register(Turb("leve"));
            s.Register(Turb("forte"));
            s.Register(new EventDefinition("derrame", "Derramamento", 5f, tags: new[] { "cabine" }));
            Assert.IsTrue(s.TryStart("leve", Flight(50f), out _, ignoreGlobalInterval: true));
            Assert.IsFalse(s.TryStart("forte", Flight(50f), out string reason, ignoreGlobalInterval: true));
            StringAssert.Contains("incompativel", reason);
            Assert.IsTrue(s.TryStart("derrame", Flight(50f), out _, ignoreGlobalInterval: true));
        }

        [Test]
        public void EncerraERespeitaCooldown()
        {
            var s = new EventScheduler(new DeterministicRandom(1), new EventSchedulerConfig { ChancePerRoll = 0f });
            s.Register(Turb(cooldown: 30f));
            int ended = 0;
            s.Ended += (e, cancelled) => ended++;
            s.TryStart("turb", Flight(0f), out _);
            for (int i = 0; i < 110; i++) s.Tick(0.1f, Flight(i * 0.1f));
            Assert.AreEqual(1, ended);
            Assert.AreEqual(0, s.Active.Count);
            Assert.IsFalse(s.TryStart("turb", Flight(11f), out string reason, ignoreGlobalInterval: true));
            StringAssert.Contains("cooldown", reason);
        }

        [Test]
        public void LimiteDeRepeticoes()
        {
            var s = new EventScheduler(new DeterministicRandom(1));
            s.Register(Turb(cooldown: 0f, max: 1));
            s.TryStart("turb", Flight(0f), out _);
            s.CancelAll();
            Assert.IsFalse(s.TryStart("turb", Flight(100f), out string reason, ignoreGlobalInterval: true));
            StringAssert.Contains("repeticoes", reason);
        }

        [Test]
        public void RoteirizadoAconteceNoTempo()
        {
            var s = new EventScheduler(new DeterministicRandom(1), new EventSchedulerConfig { ChancePerRoll = 0f });
            s.Register(Turb());
            s.AddScripted(20f, "turb");
            float t = 0f;
            for (int i = 0; i < 190; i++) { t += 0.1f; s.Tick(0.1f, Flight(t)); }
            Assert.AreEqual(0, s.OccurrencesOf("turb"));
            for (int i = 0; i < 20; i++) { t += 0.1f; s.Tick(0.1f, Flight(t)); }
            Assert.AreEqual(1, s.OccurrencesOf("turb"));
        }

        private static List<string> Simular(ulong seed)
        {
            var s = new EventScheduler(new DeterministicRandom(seed),
                new EventSchedulerConfig { SelectionIntervalSeconds = 2f, GlobalMinIntervalSeconds = 5f, ChancePerRoll = 0.5f, MaxActiveEvents = 2 });
            s.Register(new EventDefinition("a", "A", 3f, weight: 1f, cooldownSeconds: 4f));
            s.Register(new EventDefinition("b", "B", 3f, weight: 2f, cooldownSeconds: 4f));
            s.Register(new EventDefinition("c", "C", 3f, weight: 3f, cooldownSeconds: 4f));
            var log = new List<string>();
            float t = 0f;
            s.Started += e => log.Add($"{t:0.0}:{e.InstanceId}");
            for (int i = 0; i < 2000; i++) { t += 0.1f; s.Tick(0.1f, Flight(t)); }
            return log;
        }

        [Test]
        public void MesmaSementeMesmaSequencia()
        {
            var a = Simular(42);
            var b = Simular(42);
            var c = Simular(7);
            Assert.Greater(a.Count, 5);
            CollectionAssert.AreEqual(a, b);
            CollectionAssert.AreNotEqual(a, c);
        }

        [Test]
        public void IntervaloGlobalRespeitado()
        {
            var log = Simular(99);
            float last = float.NegativeInfinity;
            foreach (string line in log)
            {
                float t = float.Parse(line.Split(':')[0], System.Globalization.CultureInfo.InvariantCulture);
                Assert.GreaterOrEqual(t - last, 5f - 0.11f);
                last = t;
            }
        }
    }

    public class InteractionTests
    {
        private static InteractionQuery Q(InteractionVerb v, string held = null, float dist = 1f,
                                          MatchState st = MatchState.InFlight, string accepted = null, bool available = true)
            => new InteractionQuery(v, dist, 2f, st, held, accepted, available);

        [Test]
        public void ForaDeAlcance() => Assert.IsFalse(InteractionRules.Validate(Q(InteractionVerb.Talk, dist: 2.5f)).Allowed);

        [Test]
        public void EstadoDaPartida()
        {
            Assert.IsFalse(InteractionRules.Validate(Q(InteractionVerb.Talk, st: MatchState.Results)).Allowed);
            Assert.IsFalse(InteractionRules.Validate(Q(InteractionVerb.Talk, st: MatchState.Ready)).Allowed);
            Assert.IsTrue(InteractionRules.Validate(Q(InteractionVerb.Talk, st: MatchState.Boarding)).Allowed);
        }

        [Test]
        public void MaosEItens()
        {
            Assert.IsFalse(InteractionRules.Validate(Q(InteractionVerb.PickUp, held: "agua")).Allowed);
            Assert.IsTrue(InteractionRules.Validate(Q(InteractionVerb.PickUp)).Allowed);
            Assert.IsFalse(InteractionRules.Validate(Q(InteractionVerb.Deliver)).Allowed);
            Assert.IsFalse(InteractionRules.Validate(Q(InteractionVerb.Dispose, held: "mala", accepted: "lixo")).Allowed);
            Assert.IsTrue(InteractionRules.Validate(Q(InteractionVerb.Dispose, held: "lixo", accepted: "lixo")).Allowed);
            Assert.IsFalse(InteractionRules.Validate(Q(InteractionVerb.Use, available: false)).Allowed);
        }

        [Test]
        public void SelecionaMelhorAlvo()
        {
            var c = new List<InteractionCandidate>
            {
                new InteractionCandidate(0.5f, 120f, 0, true),  // atras do jogador
                new InteractionCandidate(1.8f, 10f, 0, true),
                new InteractionCandidate(1.0f, 15f, 0, false), // invalido
                new InteractionCandidate(1.2f, 20f, 0, true),
            };
            Assert.AreEqual(3, InteractionTargetSelector.SelectBest(c));
            c.Add(new InteractionCandidate(1.9f, 30f, 5, true)); // prioridade alta
            Assert.AreEqual(4, InteractionTargetSelector.SelectBest(c));
            Assert.AreEqual(-1, InteractionTargetSelector.SelectBest(new List<InteractionCandidate>()));
        }

        [Test]
        public void ItemNoChaoGanhaDePassageiroQueNaoPrecisaDeNada()
        {
            // Mala no corredor logo a frente e um passageiro sentado ao lado (ambos validos).
            var mala = new InteractionCandidate(0.6f, 10f, InteractionPriorities.Item, true);
            var tranquilo = new InteractionCandidate(0.8f, 40f, InteractionPriorities.Passenger(false, false), true);
            Assert.AreEqual(0, InteractionTargetSelector.SelectBest(new List<InteractionCandidate> { mala, tranquilo }));

            // Em panico (conversar acalma) ou recebendo o que pediu: o passageiro vem primeiro.
            var panico = new InteractionCandidate(0.8f, 40f, InteractionPriorities.Passenger(true, false), true);
            Assert.AreEqual(1, InteractionTargetSelector.SelectBest(new List<InteractionCandidate> { mala, panico }));
            var entrega = new InteractionCandidate(0.8f, 40f, InteractionPriorities.Passenger(false, true), true);
            var carrinho = new InteractionCandidate(0.7f, 20f, InteractionPriorities.Station, true);
            Assert.AreEqual(1, InteractionTargetSelector.SelectBest(new List<InteractionCandidate> { carrinho, entrega }));
            // Maos vazias perto de quem pediu algo: o carrinho continua alcancavel.
            Assert.AreEqual(0, InteractionTargetSelector.SelectBest(new List<InteractionCandidate> { carrinho, tranquilo }));
        }
    }
}
