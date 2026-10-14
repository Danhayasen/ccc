using NUnit.Framework;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.Tasks;

namespace SkyCrew.Tests.EditMode
{
    public class MatchStateMachineTests
    {
        [Test]
        public void ComecaEmInitializing()
        {
            var sm = new MatchStateMachine();
            Assert.AreEqual(MatchState.Initializing, sm.Current);
            Assert.IsFalse(sm.IsPlayable);
        }

        [TestCase(MatchState.Initializing, MatchState.Ready)]
        [TestCase(MatchState.Ready, MatchState.Boarding)]
        [TestCase(MatchState.Boarding, MatchState.InFlight)]
        [TestCase(MatchState.InFlight, MatchState.Results)]
        [TestCase(MatchState.InFlight, MatchState.Failed)]
        [TestCase(MatchState.Results, MatchState.ReturningToMenu)]
        [TestCase(MatchState.Failed, MatchState.Initializing)]
        public void TransicoesPermitidas(MatchState from, MatchState to)
        {
            Assert.IsTrue(MatchStateMachine.IsAllowed(from, to));
        }

        [TestCase(MatchState.Initializing, MatchState.InFlight)]
        [TestCase(MatchState.Ready, MatchState.InFlight)]
        [TestCase(MatchState.Boarding, MatchState.Results)]
        [TestCase(MatchState.Results, MatchState.InFlight)]
        [TestCase(MatchState.Results, MatchState.Results)]
        [TestCase(MatchState.ReturningToMenu, MatchState.Boarding)]
        public void TransicoesProibidas(MatchState from, MatchState to)
        {
            Assert.IsFalse(MatchStateMachine.IsAllowed(from, to));
        }

        [Test]
        public void TransicaoInvalidaNaoMudaEstadoNemDisparaEvento()
        {
            var sm = new MatchStateMachine();
            int calls = 0;
            sm.Changed += (a, b) => calls++;
            Assert.IsFalse(sm.TryTransition(MatchState.InFlight, out string reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(MatchState.Initializing, sm.Current);
            Assert.AreEqual(0, calls);
        }
    }

    public class MatchSessionTests
    {
        private static MatchSession NewSession(float boarding = 5f, float flight = 60f)
        {
            var config = new MatchConfig { BoardingSeconds = boarding, FlightSeconds = flight };
            config.RequiredTaskIds.Add("t1");
            var s = new MatchSession(config);
            s.Tasks.Register(new TaskDefinition("t1", "T1", "",
                new ObjectiveDefinition[] { new PerformActionObjectiveDefinition("x", "acao") }, reward: 100));
            return s;
        }

        [Test]
        public void InicializacaoIncompletaNaoFicaPronta()
        {
            var s = NewSession();
            s.RegisterInitStep("cena");
            s.RegisterInitStep("navmesh");
            Assert.IsFalse(s.CompleteInitStep("cena"));
            Assert.AreEqual(MatchState.Initializing, s.State.Current);
            Assert.IsFalse(s.TryBecomeReady(out string reason));
            StringAssert.Contains("navmesh", reason);
            Assert.IsTrue(s.CompleteInitStep("navmesh"));
            Assert.AreEqual(MatchState.Ready, s.State.Current);
        }

        [Test]
        public void EmbarqueTerminaAntesDoVoo()
        {
            var s = NewSession(boarding: 5f);
            Assert.IsTrue(s.TryBecomeReady(out _));
            Assert.IsTrue(s.StartBoarding(out _));
            s.Tick(4.9f);
            Assert.AreEqual(MatchState.Boarding, s.State.Current);
            Assert.AreEqual(0f, s.FlightTime);
            s.Tick(0.2f);
            Assert.AreEqual(MatchState.InFlight, s.State.Current);
        }

        [Test]
        public void TickForaDeJogoNaoAvanca()
        {
            var s = NewSession();
            s.Tick(10f);
            Assert.AreEqual(MatchState.Initializing, s.State.Current);
            Assert.AreEqual(0f, s.StateTime);
        }

        [Test]
        public void SinaisSaoIgnoradosForaDeJogo()
        {
            var s = NewSession();
            s.TryBecomeReady(out _);
            Assert.IsFalse(s.Tasks.TryActivate("t1", out _), "tarefas so ativam em Boarding/InFlight");
            s.Signal(GameplaySignal.Action("acao"));
            Assert.AreEqual(TaskState.Available, s.Tasks.Get("t1").State);
        }

        [Test]
        public void VitoriaSoNoPousoEResultadoUmaUnicaVez()
        {
            var s = NewSession(boarding: 1f, flight: 10f);
            s.TryBecomeReady(out _);
            s.StartBoarding(out _);
            s.Tick(1.1f);
            int results = 0;
            s.ResultsReady += r => results++;
            Assert.IsTrue(s.Tasks.TryActivate("t1", out _));
            s.Signal(GameplaySignal.Action("acao"));
            s.Tick(0.1f);
            Assert.AreEqual(MatchState.InFlight, s.State.Current, "o voo continua ate o pouso");
            for (int i = 0; i < 100; i++)
            {
                s.Tick(0.1f);
            }

            Assert.AreEqual(MatchState.Results, s.State.Current);
            Assert.IsTrue(s.Results.Victory);
            Assert.IsFalse(s.Finish(true, "de novo"), "conclusao duplicada deve ser rejeitada");
            s.Tick(1f);
            Assert.AreEqual(1, results);
            Assert.AreEqual(100, s.Score.Total, "recompensa aplicada uma vez");
        }

        [Test]
        public void TempoEsgotadoFalha()
        {
            var s = NewSession(boarding: 1f, flight: 10f);
            s.TryBecomeReady(out _);
            s.StartBoarding(out _);
            s.Tick(1.1f);
            for (int i = 0; i < 101; i++)
            {
                s.Tick(0.1f);
            }

            Assert.AreEqual(MatchState.Failed, s.State.Current);
            Assert.IsFalse(s.Results.Victory);
            Assert.AreEqual(0, s.Results.Score.Stars);
        }

        [Test]
        public void ReiniciarNaoPreservaEstadoAnterior()
        {
            var s = NewSession(boarding: 1f, flight: 5f);
            s.TryBecomeReady(out _);
            s.StartBoarding(out _);
            s.Tick(1.1f);
            s.Tasks.TryActivate("t1", out _);
            s.Signal(GameplaySignal.Action("acao"));
            for (int i = 0; i < 60; i++)
            {
                s.Tick(0.1f);
            }

            Assert.AreEqual(MatchState.Results, s.State.Current);
            var oldTasks = s.Tasks;

            Assert.IsTrue(s.Reset(out _));
            Assert.AreEqual(MatchState.Initializing, s.State.Current);
            Assert.AreEqual(0, s.Score.Total);
            Assert.AreEqual(0f, s.FlightTime);
            Assert.IsNull(s.Results);
            Assert.AreNotSame(oldTasks, s.Tasks);
            Assert.AreEqual(0, s.Tasks.All.Count);
            Assert.AreEqual(0, s.Passengers.Count);
        }

        [Test]
        public void NaoPodeReiniciarNoMeioDoVoo()
        {
            var s = NewSession(boarding: 1f);
            s.TryBecomeReady(out _);
            s.StartBoarding(out _);
            Assert.IsFalse(s.Reset(out _));
            Assert.AreEqual(MatchState.Boarding, s.State.Current);
        }
    }
}
