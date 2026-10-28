using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven materials rebuilt for Frosthaven's renderer (see GHModel). Gloomhaven's character materials sit on
    /// Amp_Char_Shader, a built-in-pipeline shader Frosthaven's URP never draws (and whose bundle the pack doesn't ship),
    /// so each one becomes a new material on Frosthaven's own character shader: Amp_Char_Shader_Universal, the Drifter's,
    /// which has every property the game's code sets on heroes (invisibility, death dissolve, jump, heal glow, the
    /// character lights); or Amp_Char_Shader_Transparent, the one that clips on alpha, for cutout textures (the Brute's
    /// fur cards; the Universal shader ignores its _AlphaCutoff). The four texture slots map by name (_Diffuse ->
    /// _Albedo, _MRAO -> _MetallicSmoothnessAO, _NormalMap, _EmissiveMap) and the lighting numbers come from the Drifter's
    /// body material. Both games pack metallic, smoothness and ambient occlusion in RGB. Gloomhaven's mask textures are
    /// flagged sRGB (it ran in gamma colour space and read them raw), so Frosthaven's linear pipeline decodes them on
    /// sampling and reads the occlusion a little darker than Gloomhaven did; a GPU copy into a linear texture can't fix
    /// that here, because these textures stream their mips and Graphics.CopyTexture copies only the mips already
    /// loaded. The pack's build step replaces a mask that would blacken the model with flat values (the Brute's body);
    /// the others (the weapons') are used as they are. The textures are what loading the prefab brought into memory.
    /// </summary>
    internal static class GHModelMaterials
    {
        internal const string OpaqueShader = "Amp_Char_Shader_Universal";
        internal const string CutoutShader = "Amp_Char_Shader_Transparent";
        private const string ShaderBundle = "misc_shaders_characters";   // one of the game's always-loaded bundles: these shaders never die

        // The Drifter's body material (HE_InoxDrifter) beyond the shader's defaults, for when hers isn't at hand.
        private static readonly (string name, float value)[] DrifterFloats =
        {
            ("_BlinnLighMainMultiplier", 0.87f), ("_BlinnLightPowerMain", 2f), ("_BlinnLightSpecularPowerMain", 2.75f),
            ("_BlinnLightPowerRim", 4.48f), ("_BlinnLightSpecularPowerRim", 1.53f), ("_GroupBlinnReduceMetalGlow", 0.228f),
        };
        private static readonly (string name, Vector4 value)[] DrifterVectors =
        {
            ("_BlinnLightPositionMain", new Vector4(0.5548f, 3.0574f, 1.1711f, 0f)),
            ("_BlinnLightPositionRim", new Vector4(-0.674f, 2.9271f, -0.8143f, 0f)),
        };

        /// <summary>One of Frosthaven's character shaders. Shader.Find sees it once anything has used it; the always-loaded
        /// shader bundle is the fallback.</summary>
        internal static Shader FindShader(string name)
        {
            Shader s = Shader.Find(name);
            if (s != null) return s;
            AssetBundle bundle = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b != null && b.name == ShaderBundle);
            return bundle != null ? bundle.LoadAllAssets<Shader>().FirstOrDefault(x => x != null && x.name == name) : null;
        }

        /// <summary>A material on <paramref name="shader"/> with the Drifter's lighting numbers and no textures: copied from
        /// her body material when it is loaded (it is whenever a Gloomhaven model is built), else the known values.</summary>
        internal static Material Template(Shader shader, Material drifterBody)
        {
            var m = new Material(shader) { name = "Gloomhaven template (" + shader.name + ")", hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (drifterBody != null && drifterBody.shader != null) CopyValues(drifterBody, m);
            else
            {
                foreach ((string n, float v) in DrifterFloats) if (m.HasProperty(n)) m.SetFloat(n, v);
                foreach ((string n, Vector4 v) in DrifterVectors) if (m.HasProperty(n)) m.SetVector(n, v);
            }
            foreach (string slot in m.GetTexturePropertyNames()) m.SetTexture(slot, null);
            return m;
        }

        /// <summary>Every float, colour and vector the two shaders share, from one material to the other (textures excluded).</summary>
        private static void CopyValues(Material from, Material to)
        {
            Shader s = from.shader;
            for (int i = 0; i < s.GetPropertyCount(); i++)
            {
                string n = s.GetPropertyName(i);
                if (!to.HasProperty(n)) continue;
                switch (s.GetPropertyType(i))
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: to.SetFloat(n, from.GetFloat(n)); break;
                    case ShaderPropertyType.Color: to.SetColor(n, from.GetColor(n)); break;
                    case ShaderPropertyType.Vector: to.SetVector(n, from.GetVector(n)); break;
                }
            }
        }

        /// <summary>The Frosthaven material for one Gloomhaven material: a copy of <paramref name="template"/> with the
        /// Gloomhaven textures (by the pack's names, else whatever the old material still holds), two-sided like the
        /// Drifter's body.</summary>
        internal static Material Rebuild(Material gh, GHModel.MaterialSpec spec, Material template, Func<string, Texture2D> texture)
        {
            var m = new Material(template) { name = gh.name + " (Frosthaven)", hideFlags = HideFlags.DontUnloadUnusedAsset };
            Texture albedo = Pick(spec?.Albedo, gh, "_Diffuse", texture);
            Texture mask = spec != null && spec.FlatMask ? Flat(spec.Metallic, spec.Smoothness, spec.Ao) : Pick(spec?.Mask, gh, "_MRAO", texture);
            Texture normal = Pick(spec?.Normal, gh, "_NormalMap", texture);
            Texture emissive = spec != null ? (spec.Emissive != null ? Pick(spec.Emissive, gh, "_EmissiveMap", texture) : null) : Pick(null, gh, "_EmissiveMap", texture);
            m.SetTexture("_Albedo", albedo);
            m.SetTexture("_MetallicSmoothnessAO", mask);
            m.SetTexture("_NormalMap", normal);
            m.SetTexture("_EmissiveMap", emissive);
            if (m.HasProperty("_EmissiveBoost")) m.SetFloat("_EmissiveBoost", spec?.EmissiveBoost ?? 1f);
            if (spec != null && spec.Brightness != 1f && m.HasProperty("_Value")) m.SetFloat("_Value", m.GetFloat("_Value") * spec.Brightness);
            if (m.HasProperty("_AlphaClipThreshold")) m.SetFloat("_AlphaClipThreshold", spec?.Cutoff ?? 0.5f);
            if (m.HasProperty("_CULL_MODE")) m.SetFloat("_CULL_MODE", (float)CullMode.Off);
            if (albedo == null) Plugin.Log.LogWarning($"Gloomhaven model: {gh.name} has no albedo texture; it renders white");
            return m;
        }

        /// <summary>A fully transparent material for a body part Frosthaven can't draw (the Nightshroud's distortion shell
        /// over his legs, which drew solid white on the character shader, 2026-10-27): the unlit particle shader, additive,
        /// clear colour. The game copies hero materials for its visibility toggles, and a copy stays transparent.</summary>
        internal static Material Invisible(string ghName)
        {
            Shader s = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = new Material(s) { name = ghName + " (hidden)", hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 4f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", 5f);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", 1f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.clear);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.clear);
            m.renderQueue = 3000;
            return m;
        }

        /// <summary>The Gloomhaven texture for a slot: what the old material still holds (its property sheet outlives
        /// the lost shader), else the texture of the pack's name among the loaded objects.</summary>
        private static Texture Pick(string name, Material gh, string ghSlot, Func<string, Texture2D> texture)
        {
            Texture t = null;
            if (gh != null)
            {
                try { t = gh.GetTexture(ghSlot); }
                catch (Exception) { t = null; }
            }
            if (t == null && name != null) t = texture(name);
            return t;
        }

        private static readonly Dictionary<(float, float, float), Texture2D> Flats = new Dictionary<(float, float, float), Texture2D>();

        /// <summary>A flat metallic / smoothness / occlusion texture (linear, as the shader reads that slot).</summary>
        internal static Texture2D Flat(float metallic, float smoothness, float ao)
        {
            var key = (metallic, smoothness, ao);
            if (Flats.TryGetValue(key, out Texture2D done) && done != null) return done;
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = $"flat mask {metallic:0.00}/{smoothness:0.00}/{ao:0.00}", hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Repeat };
            var c = new Color(metallic, smoothness, ao, 1f);
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply(false, true);
            Flats[key] = t;
            return t;
        }

    }
}
