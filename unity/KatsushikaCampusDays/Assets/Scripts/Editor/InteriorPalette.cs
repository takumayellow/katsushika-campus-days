using System.Collections.Generic;
using UnityEngine;

namespace KCD.Editor
{
    /// <summary>
    /// 屋内 FBX のマテリアル名 → 見た目。blender/kcd_interior/imats.py の PALETTE / TRANSPARENT / EMISSIVE と同じ値
    /// （色は線形 RGB を sRGB に直した 16 進、smoothness = 1 - roughness）。
    /// </summary>
    public static class InteriorPalette
    {
        /// <summary>不透明の材質: 色, smoothness, metallic。</summary>
        public readonly struct Surface
        {
            public readonly string Hex;
            public readonly float Smoothness;
            public readonly float Metallic;

            public Surface(string hex, float smoothness, float metallic)
            {
                Hex = hex;
                Smoothness = smoothness;
                Metallic = metallic;
            }
        }

        private static readonly Dictionary<string, Surface> Surfaces = new Dictionary<string, Surface>
        {
            { "floor_tile_white", new Surface("EEEDEB", 0.70f, 0.02f) },
            { "floor_tile_grey", new Surface("C5C6C6", 0.66f, 0.02f) },
            { "floor_tile_dark", new Surface("848586", 0.62f, 0.02f) },
            { "floor_wood", new Surface("BFA07B", 0.52f, 0.00f) },
            { "floor_wood_light", new Surface("E2CAA0", 0.58f, 0.00f) },
            { "floor_carpet_blue", new Surface("65779B", 0.05f, 0.00f) },
            { "floor_carpet_grey", new Surface("959698", 0.05f, 0.00f) },
            { "floor_carpet_red", new Surface("9B5B5D", 0.05f, 0.00f) },
            { "floor_concrete", new Surface("C3C3C1", 0.28f, 0.00f) },
            { "floor_resin_grey", new Surface("ACAEAF", 0.54f, 0.00f) },
            { "wall_white", new Surface("F3F3F1", 0.38f, 0.00f) },
            { "wall_grey", new Surface("D8D9D8", 0.34f, 0.00f) },
            { "wall_wood", new Surface("B69A79", 0.40f, 0.00f) },
            { "wall_accent_green", new Surface("00AF80", 0.42f, 0.00f) },
            { "wall_accent_navy", new Surface("55698B", 0.40f, 0.00f) },
            { "ceiling_white", new Surface("F6F6F5", 0.28f, 0.00f) },
            { "ceiling_grid", new Surface("D1D2D3", 0.40f, 0.20f) },
            { "ceiling_dark", new Surface("6E6F70", 0.20f, 0.00f) },
            { "light_panel", new Surface("FFFDF7", 0.80f, 0.00f) },
            { "light_strip", new Surface("FFFAF0", 0.80f, 0.00f) },
            { "sign_exit_green", new Surface("42DA99", 0.70f, 0.00f) },
            { "screen_white", new Surface("F7F7F6", 0.58f, 0.00f) },
            { "screen_blue", new Surface("4D76A2", 0.76f, 0.00f) },
            { "board_white", new Surface("F8F8F8", 0.72f, 0.00f) },
            { "board_green", new Surface("5D937E", 0.45f, 0.00f) },
            { "paper_white", new Surface("F8F8F6", 0.22f, 0.00f) },
            { "sign_plate_blue", new Surface("4D84AF", 0.55f, 0.00f) },
            { "desk_wood", new Surface("D1B48D", 0.45f, 0.00f) },
            { "desk_white", new Surface("F0F0EE", 0.55f, 0.00f) },
            { "desk_dark", new Surface("776A5F", 0.48f, 0.00f) },
            { "counter_dark", new Surface("675B52", 0.58f, 0.00f) },
            { "counter_wood", new Surface("AD8D6A", 0.50f, 0.00f) },
            { "counter_stone", new Surface("767677", 0.72f, 0.05f) },
            { "chair_blue", new Surface("6185B6", 0.34f, 0.00f) },
            { "chair_hall_red", new Surface("A24B52", 0.28f, 0.00f) },
            { "chair_green", new Surface("529B80", 0.32f, 0.00f) },
            { "chair_orange", new Surface("DD9B50", 0.32f, 0.00f) },
            { "chair_grey", new Surface("88898B", 0.30f, 0.00f) },
            { "fabric_beige", new Surface("DAD1C2", 0.12f, 0.00f) },
            { "fabric_green", new Surface("799E89", 0.12f, 0.00f) },
            { "cushion_red", new Surface("AF6567", 0.12f, 0.00f) },
            { "metal_gray", new Surface("B8BABC", 0.65f, 0.80f) },
            { "metal_dark", new Surface("6C6E6F", 0.58f, 0.75f) },
            { "stainless", new Surface("DADBDB", 0.78f, 0.90f) },
            { "plastic_white", new Surface("F2F2F0", 0.60f, 0.00f) },
            { "plastic_black", new Surface("424244", 0.55f, 0.00f) },
            { "rubber_black", new Surface("373738", 0.12f, 0.00f) },
            { "fire_red", new Surface("C5423C", 0.55f, 0.00f) },
            { "pipe_grey", new Surface("C5C8C9", 0.60f, 0.60f) },
            { "gas_green", new Surface("429B79", 0.60f, 0.35f) },
            { "gas_blue", new Surface("4584B6", 0.60f, 0.35f) },
            { "book_a", new Surface("C26561", 0.20f, 0.00f) },
            { "book_b", new Surface("5D8BB8", 0.20f, 0.00f) },
            { "book_c", new Surface("67A07E", 0.20f, 0.00f) },
            { "book_d", new Surface("CEB86C", 0.20f, 0.00f) },
            { "book_e", new Surface("9973AA", 0.20f, 0.00f) },
            { "book_f", new Surface("DDDAD1", 0.20f, 0.00f) },
            { "book_g", new Surface("C8935D", 0.20f, 0.00f) },
            { "book_h", new Surface("696A6F", 0.20f, 0.00f) },
            { "court_line", new Surface("F7F7F5", 0.45f, 0.00f) },
            { "court_line_blue", new Surface("6189BF", 0.40f, 0.00f) },
            { "backboard_white", new Surface("F3F4F3", 0.75f, 0.00f) },
            { "hoop_orange", new Surface("E29342", 0.55f, 0.30f) },
            { "net_white", new Surface("F1F1F0", 0.30f, 0.00f) },
            { "curtain_blue", new Surface("5F80AA", 0.08f, 0.00f) },
            { "sb_green", new Surface("00A684", 0.50f, 0.00f) },
            { "sb_wood", new Surface("9B7D5F", 0.45f, 0.00f) },
            { "fm_green", new Surface("00C5A2", 0.50f, 0.00f) },
            { "fm_blue", new Surface("009BCE", 0.50f, 0.00f) },
            { "cake_pink", new Surface("F1D1D1", 0.40f, 0.00f) },
            { "coffee_brown", new Surface("84654B", 0.45f, 0.00f) },
            { "plant_green", new Surface("699E65", 0.12f, 0.00f) },
            { "plant_pot", new Surface("AA978A", 0.20f, 0.00f) },
            { "soil_dark", new Surface("776757", 0.05f, 0.00f) },
            { "tray_beige", new Surface("D1C5AF", 0.35f, 0.00f) },
            // 寮（葛飾コミュニティハウス 1F。館内写真から。imats.py と同じ値）
            { "wallpaper_orange", new Surface("C4623F", 0.15f, 0.00f) },
            { "fabric_yellow", new Surface("E3BD2E", 0.14f, 0.00f) },
            { "floor_carpet_gold", new Surface("B08F4E", 0.05f, 0.00f) },
            { "door_frosted", new Surface("DDE4E6", 0.70f, 0.00f) }
        };

        /// <summary>透ける材質: 色とアルファ。</summary>
        private static readonly Dictionary<string, KeyValuePair<string, float>> Glass = new Dictionary<string, KeyValuePair<string, float>>
        {
            { "glass_interior", new KeyValuePair<string, float>("CEDDDF", 0.22f) },
            { "glass_partition", new KeyValuePair<string, float>("E2EAEB", 0.16f) }
        };

        /// <summary>発光する材質: 発光色と強さ（Blender の strength をそのまま。使う側で落とす）。</summary>
        private static readonly Dictionary<string, KeyValuePair<Color, float>> Emissive = new Dictionary<string, KeyValuePair<Color, float>>
        {
            { "light_panel", new KeyValuePair<Color, float>(new Color(1.000f, 0.975f, 0.920f), 4.50f) },
            { "light_strip", new KeyValuePair<Color, float>(new Color(1.000f, 0.955f, 0.870f), 5.50f) },
            { "sign_exit_green", new KeyValuePair<Color, float>(new Color(0.100f, 0.950f, 0.420f), 3.00f) },
            { "screen_blue", new KeyValuePair<Color, float>(new Color(0.180f, 0.420f, 0.850f), 1.60f) },
            { "screen_white", new KeyValuePair<Color, float>(new Color(0.920f, 0.940f, 0.980f), 0.22f) }
        };

        public static bool TryGetSurface(string name, out Surface surface)
        {
            return Surfaces.TryGetValue(name, out surface);
        }

        public static bool TryGetGlass(string name, out string hex, out float alpha)
        {
            if (Glass.TryGetValue(name, out KeyValuePair<string, float> glass))
            {
                hex = glass.Key;
                alpha = glass.Value;
                return true;
            }

            hex = null;
            alpha = 1f;
            return false;
        }

        /// <summary>発光色（線形）。強さは Blender 値の半分に落として白飛びを避ける。</summary>
        public static bool TryGetEmission(string name, out Color emission)
        {
            if (Emissive.TryGetValue(name, out KeyValuePair<Color, float> value))
            {
                emission = value.Key * (value.Value * 0.5f);
                return true;
            }

            emission = Color.black;
            return false;
        }
    }
}
