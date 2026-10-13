using System.Collections.Generic;
using NUnit.Framework;
using SkyCrew.Domain.Scoring;
using SkyCrew.Domain.Tasks;

namespace SkyCrew.Tests.EditMode
{
    public class TaskManagerTests
    {
        private ScoreLedger _ledger;
        private TaskManager _tasks;
        private bool _allowed;

        [SetUp]
        public void SetUp()
        {
            _ledger = new ScoreLedger();
            _allowed = true;
            _tasks = new TaskManager(_ledger, () => _allowed);
        }

        private static TaskDefinition Water(string id = "agua", float limit = 0f, int penalty = 0, string[] pre = null)
            => new TaskDefinition(id, "Agua", "",
                new ObjectiveDefinition[] { new DeliverItemObjectiveDefinition("entregar", "agua", "p1") },
                timeLimitSeconds: limit, reward: 200, penalty: penalty, prerequisites: pre);

        [Test]
        public void AtivaEConclui()
        {
            _tasks.Register(Water());
            var changes = new List<TaskState>();
            _tasks.StateChanged += (t, prev) => changes.Add(t.State);
            Assert.IsTrue(_tasks.TryActivate("agua", out _));
            _tasks.HandleSignal(GameplaySignal.Delivered("agua", "p1"));
            Assert.AreEqual(TaskState.Completed, _tasks.Get("agua").State);
            CollectionAssert.AreEqual(new[] { TaskState.Active, TaskState.Completed }, changes);
            Assert.AreEqual(200, _ledger.Total);
        }

        [Test]
        public void SinalErradoNaoProgride()
        {
            _tasks.Register(Water());
            _tasks.TryActivate("agua", out _);
            _tasks.HandleSignal(GameplaySignal.Delivered("agua", "p2"));
            _tasks.HandleSignal(GameplaySignal.Delivered("cafe", "p1"));
            _tasks.HandleSignal(GameplaySignal.Talked("p1"));
            Assert.AreEqual(TaskState.Active, _tasks.Get("agua").State);
            Assert.AreEqual(0f, _tasks.Get("agua").Progress);
        }

        [Test]
        public void SinalAntesDaAtivacaoNaoConta()
        {
            _tasks.Register(Water());
            _tasks.HandleSignal(GameplaySignal.Delivered("agua", "p1"));
            Assert.AreEqual(TaskState.Available, _tasks.Get("agua").State);
            _tasks.TryActivate("agua", out _);
            Assert.AreEqual(0f, _tasks.Get("agua").Progress);
        }

        [Test]
        public void RecompensaUmaUnicaVezMesmoComSinaisDuplicados()
        {
            _tasks.Register(Water());
            _tasks.TryActivate("agua", out _);
            _tasks.HandleSignal(GameplaySignal.Delivered("agua", "p1"));
            _tasks.HandleSignal(GameplaySignal.Delivered("agua", "p1"));
            Assert.IsFalse(_tasks.TryActivate("agua", out _));
            _tasks.HandleSignal(GameplaySignal.Delivered("agua", "p1"));
            Assert.AreEqual(200, _ledger.Total);
            Assert.AreEqual(1, _ledger.Entries.Count);
        }

        [Test]
        public void PrazoEsgotadoFalhaComPenalidade()
        {
            _tasks.Register(Water(limit: 10f, penalty: 50));
            _tasks.TryActivate("agua", out _);
            _tasks.Tick(9.9f);
            Assert.AreEqual(TaskState.Active, _tasks.Get("agua").State);
            _tasks.Tick(0.2f);
            Assert.AreEqual(TaskState.Failed, _tasks.Get("agua").State);
            Assert.AreEqual(-50, _ledger.Total);
            _tasks.HandleSignal(GameplaySignal.Delivered("agua", "p1"));
            Assert.AreEqual(TaskState.Failed, _tasks.Get("agua").State, "tarefa falhada nao conclui depois");
        }

        [Test]
        public void CanceladaNaoContaComoConcluida()
        {
            _tasks.Register(Water());
            _tasks.TryActivate("agua", out _);
            Assert.IsTrue(_tasks.Cancel("agua", "teste"));
            _tasks.HandleSignal(GameplaySignal.Delivered("agua", "p1"));
            Assert.AreEqual(TaskState.Cancelled, _tasks.Get("agua").State);
            Assert.AreEqual(0, _ledger.Total);
        }

        [Test]
        public void PreRequisitoBloqueiaECancelamentoPropaga()
        {
            _tasks.Register(new TaskDefinition("buscar", "Buscar garrafa", "",
                new ObjectiveDefinition[] { new PerformActionObjectiveDefinition("pegar", "pegar_garrafa") }));
            _tasks.Register(Water("entregar", pre: new[] { "buscar" }));

            Assert.IsFalse(_tasks.TryActivate("entregar", out string reason));
            StringAssert.Contains("buscar", reason);

            _tasks.TryActivate("buscar", out _);
            Assert.IsTrue(_tasks.Cancel("buscar", "garrafas acabaram"));
            Assert.AreEqual(TaskState.Cancelled, _tasks.Get("entregar").State);
            Assert.IsFalse(_tasks.TryActivate("entregar", out _));
        }

        [Test]
        public void DependenciaConcluidaLiberaProxima()
        {
            _tasks.Register(new TaskDefinition("buscar", "Buscar garrafa", "",
                new ObjectiveDefinition[] { new PerformActionObjectiveDefinition("pegar", "pegar_garrafa") }));
            _tasks.Register(Water("entregar", pre: new[] { "buscar" }));
            _tasks.TryActivate("buscar", out _);
            _tasks.HandleSignal(GameplaySignal.Action("pegar_garrafa"));
            Assert.IsTrue(_tasks.TryActivate("entregar", out _));
        }

        [Test]
        public void RegraExternaBloqueiaAtivacao()
        {
            _tasks.Register(Water());
            _allowed = false;
            Assert.IsFalse(_tasks.TryActivate("agua", out _));
            Assert.AreEqual(TaskState.Available, _tasks.Get("agua").State);
        }

        [Test]
        public void IdDuplicadoERejeitado()
        {
            _tasks.Register(Water());
            Assert.Throws<System.InvalidOperationException>(() => _tasks.Register(Water()));
        }
    }

    public class ObjectiveTests
    {
        [Test]
        public void SequenciaExigeOrdem()
        {
            var def = new SequenceObjectiveDefinition("seq",
                new PerformActionObjectiveDefinition("a", "abrir"),
                new PlaceItemInZoneObjectiveDefinition("b", "mala", "bagageiro"),
                new PerformActionObjectiveDefinition("c", "fechar"));
            var o = def.CreateRuntime();

            o.Handle(GameplaySignal.Action("fechar"));
            Assert.AreEqual(0f, o.Progress);
            o.Handle(GameplaySignal.Action("abrir"));
            o.Handle(GameplaySignal.Action("fechar"));
            Assert.AreEqual(1f / 3f, o.Progress, 1e-5);
            o.Handle(GameplaySignal.PlacedInZone("mala", "bagageiro"));
            o.Handle(GameplaySignal.Action("fechar"));
            Assert.IsTrue(o.IsComplete);
        }

        [Test]
        public void ContagemDeDescarte()
        {
            var o = new DisposeItemsObjectiveDefinition("lixo", "lixo", 3).CreateRuntime();
            o.Handle(GameplaySignal.Disposed("lixo"));
            o.Handle(GameplaySignal.Disposed("agua"));
            o.Handle(GameplaySignal.Disposed("lixo"));
            Assert.AreEqual(2f / 3f, o.Progress, 1e-5);
            Assert.IsFalse(o.IsComplete);
            o.Handle(GameplaySignal.Disposed("lixo"));
            Assert.IsTrue(o.IsComplete);
            Assert.IsFalse(o.Handle(GameplaySignal.Disposed("lixo")), "nao passa de 100%");
        }

        [Test]
        public void ObjetivosDeDuasAtivacoesNaoCompartilhamEstado()
        {
            var def = new TalkToObjectiveDefinition("falar", "p1");
            var a = def.CreateRuntime();
            var b = def.CreateRuntime();
            a.Handle(GameplaySignal.Talked("p1"));
            Assert.IsTrue(a.IsComplete);
            Assert.IsFalse(b.IsComplete);
        }
    }

    public class ScoreTests
    {
        [Test]
        public void LedgerIdempotentePorChave()
        {
            var l = new ScoreLedger();
            Assert.IsTrue(l.TryApply("tarefa:a", 100, "a"));
            Assert.IsFalse(l.TryApply("tarefa:a", 100, "a"));
            Assert.IsTrue(l.TryApply("falha:b", -40, "b"));
            Assert.AreEqual(60, l.Total);
            Assert.AreEqual(100, l.Rewards);
            Assert.AreEqual(40, l.Penalties);
        }

        [Test]
        public void FormulaDocumentada()
        {
            var l = new ScoreLedger();
            l.TryApply("a", 550, "tarefas");
            l.TryApply("b", -100, "falha");
            var s = ScoreFormula.Compute(l, averageSatisfaction: 0.8f, secondsLeft: 60f, victory: true);
            Assert.AreEqual(400, s.SatisfactionBonus);
            Assert.AreEqual(120, s.TimeBonus);
            Assert.AreEqual(550 - 100 + 400 + 120, s.Total);
            Assert.AreEqual(2, s.Stars);
        }

        [Test]
        public void BonusDeTempoTemTetoESoValeNaVitoria()
        {
            var l = new ScoreLedger();
            Assert.AreEqual(ScoreFormula.MaxTimeBonus, ScoreFormula.Compute(l, 0f, 9999f, true).TimeBonus);
            var derrota = ScoreFormula.Compute(l, 1f, 100f, false);
            Assert.AreEqual(0, derrota.TimeBonus);
            Assert.AreEqual(0, derrota.Stars);
        }

        [Test]
        public void TotalNuncaNegativo()
        {
            var l = new ScoreLedger();
            l.TryApply("x", -5000, "desastre");
            Assert.AreEqual(0, ScoreFormula.Compute(l, 0f, 0f, false).Total);
        }
    }
}
