using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SkyCrew.Domain.Content;
using SkyCrew.Domain.Events;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Domain.Scoring;
using SkyCrew.Domain.Tasks;

namespace SkyCrew.Tests.EditMode
{
    public class ServiceRequestTests
    {
        private static void Run(PassengerBrain b, IPassengerWorld w, float seconds)
        {
            for (float t = 0; t < seconds; t += 0.05f)
            {
                b.Tick(0.05f, w);
            }
        }

        [TestCase(0.8f, 0f, 0f, ServiceKind.Water)]
        [TestCase(0f, 0.8f, 0f, ServiceKind.Snack)]
        [TestCase(0f, 0f, 0.85f, ServiceKind.Blanket)]
        [TestCase(0.75f, 0.95f, 0f, ServiceKind.Snack)] // fome passou mais do limite que a sede
        public void PedeOServicoMaisUrgente(float thirst, float hunger, float cold, ServiceKind expected)
        {
            var b = new PassengerBrain("p", PassengerProfiles.Get(Personality.Calmo));
            b.SitImmediately();
            b.Needs.DebugSet(thirst: thirst, hunger: hunger, cold: cold);
            ServiceKind asked = ServiceKind.None;
            b.ServiceRequested += (_, k) => asked = k;
            Run(b, new FakeWorld(), 0.5f);
            Assert.AreEqual(expected, asked);
            Assert.AreEqual(expected, b.PendingService);
        }

        [Test]
        public void SoAceitaOQuePediu()
        {
            var b = new PassengerBrain("p", PassengerProfiles.Get(Personality.Calmo));
            b.SitImmediately();
            b.Needs.DebugSet(cold: 0.95f);
            Run(b, new FakeWorld(), 0.5f);
            Assert.IsFalse(b.Serve(ServiceKind.Water));
            Assert.IsTrue(b.Serve(ServiceKind.Blanket));
            Assert.AreEqual(0f, b.Needs.Cold);
        }

        [Test]
        public void DesistirDoPedidoCustaPacienciaEPodePedirDeNovo()
        {
            var b = new PassengerBrain("p", PassengerProfiles.Get(Personality.Calmo));
            b.SitImmediately();
            b.Needs.DebugSet(thirst: 0.9f);
            int pedidos = 0;
            b.ServiceRequested += (_, k) => pedidos++;
            Run(b, new FakeWorld(), 0.5f);
            float paciencia = b.Needs.Patience;
            Assert.IsTrue(b.AbandonRequest());
            Assert.AreEqual(ServiceKind.None, b.PendingService);
            Assert.AreEqual(PassengerStateId.Seated, b.State);
            Assert.Less(b.Needs.Patience, paciencia);
            Run(b, new FakeWorld(), 0.5f);
            Assert.AreEqual(2, pedidos, "a sede continua: pede de novo");
        }

        [Test]
        public void LimiteDePedidosAbertosNaCabine()
        {
            var config = new MatchConfig { BoardingSeconds = 0.1f, FlightSeconds = 100f, MaxOpenRequests = 3 };
            var s = new MatchSession(config);
            for (int i = 0; i < 5; i++)
            {
                var b = new PassengerBrain("p" + i, PassengerProfiles.Get(Personality.Calmo));
                s.AddPassenger(b);
                b.SitImmediately();
                b.Needs.DebugSet(thirst: 0.95f);
            }

            s.TryBecomeReady(out _);
            s.StartBoarding(out _);
            for (int i = 0; i < 40; i++)
            {
                s.Tick(0.05f);
            }

            Assert.AreEqual(3, s.OpenRequests);
            s.TryDeliverToPassenger(s.Passengers.First(p => p.PendingService != ServiceKind.None).Id, ItemTypes.Water);
            for (int i = 0; i < 20; i++)
            {
                s.Tick(0.05f);
            }

            Assert.AreEqual(3, s.OpenRequests, "atendido um, outro pode pedir");
        }
    }

    public class PersonalityEffectTests
    {
        private static float FearAfter(bool withHelper)
        {
            var s = new MatchSession(new MatchConfig { BoardingSeconds = 0.1f, FlightSeconds = 100f });
            var scared = new PassengerBrain("medroso", PassengerProfiles.Get(Personality.Ansioso));
            s.AddPassenger(scared);
            scared.SitImmediately();
            if (withHelper)
            {
                var helper = new PassengerBrain("ajudante", PassengerProfiles.Get(Personality.Prestativo));
                s.AddPassenger(helper);
                helper.SitImmediately();
            }

            s.TryBecomeReady(out _);
            s.StartBoarding(out _);
            s.Tick(0.2f);
            scared.Needs.DebugSet(fear: 0.9f);
            for (int i = 0; i < 100; i++)
            {
                s.Tick(0.05f);
            }

            return scared.Needs.Fear;
        }

        [Test]
        public void PrestativoAcalmaOsVizinhos()
        {
            float sem = FearAfter(false);
            float com = FearAfter(true);
            Assert.Less(com, sem - 0.1f, $"sem ajudante {sem:0.00}, com ajudante {com:0.00}");
        }

        [Test]
        public void DesastradoDerrubaMaisCoisas()
        {
            var vezes = new Dictionary<string, int>();
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var s = new MatchSession(PrototypeScenario.CreateConfig(seed));
                var scenario = PrototypeScenario.Install(s);
                string quem = null;
                scenario.WorldRequested += r => { if (r.Kind == WorldRequestKind.Trash) quem = r.PassengerId; };
                s.TryBecomeReady(out _);
                s.StartBoarding(out _);
                s.Tick(s.Config.BoardingSeconds + 0.1f);
                Assert.IsTrue(s.Events.TryStart(PrototypeScenario.EventDroppedItem,
                    new EventContext(MatchState.InFlight, 100f), out string reason, ignoreGlobalInterval: true), reason);
                vezes[quem] = vezes.TryGetValue(quem, out int n) ? n + 1 : 1;
            }

            string campeao = vezes.OrderByDescending(kv => kv.Value).First().Key;
            Assert.AreEqual("p_lia", campeao, string.Join(", ", vezes.Select(kv => $"{kv.Key}={kv.Value}")));
        }
    }

    public class QuickResponseAndEventEndTests
    {
        [TestCase(4f, 250)]
        [TestCase(6f, 200)]
        public void BonusDeRespostaRapida(float elapsed, int expectedTotal)
        {
            var ledger = new ScoreLedger();
            var tasks = new TaskManager(ledger);
            tasks.Register(new TaskDefinition("t", "T", "",
                new ObjectiveDefinition[] { new TalkToObjectiveDefinition("x", "p") }, timeLimitSeconds: 10f, reward: 200));
            tasks.TryActivate("t", out _);
            tasks.Tick(elapsed);
            tasks.HandleSignal(GameplaySignal.Talked("p"));
            Assert.AreEqual(expectedTotal, ledger.Total);
        }

        [Test]
        public void EncerrarEventoAntesDoTempo()
        {
            var s = new EventScheduler(new SkyCrew.Domain.Common.DeterministicRandom(1));
            s.Register(new EventDefinition("e", "E", 30f));
            bool? cancelled = null;
            s.Ended += (e, c) => cancelled = c;
            Assert.IsTrue(s.TryStart("e", new EventContext(MatchState.InFlight, 0f), out _));
            Assert.IsTrue(s.TryEnd("e"));
            Assert.AreEqual(false, cancelled);
            Assert.AreEqual(0, s.Active.Count);
            Assert.IsFalse(s.TryEnd("e"));
        }

        [Test]
        public void DescarteSoContaItensDaTarefa()
        {
            var o = new DisposeItemsObjectiveDefinition("lixo", ItemTypes.Trash, 2, new[] { "a", "b" }).CreateRuntime();
            o.Handle(GameplaySignal.Disposed(ItemTypes.Trash, itemId: "x"));
            o.Handle(GameplaySignal.Disposed(ItemTypes.Trash));
            Assert.AreEqual(0f, o.Progress);
            o.Handle(GameplaySignal.Disposed(ItemTypes.Trash, itemId: "a"));
            o.Handle(GameplaySignal.Disposed(ItemTypes.Trash, itemId: "b"));
            Assert.IsTrue(o.IsComplete);
        }
    }

    /// <summary>Cada evento da Fase 2 forcado numa viagem do prototipo.</summary>
    public class ScenarioEventTests
    {
        private MatchSession _s;
        private PrototypeScenario _scenario;
        private List<WorldRequest> _requested;
        private List<WorldRequest> _cleared;

        [SetUp]
        public void SetUp()
        {
            _s = new MatchSession(PrototypeScenario.CreateConfig(seed: 77));
            _scenario = PrototypeScenario.Install(_s);
            _requested = new List<WorldRequest>();
            _cleared = new List<WorldRequest>();
            _scenario.WorldRequested += _requested.Add;
            _scenario.WorldCleared += _cleared.Add;
            _s.TryBecomeReady(out _);
            _s.StartBoarding(out _);
            _s.Signal(GameplaySignal.PlacedInZone(ItemTypes.Suitcase, PrototypeScenario.ZoneOverheadBin,
                                                  itemId: PrototypeScenario.ForgottenSuitcaseId));
            _s.Tick(_s.Config.BoardingSeconds + 0.1f);
            Assert.AreEqual(MatchState.InFlight, _s.State.Current);
            _s.Tick(1f);
        }

        private void Force(string id)
        {
            _s.Events.CancelAll();
            Assert.IsTrue(_s.Events.TryStart(id, new EventContext(MatchState.InFlight, 100f), out string reason,
                                             ignoreGlobalInterval: true), reason);
        }

        private TaskRuntime LastTask(string prefix) => _s.Tasks.All.Last(t => t.Id.StartsWith(prefix));

        [Test]
        public void DerramamentoLimpoComTresEsfregadas()
        {
            Force(PrototypeScenario.EventSpill);
            var req = _requested.Single(r => r.Kind == WorldRequestKind.Spill);
            var task = _s.Tasks.Get(req.TaskId);
            Assert.AreEqual(TaskState.Active, task.State);
            _s.Signal(GameplaySignal.Action(PrototypeScenario.ActionClean, "outra_mancha"));
            Assert.AreEqual(0f, task.Progress);
            for (int i = 0; i < PrototypeScenario.CleanSwipes; i++)
            {
                _s.Signal(GameplaySignal.Action(PrototypeScenario.ActionClean, req.Id));
            }

            Assert.AreEqual(TaskState.Completed, task.State);
            Assert.IsFalse(_s.Events.IsActive(PrototypeScenario.EventSpill), "resolver encerra o evento");
            Assert.AreEqual(req.Id, _cleared.Single().Id);
        }

        [Test]
        public void DerramamentoIgnoradoFalhaELimpaOMundo()
        {
            Force(PrototypeScenario.EventSpill);
            var req = _requested.Single(r => r.Kind == WorldRequestKind.Spill);
            for (int i = 0; i < 940; i++)
            {
                _s.Events.Tick(0.05f, new EventContext(MatchState.InFlight, 100f + i * 0.05f));
                _s.Tasks.Tick(0.05f);
            }

            Assert.AreEqual(TaskState.Failed, _s.Tasks.Get(req.TaskId).State);
            Assert.AreEqual(1, _cleared.Count);
        }

        [Test]
        public void CorredorBloqueadoResolvidoNaCozinha()
        {
            Force(PrototypeScenario.EventBlockedAisle);
            var req = _requested.Single(r => r.Kind == WorldRequestKind.Cart);
            _s.Signal(GameplaySignal.PlacedInZone(ItemTypes.Cart, PrototypeScenario.ZoneGalley, itemId: req.Id));
            Assert.AreEqual(TaskState.Completed, _s.Tasks.Get(req.TaskId).State);
            Assert.AreEqual(1, _cleared.Count);
        }

        [Test]
        public void GeladeiraQuebradaEConserto()
        {
            Force(PrototypeScenario.EventBrokenEquipment);
            Assert.IsFalse(_s.IsEquipmentAvailable(PrototypeScenario.EquipmentFridge));
            var task = LastTask("consertar_geladeira");
            for (int i = 0; i < PrototypeScenario.RepairHits; i++)
            {
                Assert.IsTrue(_s.IsEquipmentAvailable(PrototypeScenario.EquipmentFridge) == false);
                _s.Signal(GameplaySignal.Action(PrototypeScenario.ActionRepair, PrototypeScenario.EquipmentFridge));
            }

            Assert.AreEqual(TaskState.Completed, task.State);
            Assert.IsTrue(_s.IsEquipmentAvailable(PrototypeScenario.EquipmentFridge));
        }

        [Test]
        public void MalaCaidaPrecisaSerAMesma()
        {
            Force(PrototypeScenario.EventFallenBag);
            var req = _requested.Single(r => r.Kind == WorldRequestKind.FallenBag);
            var task = _s.Tasks.Get(req.TaskId);
            _s.Signal(GameplaySignal.PlacedInZone(ItemTypes.Suitcase, PrototypeScenario.ZoneOverheadBin, itemId: "outra"));
            Assert.AreEqual(TaskState.Active, task.State);
            _s.Signal(GameplaySignal.PlacedInZone(ItemTypes.Suitcase, PrototypeScenario.ZoneOverheadBin, itemId: req.ItemIds[0]));
            Assert.AreEqual(TaskState.Completed, task.State);
        }

        [Test]
        public void PassageiroAssustadoPrecisaDeConversa()
        {
            Force(PrototypeScenario.EventScaredPassenger);
            var task = _s.Tasks.All.Last(t => t.Id.StartsWith(PrototypeScenario.TaskCalmPassenger));
            string alvo = ((TalkToObjectiveDefinition)task.Definition.Objectives[0]).TargetId;
            var p = _s.GetPassenger(alvo);
            Assert.GreaterOrEqual(p.Needs.Fear, 0.55f);
            _s.Tick(0.3f);
            Assert.AreEqual(PassengerStateId.ReactingToEvent, p.State);
            Assert.IsTrue(_s.TryTalkToPassenger(alvo));
            Assert.AreEqual(TaskState.Completed, task.State);
        }

        [Test]
        public void ObjetoDerrubadoGeraLixoComIds()
        {
            Force(PrototypeScenario.EventDroppedItem);
            var req = _requested.Single(r => r.Kind == WorldRequestKind.Trash);
            Assert.IsNotNull(req.PassengerId);
            Assert.AreEqual(1, req.ItemIds.Count);
            _s.Signal(GameplaySignal.Disposed(ItemTypes.Trash, itemId: req.ItemIds[0]));
            Assert.AreEqual(TaskState.Completed, _s.Tasks.Get(req.TaskId).State);
        }
    }

    /// <summary>
    /// Resistencia do loop principal: dezenas de viagens completas com sementes diferentes e um
    /// "robo" que resolve as tarefas com atraso. Nada pode travar, repetir resultado ou explodir.
    /// </summary>
    public class LoopSoakTests
    {
        private sealed class Bot
        {
            private readonly MatchSession _s;
            private readonly float _reaction;

            public Bot(MatchSession s, float reactionSeconds)
            {
                _s = s;
                _reaction = reactionSeconds;
            }

            public void Act()
            {
                foreach (var t in _s.Tasks.All.ToList())
                {
                    if (t.State != TaskState.Active || t.Elapsed < _reaction)
                    {
                        continue;
                    }

                    foreach (var o in t.Definition.Objectives)
                    {
                        switch (o)
                        {
                            case DeliverItemObjectiveDefinition d:
                                _s.TryDeliverToPassenger(d.TargetId, d.ItemType);
                                break;
                            case TalkToObjectiveDefinition tk:
                                _s.TryTalkToPassenger(tk.TargetId);
                                break;
                            case PlaceItemInZoneObjectiveDefinition z:
                                _s.Signal(GameplaySignal.PlacedInZone(z.ItemType, z.ZoneId, itemId: z.ItemId));
                                break;
                            case DisposeItemsObjectiveDefinition di:
                                foreach (string id in di.ItemIds)
                                {
                                    _s.Signal(GameplaySignal.Disposed(di.ItemType, itemId: id));
                                }

                                break;
                            case PerformActionObjectiveDefinition a:
                                for (int i = 0; i < a.Count; i++)
                                {
                                    _s.Signal(GameplaySignal.Action(a.ActionId, a.TargetId));
                                }

                                break;
                        }
                    }
                }
            }
        }

        [Test]
        public void QuarentaViagensSemTravar()
        {
            int victories = 0;
            var eventKinds = new HashSet<string>();
            int completedTotal = 0;
            var totals = new List<int>();
            var stars = new int[4];
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var s = new MatchSession(PrototypeScenario.CreateConfig(seed));
                var scenario = PrototypeScenario.Install(s);
                int results = 0;
                s.ResultsReady += _ => results++;
                s.Events.Started += e => eventKinds.Add(e.Definition.Id);
                var bot = new Bot(s, reactionSeconds: 3f + seed % 5);

                s.TryBecomeReady(out _);
                s.StartBoarding(out _);
                float t = 0f;
                while (s.State.IsPlayable && t < 400f)
                {
                    s.Tick(0.05f);
                    t += 0.05f;
                    foreach (var p in s.Passengers)
                    {
                        while (p.TryDequeueCommand(out var c))
                        {
                            if (c.Kind == CommandKind.WalkTo)
                            {
                                p.OnArrived();
                            }
                        }
                    }

                    bot.Act();
                }

                Assert.IsFalse(s.State.IsPlayable, $"semente {seed}: a viagem nao terminou");
                Assert.AreEqual(1, results, $"semente {seed}: resultado gerado {results} vezes");
                foreach (var p in s.Passengers)
                {
                    Assert.Less(p.TransitionCount, 300, $"semente {seed}, {p.Id}: possivel ciclo\n" + string.Join("\n", p.History));
                }

                if (s.Results.Victory) victories++;
                completedTotal += s.Results.TasksCompleted;
                totals.Add(s.Results.Score.Total);
                stars[s.Results.Score.Stars]++;
            }

            TestContext.WriteLine($"vitorias {victories}/40, tarefas concluidas {completedTotal}, " +
                                  $"pontos min/media/max {totals.Min()}/{totals.Average():0}/{totals.Max()}, " +
                                  $"estrelas 0..3 = {string.Join("/", stars)}, eventos: {string.Join(", ", eventKinds)}");
            Assert.GreaterOrEqual(victories, 38, "um robo competente deveria vencer quase sempre");
            Assert.GreaterOrEqual(stars[3], 35, "reagindo em 3-7 s deveria tirar 3 estrelas");
            Assert.GreaterOrEqual(eventKinds.Count, 6, "a variedade de eventos deve aparecer: " + string.Join(", ", eventKinds));
        }

        /// <summary>Mede pontos por tempo de reacao (usado para calibrar as estrelas; ver ARCHITECTURE.md).</summary>
        [TestCase(15f)]
        [TestCase(25f)]
        [TestCase(35f)]
        public void CalibragemDePontos(float reaction)
        {
            var totals = new List<int>();
            int wins = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var s = new MatchSession(PrototypeScenario.CreateConfig(seed));
                PrototypeScenario.Install(s);
                var bot = new Bot(s, reaction);
                s.TryBecomeReady(out _);
                s.StartBoarding(out _);
                float t = 0f;
                while (s.State.IsPlayable && t < 400f)
                {
                    s.Tick(0.05f);
                    t += 0.05f;
                    foreach (var p in s.Passengers)
                    {
                        while (p.TryDequeueCommand(out var c))
                        {
                            if (c.Kind == CommandKind.WalkTo) p.OnArrived();
                        }
                    }

                    bot.Act();
                }

                if (s.Results.Victory) wins++;
                totals.Add(s.Results.Score.Total);
            }

            TestContext.WriteLine($"reacao {reaction}s: vitorias {wins}/20, pontos min/media/max {totals.Min()}/{totals.Average():0}/{totals.Max()}");
            Assert.Pass();
        }

        [Test]
        public void RoboLentoPerde()
        {
            var s = new MatchSession(PrototypeScenario.CreateConfig(5));
            PrototypeScenario.Install(s);
            var bot = new Bot(s, reactionSeconds: 90f); // mais lento que qualquer prazo
            s.TryBecomeReady(out _);
            s.StartBoarding(out _);
            float t = 0f;
            while (s.State.IsPlayable && t < 400f)
            {
                s.Tick(0.05f);
                t += 0.05f;
                bot.Act();
            }

            Assert.AreEqual(MatchState.Failed, s.State.Current);
        }
    }
}
