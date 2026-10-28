using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ScenarioRuleLibrary;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// A Gloomhaven class's own ability effects, played in place of the Drifter's on the moves that had them in
    /// Gloomhaven. The effect prefabs ride in the class's hero bundle (loading the model brings them into memory);
    /// tools/gh_vfx.py records what the plugin must do to each one, and tools/gh_model.py which effect each animator
    /// state plays and when (StateTiming.Effects, AnimationSpec.GhEffects).
    ///
    /// Per effect, a template is made once (an inactive-parented copy, like the model's): its materials are rebuilt on
    /// Frosthaven's copy of the same shader (the two games share their effect shaders by name; Unity's legacy particle
    /// shaders, which Frosthaven still carries, stand in for the few Gloomhaven-only ones), slots that came up empty
    /// (materials Gloomhaven kept in bundles of their own, shipped with the pack) are filled by name, and the effect
    /// scripts Frosthaven has too are re-added with their recorded fields. The templates then go into the Drifter's
    /// cast behaviours as the effects to spawn (GHModel.Retime, LendClip), where the game plays them its usual way:
    /// pooled, at the recorded moment, on the recorded bone or hex.
    /// </summary>
    internal static class GHVfx
    {
        // ------------------------------------------------------------------ pack data

        internal sealed class ObjectSpec
        {
            public string[] Materials;            // per renderer slot; null: left as it is
            public string TrailMaterial;
            public string[] Loaded;               // a MaterialLoader's materials for this renderer
            public bool KeepExisting;
            public List<KeyValuePair<string, JObject>> Components = new List<KeyValuePair<string, JObject>>();
        }

        internal sealed class EffectSpec
        {
            public string Name;
            public Dictionary<string, ObjectSpec> Objects = new Dictionary<string, ObjectSpec>(StringComparer.Ordinal);   // path under the prefab ("" = root)
        }

        internal sealed class MaterialSpec
        {
            public string Name, Shader, Bundle;
            public Dictionary<string, float> Floats = new Dictionary<string, float>(StringComparer.Ordinal);
            public Dictionary<string, Color> Colors = new Dictionary<string, Color>(StringComparer.Ordinal);
            public Dictionary<string, string> Textures = new Dictionary<string, string>(StringComparer.Ordinal);
            public string[] Keywords = new string[0];
            public int Queue = -1;
        }

        internal sealed class BundleSpec { public string File, Name; }

        internal sealed class Caster { public string Effect, Attach; public float Start; public bool Root; }
        internal sealed class HexEffect { public string Effect; public float Start; public int TargetType; }
        internal sealed class Hex { public float Hit; public List<HexEffect> Effects = new List<HexEffect>(); }

        /// <summary>What a state plays: effects on the caster, and on the target / area / central hexes or as an aura,
        /// with the area alignment flags when any hex list is set.</summary>
        internal sealed class Play
        {
            public List<Caster> Caster = new List<Caster>();
            public Hex Target, Area, Central, Aura;
            public int Alignment;
            public bool CasterCentre, Adjacent;
            public bool AnyHex => Target != null || Area != null || Central != null || Aura != null;
        }

        internal static Dictionary<string, EffectSpec> ParseEffects(JToken j)
        {
            var out_ = new Dictionary<string, EffectSpec>(StringComparer.Ordinal);
            foreach (JProperty p in (j as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var spec = new EffectSpec { Name = p.Name };
                foreach (JProperty o in (p.Value?["objects"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    var v = o.Value as JObject;
                    if (v == null) continue;
                    var os = new ObjectSpec
                    {
                        Materials = v["materials"] is JArray ma ? ma.Select(x => (string)x).ToArray() : null,
                        TrailMaterial = (string)v["trail_material"],
                        Loaded = v["loaded"]?["materials"] is JArray la ? la.Select(x => (string)x).ToArray() : null,
                        KeepExisting = (bool?)v["loaded"]?["keep"] ?? false,
                    };
                    foreach (JObject c in v["components"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                        if ((string)c["type"] != null) os.Components.Add(new KeyValuePair<string, JObject>((string)c["type"], c["fields"] as JObject ?? new JObject()));
                    spec.Objects[o.Name] = os;
                }
                out_[p.Name] = spec;
            }
            return out_;
        }

        internal static Dictionary<string, MaterialSpec> ParseMaterials(JToken j)
        {
            var out_ = new Dictionary<string, MaterialSpec>(StringComparer.Ordinal);
            foreach (JProperty p in (j as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var v = p.Value as JObject;
                if (v == null) continue;
                var spec = new MaterialSpec { Name = p.Name, Shader = (string)v["shader"], Bundle = (string)v["bundle"], Queue = (int?)v["queue"] ?? -1 };
                foreach (JProperty f in (v["floats"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) spec.Floats[f.Name] = (float)f.Value;
                foreach (JProperty c in (v["colors"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    if (c.Value is JArray a && a.Count == 4) spec.Colors[c.Name] = new Color((float)a[0], (float)a[1], (float)a[2], (float)a[3]);
                foreach (JProperty t in (v["textures"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) spec.Textures[t.Name] = (string)t.Value;
                spec.Keywords = (v["keywords"] as JArray)?.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToArray() ?? new string[0];
                out_[p.Name] = spec;
            }
            return out_;
        }

        internal static List<BundleSpec> ParseBundles(JToken j) =>
            (j as JArray)?.OfType<JObject>().Select(b => new BundleSpec { File = (string)b["file"], Name = (string)b["name"] })
                .Where(b => !string.IsNullOrEmpty(b.File)).ToList() ?? new List<BundleSpec>();

        internal static Play ParsePlay(JToken j)
        {
            var v = j as JObject;
            if (v == null) return null;
            var play = new Play
            {
                Alignment = (int?)v["alignment"] ?? 0, CasterCentre = (bool?)v["caster_centre"] ?? false, Adjacent = (bool?)v["adjacent"] ?? false,
                Target = ParseHex(v["target"]), Area = ParseHex(v["area"]), Central = ParseHex(v["central"]), Aura = ParseHex(v["aura"]),
            };
            foreach (JObject c in v["caster"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                if ((string)c["effect"] != null)
                    play.Caster.Add(new Caster { Effect = (string)c["effect"], Start = (float?)c["start"] ?? 0f, Attach = (string)c["attach"] ?? "", Root = (bool?)c["root"] ?? false });
            return play.Caster.Count > 0 || play.AnyHex ? play : null;
        }

        private static Hex ParseHex(JToken j)
        {
            var v = j as JObject;
            if (v == null) return null;
            var hex = new Hex { Hit = (float?)v["hit"] ?? 0f };
            foreach (JObject x in v["effects"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                if ((string)x["effect"] != null)
                    hex.Effects.Add(new HexEffect { Effect = (string)x["effect"], Start = (float?)x["start"] ?? 0f, TargetType = (int?)x["target_type"] ?? 0 });
            return hex.Effects.Count > 0 ? hex : null;
        }

        // ------------------------------------------------------------------ our entries in the Drifter's behaviours

        /// <summary>Marked, so GHModel.Retime leaves their Gloomhaven moments alone (they are on the Gloomhaven clip's
        /// own timeline; hers are rescaled to it).</summary>
        internal sealed class GhSpawned : VFXShared.SpawnedEffect { }

        internal sealed class GhHexEffect : VFXShared.BasicEffectTyped
        {
            public GhHexEffect(GameObject fx, float start, VFXShared.ActorType targets, CActor.EType caster) : base(fx, start, targets, caster) { OriginalStartTime = start; }
        }

        internal sealed class GhHex : VFXShared.TargetHexEffects { }

        /// <summary>What a cast behaviour had before Apply, for LendClip to put back.</summary>
        internal sealed class Kept
        {
            public List<VFXShared.SpawnedEffect> Caster;
            public VFXShared.TargetHexEffects Target, Area, Central, Aura;
            public VFXShared.AreaEffectAlignments Alignment;
            public bool Centre, Adjacent;
        }

        internal static Kept Keep(CastEffectsSMB c) => new Kept
        {
            Caster = c.CasterEffects, Target = c.TargetHexEffects, Area = c.AreaHexEffects, Central = c.AreaCentralHexEffects, Aura = c.AuraEffects,
            Alignment = c.AreaEffectAlignment, Centre = c.CasterIsCentre, Adjacent = c.AreaEffectAdjacentToTargets,
        };

        internal static void Restore(CastEffectsSMB c, Kept k)
        {
            if (c == null || k == null) return;
            c.CasterEffects = k.Caster;
            c.TargetHexEffects = k.Target;
            c.AreaHexEffects = k.Area;
            c.AreaCentralHexEffects = k.Central;
            c.AuraEffects = k.Aura;
            c.AreaEffectAlignment = k.Alignment;
            c.CasterIsCentre = k.Centre;
            c.AreaEffectAdjacentToTargets = k.Adjacent;
        }

        /// <summary>Puts the Gloomhaven effects of <paramref name="play"/> on the behaviour: the caster list when the
        /// state had one; the hex lists and alignment when it had any (an empty list where it had none, so the Drifter's
        /// hex effect doesn't land beside his), her aura kept unless he brings one (a persistent shield ring the game
        /// expects). Nothing of hers is removed when his templates aren't there.</summary>
        internal static void Apply(CastEffectsSMB cast, Play play, GHModel.Entry e)
        {
            if (cast == null || play == null || e == null) return;
            if (play.Caster.Count > 0)
            {
                var list = new List<VFXShared.SpawnedEffect>();
                foreach (Caster c in play.Caster)
                {
                    GameObject t = Template(e, c.Effect);
                    if (t == null) continue;
                    list.Add(new GhSpawned { Effect = t, StartTime = c.Start, OriginalStartTime = c.Start, AttachToRoot = c.Root, EffectAttachPoint = c.Attach ?? "" });
                }
                if (list.Count > 0) cast.CasterEffects = list;
            }
            if (!play.AnyHex) return;
            float beat = new[] { play.Target, play.Area, play.Central, play.Aura }.Where(h => h != null).Max(h => h.Hit);
            VFXShared.TargetHexEffects target = MakeHex(play.Target, beat, e), area = MakeHex(play.Area, beat, e), central = MakeHex(play.Central, beat, e);
            if (!HasAny(target) && !HasAny(area) && !HasAny(central) && (play.Aura == null || !HasAny(MakeHex(play.Aura, beat, e)))) return;   // no template came out: hers stay
            cast.TargetHexEffects = target;
            cast.AreaHexEffects = area;
            cast.AreaCentralHexEffects = central;
            if (play.Aura != null) cast.AuraEffects = MakeHex(play.Aura, beat, e);
            cast.AreaEffectAlignment = (VFXShared.AreaEffectAlignments)play.Alignment;
            cast.CasterIsCentre = play.CasterCentre;
            cast.AreaEffectAdjacentToTargets = play.Adjacent;
        }

        private static bool HasAny(VFXShared.TargetHexEffects h) => h?.Effects != null && h.Effects.Count > 0;

        private static GhHex MakeHex(Hex h, float beat, GHModel.Entry e)
        {
            var hex = new GhHex { HitTime = h?.Hit ?? beat, OriginalHitTime = h?.Hit ?? beat, Effects = new List<VFXShared.BasicEffectTyped>() };
            if (h == null) return hex;
            foreach (HexEffect x in h.Effects)
            {
                GameObject t = Template(e, x.Effect);
                if (t != null) hex.Effects.Add(new GhHexEffect(t, x.Start, (VFXShared.ActorType)x.TargetType, CActor.EType.Player));
            }
            return hex;
        }

        // ------------------------------------------------------------------ templates

        private sealed class Set
        {
            public readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            public readonly Dictionary<string, Material> Made = new Dictionary<string, Material>(StringComparer.Ordinal);       // Gloomhaven material name -> Frosthaven material
            public readonly Dictionary<string, Material> Gloomhaven = new Dictionary<string, Material>(StringComparer.Ordinal); // from the shipped material bundles
            public Dictionary<string, Texture> Textures;                                                                          // every loaded texture by name, on demand
        }

        private static readonly Dictionary<string, Set> Sets = new Dictionary<string, Set>(StringComparer.OrdinalIgnoreCase);   // by model (its looks share the effects)
        private static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>The template of one of the model's effects, or null (not in the pack, or its build failed: logged once).</summary>
        internal static GameObject Template(GHModel.Entry e, string name)
        {
            if (e == null || string.IsNullOrEmpty(name) || !Sets.TryGetValue(e.Model.ToString(), out Set set)) return null;
            if (set.Templates.TryGetValue(name, out GameObject t) && t != null) return t;
            if (Reported.Add("missing " + name)) Plugin.Log.LogWarning($"Gloomhaven effects: {e.Model} has no template for {name}; the Drifter's effect plays there");
            return null;
        }

        /// <summary>Builds the model's effect templates once the bundle is open (GHModel.Build). Never throws: a model
        /// without working effects keeps the Drifter's.</summary>
        internal static void Prepare(GHModel.Entry e, GHModel.Loaded bundle)
        {
            if (e == null || e.Effects.Count == 0 || bundle?.Bundle == null) return;
            string key = e.Model.ToString();
            if (Sets.TryGetValue(key, out Set set) && set.Templates.Count > 0 && set.Templates.Values.All(t => t != null)) return;
            set = new Set();
            Sets[key] = set;
            try
            {
                GHSound.Ensure(e.Model, e.Folder);   // an effect's own sound script is kept only for a registered event: register first
                Dictionary<string, GameObject> roots = bundle.Roots();
                LoadMaterialBundles(e, set);
                var stateAudio = new HashSet<string>(e.Animation.States.Values.Select(t => t.Audio).Concat(e.Animation.GhAudio.Values).Where(a => !string.IsNullOrEmpty(a)), StringComparer.Ordinal);
                var missing = new List<string>();
                foreach (EffectSpec spec in e.Effects.Values)
                {
                    if (!roots.TryGetValue(spec.Name, out GameObject root) || root == null) { missing.Add(spec.Name); continue; }
                    GameObject t = UnityEngine.Object.Instantiate(root, GHModel.Holder);
                    t.name = spec.Name;
                    t.SetActive(true);                                       // activeSelf under the inactive holder: pooled copies come out active, like a prefab's
                    try { Dress(t, spec, e, set, stateAudio); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"Gloomhaven effects: {spec.Name} is only partly set up: {ex.Message}"); }
                    try { Audit(t, spec.Name, set, e); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"Gloomhaven effects: {spec.Name} could not be audited: {ex.Message}"); }
                    set.Templates[spec.Name] = t;
                }
                Plugin.Log.LogInfo($"Gloomhaven effects: {set.Templates.Count} of {e.Model}'s effects ready, {set.Made.Values.Count(m => m != null)} materials rebuilt on Frosthaven's shaders"
                                   + (missing.Count > 0 ? $"; not in the bundle: {string.Join(", ", missing)}" : ""));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Gloomhaven effects: could not set up {e.Model}'s effects, the Drifter's stay: {ex}");
            }
        }

        /// <summary>The hero model's own particles (pack "hero_particles": the Nightshroud's wisps), dressed with the
        /// rebuilt materials like an effect's objects and shown; before this they were switched off (null materials,
        /// magenta patches). Runs after Prepare, which opens the material bundles. Never throws.</summary>
        internal static void DressHero(GHModel.Entry e, GameObject visual)
        {
            if (e?.HeroParticles == null || visual == null) return;
            try
            {
                string key = e.Model.ToString();
                if (!Sets.TryGetValue(key, out Set set))
                {
                    set = new Set();
                    Sets[key] = set;
                    LoadMaterialBundles(e, set);
                }
                int dressed = 0;
                foreach (KeyValuePair<string, ObjectSpec> kv in e.HeroParticles.Objects)
                {
                    Transform tr = kv.Key.Length == 0 ? visual.transform : visual.transform.Find(kv.Key);
                    if (tr == null) { if (Reported.Add("hero " + key + "/" + kv.Key)) Plugin.Log.LogWarning($"Gloomhaven effects: {e.Model}'s model has no {kv.Key}"); continue; }
                    DressRenderer(tr, kv.Value, e, set);
                    var r = tr.GetComponent<Renderer>();
                    if (r != null) r.forceRenderingOff = false;
                    dressed++;
                }
                Plugin.Log.LogInfo($"Gloomhaven effects: {dressed} of {e.Model}'s own particle object(s) dressed");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"Gloomhaven effects: {e.Model}'s own particles stay off: {ex.Message}"); }
        }

        private static void LoadMaterialBundles(GHModel.Entry e, Set set)
        {
            foreach (BundleSpec b in e.EffectBundles)
            {
                AssetBundle ab = !string.IsNullOrEmpty(b.Name) ? AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(x => x != null && x.name == b.Name) : null;
                if (ab == null)
                {
                    string path = Path.Combine(e.Folder, "bundles", b.File);
                    if (!File.Exists(path)) { if (Reported.Add("no bundle " + b.File)) Plugin.Log.LogWarning($"Gloomhaven effects: the pack has no {b.File}; some effects will have holes"); continue; }
                    ab = AssetBundle.LoadFromFile(path);
                    if (ab == null) { Plugin.Log.LogWarning($"Gloomhaven effects: could not open {b.File}"); continue; }
                }
                foreach (Material m in ab.LoadAllAssets<Material>())
                    if (m != null && !set.Gloomhaven.ContainsKey(m.name)) set.Gloomhaven[m.name] = m;
            }
        }

        /// <summary>Materials, loader-fed slots and carried scripts, object by object.</summary>
        private static void Dress(GameObject template, EffectSpec spec, GHModel.Entry e, Set set, HashSet<string> stateAudio)
        {
            foreach (KeyValuePair<string, ObjectSpec> kv in spec.Objects)
            {
                Transform tr = kv.Key.Length == 0 ? template.transform : template.transform.Find(kv.Key);
                if (tr == null) { if (Reported.Add(spec.Name + "/" + kv.Key)) Plugin.Log.LogWarning($"Gloomhaven effects: {spec.Name} has no {kv.Key}"); continue; }
                ObjectSpec o = kv.Value;
                // An effect can hold two objects of the same name (the Quartermaster's radial buff has two "MeshEmitterBits (2)");
                // the pack's spec is keyed by name, so it applies to every one of them (left undressed, the second kept
                // Gloomhaven's material and drew untextured white over the screen for the aura's whole round)
                var targets = new List<Transform> { tr };
                if (kv.Key.Length > 0 && kv.Key.IndexOf('/') < 0)
                    targets.AddRange(template.GetComponentsInChildren<Transform>(true).Where(x => x != tr && x.name == kv.Key));
                foreach (Transform same in targets)
                    DressRenderer(same, o, e, set);
                foreach (KeyValuePair<string, JObject> c in o.Components) AddScript(tr.gameObject, c.Key, c.Value, set, stateAudio);
            }
            if (template.GetComponent<EffectOnEnable>() == null) template.AddComponent<EffectOnEnable>().LifeTime = EffectOnEnable.DefaultLifeTime;   // the game warns without one
        }

        private static void DressRenderer(Transform tr, ObjectSpec o, GHModel.Entry e, Set set)
        {
            {
                var r = tr.GetComponent<Renderer>();
                if (r != null && (o.Materials != null || o.Loaded != null))
                {
                    Material[] current = r.sharedMaterials;
                    var slots = new List<Material>(current);
                    if (o.Materials != null)
                        for (int i = 0; i < o.Materials.Length; i++)
                        {
                            Material m = Rebuild(set, e, o.Materials[i], i < current.Length ? current[i] : null);
                            if (i < slots.Count) slots[i] = m; else slots.Add(m);
                        }
                    if (o.Loaded != null)
                    {
                        // as Gloomhaven's MaterialLoaderData did it: the loaded ones first, then each kept slot back at its own index
                        var loaded = o.Loaded.Select(n => Rebuild(set, e, n, null)).ToList();
                        if (o.KeepExisting)
                        {
                            var merged = new List<Material>(loaded);
                            while (merged.Count < loaded.Count + slots.Count(m => m != null)) merged.Add(null);
                            for (int i = 0; i < slots.Count; i++) if (slots[i] != null && i < merged.Count) merged[i] = slots[i];
                            slots = merged;
                        }
                        else slots = loaded;
                    }
                    r.sharedMaterials = slots.ToArray();
                    if (r is ParticleSystemRenderer psr && o.TrailMaterial != null) psr.trailMaterial = Rebuild(set, e, o.TrailMaterial, psr.trailMaterial);
                }
            }
        }

        // ------------------------------------------------------------------ materials

        /// <summary>Frosthaven's version of a Gloomhaven effect material: the recorded values on Frosthaven's copy of
        /// the shader, its textures from the Gloomhaven material (its own bundle keeps them). Null stays null.</summary>
        private static Material Rebuild(Set set, GHModel.Entry e, string name, Material gh)
        {
            if (string.IsNullOrEmpty(name)) return gh;
            if (set.Made.TryGetValue(name, out Material made)) return made ?? gh;
            if (gh == null) set.Gloomhaven.TryGetValue(name, out gh);
            if (!e.EffectMaterials.TryGetValue(name, out MaterialSpec spec)) { set.Made[name] = gh; return gh; }
            Shader shader = ShaderFor(spec);
            if (shader == null)
            {
                if (Reported.Add("shader " + spec.Shader)) Plugin.Log.LogWarning($"Gloomhaven effects: Frosthaven has no shader for {spec.Shader} ({name}); that part stays as it is");
                set.Made[name] = gh;
                return gh;
            }
            var m = new Material(shader) { name = name + " (Frosthaven)", hideFlags = HideFlags.DontUnloadUnusedAsset };
            // Blend-mode floats are real only on the Standard-family shaders. On Gloomhaven's VFX shaders they are
            // leftovers from an earlier shader of the material (its additive glint recorded _SrcBlend 1, _DstBlend 0,
            // ZWrite on), and Frosthaven's copy of the shader may expose them: applied, they turned the glint opaque
            // (a black square round each sparkle).
            bool blendReal = BlendFloatsAreReal(spec.Shader);
            foreach (KeyValuePair<string, float> f in spec.Floats)
                if (m.HasProperty(f.Key) && (blendReal || !BlendFloats.Contains(f.Key))) m.SetFloat(f.Key, f.Value);
            foreach (KeyValuePair<string, Color> c in spec.Colors) if (m.HasProperty(c.Key)) m.SetColor(c.Key, c.Value);
            foreach (KeyValuePair<string, string> t in spec.Textures)
            {
                string key = m.HasProperty(t.Key) ? t.Key : Alias(m, t.Key);   // a stand-in shader names its slots differently
                if (key == null) continue;
                Texture tex = gh != null ? gh.GetTexture(t.Key) : null;
                if (tex == null) tex = FindTexture(set, t.Value);
                if (tex == null) continue;
                m.SetTexture(key, tex);
                if (gh != null)
                {
                    m.SetTextureScale(key, gh.GetTextureScale(t.Key));
                    m.SetTextureOffset(key, gh.GetTextureOffset(t.Key));
                }
            }
            // the Gloomhaven shaft shader multiplies its beam colour by _Intensity (4 on the Cragheart's shafts);
            // Frosthaven's LightShaftShd, which draws them now, has no such property, so the dim beam colour
            // (0.39, 0.39, 0.21) left the shaft invisible (the user's Sentient Growth screenshot, 2026-10-27):
            // the intensity goes into the colour, alpha kept
            if (spec.Shader == "CragHeartSummonShaftShd" && m.HasProperty("_BeamColor") && !m.HasProperty("_Intensity")
                && spec.Floats.TryGetValue("_Intensity", out float intensity) && intensity > 0f)
            {
                Color beam = m.GetColor("_BeamColor");
                m.SetColor("_BeamColor", new Color(beam.r * intensity, beam.g * intensity, beam.b * intensity, beam.a));
                // and it follows its particle's colour and fade as Frosthaven's own shafts do (the Sawbones' chest shaft
                // records _EnableParticleControls 1); without it the cone stood static at full strength (2026-10-27)
                if (m.HasProperty("_EnableParticleControls")) m.SetFloat("_EnableParticleControls", 1f);
            }
            foreach (string kw in spec.Keywords) m.EnableKeyword(kw);
            if (spec.Queue >= 0) m.renderQueue = spec.Queue;
            if (spec.Shader == "VFX/ParticleMasterUnlitAdd_Shd" || spec.Shader == "VFX/SimpleInvisibleShd")
            {
                Additive(m);
                if (spec.Shader == "VFX/SimpleInvisibleShd") m.SetColor("_Color", Color.clear);
            }
            set.Made[name] = m;
            return m;
        }

        /// <summary>The unlit particle shader's additive mode, as the sparks' material records it (a variant the game
        /// ships, since its own effects use it).</summary>
        private static void Additive(Material m)
        {
            m.SetFloat("_Mode", 4f);
            m.SetFloat("_SrcBlend", 5f);   // SrcAlpha
            m.SetFloat("_DstBlend", 1f);   // One
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
        }

        /// <summary>One log line per renderer of a template, listing what each slot ended up with, so a pink or black part
        /// can be traced to its material. A slot whose shader the game cannot run (a Gloomhaven-only one that slipped
        /// through) is rebuilt from the pack's description of its material when there is one; otherwise a part with a
        /// texture gets an unlit stand-in drawing that texture, and a part without one (or a decal: Frosthaven has no
        /// decal shader, and a decal volume drawn as a mesh is a solid sphere or box) is dropped. A renderer left with
        /// nothing usable is turned off: an untextured stand-in drew as solid white (the Sawbones' medical-pack decals,
        /// spheres round both heroes).</summary>
        private static void Audit(GameObject template, string name, Set set, GHModel.Entry e)
        {
            foreach (Renderer r in template.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                var parts = new List<string>();
                bool changed = false, usable = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) { parts.Add("-"); continue; }
                    Shader sh = m.shader;
                    bool ok = Usable(m);
                    string fate = "";
                    if (!ok && e != null && e.EffectMaterials.ContainsKey(m.name))
                    {
                        Material rebuilt = Rebuild(set, e, m.name, m);
                        if (rebuilt != null && rebuilt != m && Usable(rebuilt)) { mats[i] = rebuilt; changed = true; ok = true; fate = " -> rebuilt"; }
                    }
                    if (!ok)
                    {
                        Texture tex = AnyTexture(m);
                        bool decal = (sh != null && sh.name.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0) || m.name.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0;
                        Shader unlit = tex != null && !decal ? FindShader("Particles/Standard Unlit") : null;
                        if (unlit != null)
                        {
                            var fix = new Material(unlit) { name = m.name + " (stand-in)", hideFlags = HideFlags.DontUnloadUnusedAsset, mainTexture = tex };
                            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) Additive(fix);   // particles glow; a prop mesh keeps its look
                            mats[i] = fix;
                            changed = true;
                            ok = true;
                            fate = " -> stand-in";
                        }
                        else
                        {
                            mats[i] = null;
                            changed = true;
                            fate = decal ? " -> dropped (decal)" : " -> dropped (no texture)";
                        }
                    }
                    usable |= ok;
                    Texture shown = m.HasProperty("_MainTex") ? m.mainTexture : null;   // asking a shader without the slot logs an error
                    parts.Add($"{m.name} [{(sh != null ? sh.name : "no shader")}{(shown != null ? ", " + shown.name : "")}]{fate}");
                }
                if (changed) r.sharedMaterials = mats;
                if (!usable && mats.Length > 0) r.enabled = false;
                string path = r.transform == template.transform ? "." : r.transform.name;
                string mode = r is ParticleSystemRenderer psr ? " " + psr.renderMode : "";
                Plugin.Log.LogInfo($"Gloomhaven effects: {name}/{path}{mode}{(r.enabled ? "" : " (off)")}: {string.Join("; ", parts)}");
            }
        }

        private static bool Usable(Material m) =>
            m != null && m.shader != null && m.shader.isSupported && m.shader.name != "Hidden/InternalErrorShader";

        /// <summary>The first texture a material still holds in any slot (a lost shader keeps the property sheet: the
        /// weapon prop's _Diffuse survives on the error shader), or null.</summary>
        private static Texture AnyTexture(Material m)
        {
            if (m.HasProperty("_MainTex") && m.mainTexture != null) return m.mainTexture;
            foreach (string slot in m.GetTexturePropertyNames())
            {
                Texture t = m.GetTexture(slot);
                if (t != null && !slot.StartsWith("unity_", StringComparison.Ordinal)) return t;
            }
            return null;
        }

        /// <summary>Frosthaven's shader for a Gloomhaven effect material: the same name (the shared VFX library), or a
        /// stand-in for the Gloomhaven-only ones: KriptoFX's particle shader becomes Unity's legacy particle shader of
        /// the same blend, the lit standard particle shader its unlit sibling.</summary>
        private static Shader ShaderFor(MaterialSpec spec)
        {
            string name = spec.Shader;
            if (string.IsNullOrEmpty(name)) return null;
            if (name == "KriptoFX/RFX4/Particle")
            {
                float src = spec.Floats.TryGetValue("SrcMode", out float s) ? s : 1f, dst = spec.Floats.TryGetValue("DstMode", out float d) ? d : 1f;
                name = dst == 1f ? "Legacy Shaders/Particles/Additive"
                     : src == 1f ? "Legacy Shaders/Particles/Alpha Blended Premultiply" : "Legacy Shaders/Particles/Alpha Blended";
            }
            else if (name == "Particles/Standard Surface") name = "Particles/Standard Unlit";
            // Frosthaven's copy of the additive unlit shader drew the dagger glint on an opaque black square, and its
            // "invisible" shader is not to be trusted either: both become the unlit particle shader in its additive mode,
            // the one the sparks use (Additive below), the invisible one with a clear colour
            else if (name == "VFX/ParticleMasterUnlitAdd_Shd" || name == "VFX/SimpleInvisibleShd") name = "Particles/Standard Unlit";
            else if (name == "VFX/LitDissolveShd")
                // the one lit (surface) shader among them: Frosthaven's copy draws magenta under its render pipeline
                // (the earthquake's flying rocks); the pipeline's own lit shader instead, plain rocks without the dissolve
                return FindShader("Universal Render Pipeline/Simple Lit") ?? FindShader("Universal Render Pipeline/Lit") ?? FindShader("Particles/Standard Unlit");
            // Gloomhaven's own surface shaders on props and weapons inside effects, which Frosthaven never shipped: the
            // Cragheart's Avalanche rock drew as a magenta block (Group 2 pass, 2026-10-27), the Sawbones' thrown bottle and
            // the Quartermaster's scrolls would too. Frosthaven's nearest of the same family, else the pipeline's lit shader.
            // (tools/vfxcheck.py carries the same table and flags any other such shader before the pack is built.)
            else if (name == "Amp_Basic_Prop_Shader") return FindShader("Amp_Basic_N_MRAO") ?? FindShader("Universal Render Pipeline/Simple Lit");
            else if (name == "Amp_Char_Shader_2Side") return FindShader("Amp_Char_Shader_Universal") ?? FindShader("Universal Render Pipeline/Simple Lit");
            else if (name == "OmniDecalSimple_Shd") return FindShader("Shader Graphs/URPProjectionDecal_FX") ?? FindShader("Particles/Standard Unlit");
            // Jaws of the Lion's effects: Unity's built-in Standard (blood splats, black mattes; not in a URP build) and the
            // low-end variants of the two Gloomhaven surface shaders above (the mech bomb, the rubble)
            else if (name == "Standard") return FindShader("Universal Render Pipeline/Lit") ?? FindShader("Universal Render Pipeline/Simple Lit");
            // the Cragheart's cast light shafts (positive, shield, heal): Frosthaven's copy of the Gloomhaven shaft shader
            // drew a solid pale cylinder round the hero (the user's screenshot, 2026-10-27; fxhide on the shaft materials
            // removed it); Frosthaven's own light-shaft shader takes the same properties (beam colour, noise mask,
            // softness, thickness, rays) and is the one the Sawbones' chest shaft already draws with
            else if (name == "CragHeartSummonShaftShd") return FindShader("LightShaftShd") ?? FindShader(name);
            else if (name == "Amp_Low/Amp_CharShader_Low" || name == "Amp_Char_Shader") return FindShader("Amp_Char_Shader_Universal") ?? FindShader("Universal Render Pipeline/Simple Lit");
            else if (name == "Amp_Low/Amp_Basic_Prop_Shader_Low") return FindShader("Amp_Basic_N_MRAO") ?? FindShader("Universal Render Pipeline/Simple Lit");
            return FindShader(name);
        }

        private static readonly HashSet<string> BlendFloats = new HashSet<string>(StringComparer.Ordinal)
            { "_SrcBlend", "_DstBlend", "_ZWrite", "_ZTest", "_Mode", "_Cull", "_BlendOp", "_Surface", "_Blend", "_AlphaClip" };

        private static bool BlendFloatsAreReal(string ghShader) =>
            ghShader != null && (ghShader == "Standard" || ghShader.StartsWith("Particles/Standard", StringComparison.Ordinal)
                                 || ghShader.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal));

        /// <summary>The texture slot a stand-in shader has for a Gloomhaven one (the render pipeline's lit shader names
        /// them differently), or null when it has none.</summary>
        private static string Alias(Material m, string property)
        {
            string[] names;
            switch (property)
            {
                case "_DiffuseMap": case "_MainTex": names = new[] { "_BaseMap" }; break;
                case "_NormalMap": names = new[] { "_BumpMap" }; break;
                case "_GlowMap": names = new[] { "_EmissionMap" }; break;
                default: return null;
            }
            return names.FirstOrDefault(m.HasProperty);
        }

        private static Shader FindShader(string name)
        {
            if (Shaders.TryGetValue(name, out Shader s) && s != null) return s;
            s = Shader.Find(name);
            if (s == null)
            {
                // Frosthaven's effect shaders live in its misc_shaders bundles (always loaded with the game); Shader.Find
                // only sees a shader once something has used it
                foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles().Where(b => b != null && b.name.StartsWith("misc_shaders", StringComparison.Ordinal)))
                {
                    s = b.LoadAllAssets<Shader>().FirstOrDefault(x => x != null && x.name == name);
                    if (s != null) break;
                }
            }
            if (s == null) s = Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(x => x != null && x.name == name);
            if (s != null) Shaders[name] = s;
            return s;
        }

        private static Texture FindTexture(Set set, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (set.Textures == null)
            {
                set.Textures = new Dictionary<string, Texture>(StringComparer.Ordinal);
                foreach (Texture t in Resources.FindObjectsOfTypeAll<Texture>()) if (t != null && !set.Textures.ContainsKey(t.name)) set.Textures[t.name] = t;
            }
            if (set.Textures.TryGetValue(name, out Texture tex)) return tex;
            // not loaded when the list was taken (the Cragheart's shaft noise, noise_01, lives in his rock bundles,
            // loaded later for the same effect: the shaft stood static without it, 2026-10-27): the textures loaded
            // now, then the loaded bundles; a texture still missing is reported once
            foreach (Texture t in Resources.FindObjectsOfTypeAll<Texture>()) if (t != null && !set.Textures.ContainsKey(t.name)) set.Textures[t.name] = t;
            if (set.Textures.TryGetValue(name, out tex)) return tex;
            foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles())
            {
                try
                {
                    if (b == null || b.isStreamedSceneAssetBundle) continue;
                    tex = b.LoadAsset<Texture>(name);
                    if (tex != null) { set.Textures[name] = tex; return tex; }
                }
                catch (Exception) { }
            }
            if (Reported.Add("texture " + name)) Plugin.Log.LogWarning($"Gloomhaven effects: texture {name} not found; the material draws without it");
            return null;
        }

        // ------------------------------------------------------------------ the carried scripts

        /// <summary>Frosthaven's component of the same class, with the recorded fields. A sound script is added only
        /// for an event the pack registered (GHSound) that no state plays itself already (the roar would sound twice).</summary>
        private static void AddScript(GameObject go, string type, JObject fields, Set set, HashSet<string> stateAudio)
        {
            if (type == "SFXOnEnable")
            {
                string ev = (string)fields["m_AudioEvent"];
                if (string.IsNullOrEmpty(ev) || !GHSound.Playable(ev) || stateAudio.Contains(ev)) return;
            }
            Type t = AccessTools.TypeByName(type);
            if (t == null || !typeof(Component).IsAssignableFrom(t)) { if (Reported.Add("type " + type)) Plugin.Log.LogWarning($"Gloomhaven effects: Frosthaven has no {type} script"); return; }
            if (typeof(EffectOnEnable).IsAssignableFrom(t) && go.GetComponent<EffectOnEnable>() != null) return;
            Component c = go.AddComponent(t);
            foreach (JProperty f in fields.Properties())
            {
                FieldInfo field = AccessTools.Field(t, f.Name);
                if (field == null) continue;
                try
                {
                    object value = Convert(field.FieldType, f.Value, set);
                    if (value != null || !field.FieldType.IsValueType) field.SetValue(c, value);
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"Gloomhaven effects: {type}.{f.Name} not set: {ex.Message}"); }
            }
        }

        /// <summary>A recorded field value (a type tree with references as names) as the field's type.</summary>
        private static object Convert(Type type, JToken j, Set set)
        {
            if (j == null || j.Type == JTokenType.Null) return null;
            if (type == typeof(float)) return (float)j;
            if (type == typeof(double)) return (double)j;
            if (type == typeof(int)) return (int)j;
            if (type == typeof(uint)) return (uint)j;
            if (type == typeof(bool)) return j.Type == JTokenType.Boolean ? (bool)j : (int)j != 0;
            if (type == typeof(string)) return (string)j;
            if (type.IsEnum) return Enum.ToObject(type, j.Type == JTokenType.String ? (object)Enum.Parse(type, (string)j) : (int)j);
            if (type == typeof(Color) && j is JObject c) return new Color((float?)c["r"] ?? 0f, (float?)c["g"] ?? 0f, (float?)c["b"] ?? 0f, (float?)c["a"] ?? 1f);
            if (type == typeof(Vector2) && j is JObject v2) return new Vector2((float?)v2["x"] ?? 0f, (float?)v2["y"] ?? 0f);
            if (type == typeof(Vector3) && j is JObject v3) return new Vector3((float?)v3["x"] ?? 0f, (float?)v3["y"] ?? 0f, (float?)v3["z"] ?? 0f);
            if (type == typeof(Vector4) && j is JObject v4) return new Vector4((float?)v4["x"] ?? 0f, (float?)v4["y"] ?? 0f, (float?)v4["z"] ?? 0f, (float?)v4["w"] ?? 0f);
            if (type == typeof(Quaternion) && j is JObject q) return new Quaternion((float?)q["x"] ?? 0f, (float?)q["y"] ?? 0f, (float?)q["z"] ?? 0f, (float?)q["w"] ?? 1f);
            if (type == typeof(AnimationCurve) && j is JObject cv) return Curve(cv);
            if (type == typeof(GameObject) && j.Type == JTokenType.String) return set.Templates.TryGetValue((string)j, out GameObject t) ? t : null;
            return null;   // other references (a material, a light): left at the component's default
        }

        private static AnimationCurve Curve(JObject j)
        {
            var keys = new List<Keyframe>();
            foreach (JObject k in j["m_Curve"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                var key = new Keyframe((float?)k["time"] ?? 0f, (float?)k["value"] ?? 0f, (float?)k["inSlope"] ?? 0f, (float?)k["outSlope"] ?? 0f,
                                       (float?)k["inWeight"] ?? 0f, (float?)k["outWeight"] ?? 0f) { weightedMode = (WeightedMode)((int?)k["weightedMode"] ?? 0) };
                keys.Add(key);
            }
            // Unity stores a curve's ends as 2 for "clamp" (the usual), anything else loops
            return new AnimationCurve(keys.ToArray())
            {
                preWrapMode = ((int?)j["m_PreInfinity"] ?? 2) == 2 ? WrapMode.ClampForever : WrapMode.Loop,
                postWrapMode = ((int?)j["m_PostInfinity"] ?? 2) == 2 ? WrapMode.ClampForever : WrapMode.Loop,
            };
        }
    }
}
