using System.IO;
using System.Linq;
using System.Text;
using SkyCrew.Core;
using SkyCrew.Data;
using SkyCrew.Domain.PassengerAI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if SKYCREW_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace SkyCrew.EditorTools
{
    /// <summary>Ferramentas do menu "SkyCrew" no Editor.</summary>
    public static class SkyCrewMenu
    {
        public const string ScenePath = "Assets/_Project/Scenes/Aircraft/Prototype.unity";
        private const string SettingsFolder = "Assets/_Project/Settings";
        private const string DataFolder = "Assets/_Project/Data";

        [MenuItem("SkyCrew/1. Configurar projeto (URP)", priority = 1)]
        public static void ConfigureProject()
        {
#if SKYCREW_URP
            Directory.CreateDirectory(SettingsFolder);
            string rendererPath = SettingsFolder + "/SkyCrew_Renderer.asset";
            string assetPath = SettingsFolder + "/SkyCrew_URP.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (pipeline == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, assetPath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            Debug.Log($"[SkyCrew] URP configurado: {assetPath}");
#else
            Debug.LogError("[SkyCrew] Pacote com.unity.render-pipelines.universal nao encontrado (veja Packages/manifest.json).");
#endif
            Debug.Log("[SkyCrew] Confira em Project Settings > Player > Active Input Handling = 'Input System Package (New)'.");
        }

        [MenuItem("SkyCrew/2. Recriar cena do protótipo", priority = 2)]
        public static void CreatePrototypeScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("SkyCrew_Bootstrap");
            go.AddComponent<GameBootstrap>();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[SkyCrew] Cena criada em {ScenePath} e adicionada ao Build.");
        }

        [MenuItem("SkyCrew/3. Validar recursos 3D", priority = 3)]
        public static void ValidateModels()
        {
            string[] paths =
            {
                "Cabine/Segmento", "Cabine/Parede_Frente", "Cabine/Parede_Fundo", "Cabine/Poltrona", "Cabine/Bagageiro",
                "Cabine/Cozinha", "Cabine/Banheiro", "Props/Mala", "Props/Garrafa", "Props/Lixo", "Props/Lixeira",
                "Props/Lanche", "Props/Cobertor", "Props/Esfregao", "Props/Carrinho", "Props/Mancha",
                "Personagens/Comissaria", "Personagens/Passageiro_1", "Personagens/Passageiro_2",
                "Personagens/Passageiro_3", "Personagens/Passageiro_4",
            };

            var report = new StringBuilder("[SkyCrew] Relatorio de recursos 3D\n");
            int problems = 0;
            foreach (string p in paths)
            {
                var prefab = Resources.Load<GameObject>(ModelLibrary.Root + p);
                if (prefab == null)
                {
                    report.AppendLine($"  FALTANDO  {p}");
                    problems++;
                    continue;
                }

                int tris = 0, renderers = 0, missingMaterials = 0;
                var bounds = new Bounds();
                bool first = true;
                var meshes = prefab.GetComponentsInChildren<MeshFilter>(true).Select(f => (f.sharedMesh, f.transform))
                    .Concat(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => (r.sharedMesh, r.transform)));
                foreach (var (mesh, owner) in meshes)
                {
                    if (mesh == null)
                    {
                        continue;
                    }

                    tris += mesh.triangles.Length / 3;
                    // limites no espaco da raiz do prefab (inclui a rotacao de eixo do FBX)
                    var m = prefab.transform.worldToLocalMatrix * owner.localToWorldMatrix;
                    var b = mesh.bounds;
                    var c = m.MultiplyPoint3x4(b.center);
                    var e = b.extents;
                    var ext = new Vector3(
                        Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                        Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                        Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
                    b = new Bounds(c, ext * 2f);
                    if (first) { bounds = b; first = false; } else { bounds.Encapsulate(b); }
                }

                bool skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
                if (p.StartsWith("Personagens/") && (!skinned || prefab.GetComponentInChildren<Animator>() == null))
                {
                    report.AppendLine($"  ATENCAO  {p}: personagem sem esqueleto/Animator (rode gerar_personagens.py e reimporte)");
                    problems++;
                }

                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    renderers++;
                    missingMaterials += r.sharedMaterials.Count(m => m == null);
                }

                bool scaleOk = bounds.size.y > 0.05f && bounds.size.y < 3.5f;
                if (!scaleOk || missingMaterials > 0) problems++;
                report.AppendLine($"  {(scaleOk && missingMaterials == 0 ? "OK" : "ATENCAO")}  {p}: {tris} tri, {renderers} renderers, " +
                                  $"tamanho {bounds.size.x:0.00}x{bounds.size.y:0.00}x{bounds.size.z:0.00} m, base y={bounds.min.y:0.00}, " +
                                  $"materiais faltando={missingMaterials}");
            }

            int clips = AssetDatabase.LoadAllAssetsAtPath(SkyCrewAnimatorBuilder.AnimationFbx)
                                     .OfType<AnimationClip>().Count(c => !c.name.StartsWith("__preview__"));
            report.AppendLine($"  {(clips >= 9 ? "OK" : "ATENCAO")}  Animacoes: {clips} clipes em {SkyCrewAnimatorBuilder.AnimationFbx}");
            if (clips < 9) problems++;
            bool controller = Resources.Load<RuntimeAnimatorController>(SkyCrew.Characters.CharacterAnimation.ControllerResource) != null;
            report.AppendLine($"  {(controller ? "OK" : "ATENCAO")}  Animator Controller {(controller ? "gerado" : "ausente: menu SkyCrew > 5")}");
            if (!controller) problems++;
            report.AppendLine(problems == 0 ? "Tudo certo." : $"{problems} problema(s).");
            if (problems == 0) Debug.Log(report.ToString()); else Debug.LogWarning(report.ToString());
        }

        [MenuItem("SkyCrew/4. Criar dados padrão (ScriptableObjects)", priority = 4)]
        public static void CreateDefaultData()
        {
            Directory.CreateDirectory(DataFolder + "/Passengers");
            Directory.CreateDirectory(DataFolder + "/Aircraft");
            foreach (Personality personality in System.Enum.GetValues(typeof(Personality)))
            {
                string path = $"{DataFolder}/Passengers/Perfil_{personality}.asset";
                if (AssetDatabase.LoadAssetAtPath<PassengerProfileAsset>(path) != null)
                {
                    continue;
                }

                var asset = ScriptableObject.CreateInstance<PassengerProfileAsset>();
                asset.CopyFrom(PassengerProfiles.Get(personality));
                AssetDatabase.CreateAsset(asset, path);
            }

            string configPath = DataFolder + "/Aircraft/Configuracao_Prototipo.asset";
            if (AssetDatabase.LoadAssetAtPath<MatchConfigAsset>(configPath) == null)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<MatchConfigAsset>(), configPath);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[SkyCrew] Dados padrao criados em " + DataFolder);
        }
    }

    /// <summary>Configuracao automatica de importacao dos FBX e da textura de paleta do projeto.</summary>
    public sealed class SkyCrewAssetImport : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.Contains("/_Project/Art/"))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.importCameras = false;
            importer.importLights = false;
            bool isAnimationLibrary = assetPath.EndsWith(SkyCrewAnimatorBuilder.AnimationFbx.Substring("Assets".Length));
            bool isCharacter = assetPath.Contains("/Art/Characters/");
            // Esqueleto generico: as animacoes de SkyCrew_Animacoes.fbx tocam em todos os personagens
            // porque os ossos e caminhos sao identicos (mesmo script de geracao).
            importer.animationType = isAnimationLibrary || isCharacter
                ? ModelImporterAnimationType.Generic
                : ModelImporterAnimationType.None;
            importer.importAnimation = isAnimationLibrary;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.useFileScale = true;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            // O FBX guarda so o nome da textura; a busca sobe ate Art/Textures/SkyCrew_Paleta.png.
            importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.EndsWith("SkyCrew_Paleta.png"))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.mipmapEnabled = false; // evita "vazar" cor entre amostras da paleta
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
        }
    }
}
