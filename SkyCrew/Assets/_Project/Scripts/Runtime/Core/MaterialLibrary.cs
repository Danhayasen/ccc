using System.Collections.Generic;
using UnityEngine;

namespace SkyCrew.Core
{
    /// <summary>
    /// Materiais criados em codigo para geometria provisoria (primitivas) e marcadores.
    /// Usa o shader Lit do URP; se nao existir (pipeline nao configurado), cai no Standard.
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> EmissiveCache = new Dictionary<Color, Material>();
        private static Shader _lit;

        public static readonly Color Branco = Hex(0xF4F1EA);
        public static readonly Color Azul = Hex(0x2452A8);
        public static readonly Color AzulEscuro = Hex(0x1B356E);
        public static readonly Color AzulClaro = Hex(0x6FA3DC);
        public static readonly Color Cinza = Hex(0xA9B0BA);
        public static readonly Color CinzaEscuro = Hex(0x4D5561);
        public static readonly Color Creme = Hex(0xEFE3C8);
        public static readonly Color Carpete = Hex(0x5B6F8F);
        public static readonly Color Amarelo = Hex(0xF5C842);
        public static readonly Color Vermelho = Hex(0xE0574F);
        public static readonly Color Verde = Hex(0x5DB760);
        public static readonly Color Marrom = Hex(0x8A5A3B);

        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        private static Shader Lit
        {
            get
            {
                if (_lit == null)
                {
                    _lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (_lit == null)
                    {
                        _lit = Shader.Find("Standard");
                    }
                }

                return _lit;
            }
        }

        public static Material Get(Color color)
        {
            if (Cache.TryGetValue(color, out var m) && m != null)
            {
                return m;
            }

            m = new Material(Lit) { name = "SkyCrew_" + ColorUtility.ToHtmlStringRGB(color), color = color };
            if (m.HasProperty("_BaseColor"))
            {
                m.SetColor("_BaseColor", color);
            }

            if (m.HasProperty("_Smoothness"))
            {
                m.SetFloat("_Smoothness", 0.15f);
            }

            Cache[color] = m;
            return m;
        }

        public static Material GetEmissive(Color color, float intensity = 1.5f)
        {
            if (EmissiveCache.TryGetValue(color, out var m) && m != null)
            {
                return m;
            }

            m = new Material(Get(color)) { name = "SkyCrew_Emissivo_" + ColorUtility.ToHtmlStringRGB(color) };
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            EmissiveCache[color] = m;
            return m;
        }
    }
}
