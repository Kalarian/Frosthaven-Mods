using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EPOOutline;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ScenarioRuleLibrary;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven's own 3D model for a ported class (stage 2 of GHClasses). The content pack ships the class's hero
    /// bundle from Gloomhaven (packs\&lt;name&gt;\bundles\, copied by tools/gh_model.py) and a "model" entry in pack.json
    /// saying what is in it. A Gloomhaven hero is built like a Frosthaven one, a bare wrapper object with a
    /// CharacterManager and a model child carrying the Animator, colliders, skinned meshes and a few helper scripts,
    /// but every script on it names GH.Runtime.dll, which this game doesn't have (they load as empty "missing script"
    /// slots), its animator controller's state behaviours are those scripts too (so nothing would ever tell the
    /// Choreographer an ability animation is done), and its materials sit on a shader Frosthaven's renderer can't draw.
    /// So the model is adopted rather than used as is. Once per class, a template is built under an inactive, scene-
    /// independent holder:
    ///   - the visual: the model's Animator object out of the bundle, with Frosthaven's own helper components added
    ///     where the game looks for them (VFXLookup, DeathDissolve, Outlinable, FootstepSound, IdleAnimParams), its
    ///     LOD switching removed (the script that drove it is gone), its materials rebuilt (GHModelMaterials);
    ///   - its animator: an override controller on the Drifter's, keeping her states, parameters, transitions and
    ///     every state behaviour the game's turn logic waits on, with each of her clips replaced by the Gloomhaven clip
    ///     tools/gh_model.py matched (both rigs are generic and share no bone names, so clips can't be retargeted, only
    ///     swapped); a tiny stand-in object named after her arrow lets the ranged-attack behaviour run its course;
    ///   - the carrier: a copy of the Drifter's wrapper (CharacterManager + the editor object the Choreographer needs on
    ///     the root), pointed at the visual, with the model's target bone, height and hit radius and the Gloomhaven
    ///     weapons re-equipped on their bones with Frosthaven's Weapon component (the sword named after the Drifter's
    ///     club, so her melee states' impact sounds find it).
    /// The template is what GetCharacterPrefabFromBundle hands back for the class: the game's object pool clones it
    /// like a prefab (an active child of an inactive parent clones active), and every path (class screen, scenario,
    /// scenario results) goes through that one call. The Drifter's bundle is unloaded between scenes, so nothing of
    /// hers is kept across calls except by copy; the override's base controller is rebuilt when hers has gone.
    /// Each spawned clone gets its state-behaviour timings scaled to the new clip lengths (InitialiseCharacter postfix).
    /// Inert without a "model" entry: the Drifter stand-in in GHClasses stays. Visual only: a co-op peer without the
    /// bundle sees the Drifter's model; nothing of this is replicated.
    /// </summary>
    [HarmonyPatch]
    internal static class GHModel
    {
        // ------------------------------------------------------------------ the pack's "model" entry

        internal sealed class WeaponSpec { public string List, Bone, Root; public Weapon.WeaponTypes Type; }

        /// <summary>One Gloomhaven material: its texture names, and whether its mask texture (metallic / smoothness /
        /// occlusion) is used or replaced by flat values (tools/gh_model.py decides: the Brute's mask would blacken him).</summary>
        internal sealed class MaterialSpec
        {
            public string Albedo, Mask, Normal, Emissive;
            public bool Cutout, FlatMask, Hidden;   // Hidden: a distortion shell Frosthaven can't draw (pack "hidden")
            public float Cutoff = 0.5f, EmissiveBoost = 1f, Metallic, Smoothness = 0.3f, Ao = 1f, Brightness = 1f;
            public Color? EmissiveTint;   // Gloomhaven's _GlowColor over a white glow map (pack "emissive_tint")
        }

        /// <summary>How one of the Drifter's animator states is retimed for the clip that replaces hers: the behaviours'
        /// moments (impact, progress, projectile release) scaled by the clip-length ratio, or Gloomhaven's own value for
        /// that beat when its state had one.</summary>
        internal sealed class StateTiming { public float Scale = 1f; public float? FhFire, GhHit, GhProgress, GhFire; public string Audio; public GHVfx.Play Effects; }

        internal sealed class AnimationSpec
        {
            public string Base = "Drifter";
            public Dictionary<string, string> Clips = new Dictionary<string, string>(StringComparer.Ordinal);      // Drifter clip -> Gloomhaven clip
            public Dictionary<string, StateTiming> States = new Dictionary<string, StateTiming>(StringComparer.Ordinal);  // "Base Layer.Attack" -> timing
            public string[] Projectiles = new string[0];                                                           // "Drifter_arrow": child names her ranged states look for
            public string MeleeWeapon;                                                                             // "Drifter_club": the name her melee states' impact sounds look for
            public HashSet<string> Silence = new HashSet<string>(StringComparer.Ordinal);                          // her voice events, blanked
            public Dictionary<string, string> StateClips = new Dictionary<string, string>(StringComparer.Ordinal); // her state -> the clip it plays
            public Dictionary<string, string> GhStates = new Dictionary<string, string>(StringComparer.Ordinal);   // Gloomhaven-only state -> its clip
            public Dictionary<string, string> GhAudio = new Dictionary<string, string>(StringComparer.Ordinal);    // Gloomhaven-only state -> its sound event
            public Dictionary<string, GHVfx.Play> GhEffects = new Dictionary<string, GHVfx.Play>(StringComparer.Ordinal);   // Gloomhaven-only state -> its effects
            public Dictionary<string, Lent> LentStates = new Dictionary<string, Lent>(StringComparer.Ordinal);         // her state whose own Gloomhaven clip lost the slot -> what to lend it
        }

        /// <summary>What a state of hers is lent for one play when its own Gloomhaven clip went to another state that
        /// shares her clip (the radial attack shares the heavy swing's): the clip, its sound, its effects.</summary>
        internal sealed class Lent { public string Clip, Audio; public GHVfx.Play Effects; }

        internal sealed class Entry
        {
            public ECharacter Model;
            public string Folder, Bundle, BundleName, Prefab, HeroPrefab, AnimatorObject, BodyMesh, TargetBone, ProjectileAnchor;
            public float Height = 2f, HitRadius = 0.1f;
            public string[] TrailPoints = new string[0];
            public string[] Feet = new string[0];                                                  // the bones the footstep dust spawns on: left, right
            public List<WeaponSpec> Weapons = new List<WeaponSpec>();
            public Dictionary<string, MaterialSpec> Materials = new Dictionary<string, MaterialSpec>(StringComparer.Ordinal);
            public AnimationSpec Animation = new AnimationSpec();
            public string Skin;                                                                    // null: the base look
            public Dictionary<string, Entry> Skins = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);   // "ALT" -> that look
            public Dictionary<string, GHVfx.EffectSpec> Effects = new Dictionary<string, GHVfx.EffectSpec>(StringComparer.Ordinal);          // its ability effects (GHVfx)
            public Dictionary<string, GHVfx.MaterialSpec> EffectMaterials = new Dictionary<string, GHVfx.MaterialSpec>(StringComparer.Ordinal);
            public GHVfx.EffectSpec HeroParticles;   // the model's own particles (pack "hero_particles"), dressed like an effect
            public List<GHVfx.BundleSpec> EffectBundles = new List<GHVfx.BundleSpec>();

            public static Entry Parse(ECharacter model, JObject j, string folder, string skin = null)
            {
                var e = new Entry
                {
                    Model = model, Folder = folder,
                    Bundle = (string)j["bundle"], BundleName = (string)j["bundle_name"], Prefab = (string)j["prefab"], HeroPrefab = (string)j["hero_prefab"],
                    AnimatorObject = (string)j["animator"], BodyMesh = (string)j["body_mesh"],
                    TargetBone = (string)j["target_bone"], ProjectileAnchor = (string)j["projectile_anchor"],
                    Height = (float?)j["height"] ?? 2f, HitRadius = (float?)j["hit_radius"] ?? 0.1f,
                    TrailPoints = Strings(j["trail_points"]), Feet = Strings(j["feet"]),
                    Effects = GHVfx.ParseEffects(j["effects"]), EffectMaterials = GHVfx.ParseMaterials(j["effect_materials"]), EffectBundles = GHVfx.ParseBundles(j["effect_bundles"]),
                };
                if (j["hero_particles"] is JObject hp)
                    e.HeroParticles = GHVfx.ParseEffects(new JObject { ["hero"] = hp }).TryGetValue("hero", out GHVfx.EffectSpec hs) ? hs : null;
                if (string.IsNullOrEmpty(e.Bundle) || string.IsNullOrEmpty(e.Prefab)) throw new FormatException("no bundle or prefab");
                foreach (JObject w in j["weapons"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                    e.Weapons.Add(new WeaponSpec
                    {
                        List = (string)w["list"], Bone = (string)w["bone"], Root = (string)w["root"],
                        Type = Enum.TryParse((string)w["type"], true, out Weapon.WeaponTypes t) ? t : Weapon.WeaponTypes.Cosmetic,
                    });
                foreach (JProperty m in (j["materials"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    var v = m.Value as JObject;
                    if (v == null) continue;
                    e.Materials[m.Name] = new MaterialSpec
                    {
                        Albedo = (string)v["albedo"], Mask = (string)v["mask"], Normal = (string)v["normal"], Emissive = (string)v["emissive"],
                        Cutout = (bool?)v["cutout"] ?? false, Cutoff = (float?)v["cutoff"] ?? 0.5f, EmissiveBoost = (float?)v["emissive_boost"] ?? 1f,
                        FlatMask = (string)v["mask_mode"] == "flat",
                        Metallic = (float?)v["metallic"] ?? 0f, Smoothness = (float?)v["smoothness"] ?? 0.3f, Ao = (float?)v["ao"] ?? 1f,
                        Brightness = (float?)v["brightness"] ?? 1f,
                        Hidden = (bool?)v["hidden"] ?? false,
                        EmissiveTint = v["emissive_tint"] is JArray et && et.Count >= 3 ? new Color((float)et[0], (float)et[1], (float)et[2], 1f) : (Color?)null,
                    };
                }
                if (j["animation"] is JObject a)
                {
                    e.Animation.Base = (string)a["base"] ?? "Drifter";
                    foreach (JProperty c in (a["clips"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) e.Animation.Clips[c.Name] = (string)c.Value;
                    foreach (JProperty s in (a["states"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    {
                        var v = s.Value as JObject;
                        if (v == null) continue;
                        e.Animation.States[s.Name] = new StateTiming
                        {
                            Scale = (float?)v["scale"] ?? 1f, FhFire = (float?)v["fh"]?["fire"],
                            GhHit = (float?)v["gh"]?["hit"], GhProgress = (float?)v["gh"]?["progress"], GhFire = (float?)v["gh"]?["fire"],
                            Audio = (string)v["audio"], Effects = GHVfx.ParsePlay(v["effects"]),
                        };
                    }
                    e.Animation.Projectiles = Strings(a["projectiles"]);
                    e.Animation.MeleeWeapon = (string)a["melee_weapon"];
                    foreach (string s in Strings(a["silence"])) e.Animation.Silence.Add(s);
                    foreach (JProperty c in (a["state_clips"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) e.Animation.StateClips[c.Name] = (string)c.Value;
                    foreach (JProperty c in (a["gh_states"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) e.Animation.GhStates[c.Name] = (string)c.Value;
                    foreach (JProperty c in (a["gh_audio"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) e.Animation.GhAudio[c.Name] = (string)c.Value;
                    foreach (JProperty c in (a["gh_effects"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    {
                        GHVfx.Play play = GHVfx.ParsePlay(c.Value);
                        if (play != null) e.Animation.GhEffects[c.Name] = play;
                    }
                    foreach (JProperty c in (a["lent_states"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                        if (c.Value is JObject l && !string.IsNullOrEmpty((string)l["clip"]))
                            e.Animation.LentStates[c.Name] = new Lent { Clip = (string)l["clip"], Audio = (string)l["audio"], Effects = GHVfx.ParsePlay(l["effects"]) };
                }
                e.Skin = skin;
                // the class's alternative looks: the base entry with what the skin changes (its prefabs, meshes, materials)
                if (skin == null && j["skins"] is JObject skins)
                    foreach (JProperty sk in skins.Properties())
                    {
                        if (!(sk.Value is JObject changes)) continue;
                        var merged = (JObject)j.DeepClone();
                        merged.Remove("skins");
                        foreach (JProperty c in changes.Properties()) merged[c.Name] = c.Value.DeepClone();
                        try { e.Skins[sk.Name] = Parse(model, merged, folder, sk.Name); }
                        catch (Exception ex) { Plugin.Log.LogWarning($"Gloomhaven model: {model}'s {sk.Name} look is unusable: {ex.Message}"); }
                    }
                return e;
            }

            private static string[] Strings(JToken t) => t?.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToArray() ?? new string[0];
        }

        private static readonly Dictionary<ECharacter, Entry> Entries = new Dictionary<ECharacter, Entry>();   // null: the pack brings no model

        /// <summary>The pack's "model" entry for <paramref name="model"/>, or null (pack.json: "model": {"Brute": {...}}).</summary>
        internal static Entry EntryFor(ECharacter model)
        {
            if (Entries.TryGetValue(model, out Entry cached)) return cached;
            Entry e = null;
            GHClasses.Pack pack = GHClasses.PackOf(model);
            if (pack != null)
            {
                try
                {
                    JObject j = JObject.Parse(File.ReadAllText(Path.Combine(pack.Folder, "pack.json")));
                    var m = (j["model"] as JObject)?.Properties().FirstOrDefault(p => string.Equals(p.Name, model.ToString(), StringComparison.OrdinalIgnoreCase))?.Value as JObject;
                    if (m != null) e = Entry.Parse(model, m, pack.Folder);
                }
                catch (Exception ex) { Plugin.Log.LogError($"Gloomhaven model: bad model entry for {model} in {pack}: {ex.Message}"); }
            }
            Entries[model] = e;
            return e;
        }

        // ------------------------------------------------------------------ the templates

        private sealed class Built
        {
            public Entry Entry;
            public Loaded Bundle;
            public GameObject Carrier, Visual;
            public AnimatorOverrideController Override;
        }

        private static readonly Dictionary<string, Built> Templates = new Dictionary<string, Built>(StringComparer.OrdinalIgnoreCase);   // "Brute", "Brute_ALT"
        private static readonly HashSet<string> Failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static GameObject _holder, _weaponSounds;

        /// <summary>The templates' parent: inactive and kept across scenes, so nothing here updates or renders while the
        /// children stay activeSelf and their pooled copies come out active, like a prefab's.</summary>
        internal static Transform Holder
        {
            get
            {
                if (_holder != null) return _holder.transform;
                _holder = new GameObject("Gloomhaven models");
                _holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
                return _holder.transform;
            }
        }

        /// <summary>The hero template for <paramref name="model"/>: built on first use, refreshed on each (the Drifter's
        /// controller it overrides dies with her bundle between scenes). Null when the pack brings no model or the build
        /// failed (logged once): the caller falls back to the Drifter stand-in.</summary>
        internal static GameObject Template(ECharacter model, string skin = null)
        {
            Entry e = EntryFor(model);
            if (e == null) return null;
            if (!string.IsNullOrEmpty(skin) && e.Skins.TryGetValue(skin, out Entry look)) e = look;   // an unknown skin: the base look
            string key = e.Skin == null ? model.ToString() : model + "_" + e.Skin;
            if (Failed.Contains(key)) return null;
            if (Templates.TryGetValue(key, out Built b) && b.Carrier != null && b.Visual != null)
            {
                try { Refresh(b); }
                catch (Exception ex) { if (Reported.Add("refresh " + model)) Plugin.Log.LogWarning($"Gloomhaven model: could not refresh the {model}'s animator: {ex}"); }
                return b.Carrier;
            }
            try
            {
                b = Build(e);
                if (b == null) return null;           // the Drifter's prefab wasn't there this time: try again on the next call
                Templates[key] = b;
                return b.Carrier;
            }
            catch (Exception ex)
            {
                Failed.Add(key);
                Plugin.Log.LogError($"Gloomhaven model: could not build the {key} model, keeping the {GHClasses.StandInModel}'s: {ex}");
                return null;
            }
        }

        /// <summary>The Drifter's hero prefab as the game hands it out (her bundle loaded on demand), or null.</summary>
        private static GameObject DrifterPrefab(AnimationSpec anim) =>
            AssetBundleManager.Instance?.GetCharacterPrefabFromBundle(CActor.EType.Player, anim?.Base ?? GHClasses.StandInModel);

        private static Built Build(Entry e)
        {
            Loaded bundle = Open(e);
            GameObject ghPrefab = bundle.Bundle.LoadAsset<GameObject>(e.Prefab);
            if (ghPrefab == null) throw new InvalidOperationException(e.Prefab + " is not in " + e.Bundle);
            GameObject drifter = DrifterPrefab(e.Animation);
            if (drifter == null)
            {
                if (Reported.Add("no base " + e.Model)) Plugin.Log.LogWarning($"Gloomhaven model: the {e.Animation.Base}'s hero prefab is not loaded yet; the {e.Model} waits for the next request");
                return null;
            }
            CharacterManager drifterCm = drifter.GetComponent<CharacterManager>();
            GameObject drifterVisual = drifterCm?.CharacterChildPrefab;
            if (drifterVisual == null) throw new InvalidOperationException("the " + e.Animation.Base + "'s hero prefab has no model");

            // 1. the visual: the model's Animator object, a direct child of the carrier once spawned, like HE_Drifter_PR
            GameObject wrapper = UnityEngine.Object.Instantiate(ghPrefab, Holder);
            Transform animatorTr = !string.IsNullOrEmpty(e.AnimatorObject) ? Find(wrapper.transform, e.AnimatorObject) : null;
            if (animatorTr == null) animatorTr = wrapper.GetComponentInChildren<Animator>(true)?.transform;
            if (animatorTr == null) throw new InvalidOperationException("no Animator under " + ghPrefab.name);
            GameObject visual = animatorTr.gameObject;
            if (visual != wrapper)
            {
                visual.transform.SetParent(Holder, false);
                UnityEngine.Object.DestroyImmediate(wrapper);
            }
            visual.name = "HE_" + e.Model;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            SetLayer(visual.transform, LayerMask.NameToLayer("Hero"));   // the Choreographer only accepts an Animator on the Hero or Monster layer
            DropLods(visual);
            SkinnedMeshRenderer[] skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0) throw new InvalidOperationException("no skinned meshes under " + ghPrefab.name);
            SkinnedMeshRenderer body = skins.FirstOrDefault(s => s.name == e.BodyMesh) ?? skins.OrderByDescending(s => s.bones.Length).First();

            // 2. materials, on Frosthaven's shaders
            Material drifterBody = drifterVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.shader != null && m.shader.name == GHModelMaterials.OpaqueShader);
            var materials = new MaterialSet(e, bundle, drifterBody);
            materials.Apply(visual);

            // 3. Frosthaven's helpers, on the Animator's object where its code looks for them
            Animator animator = visual.GetComponent<Animator>();
            bundle.AddClips(animator.runtimeAnimatorController);            // the Gloomhaven controller's clips: the ones the pack's table names
            var vfx = visual.AddComponent<VFXLookup>();                     // RequireComponent adds the AnimFXTrigger with it
            vfx.CharacterRenderers = skins.Cast<Renderer>().ToList();
            vfx.CharacterBodyMesh = body;
            vfx.TrailPoints = e.TrailPoints.Select(n => Find(visual.transform, n)).Where(t => t != null).ToList();
            if (visual.GetComponent<AnimFXTrigger>() == null) visual.AddComponent<AnimFXTrigger>();
            var dissolve = visual.AddComponent<DeathDissolve>();            // the Choreographer calls it without a null check; its Cleanup is what takes an exhausted hero off the map
            DeathDissolve like = drifterVisual.GetComponent<DeathDissolve>();
            dissolve.Particles = DissolveParticles(like);
            dissolve.ShowParticleEffect = dissolve.Particles != null;
            dissolve.DissolveLifeTime = like != null ? like.DissolveLifeTime : 2.28f;
            dissolve.DissolveInitialDelay = like != null ? like.DissolveInitialDelay : 1.08f;
            if (like != null) { dissolve.dissolveCurve = like.dissolveCurve; dissolve.AnimStrength = like.AnimStrength; }
            dissolve.CharacterMesh = body;
            var outline = visual.AddComponent<Outlinable>();                // the hover / selection outline is a component, not a material property
            outline.RenderStyle = RenderStyle.Single;
            outline.AddAllChildRenderersToRenderingList(RenderersAddingMode.SkinnedMeshRenderer, "_Albedo");
            outline.OutlineParameters.Enabled = true;
            var idle = visual.AddComponent<IdleAnimParams>();
            IdleAnimParams idleLike = drifterVisual.GetComponent<IdleAnimParams>();
            if (idleLike != null) { idle.MinIdleCyclesToRare = idleLike.MinIdleCyclesToRare; idle.MaxIdleCyclesToRare = idleLike.MaxIdleCyclesToRare; }
            var feet = visual.AddComponent<FootstepSound>();                // receives the run clip's footstep events: the Drifter's step sounds and her footstep dust
            CopyFields(drifterVisual.GetComponent<FootstepSound>(), feet, FootstepFields);
            SetFeet(feet, visual.transform, e);
            foreach (string projectile in e.Animation.Projectiles)
            {
                // the Drifter's ranged states fire a child of this name: an empty one lets them run (the trail flies, the hit lands)
                if (Find(visual.transform, projectile) != null) continue;
                Transform anchor = (!string.IsNullOrEmpty(e.ProjectileAnchor) ? Find(visual.transform, e.ProjectileAnchor) : null) ?? visual.transform;
                new GameObject(projectile).transform.SetParent(anchor, false);
            }

            // 4. the animator: the Drifter's controller with the Gloomhaven clips
            var built = new Built { Entry = e, Bundle = bundle, Visual = visual };
            built.Override = MakeOverride(built, drifterVisual.GetComponent<Animator>()?.runtimeAnimatorController);
            animator.runtimeAnimatorController = built.Override;
            Plugin.Log.LogInfo($"Gloomhaven model: {e.Model} animator lists {string.Join(", ", built.Override.animationClips.Where(c => c != null).Select(c => c.name).Distinct().Take(6))}...");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;        // Frosthaven's heroes; the game samples clips on the object
            animator.applyRootMotion = false;                                // the scenario forces this off itself; the class screen does not

            // 5. the carrier: a wrapper like the Drifter's, pointed at the visual. Built with AddComponent and the Drifter's
            // field values, never as an Instantiate copy of her prefab: a copy's components keep pointing at the script
            // stubs inside her bundle, and when the game unloads that bundle after a scenario they turn into "missing
            // script" slots, so the next clone has no CharacterManager at all (a black screen on the character view).
            var carrier = new GameObject(e.Model.ToString());                // pool container "Brute Pool", clones "Brute(Clone)"
            carrier.transform.SetParent(Holder, false);
            carrier.layer = drifter.layer;
            CopyComponent<UnityGameEditorObject>(drifter, carrier);          // the Choreographer dereferences it on the root
            CharacterManager cm = CopyComponent<CharacterManager>(drifter, carrier);
            cm.CharacterChildPrefab = visual;
            cm.CharacterMenuModelPrefab = null;
            if (!string.IsNullOrEmpty(e.TargetBone)) cm.TargetBone = e.TargetBone;
            cm.Height = e.Height;
            cm.HitRadius = e.HitRadius;
            cm.m_SurfaceType = CharacterManager.SurfaceTypes.Flesh;
            cm.StandardHitEffect = cm.ShieldHitEffect = cm.CriticalHitEffect = null;   // the game's defaults
            cm.WeaponLists = Weapons(e, bundle, materials, drifterCm);
            built.Carrier = carrier;
            GHVfx.Prepare(e, bundle);                                        // his ability effects (never fails the model)
            GHVfx.DressHero(e, visual);                                      // his own particles, on the effects' rebuilt materials
            Plugin.Log.LogInfo($"Gloomhaven model: {e.Model} built from {e.Bundle} ({skins.Length} meshes, {materials.Count} materials, " +
                               $"{cm.WeaponLists.Count} weapons, height {e.Height}; {built.Override.name})");
            return built;
        }

        /// <summary>Re-points what the template borrows from the Drifter's bundle, which the game unloads between scenes:
        /// the base of the override controller (its clips are the pack's and survive). The override object itself is
        /// kept and re-based in place, so pooled clones from an earlier scene that still hold it are mended too.</summary>
        private static void Refresh(Built b)
        {
            Animator animator = b.Visual.GetComponent<Animator>();
            if (animator == null) return;
            bool rebase = !(b.Override != null && b.Override.runtimeAnimatorController != null && animator.runtimeAnimatorController == b.Override);
            FootstepSound feet = b.Visual.GetComponent<FootstepSound>();
            bool dust = DustGone(feet);   // her footstep dust prefab went with her bundle too
            if (!rebase && !dust) return;
            GameObject drifter = DrifterPrefab(b.Entry.Animation);
            GameObject drifterVisual = drifter?.GetComponent<CharacterManager>()?.CharacterChildPrefab;
            if (dust && drifterVisual != null) CopyFields(drifterVisual.GetComponent<FootstepSound>(), feet, new[] { FootstepDust });
            if (!rebase) return;
            RuntimeAnimatorController baseController = drifterVisual?.GetComponent<Animator>()?.runtimeAnimatorController;
            if (baseController == null)
            {
                if (Reported.Add("no base " + b.Entry.Model)) Plugin.Log.LogWarning($"Gloomhaven model: the {b.Entry.Animation.Base}'s animator is not loaded; the {b.Entry.Model} keeps its old one");
                return;
            }
            b.Override = MakeOverride(b, baseController, b.Override);
            animator.runtimeAnimatorController = b.Override;
            DeathDissolve dissolve = b.Visual.GetComponent<DeathDissolve>();
            if (dissolve != null && dissolve.Particles == null) dissolve.Particles = DissolveParticles(drifter.GetComponent<CharacterManager>()?.CharacterChildPrefab?.GetComponent<DeathDissolve>());
            Plugin.Log.LogInfo($"Gloomhaven model: {b.Entry.Model} animator re-based on the reloaded {baseController.name}");
        }

        /// <summary>The death dissolve particles: the game's default (always loaded, with the children DeathDissolve.Play
        /// needs), else the Drifter's.</summary>
        private static GameObject DissolveParticles(DeathDissolve like)
        {
            GameObject fx = null;
            try { fx = GlobalSettings.Instance?.m_GlobalParticles?.DefaultDeathDissolve; } catch (Exception) { fx = null; }
            return fx != null ? fx : like?.Particles;
        }

        /// <summary>The Drifter's controller with each of her clips replaced by the pack's clip for it; a clip the pack
        /// doesn't name gets the one most of hers got (the idle), never nothing: an un-overridden clip binds bones the
        /// Gloomhaven rig lacks and would freeze it. Given an existing override, re-bases it in place (same object).</summary>
        private static AnimatorOverrideController MakeOverride(Built b, RuntimeAnimatorController baseController, AnimatorOverrideController existing = null)
        {
            if (baseController == null) throw new InvalidOperationException("no base animator controller");
            AnimationSpec anim = b.Entry.Animation;
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            var uses = new Dictionary<AnimationClip, List<AnimationClip>>();
            var missing = new List<string>();
            AnimationClip fallback = anim.Clips.Values.GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => b.Bundle.Clip(g.Key)).FirstOrDefault(c => c != null)
                                     ?? b.Bundle.Clips.Values.FirstOrDefault();
            foreach (AnimationClip original in baseController.animationClips.Where(c => c != null).Distinct())
            {
                AnimationClip replacement = anim.Clips.TryGetValue(original.name, out string wanted) ? b.Bundle.Clip(wanted) : null;
                if (replacement == null) { missing.Add(original.name); replacement = fallback; }
                if (replacement == null) continue;
                pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(original, replacement));
                if (!uses.TryGetValue(replacement, out List<AnimationClip> list)) uses[replacement] = list = new List<AnimationClip>();
                list.Add(original);
            }
            // ActorBehaviour measures movement speeds from the clips' NAMES (a jump loop ends in "JumpLoop", a run clip
            // contains "Run" or ends in "MoveLoop"): a Gloomhaven clip that stands in for one of those is named so too,
            // or its one-frame jump pose would be measured as the run.
            foreach (KeyValuePair<AnimationClip, List<AnimationClip>> u in uses)
            {
                if (u.Value.All(o => o.name.EndsWith("JumpLoop", StringComparison.Ordinal)) && !u.Key.name.EndsWith("JumpLoop", StringComparison.Ordinal))
                    u.Key.name += "_JumpLoop";
                else if (u.Value.All(o => IsRunClip(o.name)) && !IsRunClip(u.Key.name))
                    u.Key.name += "_MoveLoop";
            }
            AnimatorOverrideController ovr = existing;
            if (ovr != null) ovr.runtimeAnimatorController = baseController;
            else ovr = new AnimatorOverrideController(baseController) { name = baseController.name + " (" + b.Entry.Model + ")", hideFlags = HideFlags.DontUnloadUnusedAsset };
            ovr.ApplyOverrides(pairs);
            if (missing.Count > 0) Plugin.Log.LogWarning($"Gloomhaven model: {b.Entry.Model}: no clip for {string.Join(", ", missing)}; they idle");
            return ovr;
        }

        private static bool IsRunClip(string name) =>
            name.Contains("Run") || name.Contains("moveForward") || name.ToLower().Contains("_move_") || name.EndsWith("MoveLoop") || name.EndsWith("Move") || name.EndsWith("_move");

        /// <summary>The Gloomhaven weapons re-equipped: a copy of each weapon root with Frosthaven's Weapon component (the
        /// game's DoEquip refuses one without), rebuilt materials and the Drifter's hit sounds. The melee weapon takes
        /// the name her melee states look up for their impact effects.</summary>
        private static List<CharacterManager.CharacterWeaponList> Weapons(Entry e, Loaded bundle, MaterialSet materials, CharacterManager drifterCm)
        {
            var lists = new List<CharacterManager.CharacterWeaponList>();
            if (e.Weapons.Count == 0) return lists;
            GameObject sounds = WeaponSounds(drifterCm);
            Dictionary<string, GameObject> roots = bundle.Roots();   // a local lookup: nothing keeps the bundle's other prefabs alive afterwards
            foreach (WeaponSpec w in e.Weapons)
            {
                if (w.Root == null || !roots.TryGetValue(w.Root, out GameObject root)) { Plugin.Log.LogWarning($"Gloomhaven model: weapon {w.Root} is not in {e.Bundle}, skipped"); continue; }
                if (string.IsNullOrEmpty(w.Bone)) continue;
                GameObject weapon = UnityEngine.Object.Instantiate(root, Holder);
                weapon.name = w.Type == Weapon.WeaponTypes.Melee && sounds != null && !string.IsNullOrEmpty(e.Animation.MeleeWeapon) ? e.Animation.MeleeWeapon : root.name;
                foreach (Animator a in weapon.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(a);   // no controller; only confuses the lookups
                var fh = weapon.AddComponent<Weapon>();
                fh.WeaponType = w.Type;
                fh.OnHitAudioBucketList = sounds;
                fh.MeleeHitEffect = null;
                materials.Apply(weapon);
                lists.Add(new CharacterManager.CharacterWeaponList { name = w.List, AttachToBone = w.Bone, Weapons = new List<GameObject> { weapon } });
            }
            return lists;
        }

        /// <summary>Our own copy of the Drifter's weapon hit sounds (an SFXBucketList of surface -> audio event names):
        /// hers live in her bundle and die with it, this one lives under the holder. Null if she has none loaded.</summary>
        private static GameObject WeaponSounds(CharacterManager drifterCm)
        {
            if (_weaponSounds != null) return _weaponSounds;
            SFXBucketList like = drifterCm?.WeaponLists?.SelectMany(l => l.Weapons).Where(g => g != null)
                .Select(g => g.GetComponent<Weapon>()?.OnHitAudioBucketList?.GetComponent<SFXBucketList>()).FirstOrDefault(l => l != null && l.m_AudioBuckets != null);
            if (like == null) return null;
            var go = new GameObject("Gloomhaven weapon hit sounds");
            go.transform.SetParent(Holder, false);
            var list = go.AddComponent<SFXBucketList>();
            list.m_AudioBuckets = new List<GameObject>();
            foreach (GameObject bucketGo in like.m_AudioBuckets)
            {
                SFXBucket bucket = bucketGo?.GetComponent<SFXBucket>();
                if (bucket?.m_SFXBucket == null) continue;
                var copy = new GameObject(bucketGo.name);
                copy.transform.SetParent(go.transform, false);
                copy.AddComponent<SFXBucket>().m_SFXBucket = new SFXBucket.AudioBucket { TargetSurface = bucket.m_SFXBucket.TargetSurface, AudioEvent = bucket.m_SFXBucket.AudioEvent };
                list.m_AudioBuckets.Add(copy);
            }
            return _weaponSounds = go;
        }

        /// <summary>Removes the LOD switching: Gloomhaven's AutomaticLOD drove it and is gone, so the full mesh stays and
        /// the lower levels (and the empty placeholder meshes) go.</summary>
        private static void DropLods(GameObject visual)
        {
            foreach (LODGroup group in visual.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null && r.gameObject != visual && r.gameObject != group.gameObject) UnityEngine.Object.DestroyImmediate(r.gameObject);
                UnityEngine.Object.DestroyImmediate(group);
            }
            foreach (SkinnedMeshRenderer r in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r != null && (r.sharedMesh == null || r.sharedMesh.vertexCount == 0) && r.gameObject != visual && r.transform.childCount == 0)
                    UnityEngine.Object.DestroyImmediate(r.gameObject);
        }

        // ------------------------------------------------------------------ the bundle

        /// <summary>One opened Gloomhaven bundle and its assets by name. Only the two prefabs have addresses in an
        /// Addressables bundle (LoadAllAssets returns just those), so clips, textures and the weapon objects are reached
        /// through what loading the prefabs brought into memory: the controller's clip list, the materials' textures,
        /// and the hero prefab's weapon roots (found among all loaded objects by name).</summary>
        internal sealed class Loaded
        {
            public AssetBundle Bundle;
            public string HeroPrefab;
            public readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);

            public void AddClips(RuntimeAnimatorController controller)
            {
                if (controller == null) return;
                foreach (AnimationClip c in controller.animationClips) if (c != null && !Clips.ContainsKey(c.name)) Clips[c.name] = c;
            }

            public AnimationClip Clip(string name)
            {
                if (name == null) return null;
                if (Clips.TryGetValue(name, out AnimationClip c)) return c;
                c = Resources.FindObjectsOfTypeAll<AnimationClip>().FirstOrDefault(x => x != null && x.name == name);
                if (c != null) Clips[name] = c;
                return c;
            }

            /// <summary>A texture loaded with the prefab, by name (an asset, not a scene object).</summary>
            public Texture2D Texture(string name) =>
                name == null ? null : Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(t => t != null && t.name == name);

            /// <summary>The loaded prefab roots by name, after loading the hero prefab (which brings its weapons along).
            /// Not kept: the game's Resources.UnloadUnusedAssets at the next loading screen then frees what the templates
            /// don't use (the Gloomhaven effects, the lower LODs, the unused textures).</summary>
            public Dictionary<string, GameObject> Roots()
            {
                var roots = new Dictionary<string, GameObject>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(HeroPrefab)) Bundle.LoadAsset<GameObject>(HeroPrefab);
                foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (g != null && g.transform.parent == null && !g.scene.IsValid() && !roots.ContainsKey(g.name)) roots[g.name] = g;
                return roots;
            }
        }

        private static readonly Dictionary<string, Loaded> Bundles = new Dictionary<string, Loaded>(StringComparer.OrdinalIgnoreCase);

        private static Loaded Open(Entry e)
        {
            string path = Path.Combine(e.Folder, "bundles", e.Bundle);
            if (Bundles.TryGetValue(path, out Loaded loaded) && loaded.Bundle != null) return loaded;
            if (!File.Exists(path)) throw new FileNotFoundException("the pack has no " + path);
            AssetBundle bundle = AssetBundle.LoadFromFile(path)
                                 ?? AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b != null && b.name == e.BundleName);   // opened before (a second open fails)
            if (bundle == null) throw new InvalidOperationException("could not open " + path);
            loaded = new Loaded { Bundle = bundle, HeroPrefab = e.HeroPrefab };
            Bundles[path] = loaded;
            Plugin.Log.LogInfo($"Gloomhaven model: opened {e.Bundle}");
            return loaded;
        }

        // ------------------------------------------------------------------ materials

        /// <summary>The rebuilt materials of one model, one Frosthaven material per Gloomhaven material (GHModelMaterials).</summary>
        internal sealed class MaterialSet
        {
            private readonly Entry _entry;
            private readonly Loaded _bundle;
            private readonly Material _opaque, _cutout;
            private readonly Dictionary<Material, Material> _made = new Dictionary<Material, Material>();

            public int Count => _made.Count;

            public MaterialSet(Entry e, Loaded bundle, Material drifterBody)
            {
                _entry = e;
                _bundle = bundle;
                Shader opaque = GHModelMaterials.FindShader(GHModelMaterials.OpaqueShader) ?? throw new InvalidOperationException(GHModelMaterials.OpaqueShader + " is not loaded");
                Shader cutout = GHModelMaterials.FindShader(GHModelMaterials.CutoutShader) ?? opaque;
                _opaque = GHModelMaterials.Template(opaque, drifterBody);
                _cutout = cutout == opaque ? _opaque : GHModelMaterials.Template(cutout, drifterBody);
            }

            /// <summary>Every mesh renderer under <paramref name="root"/> gets Frosthaven materials; any other renderer
            /// (Gloomhaven effects) is switched off for good.</summary>
            /// <summary>A transform's path under the root ("a/b/c"; "" for the root), as the pack records objects.</summary>
            internal static string PathUnder(Transform root, Transform t)
            {
                var names = new List<string>();
                for (Transform x = t; x != null && x != root; x = x.parent) names.Add(x.name);
                names.Reverse();
                return string.Join("/", names);
            }

            public void Apply(GameObject root)
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    r.renderingLayerMask = uint.MaxValue;   // like Frosthaven's heroes; the game re-layers at runtime anyway
                    if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer))
                    {
                        // Gloomhaven fed these (the idle wisps, the fireflies) from a MaterialLoader at runtime, so their slots are null;
                        // CharacterManager.RefreshVisibility logs "Null material found in ..." for every null slot at every glow or
                        // invisibility change, so each null slot gets the opaque template material (the renderer never draws).
                        // An empty material array here crashed the game natively at scenario load (2026-10-27, two benches).
                        // (Rebuilding them like the cast effects: see the polish list.)
                        // forceRenderingOff alone was not enough: the game clones the materials for its visibility toggles and
                        // the particles drew as magenta patches round the hero (the harness's "pink" listed P_NightShroud_Idle,
                        // P_PlagueHerald_Idle; Group 2 pass, 2026-10-27), so the particle objects are switched off outright
                        r.forceRenderingOff = true;
                        Material[] off = r.sharedMaterials;
                        bool holes = false;
                        for (int i = 0; i < off.Length; i++) if (off[i] == null) { off[i] = _opaque; holes = true; }
                        if (holes) r.sharedMaterials = off;
                        // the particles the pack recorded stay (hidden until GHVfx.DressHero gives them their rebuilt
                        // materials, after the effects are prepared); the rest are switched off as before
                        string here = PathUnder(root.transform, r.transform);
                        bool recorded = _entry.HeroParticles != null && _entry.HeroParticles.Objects.Keys.Any(k => GHVfx.Under(k, root.transform) == here || (here.Length > 0 && k.EndsWith("/" + here, StringComparison.Ordinal)));
                        if (!recorded && (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)) r.gameObject.SetActive(false);
                        continue;
                    }
                    Material[] slots = r.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        Material gh = slots[i];
                        if (gh == null) continue;
                        if (!_made.TryGetValue(gh, out Material fh))
                        {
                            _entry.Materials.TryGetValue(gh.name, out MaterialSpec spec);
                            fh = spec != null && spec.Hidden ? GHModelMaterials.Shadow(gh, _opaque)
                                : GHModelMaterials.Rebuild(gh, spec, spec != null && spec.Cutout ? _cutout : _opaque, _bundle.Texture);
                            _made[gh] = fh;
                        }
                        slots[i] = fh;
                    }
                    r.sharedMaterials = slots;
                }
            }
        }

        // ------------------------------------------------------------------ summons (2026-10-27)

        // A Gloomhaven summon (pack.json "summon_models": the class's summons' own Gloomhaven bundles, gh_model.export_npc)
        // is built the way a hero is: a Frosthaven summon's prefab is the template (its CharacterManager and controller,
        // the entry's "animation.base", the Polar Cat), the Gloomhaven model and clips go in, its materials are rebuilt on
        // Frosthaven's shaders. The game asks for the prefab by the summon's model name (CActor.GetPrefabName: the
        // HeroSummon def's Model) and gets the template (GHClasses.Prefab); the bundle it would load first is the
        // template's (GHClasses.EnsureActor). docs/reports/2026-10-27-summons.md.
        private static readonly Dictionary<string, Entry> SummonEntries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Built> SummonTemplates = new Dictionary<string, Built>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SummonFailed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The pack entry for a summon model name ("BattleBoar"), or null when no installed pack carries it.</summary>
        internal static Entry SummonEntryFor(string model)
        {
            if (string.IsNullOrEmpty(model)) return null;
            if (SummonEntries.TryGetValue(model, out Entry cached)) return cached;
            Entry e = null;
            foreach (GHClasses.Pack pack in GHClasses.Packs ?? new List<GHClasses.Pack>())
            {
                try
                {
                    JObject j = JObject.Parse(File.ReadAllText(Path.Combine(pack.Folder, "pack.json")));
                    var m = (j["summon_models"] as JObject)?.Properties().FirstOrDefault(p => string.Equals(p.Name, model, StringComparison.OrdinalIgnoreCase))?.Value as JObject;
                    if (m == null) continue;
                    e = Entry.Parse(default(ECharacter), m, pack.Folder);
                    break;
                }
                catch (Exception ex) { Plugin.Log.LogError($"Gloomhaven summons: bad summon entry for {model} in {pack}: {ex.Message}"); }
            }
            SummonEntries[model] = e;
            return e;
        }

        /// <summary>The summon's template prefab, built on first use (null: no entry, or the build failed, logged once).</summary>
        internal static GameObject SummonTemplate(string model)
        {
            Entry e = SummonEntryFor(model);
            if (e == null || SummonFailed.Contains(model)) return null;
            if (SummonTemplates.TryGetValue(model, out Built b) && b.Carrier != null && b.Visual != null)
            {
                try { RefreshSummon(b, model); }
                catch (Exception ex) { if (Reported.Add("refresh summon " + model)) Plugin.Log.LogWarning($"Gloomhaven summons: could not refresh the {model}'s animator: {ex}"); }
                return b.Carrier;
            }
            try
            {
                b = BuildSummon(e, model);
                if (b == null) return null;
                SummonTemplates[model] = b;
                return b.Carrier;
            }
            catch (Exception ex)
            {
                SummonFailed.Add(model);
                Plugin.Log.LogError($"Gloomhaven summons: could not build the {model} model, keeping the {e.Animation.Base}'s: {ex}");
                return null;
            }
        }

        private static GameObject SummonTemplatePrefab(Entry e) =>
            AssetBundleManager.Instance?.GetCharacterPrefabFromBundle(CActor.EType.HeroSummon, e.Animation.Base);

        private static Built BuildSummon(Entry e, string model)
        {
            Loaded bundle = Open(e);
            GameObject ghPrefab = bundle.Bundle.LoadAsset<GameObject>(e.Prefab);
            if (ghPrefab == null) throw new InvalidOperationException(e.Prefab + " is not in " + e.Bundle);
            GameObject template = SummonTemplatePrefab(e);
            if (template == null)
            {
                if (Reported.Add("no summon base " + model)) Plugin.Log.LogWarning($"Gloomhaven summons: the {e.Animation.Base}'s prefab is not loaded yet; the {model} waits for the next request");
                return null;
            }
            CharacterManager templateCm = template.GetComponent<CharacterManager>();
            GameObject templateVisual = templateCm?.CharacterChildPrefab;
            if (templateVisual == null) throw new InvalidOperationException("the " + e.Animation.Base + "'s prefab has no model");

            // 1. the visual: the Gloomhaven model's Animator object
            GameObject wrapper = UnityEngine.Object.Instantiate(ghPrefab, Holder);
            Transform animatorTr = !string.IsNullOrEmpty(e.AnimatorObject) ? Find(wrapper.transform, e.AnimatorObject) : null;
            if (animatorTr == null) animatorTr = wrapper.GetComponentInChildren<Animator>(true)?.transform;
            if (animatorTr == null) throw new InvalidOperationException("no Animator under " + ghPrefab.name);
            GameObject visual = animatorTr.gameObject;
            if (visual != wrapper)
            {
                visual.transform.SetParent(Holder, false);
                UnityEngine.Object.DestroyImmediate(wrapper);
            }
            visual.name = "SU_" + model;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            SetLayer(visual.transform, templateVisual.layer);
            DropLods(visual);
            SkinnedMeshRenderer[] skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0) throw new InvalidOperationException("no skinned meshes under " + ghPrefab.name);
            SkinnedMeshRenderer body = skins.FirstOrDefault(s => s.name == e.BodyMesh) ?? skins.OrderByDescending(s => s.bones.Length).First();

            // 2. materials, on Frosthaven's shaders (the template's body material as the base)
            Material[] templateMats = templateVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).ToArray();
            Material templateBody = templateMats.FirstOrDefault(m => m.shader != null && m.shader.name == GHModelMaterials.OpaqueShader) ?? templateMats.FirstOrDefault();
            var materials = new MaterialSet(e, bundle, templateBody);
            materials.Apply(visual);

            // 3. Frosthaven's helpers on the Animator's object
            Animator animator = visual.GetComponent<Animator>();
            bundle.AddClips(animator.runtimeAnimatorController);
            var vfx = visual.AddComponent<VFXLookup>();
            vfx.CharacterRenderers = skins.Cast<Renderer>().ToList();
            vfx.CharacterBodyMesh = body;
            vfx.TrailPoints = new List<Transform>();
            if (visual.GetComponent<AnimFXTrigger>() == null) visual.AddComponent<AnimFXTrigger>();
            var dissolve = visual.AddComponent<DeathDissolve>();
            DeathDissolve like = templateVisual.GetComponent<DeathDissolve>();
            dissolve.Particles = DissolveParticles(like);
            dissolve.ShowParticleEffect = dissolve.Particles != null;
            dissolve.DissolveLifeTime = like != null ? like.DissolveLifeTime : 2.28f;
            dissolve.DissolveInitialDelay = like != null ? like.DissolveInitialDelay : 1.08f;
            if (like != null) { dissolve.dissolveCurve = like.dissolveCurve; dissolve.AnimStrength = like.AnimStrength; }
            dissolve.CharacterMesh = body;
            var outline = visual.AddComponent<Outlinable>();
            outline.RenderStyle = RenderStyle.Single;
            outline.AddAllChildRenderersToRenderingList(RenderersAddingMode.SkinnedMeshRenderer, "_Albedo");
            outline.OutlineParameters.Enabled = true;
            var idle = visual.AddComponent<IdleAnimParams>();
            IdleAnimParams idleLike = templateVisual.GetComponent<IdleAnimParams>();
            if (idleLike != null) { idle.MinIdleCyclesToRare = idleLike.MinIdleCyclesToRare; idle.MaxIdleCyclesToRare = idleLike.MaxIdleCyclesToRare; }
            FootstepSound templateFeet = templateVisual.GetComponent<FootstepSound>();
            if (templateFeet != null) CopyFields(templateFeet, visual.AddComponent<FootstepSound>(), FootstepFields);

            // 4. the template's controller with the Gloomhaven clips (by state name: both games' NPCs share them)
            var built = new Built { Entry = e, Bundle = bundle, Visual = visual };
            built.Override = MakeOverride(built, templateVisual.GetComponent<Animator>()?.runtimeAnimatorController);
            animator.runtimeAnimatorController = built.Override;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;

            // 5. the carrier, with the template's CharacterManager values
            var carrier = new GameObject(model);
            carrier.transform.SetParent(Holder, false);
            carrier.layer = template.layer;
            if (template.GetComponent<UnityGameEditorObject>() != null) CopyComponent<UnityGameEditorObject>(template, carrier);
            CharacterManager cm = CopyComponent<CharacterManager>(template, carrier);
            cm.CharacterChildPrefab = visual;
            cm.CharacterMenuModelPrefab = null;
            if (!string.IsNullOrEmpty(e.TargetBone)) cm.TargetBone = e.TargetBone;
            cm.Height = e.Height;
            cm.HitRadius = e.HitRadius;
            cm.WeaponLists = new List<CharacterManager.CharacterWeaponList>();
            built.Carrier = carrier;
            Plugin.Log.LogInfo($"Gloomhaven summons: {model} built from {e.Bundle} on the {e.Animation.Base}'s template ({skins.Length} meshes, {materials.Count} materials, height {e.Height}; {built.Override.name})");
            return built;
        }

        /// <summary>Re-bases a summon template's override on the template NPC's controller after the game reloaded it.</summary>
        private static void RefreshSummon(Built b, string model)
        {
            Animator animator = b.Visual.GetComponent<Animator>();
            if (animator == null) return;
            if (b.Override != null && b.Override.runtimeAnimatorController != null && animator.runtimeAnimatorController == b.Override) return;
            GameObject template = SummonTemplatePrefab(b.Entry);
            RuntimeAnimatorController baseController = template?.GetComponent<CharacterManager>()?.CharacterChildPrefab?.GetComponent<Animator>()?.runtimeAnimatorController;
            if (baseController == null)
            {
                if (Reported.Add("no summon base " + model)) Plugin.Log.LogWarning($"Gloomhaven summons: the {b.Entry.Animation.Base}'s animator is not loaded; the {model} keeps its last controller");
                return;
            }
            b.Override = MakeOverride(b, baseController, b.Override);
            animator.runtimeAnimatorController = b.Override;
        }

        // ------------------------------------------------------------------ each spawned copy

        /// <summary>Every clone the game spawns from a template passes through InitialiseCharacter: give it the current
        /// animator (a pooled clone may predate a rebuild), clear what the class screen left on its renderers, and scale
        /// the Drifter's state-behaviour timings to the clips that replaced hers.</summary>
        [HarmonyPatch(typeof(CharacterManager), nameof(CharacterManager.InitialiseCharacter))]
        [HarmonyPostfix]
        private static void Spawned(CharacterManager __instance)
        {
            if (__instance == null || __instance.CharacterChildPrefab == null || Templates.Count == 0) return;
            Built b = Templates.Values.FirstOrDefault(x => x.Visual == __instance.CharacterChildPrefab);
            if (b == null) return;
            try
            {
                GameObject clone = __instance.CharacterChildPrefabInstance;
                Animator animator = clone?.GetComponent<Animator>();
                if (animator == null) return;
                GHSound.Ensure(b.Entry.Model, b.Entry.Folder);
                if (b.Override != null && animator.runtimeAnimatorController != b.Override) animator.runtimeAnimatorController = b.Override;
                foreach (Renderer r in clone.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(null);   // the class screen's lighting tweak, kept by the pool
                FootstepSound cloneFeet = clone.GetComponent<FootstepSound>();
                if (DustGone(cloneFeet) && !DustGone(b.Visual.GetComponent<FootstepSound>()))                  // a pooled clone from an earlier scene: the template's dust was reloaded since
                    FootstepDust.SetValue(cloneFeet, FootstepDust.GetValue(b.Visual.GetComponent<FootstepSound>()));
                // The behaviours exist once the Animator has bound its controller; a clone spawned under an inactive
                // holder (the class screen) binds later, so wait for it then (the game's own preview code does the same).
                if (animator.isInitialized && Retime(animator, b.Entry) > 0) return;
                CoroutineHost.Get().StartCoroutine(RetimeWhenReady(animator, b.Entry));
            }
            catch (Exception e)
            {
                if (Reported.Add("spawned")) Plugin.Log.LogWarning($"Gloomhaven model: could not set up a spawned {b.Entry.Model}: {e}");
            }
        }

        private static System.Collections.IEnumerator RetimeWhenReady(Animator animator, Entry e)
        {
            for (int i = 0; i < 600 && animator != null; i++)
            {
                yield return null;
                if (!animator.isInitialized || !animator.gameObject.activeInHierarchy) continue;
                int n;
                try { n = Retime(animator, e); }
                catch (Exception ex) { if (Reported.Add("retime")) Plugin.Log.LogWarning($"Gloomhaven model: could not retime a spawned {e.Model}: {ex}"); yield break; }
                if (n > 0) yield break;
            }
            if (animator != null && Reported.Add("no behaviours " + e.Model))
                Plugin.Log.LogWarning($"Gloomhaven model: a spawned {e.Model} exposed no animator behaviours to retime; the {e.Animation.Base}'s timings stay");
        }

        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>The Drifter's state behaviours keep their moments in seconds from the state's start, tuned to her clips.
        /// Each is reset from the value it was authored with (the behaviours remember it), scaled by the clip-length
        /// ratio or set to Gloomhaven's own moment for that beat, so doing this again changes nothing. Her voice is
        /// blanked (an empty event is skipped). Returns how many behaviours were seen (0: the Animator wasn't bound yet).</summary>
        // ------------------------------------------------------------------ Gloomhaven-only card animations

        private static AnimatorOverrideController _lentOn;
        private static Animator _lentAnimator;
        private static HashSet<int> _lentHashes;
        private static float _lentAt;
        private static AnimationClip _lentSlot, _lentBefore;
        private static CastEffectsSMB _lentSound;
        private static string _lentSoundBefore;
        private static CastEffectsSMB _lentFx;
        private static GHVfx.Kept _lentFxBefore;

        /// <summary>A card can ask for an animation the Drifter's animator has no state for (the Brute's shouts:
        /// "TargetedShout", "RadialShout"); the game then plays the next state on its list ("Push", "PowerUp"), whose clip
        /// is shared with other states (her cast). For that one play, the fallback state's clip slot is lent the
        /// Gloomhaven state's clip, and given back once the animator has left the state. The state, its timing and its
        /// events stay the ones the game expects, so nothing waits on an event that never comes. A state of hers whose
        /// own Gloomhaven clip lost its slot to another state sharing her clip (the radial attack and the heavy swing
        /// both play her strong melee clip) is lent the same way (AnimationSpec.LentStates). The lent state's Gloomhaven
        /// sound and effects ride along on its cast behaviour.</summary>
        [HarmonyPatch(typeof(Choreographer), "PlayFirstAvailableAnimationStateOnObject")]
        [HarmonyPrefix]
        private static void LendClip(GameObject objectToAnimate, List<string> statesToTry)
        {
            Animator animator = objectToAnimate != null ? MF.GetGameObjectAnimator(objectToAnimate) : null;
            // a lend still playing is given back only when its own animator asks for its next animation; another actor's
            // request (the pushed enemy's hurt) must not cut the roar short
            if (_lentOn != null && (animator == null || animator == _lentAnimator || !StillLent())) GiveBack();
            if (animator == null || statesToTry == null) return;
            try
            {
                if (!(animator.runtimeAnimatorController is AnimatorOverrideController ovr)) return;
                Built b = Templates.Values.FirstOrDefault(t => t != null && t.Override == ovr);
                if (b == null) return;
                AnimationSpec anim = b.Entry.Animation;
                bool Has(string n) => n != null && animator.HasState(0, Animator.StringToHash(n));
                string fallback, clipName, sound;
                GHVfx.Play play;
                int at = statesToTry.FindIndex(n => n != null && anim.GhStates.ContainsKey(n) && !Has(n));
                if (at >= 0)
                {
                    fallback = statesToTry.Skip(at + 1).FirstOrDefault(Has);
                    clipName = anim.GhStates[statesToTry[at]];
                    anim.GhAudio.TryGetValue(statesToTry[at], out sound);
                    anim.GhEffects.TryGetValue(statesToTry[at], out play);
                }
                else
                {
                    fallback = statesToTry.FirstOrDefault(Has);
                    if (fallback == null || !anim.LentStates.TryGetValue(fallback, out Lent lent)) return;
                    clipName = lent.Clip;
                    sound = lent.Audio;
                    play = lent.Effects;
                }
                if (fallback == null || !anim.StateClips.TryGetValue(fallback, out string herClip)) return;
                AnimationClip gh = b.Bundle?.Clip(clipName);
                AnimationClip slot = ovr.runtimeAnimatorController?.animationClips.FirstOrDefault(c => c != null && c.name == herClip);
                if (gh == null || slot == null) return;
                _lentOn = ovr;
                _lentAnimator = animator;
                _lentHashes = new HashSet<int> { Animator.StringToHash(fallback), Animator.StringToHash(fallback + "-1"), Animator.StringToHash(fallback + "-2") };
                _lentAt = Time.realtimeSinceStartup;
                _lentSlot = slot;
                _lentBefore = ovr[slot];
                ovr[slot] = gh;
                CastEffectsSMB cast = animator.GetBehaviours(Animator.StringToHash("Base Layer." + fallback), 0)?.OfType<CastEffectsSMB>().FirstOrDefault();
                if (cast != null && !string.IsNullOrEmpty(sound) && GHSound.Playable(sound))
                {
                    // its sound (the roar, the quake), on the state's cast behaviour
                    _lentSound = cast;
                    _lentSoundBefore = cast.AbilityAudioEvent;
                    cast.AbilityAudioEvent = sound;
                }
                if (cast != null && play != null)
                {
                    // and its effects (the shout's ring, the wave through the hexes)
                    _lentFx = cast;
                    _lentFxBefore = GHVfx.Keep(cast);
                    GHVfx.Apply(cast, play, b.Entry);
                }
                CoroutineHost.Get().StartCoroutine(GiveBackAfter(animator));
            }
            catch (Exception e)
            {
                if (Reported.Add("lend clip")) Plugin.Log.LogWarning($"Gloomhaven model: could not play a Gloomhaven-only card animation: {e}");
            }
        }

        /// <summary>Whether the lent play is still going: the lending animator is in the state (or in a transition), or
        /// the lend is too fresh for the animator to have entered it yet.</summary>
        private static bool StillLent()
        {
            if (_lentAnimator == null || _lentHashes == null) return false;
            if (Time.realtimeSinceStartup - _lentAt < 0.3f) return true;
            if (Time.realtimeSinceStartup - _lentAt > 10f) return false;
            try { return _lentHashes.Contains(_lentAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash) || _lentAnimator.IsInTransition(0); }
            catch (Exception) { return false; }
        }

        /// <summary>Gives the lent clip slot its own clip back once the animator has left the state (and any
        /// transition out of it), or after 10 seconds.</summary>
        private static System.Collections.IEnumerator GiveBackAfter(Animator animator)
        {
            AnimationClip slot = _lentSlot;
            yield return null;
            yield return null;
            while (_lentSlot == slot && animator != null && StillLent())
                yield return null;
            if (_lentSlot == slot) GiveBack();
        }

        private static void GiveBack()
        {
            if (_lentOn != null && _lentSlot != null)
            {
                try { _lentOn[_lentSlot] = _lentBefore; } catch (Exception) { /* the controller died with the scene */ }
            }
            if (_lentSound != null) _lentSound.AbilityAudioEvent = _lentSoundBefore;
            if (_lentFx != null) GHVfx.Restore(_lentFx, _lentFxBefore);
            _lentFx = null;
            _lentFxBefore = null;
            _lentOn = null;
            _lentAnimator = null;
            _lentHashes = null;
            _lentSlot = _lentBefore = null;
            _lentSound = null;
            _lentSoundBefore = null;
        }

        private static int Retime(Animator animator, Entry e)
        {
            AnimationSpec anim = e?.Animation;
            if (anim == null || anim.States.Count == 0) return 0;
            int seen = 0;
            foreach (KeyValuePair<string, StateTiming> state in anim.States)
            {
                StateTiming t = state.Value;
                StateMachineBehaviour[] behaviours;
                try { behaviours = animator.GetBehaviours(Animator.StringToHash(state.Key), 0); }
                catch (Exception) { continue; }
                if (behaviours == null) continue;
                seen += behaviours.Length;
                foreach (StateMachineBehaviour smb in behaviours)
                {
                    if (smb is CastEffectsSMB cast)
                    {
                        Retime(cast.TargetHexEffects, t);
                        Retime(cast.AreaHexEffects, t);
                        Retime(cast.AreaCentralHexEffects, t);
                        Retime(cast.AuraEffects, t);
                        if (cast.CasterEffects != null)
                            foreach (VFXShared.SpawnedEffect fx in cast.CasterEffects)
                            {
                                if (fx == null || fx is GHVfx.GhSpawned || (fx.OriginalStartTime <= 0f && fx.StartTime > 0f)) continue;   // his effects keep their own moments
                                // an effect riding the projectile (the arrow's trail) leaves when the projectile does
                                bool rides = t.GhFire.HasValue && !fx.AttachToRoot && Array.IndexOf(anim.Projectiles, fx.EffectAttachPoint) >= 0;
                                fx.StartTime = rides
                                    ? Mathf.Max(0f, t.GhFire.Value + (t.FhFire.HasValue ? fx.OriginalStartTime - t.FhFire.Value : 0f))
                                    : fx.OriginalStartTime * t.Scale;
                            }
                        if (cast.ProgressChoreographer != null)
                            foreach (VFXShared.Progress p in cast.ProgressChoreographer)
                                if (p != null && (p.OriginalProgressTime > 0f || p.ProgressTime <= 0f)) p.ProgressTime = t.GhProgress ?? p.OriginalProgressTime * t.Scale;
                        if (!string.IsNullOrEmpty(cast.AbilityAudioEvent) && anim.Silence.Contains(cast.AbilityAudioEvent)) cast.AbilityAudioEvent = "";
                        if (GHSound.Playable(t.Audio)) cast.AbilityAudioEvent = t.Audio;   // the class's own sound for this move
                        if (t.Effects != null) GHVfx.Apply(cast, t.Effects, e);             // the class's own effects for this move
                    }
                    else if (smb is FireEquippedProjectileSMB fire)
                    {
                        if (t.GhFire.HasValue) fire.FireTime = t.GhFire.Value;
                        else if (t.FhFire.HasValue) fire.FireTime = t.FhFire.Value * t.Scale;
                    }
                    else if (smb is AudioSMB audio && !string.IsNullOrEmpty(audio.AudioEvent) && anim.Silence.Contains(audio.AudioEvent))
                    {
                        // a voice line of hers: the class's own sound when the state has no cast behaviour to carry it
                        // (her Celebration), else skipped (the game logs one line if this pooled clone is initialised again)
                        audio.AudioEvent = GHSound.Playable(t.Audio) && !behaviours.Any(x => x is CastEffectsSMB) ? t.Audio : "";
                    }
                }
            }
            return seen;
        }

        private static void Retime(VFXShared.TargetHexEffects hex, StateTiming t)
        {
            if (hex == null || hex is GHVfx.GhHex || (hex.OriginalHitTime <= 0f && hex.HitTime > 0f)) return;   // nothing to do, his already, or not awake yet
            // Gloomhaven's beat for the impact: its hit, else its progress moment (a cast that resolves by progress alone,
            // the heal, lands its impact effect when the ability resolves)
            float? ghHit = t.GhHit ?? t.GhProgress;
            float hit = ghHit ?? hex.OriginalHitTime * t.Scale;
            if (hex.OriginalHitTime > 0f) hex.HitTime = hit;
            if (hex.Effects == null) return;
            foreach (VFXShared.BasicEffectTyped fx in hex.Effects)
            {
                if (fx == null) continue;
                fx.StartTime = ghHit.HasValue && hex.OriginalHitTime > 0f
                    ? Mathf.Max(0f, fx.OriginalStartTime + (hit - hex.OriginalHitTime))   // keep the effect's offset from the hit
                    : fx.OriginalStartTime * t.Scale;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>FootstepSound's private serialized fields: the footstep dust prefab (the puff every Frosthaven hero
        /// leaves at a footfall; it lives in the Drifter's bundle, so it is re-copied when it has died), and the feet it
        /// spawns on (the run clip's footstep events index them: 1 left, 2 right). Without both, the component skips the
        /// dust and only plays the step sound.</summary>
        private static readonly FieldInfo FootstepDust = typeof(FootstepSound).GetField("m_PFXPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FootstepFeet = typeof(FootstepSound).GetField("m_FeetArray", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>What a ported hero's FootstepSound copies from the Drifter's: her step and cloth sound events and her
        /// footstep dust. Her enhanced-steps dust is hers alone and stays null (the component then uses the plain dust).</summary>
        private static readonly FieldInfo[] FootstepFields =
        {
            typeof(FootstepSound).GetField("m_FootstepAudioEvent", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(FootstepSound).GetField("m_ClothAudioEvent", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(FootstepSound).GetField("m_ClothAudioEnhanced", BindingFlags.Instance | BindingFlags.NonPublic),
            FootstepDust,
        };

        /// <summary>The bones the footstep dust spawns on, left then right: the pack's names (the Gloomhaven model's own
        /// FootstepSound feet, recorded by tools/gh_model.py), else the first bone named for each foot.</summary>
        private static void SetFeet(FootstepSound feet, Transform visual, Entry e)
        {
            if (feet == null || FootstepFeet == null) return;
            List<Transform> bones = e.Feet.Select(n => Find(visual, n)).Where(t => t != null).ToList();
            if (bones.Count == 0)
            {
                Transform[] all = visual.GetComponentsInChildren<Transform>(true);
                foreach (string side in new[] { "L_", "R_" })
                {
                    Transform foot = all.FirstOrDefault(t => t.name.StartsWith(side, StringComparison.OrdinalIgnoreCase) && t.name.IndexOf("foot", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (foot != null) bones.Add(foot);
                }
            }
            if (bones.Count == 0)
            {
                if (Reported.Add("feet " + e.Model)) Plugin.Log.LogWarning($"Gloomhaven model: no foot bones found on the {e.Model}; it leaves no footstep dust");
                return;
            }
            FootstepFeet.SetValue(feet, bones.ToArray());
        }

        /// <summary>True when the footstep dust prefab on <paramref name="feet"/> is gone (never set, or unloaded with the
        /// Drifter's bundle).</summary>
        private static bool DustGone(FootstepSound feet) =>
            feet != null && FootstepDust != null && (FootstepDust.GetValue(feet) as UnityEngine.Object) == null;

        private static void CopyFields(Component from, Component to, FieldInfo[] fields)
        {
            if (from == null || to == null) return;
            foreach (FieldInfo f in fields) if (f != null) f.SetValue(to, f.GetValue(from));
        }

        /// <summary>A fresh component of type <typeparamref name="T"/> on <paramref name="to"/> with the serialized field
        /// values of the one on <paramref name="from"/> (public instance fields not marked NonSerialized). The new component
        /// belongs to the game's own script registry, not to the bundle the source came from.</summary>
        private static T CopyComponent<T>(GameObject from, GameObject to) where T : Component
        {
            T src = from.GetComponent<T>();
            T dst = to.AddComponent<T>();
            if (src == null) return dst;
            foreach (FieldInfo f in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (!f.IsNotSerialized && !f.IsInitOnly && !f.IsLiteral) f.SetValue(dst, f.GetValue(src));
            return dst;
        }

        private static Transform Find(Transform root, string name) =>
            string.IsNullOrEmpty(name) ? null : root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        private static void SetLayer(Transform t, int layer)
        {
            if (layer < 0) return;
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }
    }
}
