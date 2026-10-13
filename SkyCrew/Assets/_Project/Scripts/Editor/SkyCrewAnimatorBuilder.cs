using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCrew.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SkyCrew.EditorTools
{
    /// <summary>
    /// Gera o Animator Controller dos personagens a partir das animacoes do Blender
    /// (Art/Animations/SkyCrew_Animacoes.fbx). Menu: SkyCrew > 5. Gerar Animator dos personagens.
    /// </summary>
    public static class SkyCrewAnimatorBuilder
    {
        public const string AnimationFbx = "Assets/_Project/Art/Animations/SkyCrew_Animacoes.fbx";
        public const string ControllerPath = "Assets/_Project/Art/Animations/Resources/SkyCrew/Animacao/Personagem.controller";

        private static readonly HashSet<string> Looping = new HashSet<string>
        {
            "Parado", "Andar", "Correr", "Carregar", "CarregarParado", "Sentado", "Panico",
        };

        /// <summary>Nome limpo do clipe ("Armature|Armature|Andar" -> "Andar").</summary>
        public static string CleanName(string clipName)
        {
            int bar = clipName.LastIndexOf('|');
            return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
        }

        [MenuItem("SkyCrew/5. Gerar Animator dos personagens", priority = 5)]
        public static void Build()
        {
            var importer = AssetImporter.GetAtPath(AnimationFbx) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[SkyCrew] {AnimationFbx} nao encontrado. Rode Tools/Blender/gerar_personagens.py.");
                return;
            }

            // 1) Nomes limpos e loops nos clipes importados
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = CleanName(c.takeName);
                c.loopTime = Looping.Contains(c.name);
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            var byName = AssetDatabase.LoadAllAssetsAtPath(AnimationFbx)
                                      .OfType<AnimationClip>()
                                      .Where(c => !c.name.StartsWith("__preview__"))
                                      .ToDictionary(c => CleanName(c.name), c => c);
            string[] required = { "Parado", "Andar", "Correr", "Carregar", "CarregarParado", "Sentado", "Panico", "Escorregar", "Falar" };
            var missing = required.Where(r => !byName.ContainsKey(r)).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogError("[SkyCrew] Clipes faltando: " + string.Join(", ", missing));
                return;
            }

            // 2) Controller
            Directory.CreateDirectory(Path.GetDirectoryName(ControllerPath));
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Seated", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Carrying", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Reacting", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Talk", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Slip", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;
            var locomotion = controller.CreateBlendTreeInController("Locomocao", out var walkTree, 0);
            walkTree.blendParameter = "Speed";
            walkTree.useAutomaticThresholds = false;
            walkTree.AddChild(byName["Parado"], 0f);
            walkTree.AddChild(byName["Andar"], 1.4f);
            walkTree.AddChild(byName["Correr"], 4.0f);

            var carry = controller.CreateBlendTreeInController("Carregando", out var carryTree, 0);
            carryTree.blendParameter = "Speed";
            carryTree.useAutomaticThresholds = false;
            carryTree.AddChild(byName["CarregarParado"], 0f);
            carryTree.AddChild(byName["Carregar"], 1.4f);

            var seated = sm.AddState("Sentado");
            seated.motion = byName["Sentado"];
            var panic = sm.AddState("Panico");
            panic.motion = byName["Panico"];
            var slip = sm.AddState("Escorregar");
            slip.motion = byName["Escorregar"];
            var talk = sm.AddState("Falar");
            talk.motion = byName["Falar"];
            sm.defaultState = locomotion;

            // Panico tem prioridade; sai quando para de reagir
            var toPanic = Transition(sm.AddAnyStateTransition(panic), 0.15f);
            toPanic.AddCondition(AnimatorConditionMode.If, 0, "Reacting");
            toPanic.canTransitionToSelf = false; // senao reiniciaria o panico a cada quadro
            var panicExit = Transition(panic.AddTransition(locomotion), 0.25f);
            panicExit.AddCondition(AnimatorConditionMode.IfNot, 0, "Reacting");
            panicExit.AddCondition(AnimatorConditionMode.IfNot, 0, "Seated");
            var panicToSeat = Transition(panic.AddTransition(seated), 0.25f);
            panicToSeat.AddCondition(AnimatorConditionMode.IfNot, 0, "Reacting");
            panicToSeat.AddCondition(AnimatorConditionMode.If, 0, "Seated");

            // Sentar / levantar
            var sit = Transition(locomotion.AddTransition(seated), 0.3f);
            sit.AddCondition(AnimatorConditionMode.If, 0, "Seated");
            Transition(seated.AddTransition(locomotion), 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0, "Seated");

            // Carregar
            Transition(locomotion.AddTransition(carry), 0.15f).AddCondition(AnimatorConditionMode.If, 0, "Carrying");
            Transition(carry.AddTransition(locomotion), 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "Carrying");

            // Uma vez: escorregar e falar voltam para a locomocao pelo tempo de saida
            var toSlip = Transition(sm.AddAnyStateTransition(slip), 0.05f);
            toSlip.AddCondition(AnimatorConditionMode.If, 0, "Slip");
            toSlip.canTransitionToSelf = false;
            var slipEnd = Transition(slip.AddTransition(locomotion), 0.2f);
            slipEnd.hasExitTime = true;
            slipEnd.exitTime = 0.9f;

            Transition(locomotion.AddTransition(talk), 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Talk");
            Transition(seated.AddTransition(talk), 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Talk");
            var talkEnd = Transition(talk.AddTransition(locomotion), 0.2f);
            talkEnd.hasExitTime = true;
            talkEnd.exitTime = 0.9f;

            AssetDatabase.SaveAssets();
            Debug.Log($"[SkyCrew] Animator gerado em {ControllerPath} com {byName.Count} clipes. " +
                      $"Carregado em tempo de execucao por Resources.Load(\"{CharacterAnimation.ControllerResource}\").");
        }

        private static AnimatorStateTransition Transition(AnimatorStateTransition t, float duration)
        {
            t.hasExitTime = false;
            t.duration = duration;
            return t;
        }
    }
}
