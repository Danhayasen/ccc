using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SkyCrew.EditorTools
{
    /// <summary>
    /// Build para navegador (WebGL). Usado pelo GitHub Actions (.github/workflows/publicar-jogo.yml)
    /// em modo batch, e tambem pelo menu SkyCrew > 6. Faz sozinho o que os menus 1, 4 e 5 fazem,
    /// porque no servidor ninguem clica neles.
    /// </summary>
    public static class SkyCrewBuild
    {
        private const string DefaultOutput = "build/WebGL/SkyCrew";

        [MenuItem("SkyCrew/6. Build para navegador (WebGL)", priority = 6)]
        public static void BuildWebGLMenu() => BuildWebGL();

        /// <summary>Ponto de entrada do CI (-executeMethod SkyCrew.EditorTools.SkyCrewBuild.BuildWebGL).</summary>
        public static void BuildWebGL()
        {
            string output = Argument("-customBuildPath") ?? DefaultOutput;
            Debug.Log("[SkyCrew] Build WebGL em " + output);

            PrepareProject();
            ConfigureWebGL();

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (!scenes.Contains(SkyCrewMenu.ScenePath))
            {
                scenes = new[] { SkyCrewMenu.ScenePath }.Concat(scenes).ToArray();
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });

            var summary = report.summary;
            Debug.Log($"[SkyCrew] Resultado do build: {summary.result}, {summary.totalErrors} erro(s), " +
                      $"{summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0}s");
            if (summary.result != BuildResult.Succeeded)
            {
                throw new Exception("[SkyCrew] Build WebGL falhou: " + summary.result);
            }
        }

        /// <summary>Configuracao que os menus 1, 4 e 5 fariam no Editor.</summary>
        public static void PrepareProject()
        {
            // Novo sistema de input (o jogo usa so o Input System). 2 = os dois, mais tolerante.
            SetProjectSetting("activeInputHandler", 2);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "SkyCrew";
            PlayerSettings.productName = "SkyCrew";
            PlayerSettings.runInBackground = true;

            SkyCrewMenu.ConfigureProject();
            SkyCrewMenu.CreateDefaultData();
            SkyCrewAnimatorBuilder.Build();
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureWebGL()
        {
            // O GitHub Pages nao envia "Content-Encoding": o fallback descompacta no navegador.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
        }

        private static void SetProjectSetting(string property, int value)
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (settings == null)
            {
                Debug.LogWarning("[SkyCrew] ProjectSettings.asset nao encontrado; " + property + " nao alterado.");
                return;
            }

            var so = new SerializedObject(settings);
            var prop = so.FindProperty(property);
            if (prop == null)
            {
                Debug.LogWarning("[SkyCrew] Propriedade " + property + " nao existe nesta versao da Unity.");
                return;
            }

            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
