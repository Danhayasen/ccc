using System.Linq;
using NUnit.Framework;
using SkyCrew.Domain.Content;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Domain.Tasks;

namespace SkyCrew.Tests.EditMode
{
    /// <summary>Simula uma viagem inteira da Fase 1 so com as regras (sem Unity).</summary>
    public class PrototypeScenarioTests
    {
        private MatchSession _s;
        private PrototypeScenario _scenario;
        private int _trashRequested;

        [SetUp]
        public void SetUp()
        {
            _s = new MatchSession(PrototypeScenario.CreateConfig(seed: 2024));
            _scenario = PrototypeScenario.Install(_s);
            _trashRequested = 0;
            _scenario.WorldRequested += r =>
            {
                if (r.Kind == WorldRequestKind.Trash && r.PassengerId == null)
                {
                    _trashRequested += r.ItemIds.Count;
                }
            };
        }

        private void Run(float seconds)
        {
            for (float t = 0; t < seconds; t += 0.05f)
            {
                _s.Tick(0.05f);
                // corpo "falso": quem foi mandado andar chega na hora
                foreach (var p in _s.Passengers)
                {
                    while (p.TryDequeueCommand(out var c))
                    {
                        if (c.Kind == CommandKind.WalkTo)
                        {
                            p.OnArrived();
                        }
                    }
                }
            }
        }

        private void Start()
        {
            Assert.IsTrue(_s.TryBecomeReady(out _));
            Assert.IsTrue(_s.StartBoarding(out _));
        }

        [Test]
        public void EmbarqueAtivaTarefaDaMalaEPassageiroCaminha()
        {
            Start();
            Assert.AreEqual(TaskState.Active, _s.Tasks.Get(PrototypeScenario.TaskStoreSuitcase).State);
            var lia = _s.GetPassenger("p_lia");
            Assert.AreEqual(PassengerStateId.WalkingToDestination, lia.State);
            Run(1f);
            Assert.AreEqual(PassengerStateId.Seated, lia.State);
        }

        [Test]
        public void ViagemCompletaComVitoria()
        {
            Start();
            _s.Signal(GameplaySignal.PlacedInZone(ItemTypes.Suitcase, PrototypeScenario.ZoneOverheadBin,
                                                  itemId: "outra_mala"));
            Assert.AreEqual(TaskState.Active, _s.Tasks.Get(PrototypeScenario.TaskStoreSuitcase).State,
                            "so a mala esquecida conta para a tarefa obrigatoria");
            _s.Signal(GameplaySignal.PlacedInZone(ItemTypes.Suitcase, PrototypeScenario.ZoneOverheadBin,
                                                  itemId: PrototypeScenario.ForgottenSuitcaseId));
            Assert.AreEqual(TaskState.Completed, _s.Tasks.Get(PrototypeScenario.TaskStoreSuitcase).State);

            Run(_s.Config.BoardingSeconds + 0.1f);
            Assert.AreEqual(MatchState.InFlight, _s.State.Current);

            // Bia (sedenta) pede agua em ~20 s de voo; a turbulencia garantida vem aos 25 s.
            Run(PrototypeScenario.GuaranteedTurbulenceAt + 1f);
            var agua = _s.Tasks.Get(PrototypeScenario.TaskServeWater);
            Assert.IsNotNull(agua, "pedido de agua deveria criar a tarefa");
            Assert.AreEqual(TaskState.Active, agua.State);
            Assert.IsFalse(_s.TryDeliverToPassenger("p_ze", ItemTypes.Water), "Ze nao pediu agua");
            Assert.IsTrue(_s.TryDeliverToPassenger("p_bia", ItemTypes.Water));
            Assert.AreEqual(TaskState.Completed, agua.State);

            var calma = _s.Tasks.Get(PrototypeScenario.TaskCalmPassenger);
            Assert.IsNotNull(calma, "turbulencia deveria criar a tarefa de acalmar");
            var alvo = ((TalkToObjectiveDefinition)calma.Definition.Objectives[0]).TargetId;
            Assert.AreEqual("p_otto", alvo, "o ansioso e quem se assusta");
            Assert.AreEqual(PrototypeScenario.TrashPerTurbulence, _trashRequested);

            Assert.IsTrue(_s.TryTalkToPassenger(alvo));
            Assert.AreEqual(TaskState.Completed, calma.State);
            int rewardsAntesDoPouso = _s.Score.Rewards;
            Run(_s.Config.FlightSeconds);
            Assert.AreEqual(MatchState.Results, _s.State.Current, _s.Results?.Reason);
            Assert.IsTrue(_s.Results.Victory);
            Assert.Greater(_s.Score.Penalties, 0, "pedidos ignorados custam pontos");
            Assert.AreEqual(0, _s.Results.Score.Stars, "so as obrigatorias: vence, mas sem estrelas");
            // 150 (mala) + 200 + 50 (agua, rapida) + 200 + 50 (acalmar, rapido)
            Assert.AreEqual(150 + 200 + 50 + 200 + 50, rewardsAntesDoPouso);
            Assert.IsTrue(_s.Passengers.All(p => p.State == PassengerStateId.Disabled));
        }

        [Test]
        public void IgnorarAAguaFazAPartidaFalhar()
        {
            Start();
            _s.Signal(GameplaySignal.PlacedInZone(ItemTypes.Suitcase, PrototypeScenario.ZoneOverheadBin,
                                                  itemId: PrototypeScenario.ForgottenSuitcaseId));
            Run(_s.Config.BoardingSeconds + 0.1f);
            Run(120f);
            Assert.AreEqual(MatchState.Failed, _s.State.Current);
            StringAssert.Contains("obrigatoria", _s.Results.Reason);
        }

        [Test]
        public void ReiniciarRecriaConteudo()
        {
            Start();
            _s.Finish(false, "teste");
            Assert.IsTrue(_s.Reset(out _));
            Assert.AreEqual(PrototypeScenario.DefaultPassengers.Length, _s.Passengers.Count);
            Assert.IsTrue(_s.Tasks.IsRegistered(PrototypeScenario.TaskStoreSuitcase));
            Assert.AreEqual(TaskState.Available, _s.Tasks.Get(PrototypeScenario.TaskStoreSuitcase).State);
            Assert.IsFalse(_s.Tasks.IsRegistered(PrototypeScenario.TaskServeWater));
            Start();
            Assert.AreEqual(TaskState.Active, _s.Tasks.Get(PrototypeScenario.TaskStoreSuitcase).State);
        }
    }
}
