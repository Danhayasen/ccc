using SkyCrew.Core;
using SkyCrew.Domain.Progression;
using UnityEngine;

namespace SkyCrew.UI
{
    /// <summary>
    /// Telas PROVISORIAS (IMGUI) da Parte 14: menu principal, configuracoes e guarda-roupa
    /// (uniformes e acessorios desbloqueados). Lobby, selecao de cenario e carregamento ficam
    /// para quando houver mais de um aviao e multiplayer. Trocadas por UI Toolkit na Fase 6.
    /// </summary>
    public sealed class MenuScreens : MonoBehaviour
    {
        private enum Screen
        {
            None,
            Settings,
            Wardrobe,
        }

        private GameBootstrap _game;
        private GameHud _hud;
        private Screen _screen = Screen.None;
        private Vector2 _scroll;

        public bool IsSubScreenOpen => _screen != Screen.None;

        public void Bind(GameBootstrap game, GameHud hud)
        {
            _game = game;
            _hud = hud;
        }

        public void OpenSettings() => _screen = Screen.Settings;

        private void OnGUI()
        {
            if (_game == null || GameBootstrap.Session == null)
            {
                return;
            }

            _hud.EnsureStylesPublic();
            float s = _hud.Scale;
            bool inMainMenu = _game.MenuOpen;
            if (!inMainMenu && _screen == Screen.None)
            {
                return;
            }

            _hud.FillRect(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), new Color(0.03f, 0.07f, 0.16f, 0.82f));
            float w = 620f * s;
            GUILayout.BeginArea(new Rect((UnityEngine.Screen.width - w) / 2f, UnityEngine.Screen.height * 0.08f, w,
                                         UnityEngine.Screen.height * 0.86f));
            switch (_screen)
            {
                case Screen.Settings:
                    DrawSettings(s);
                    break;
                case Screen.Wardrobe:
                    DrawWardrobe(s);
                    break;
                default:
                    DrawMain(s);
                    break;
            }

            GUILayout.EndArea();
        }

        private void DrawMain(float s)
        {
            var stats = _game.Save.Data.Stats;
            GUILayout.Label("SkyCrew", new GUIStyle(_hud.TitleStyle) { fontSize = Mathf.RoundToInt(64 * s) });
            GUILayout.Label("Juntos, mantemos todos a bordo!", new GUIStyle(_hud.TextStyle) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10f * s);
            GUILayout.Label($"★ {stats.TotalStars} estrelas   ·   {stats.FlightsWon}/{stats.FlightsPlayed} voos   ·   recorde {stats.BestScore}",
                            new GUIStyle(_hud.SmallStyle) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(24f * s);
            if (GUILayout.Button("Jogar (Avião SkyCrew 1)", _hud.ButtonStyle)) _game.StartFlight();
            if (GUILayout.Button("Uniformes e acessórios", _hud.ButtonStyle)) _screen = Screen.Wardrobe;
            if (GUILayout.Button("Configurações", _hud.ButtonStyle)) _screen = Screen.Settings;
            if (GUILayout.Button("Sair do jogo", _hud.ButtonStyle)) _game.QuitGame();
            GUILayout.Space(16f * s);
            GUILayout.Label("Modo cooperativo online: em desenvolvimento (Fase 5).", _hud.SmallStyle);
        }

        private float Slider(string label, float value, float min = 0f, float max = 1f, string format = "P0")
        {
            GUILayout.Label($"{label}: {value.ToString(format)}", _hud.TextStyle);
            return GUILayout.HorizontalSlider(value, min, max);
        }

        private void DrawSettings(float s)
        {
            var settings = _game.Save.Data.Settings;
            GUILayout.Label("Configurações", _hud.TitleStyle);
            GUILayout.Space(10f * s);
            settings.MasterVolume = Slider("Volume geral", settings.MasterVolume);
            settings.MusicVolume = Slider("Música", settings.MusicVolume);
            settings.AmbienceVolume = Slider("Ambiente (motor, turbulência)", settings.AmbienceVolume);
            settings.SfxVolume = Slider("Efeitos e interface", settings.SfxVolume);
            settings.VoiceVolume = Slider("Vozes", settings.VoiceVolume);
            settings.MouseSensitivity = Slider("Sensibilidade do mouse", settings.MouseSensitivity, 0.2f, 3f, "0.00");
            settings.InvertY = GUILayout.Toggle(settings.InvertY, " Inverter eixo vertical da câmera", _hud.TextStyle);
            GUILayout.Space(16f * s);
            if (GUILayout.Button("Salvar e voltar", _hud.ButtonStyle))
            {
                _game.ApplySettings();
                _screen = Screen.None;
            }
        }

        private void DrawWardrobe(float s)
        {
            var save = _game.Save.Data;
            GUILayout.Label("Uniformes e acessórios", _hud.TitleStyle);
            GUILayout.Label($"Você tem ★ {save.Stats.TotalStars} e {save.Stats.FlightsWon} vitórias.", _hud.SmallStyle);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(UnityEngine.Screen.height * 0.55f));
            foreach (var item in ProgressionService.Catalog)
            {
                bool unlocked = save.Unlocks.Contains(item.Id);
                bool wearable = item.Category == UnlockCategory.Uniforme || item.Category == UnlockCategory.Acessorio;
                bool equipped = item.Id == save.EquippedUniform || item.Id == save.EquippedAccessory;
                GUILayout.BeginHorizontal();
                string requirement = unlocked ? "" : $"  (precisa ★{item.StarsRequired}" +
                                                     (item.WinsRequired > 0 ? $" e {item.WinsRequired} vitórias)" : ")");
                GUILayout.Label($"{(unlocked ? "✔" : "🔒")} {item.DisplayName} [{item.Category}]{requirement}",
                                _hud.TextStyle, GUILayout.Width(440f * s));
                GUI.enabled = unlocked && wearable && !equipped;
                if (wearable && GUILayout.Button(equipped ? "Em uso" : "Usar"))
                {
                    ProgressionService.TryEquip(save, item.Id);
                    _game.ApplyOutfit();
                    _game.Save.SaveSettings();
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(save.EquippedAccessory) && GUILayout.Button("Tirar acessório", _hud.ButtonStyle))
            {
                ProgressionService.TryEquip(save, "");
                _game.ApplyOutfit();
                _game.Save.SaveSettings();
            }

            if (GUILayout.Button("Voltar", _hud.ButtonStyle))
            {
                _screen = Screen.None;
            }
        }
    }
}
