using System.Collections;
using System.Linq;
using NUnit.Framework;
using SkyCrew.Aircraft;
using SkyCrew.Core;
using SkyCrew.Domain.Content;
using SkyCrew.Domain.Match;
using SkyCrew.Domain.PassengerAI;
using SkyCrew.Domain.Tasks;
using SkyCrew.Interaction;
using SkyCrew.Navigation;
using SkyCrew.PassengerAI;
using SkyCrew.Physics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SkyCrew.Tests.PlayMode
{
    /// <summary>
    /// Sequencias reais de gameplay na cena montada pelo GameBootstrap.
    /// Rodam no Test Runner da Unity (Window > General > Test Runner > PlayMode).
    /// </summary>
    public class PrototypePlayModeTests
    {
        private GameBootstrap _game;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveManager.StorageOverride = new SkyCrew.Domain.Persistence.MemorySaveStorage();
            GameBootstrap.AutoStart = true; // sem menu principal nos testes
            _game = new GameObject("SkyCrew_Bootstrap_Teste").AddComponent<GameBootstrap>();
            yield return WaitFor(() => GameBootstrap.Session.State.Current == MatchState.Boarding, 10f);
            Assert.AreEqual(MatchState.Boarding, GameBootstrap.Session.State.Current, "a partida deveria chegar ao embarque");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Object.Destroy(root);
            }

            Time.timeScale = 1f;
            SaveManager.StorageOverride = null;
            GameBootstrap.AutoStart = false;
            yield return null;
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, float timeoutSeconds)
        {
            float t = 0f;
            while (!condition() && t < timeoutSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator CenarioTemNavegacaoEPassageiros()
        {
            Assert.AreEqual(PrototypeScenario.DefaultPassengers.Length, _game.Passengers.Count);
            Assert.IsTrue(CabinNavMesh.HasCompletePath(CabinLayout.BoardingDoor, CabinLayout.AislePointForRow(CabinLayout.Rows)),
                          "corredor da porta ate a ultima fileira");
            Assert.IsTrue(CabinNavMesh.HasCompletePath(CabinLayout.AislePointForRow(1), CabinLayout.LavatoryPoint),
                          "corredor ate o banheiro");
            Assert.IsFalse(CabinNavMesh.HasCompletePath(CabinLayout.AislePointForRow(3),
                                                        new Vector3(CabinLayout.SeatX(0), 0f, CabinLayout.RowZ(3) - 0.45f)),
                           "o vao entre fileiras nao deve ser navegavel");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PassageiroDoEmbarqueChegaNaPoltrona()
        {
            var lia = GameBootstrap.Session.GetPassenger("p_lia");
            yield return WaitFor(() => lia.State == PassengerStateId.Seated, 20f);
            Assert.AreEqual(PassengerStateId.Seated, lia.State, string.Join("\n", lia.History));
            var body = _game.Passengers.First(p => p.Brain == lia);
            CabinLayout.TryParseSeat("2F", out int row, out int letter);
            Assert.Less(Vector3.Distance(body.transform.position, CabinLayout.SeatPosition(row, letter)), 0.3f);
        }

        [UnityTest]
        public IEnumerator GuardarMalaNoBagageiroConcluiTarefa()
        {
            var interactor = _game.Player.Interactor;
            var suitcase = Object.FindObjectsByType<CarryableItem>(FindObjectsSortMode.None)
                                 .First(i => i.ItemType == ItemTypesUnity.Suitcase);
            Assert.IsTrue(interactor.Hands.PickUp(suitcase));
            yield return null;
            Assert.IsTrue(_game.Level.Bins[0].Execute(interactor));
            yield return null;
            var task = GameBootstrap.Session.Tasks.Get(PrototypeScenario.TaskStoreSuitcase);
            Assert.AreEqual(TaskState.Completed, task.State);
            Assert.IsTrue(suitcase.IsLocked);
            Assert.IsNull(interactor.Hands.Held);
        }

        [UnityTest]
        public IEnumerator DerramamentoLimpoComEsfregao()
        {
            var session = GameBootstrap.Session;
            session.Tick(session.Config.BoardingSeconds + 0.1f); // avanca o embarque
            Assert.AreEqual(MatchState.InFlight, session.State.Current);
            session.Events.CancelAll();
            Assert.IsTrue(session.Events.TryStart(PrototypeScenario.EventSpill,
                new SkyCrew.Domain.Events.EventContext(MatchState.InFlight, 100f), out string reason, ignoreGlobalInterval: true), reason);
            yield return null;

            var spill = Object.FindAnyObjectByType<SpillZone>();
            Assert.IsNotNull(spill, "o WorldDirector deveria criar a mancha");
            var interactor = _game.Player.Interactor;
            var mop = ItemFactory.Create(ItemTypesUnity.Mop, interactor.transform.position + Vector3.up, Quaternion.identity);
            Assert.IsTrue(interactor.Hands.PickUp(mop));
            for (int i = 0; i < PrototypeScenario.CleanSwipes; i++)
            {
                Assert.IsTrue(spill.Execute(interactor));
            }

            yield return null;
            Assert.IsFalse(session.Events.IsActive(PrototypeScenario.EventSpill), "limpar encerra o evento");
            Assert.IsTrue(spill == null, "a mancha some quando o evento termina");
            Assert.AreSame(mop, interactor.Hands.Held, "o esfregao continua na mao");
        }

        [UnityTest]
        public IEnumerator UniformeEAcessorioEquipados()
        {
            var save = _game.Save.Data;
            save.Unlocks.Add("uniforme_vermelho");
            save.Unlocks.Add("quepe_dourado");
            Assert.IsTrue(SkyCrew.Domain.Progression.ProgressionService.TryEquip(save, "uniforme_vermelho"));
            Assert.IsTrue(SkyCrew.Domain.Progression.ProgressionService.TryEquip(save, "quepe_dourado"));
            _game.ApplyOutfit();
            yield return null;
            var player = _game.Player.transform;
            var cap = SkyCrew.Characters.OutfitApplier.FindDeep(player, "Acessorio_quepe_dourado");
            Assert.IsNotNull(cap, "o quepe deveria estar no personagem");
            Assert.AreEqual(1, player.GetComponents<MonoBehaviour>().Count(m => m is SkyCrew.Characters.ICharacterAnimation),
                            "uma unica animacao apos trocar de roupa");
            Assert.Greater(cap.position.y, 1.3f, "o quepe fica na altura da cabeca");
        }

        [UnityTest]
        public IEnumerator ObjetoSoltoNaoAtravessaOPiso()
        {
            var water = ItemFactory.Create(ItemTypesUnity.Water, new Vector3(0f, 1.5f, 0f), Quaternion.identity);
            yield return new WaitForSeconds(2f);
            Assert.Greater(water.transform.position.y, -0.05f);
            Assert.Less(water.transform.position.y, 0.5f);
        }
    }
}
