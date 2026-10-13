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
        public IEnumerator ObjetoSoltoNaoAtravessaOPiso()
        {
            var water = ItemFactory.Create(ItemTypesUnity.Water, new Vector3(0f, 1.5f, 0f), Quaternion.identity);
            yield return new WaitForSeconds(2f);
            Assert.Greater(water.transform.position.y, -0.05f);
            Assert.Less(water.transform.position.y, 0.5f);
        }
    }
}
