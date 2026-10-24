using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using FHRL;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven classes in Frosthaven (stage 1 spike, the author's PC only). tools/gh_port.py converts a class from the
    /// player's own Gloomhaven install into new ruleset files, its English text and its pictures; this side makes the
    /// game cope with a class it has no art of its own for:
    ///   - 3D model: Frosthaven ships no hero bundle for Gloomhaven models, and asking for one is a null-reference crash
    ///     (AssetBundleManager.BundleConfigForPrefab finds no config). A pack that brings the class's Gloomhaven bundle
    ///     gets its real model, adopted at runtime (GHModel); any other hero model without a bundle borrows the
    ///     Drifter's (an Inox, like the Brute). Either way the Drifter's bundle is the one the game loads for the class.
    ///   - UI config: Frosthaven keeps one CharacterConfigUI per hero (portraits, icons, class colour, card skin, where the
    ///     model stands on the character screen). A class without one gets the Banner Spear's for most lookups and null
    ///     for the 3D preview, which parks the model half-way into the floor. Each ported class gets a copy of the
    ///     Drifter's config with its own name and colour and the pictures the converter prepared (ghclasses\art).
    ///   - Cards: card IDs of 10000 and more select the Gloomhaven-era card prefab, which Frosthaven ships without its
    ///     background sprites (a white rectangle with two red "lost" tiles). Gloomhaven classes' cards are drawn with a
    ///     Frosthaven card prefab instead, through the same generated-layout code the game uses for its long rest card,
    ///     with the corner badge of a real Frosthaven card in place of the template's older one.
    ///   - Text: keys Frosthaven lacks ($ABILITY_CARD_Trample$, PERK_Brute_01...) are answered from the packs'
    ///     strings.tsv; Frosthaven's own translation wins wherever it has the key.
    /// The data is a content pack: FrosthavenMods\packs\&lt;name&gt;\ (ruleset files, strings.tsv, art\, pack.json), built
    /// on each PC by tools/gh_port.py from the player's own Gloomhaven and added to Base.ruleset by the installer or
    /// fh_mods.py install --packs. Inert without one: no ruleset class with a Gloomhaven model means no config copy and
    /// no card redirect, no strings means no text answers, and Frosthaven's own heroes all have bundles.
    /// </summary>
    [HarmonyPatch]
    internal static class GHClasses
    {
        internal const string StandInModel = "Drifter";
        private static string _packsFolder;
        private static string PacksFolder => _packsFolder ?? (_packsFolder = Path.Combine(Paths.GameRootPath, "FrosthavenMods", "packs"));  // lazy: tools/hookcheck runs the static init outside the game
        private static Dictionary<string, string> _strings;
        private static List<Pack> _packs;

        /// <summary>A content pack built on this PC: FrosthavenMods\packs\&lt;name&gt;\ with the manifest pack.json that
        /// tools/gh_port.py writes (name, version, classes, data_sha over the data that matters in play).</summary>
        internal sealed class Pack
        {
            public string Name, Title, DataSha, Folder;
            public int Version;
            public string[] Classes = new string[0];

            /// <summary>What two co-op peers must agree on: same pack, same version, same data.</summary>
            public string Stamp => $"{Name}/{Version}/{DataSha}";
            public override string ToString() => $"{Title ?? Name} v{Version}";
        }

        /// <summary>The packs installed on this PC (manifests read once; none is fine).</summary>
        internal static List<Pack> Packs
        {
            get
            {
                if (_packs != null) return _packs;
                _packs = new List<Pack>();
                try
                {
                    if (Directory.Exists(PacksFolder))
                        foreach (string file in Directory.GetFiles(PacksFolder, "pack.json", SearchOption.AllDirectories))
                        {
                            try
                            {
                                JObject j = JObject.Parse(File.ReadAllText(file));
                                _packs.Add(new Pack
                                {
                                    Folder = Path.GetDirectoryName(file),
                                    Name = (string)j["name"] ?? Path.GetFileName(Path.GetDirectoryName(file)),
                                    Title = (string)j["title"],
                                    Version = (int?)j["version"] ?? 0,
                                    DataSha = (string)j["data_sha"] ?? "",
                                    Classes = j["classes"]?.Select(c => (string)c).Where(c => c != null).ToArray() ?? new string[0],
                                });
                            }
                            catch (Exception e) { Plugin.Log.LogError($"Content packs: could not read {file}: {e.Message}"); }
                        }
                    _packs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    Plugin.Log.LogInfo(_packs.Count == 0 ? "Content packs: none installed" : "Content packs: " + string.Join(", ", _packs.Select(p => p.ToString())));
                }
                catch (Exception e) { Plugin.Log.LogError("Content packs: could not list " + PacksFolder + ": " + e); }
                return _packs;
            }
        }

        /// <summary>The pack that brings <paramref name="model"/>'s class, or null.</summary>
        internal static Pack PackOf(ECharacter model) =>
            Packs.FirstOrDefault(p => p.Classes.Any(c => string.Equals(c, model.ToString(), StringComparison.OrdinalIgnoreCase)))
            ?? Packs.FirstOrDefault(p => Directory.Exists(Path.Combine(p.Folder, "art", model.ToString())));
        private static readonly Dictionary<string, bool> GameHasKey = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        internal static readonly HashSet<string> Reported = new HashSet<string>();
        private static bool _quitting;

        static GHClasses()
        {
            // Pooled cards are torn down at quit after the UI tools are gone; see NotWhileQuitting.
            Application.quitting += () => _quitting = true;
        }

        private static Dictionary<string, string> Strings
        {
            get
            {
                if (_strings != null) return _strings;
                _strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Pack pack in Packs)
                {
                    string file = Path.Combine(pack.Folder, "strings.tsv");
                    try
                    {
                        if (!File.Exists(file)) continue;
                        int n = 0;
                        foreach (string line in File.ReadAllLines(file))
                        {
                            int tab = line.IndexOf('\t');
                            if (tab <= 0) continue;
                            _strings[line.Substring(0, tab)] = line.Substring(tab + 1).Replace("\\n", "\n");
                            n++;
                        }
                        Plugin.Log.LogInfo($"Content packs: {n} text keys from {pack}");
                    }
                    catch (Exception e) { Plugin.Log.LogError("Content packs: could not read " + file + ": " + e); }
                }
                return _strings;
            }
        }

        /// <summary>True for Gloomhaven's and Jaws of the Lion's heroes: the enum entries before Frosthaven's first, the
        /// Infuser. The game compares class names case-insensitively, so this does too.</summary>
        internal static bool IsGloomhavenClass(string name, out ECharacter model)
        {
            model = ECharacter.None;
            return !string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out model) && Enum.IsDefined(typeof(ECharacter), model)
                   && model != ECharacter.None && model < ECharacter.Infuser;
        }

        /// <summary>The ruleset class whose model is <paramref name="model"/>, or null: only ported classes get treatment.
        /// (Vanilla code asks about ECharacter.Elementalist as a placeholder; it must keep getting the game's own answer.)</summary>
        private static CCharacterClass PortedClass(ECharacter model) =>
            CharacterClassManager.Classes?.FirstOrDefault(c => c != null && c.CharacterModel == model);

        internal static CCharacterClass ClassById(string classId) =>
            classId == null ? null : CharacterClassManager.Classes?.FirstOrDefault(c => c != null && c.ID == classId);

        // ------------------------------------------------------------------ the pack's classes

        /// <summary>The ruleset classes a content pack brought: their model is one of Gloomhaven's or Jaws of the Lion's.</summary>
        internal static List<CCharacterClass> PackClasses() =>
            (CharacterClassManager.Classes ?? new List<CCharacterClass>()).Where(c => c != null && IsGloomhavenClass(c.CharacterModel.ToString(), out _)).ToList();

        // The switch itself, its guard and the command that unlocks the classes on every PC: GHClassesRule.cs. The
        // co-op join check: PackGate.cs.

        // ------------------------------------------------------------------ 3D model: the pack's own, or the stand-in

        /// <summary>The hero model whose bundle to load instead of <paramref name="prefabName"/>'s, or null to load it as
        /// asked. The Drifter's bundle is what a Gloomhaven class loads either way: it is the stand-in model, and the
        /// pack's own model is built around her wrapper and animator (GHModel).</summary>
        private static string StandIn(string prefabName)
        {
            if (!IsGloomhavenClass(prefabName, out ECharacter model)) return null;
            AssetBundleManager abm = AssetBundleManager.Instance;
            if (abm == null || abm.AssetBundleExists("hero_" + prefabName.ToLowerInvariant().Replace(" ", ""))) return null;
            if (Reported.Add(prefabName))
                Plugin.Log.LogInfo(GHModel.EntryFor(model) != null
                    ? $"Gloomhaven classes: {prefabName} brings its own 3D model; the {StandInModel}'s bundle carries it"
                    : $"Gloomhaven classes: no 3D model for {prefabName}, using the {StandInModel}'s");
            return StandInModel;
        }

        /// <summary>The game asks for a hero prefab here from every path (class screen, scenario, results). A Gloomhaven
        /// class with a model in its pack gets the adopted model; without one, the Drifter's prefab under her name.</summary>
        [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.GetCharacterPrefabFromBundle))]
        [HarmonyPrefix]
        private static bool Prefab(CActor.EType type, ref string prefabName, ref string skinString, ref GameObject __result)
        {
            if (type != CActor.EType.Player) return true;
            string standIn = StandIn(prefabName);
            if (standIn == null) return true;
            GameObject own = IsGloomhavenClass(prefabName, out ECharacter model) ? GHModel.Template(model, skinString) : null;
            if (own != null) { __result = own; return false; }
            prefabName = standIn;
            skinString = null;
            return true;
        }

        [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.GetCharacterPrefabFromBundleAsync))]
        [HarmonyPrefix]
        private static bool PrefabAsync(CActor.EType type, ref string prefabName, ref string skinString, ref System.Threading.Tasks.Task<GameObject> __result)
        {
            GameObject own = null;
            if (!Prefab(type, ref prefabName, ref skinString, ref own)) { __result = System.Threading.Tasks.Task.FromResult(own); return false; }
            return true;
        }

        [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.EnsureActorPrefabBundlesLoadedBeforeAction))]
        [HarmonyPrefix]
        private static void EnsureActor(ref string actorPrefabName) => actorPrefabName = StandIn(actorPrefabName) ?? actorPrefabName;

        [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.EnsureCharacterLoadedBeforeAction))]
        [HarmonyPrefix]
        private static void EnsureCharacter(ref ECharacter characterToBeLoaded)
        {
            string standIn = StandIn(characterToBeLoaded.ToString());
            if (standIn != null) characterToBeLoaded = (ECharacter)Enum.Parse(typeof(ECharacter), standIn);
        }

        /// <summary>Scenario loading asks for the bundles of every party member's model up front.</summary>
        [HarmonyPatch(typeof(BundleLoadSettings), nameof(BundleLoadSettings.GetBundlesNeededForRequirements))]
        [HarmonyPrefix]
        private static void Bundles(ref List<ECharacter> charactersNeeded)
        {
            if (charactersNeeded == null) return;
            List<ECharacter> swapped = null;
            for (int i = 0; i < charactersNeeded.Count; i++)
            {
                string standIn = StandIn(charactersNeeded[i].ToString());
                if (standIn == null) continue;
                swapped = swapped ?? new List<ECharacter>(charactersNeeded);
                swapped[i] = (ECharacter)Enum.Parse(typeof(ECharacter), standIn);
            }
            if (swapped != null) charactersNeeded = swapped;
        }

        // ------------------------------------------------------------------ class UI config (portraits, icons, colour, 3D placement)

        /// <summary>Gloomhaven's own class colours (from its CharacterConfigUI assets; not stored in a loadable bundle).</summary>
        internal static readonly Dictionary<ECharacter, Color> ClassColours = new Dictionary<ECharacter, Color>
        {
            { ECharacter.Brute, new Color(0.451f, 0.659f, 1f) },
        };

        /// <summary>
        /// UIInfoTools.characterConfigsUI holds one config per Frosthaven hero (the Banner Spear's first). For any other
        /// name GetCharacterConfigUI returns that first entry, or null when the caller says so; the 3D preview is such a
        /// caller and, with no placement to use, drops the model to half its height below the floor. UIInfoTools only
        /// exists once the game's loading screen has built it, so the copies are added here, on the first lookup of each
        /// ported class, after which the game's own search finds them like any other entry (never twice: an entry is
        /// added only when the search found nothing for that name, and SingleOrDefault would throw on a duplicate).
        /// </summary>
        [HarmonyPatch(typeof(UIInfoTools), nameof(UIInfoTools.GetCharacterConfigUI), new[] { typeof(string), typeof(bool), typeof(string) })]
        [HarmonyPostfix]
        private static void Config(UIInfoTools __instance, string character, string customConfigID, ref CharacterConfigUI __result)
        {
            if (customConfigID != null || string.IsNullOrEmpty(character) || __instance == null || __instance.characterConfigsUI == null) return;
            string name = character.EndsWith("ID", StringComparison.Ordinal) ? character.Substring(0, character.Length - 2) : character;  // the game strips "ID$" too
            if (__result != null && string.Equals(__result.character.ToString(), name, StringComparison.OrdinalIgnoreCase)) return;  // found its own
            if (!IsGloomhavenClass(name, out ECharacter model) || PortedClass(model) == null) return;
            List<CharacterConfigUI> list = __instance.characterConfigsUI;
            if (list.Exists(c => c != null && c.character == model)) return;
            CharacterConfigUI source = list.Find(c => c != null && c.character.ToString() == StandInModel);
            if (source == null) return;
            CharacterConfigUI config;
            try { config = MakeConfig(source, model); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Gloomhaven classes: could not make a UI config for {model}: {e}");
                return;
            }
            list.Add(config);
            __result = config;
        }

        internal static string SkinName(ECharacter model) => model + "_GH_CardSkinConfig";

        /// <summary>A copy of the stand-in's config for <paramref name="model"/>. Instantiate deep-copies the serialized data
        /// (placements, colours, lists) and shares the sprites; the Drifter's own asset is never changed.</summary>
        private static CharacterConfigUI MakeConfig(CharacterConfigUI source, ECharacter model)
        {
            CharacterConfigUI cfg = UnityEngine.Object.Instantiate(source);
            cfg.name = model.ToString();
            cfg.hideFlags = HideFlags.DontUnloadUnusedAsset;
            cfg.character = model;                                         // what GetCharacterConfigUI matches; left as Drifter it would break every Drifter lookup
            cfg.ID = model.ToString();
            cfg.Cards = new CharacterConfigUI.AbilityCardConfigUI[0];      // the Drifter's pre-built card prefabs and headers are hers
            // the skin toggle on the party screen offers the class's own alternative looks (the Drifter's would be hers)
            GHModel.Entry looks = GHModel.EntryFor(model);
            cfg.alternativeSkins = looks != null ? looks.Skins.Keys.ToList() : new List<string>();
            Plugin.Log.LogInfo($"Gloomhaven classes: {model}'s skin toggle offers {(cfg.alternativeSkins.Count > 0 ? string.Join(", ", cfg.alternativeSkins) : "no other looks")}");
            if (cfg.cardSkin != null)
            {
                // a skin of its own: GHCardFace gives it the class's card halves and pills (the Drifter's stays hers)
                cfg.cardSkin = UnityEngine.Object.Instantiate(cfg.cardSkin);
                cfg.cardSkin.name = SkinName(model);
                cfg.cardSkin.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            if (ClassColours.TryGetValue(model, out Color colour) && cfg.questConfig != null) cfg.questConfig.color = colour;
            // Kept from the Drifter: the 3D placement on the character screen (it is the Drifter model that stands there),
            // card skin and card prefab (see CardPrefab), gender, active-bonus sidebar, highlight frame, tokens.
            int loaded = Pictures(cfg, source, model);
            Plugin.Log.LogInfo($"Gloomhaven classes: {model} gets its own UI config (a copy of the {StandInModel}'s, {loaded} of its own pictures)");
            return cfg;
        }

        // ------------------------------------------------------------------ pictures prepared by tools/gh_art.py

        private static readonly Dictionary<string, Sprite> PictureCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Puts the class's own pictures on the config: ghclasses\art\&lt;Model&gt;\&lt;role&gt;.png, which
        /// tools/gh_art.py composed from the player's Gloomhaven into the shapes of the Drifter's pictures. Each gets the
        /// Drifter picture's on-screen native size, so layouts that call SetNativeSize or carry offsets tuned for hers
        /// behave the same. A missing file keeps the Drifter's picture. Returns how many loaded.</summary>
        private static int Pictures(CharacterConfigUI cfg, CharacterConfigUI like, ECharacter model)
        {
            Pack pack = PackOf(model);
            string dir = pack == null ? null : Path.Combine(pack.Folder, "art", model.ToString());
            if (dir == null || !Directory.Exists(dir))
            {
                Plugin.Log.LogWarning($"Gloomhaven classes: no pictures for {model} ({dir ?? "no pack lists it"}): rerun tools/gh_port.py with UnityPy and Pillow installed");
                return 0;
            }
            int n = 0;
            Sprite p;
            if ((p = Picture(dir, "iconClass", like.iconClass)) != null) { cfg.iconClass = p; n++; }
            if ((p = Picture(dir, "iconClassHighlight", like.iconClassHighlight)) != null) { cfg.iconClassHighlight = p; n++; }
            if ((p = Picture(dir, "iconClassGold", like.iconClassGold)) != null) { cfg.iconClassGold = p; n++; }
            if ((p = Picture(dir, "illustration", like.illustration)) != null) { cfg.illustration = p; cfg.distributionPortrait = p; n++; }  // the Drifter uses one picture for both
            if ((p = Picture(dir, "InfoIllustration", like.InfoIllustration)) != null) { cfg.InfoIllustration = p; n++; }
            if ((p = Picture(dir, "newAdventurePortrait", like.newAdventurePortrait)) != null) { cfg.newAdventurePortrait = p; n++; }
            // newHighlightAdventurePortrait stays the Drifter's: it is a generic glow frame, not a portrait
            if ((p = Picture(dir, "scenarioPortrait", like.scenarioPortrait)) != null) { cfg.scenarioPortrait = p; cfg.assemblyPortrait = p; n++; }
            if (cfg.questConfig != null && (p = Picture(dir, "marker", like.questConfig?.marker)) != null) { cfg.questConfig.marker = p; n++; }
            if (cfg.tabIconConfig != null)
            {
                if ((p = Picture(dir, "tabNormal", like.tabIconConfig?.normalIcon)) != null) { cfg.tabIconConfig.normalIcon = p; n++; }
                if ((p = Picture(dir, "tabSelected", like.tabIconConfig?.selectedIcon)) != null) { cfg.tabIconConfig.selectedIcon = p; n++; }
            }
            if ((p = Picture(dir, "activeAbility", like.activeAbility)) != null) { cfg.activeAbility = p; n++; }
            return n;
        }

        /// <summary>The sprite for &lt;dir&gt;\&lt;role&gt;.png with the on-screen native size of <paramref name="like"/>, or
        /// null when there is no such file or it can't be read (never throws). One texture per file.</summary>
        internal static Sprite Picture(string dir, string role, Sprite like)
        {
            string file = Path.Combine(dir, role + ".png");
            if (PictureCache.TryGetValue(file, out Sprite cached)) return cached;
            Sprite sprite = null;
            try
            {
                if (File.Exists(file))
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = role, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
                    if (ImageConversion.LoadImage(texture, File.ReadAllBytes(file), true))
                    {
                        float ppu = like != null && like.rect.width > 0 ? like.pixelsPerUnit * texture.width / like.rect.width : 100f;
                        sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
                        sprite.name = role;
                        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(texture);
                        Plugin.Log.LogWarning($"Gloomhaven classes: {file} is not a PNG Unity can read");
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Gloomhaven classes: {file}: {e.Message}"); }
            PictureCache[file] = sprite;
            return sprite;
        }

        // ------------------------------------------------------------------ ability cards

        /// <summary>A card ID of 10000 or more selects the Gloomhaven-era "AbilityCard" prefab without looking at the
        /// class (PersistentData.GetAbilityCardPrefabInfo). Frosthaven ships that prefab with its background sprites
        /// stripped: a null sprite draws as solid white, the generated text is white, and its two "lost" tiles (red X)
        /// are permanently on because nothing switches them off. A Gloomhaven class's cards use a Frosthaven card
        /// prefab instead: the one its config names (the Drifter's, by the copy above), else the generic one. It is the
        /// path the game itself takes for its long rest card and in the level editor.</summary>
        [HarmonyPatch(typeof(PersistentData), nameof(PersistentData.GetAbilityCardPrefabInfo))]
        [HarmonyPostfix]
        private static void CardPrefab(AbilityCardYMLData abilityCard, ref CCharacterClass playerClass, ref CharacterConfigUI config, ref string __result)
        {
            if (abilityCard == null || abilityCard.ID == -1 || playerClass == null || !IsGloomhavenClass(playerClass.AbilityCardArt, out _)) return;
            UIInfoTools ui = UIInfoTools.Instance;
            CharacterConfigUI cfg = ui == null ? null
                : ui.GetCharacterConfigUI(playerClass.AbilityCardArt, useDefault: false) ?? ui.GetCharacterConfigUI(StandInModel, useDefault: true);
            if (cfg != null) config = cfg;
            __result = cfg?.customCardPrefab?.name ?? "AbilityCard-FH";
            if (Reported.Add("cards " + playerClass.AbilityCardArt))
                Plugin.Log.LogInfo($"Gloomhaven classes: {playerClass.AbilityCardArt} cards are drawn with the {__result} prefab");
        }

        /// <summary>Frosthaven's card prefabs are templates whose hand-card picture is a Banner Spear painting
        /// (Image_CardArt); the game's own cards replace it from their class bundles, a generated card never does.
        /// A Gloomhaven card shows its class's banner there (GHCardFace.MiniArt), or nothing without one.</summary>
        [HarmonyPatch(typeof(AbilityCardUI), nameof(AbilityCardUI.MakeAbilityCard))]
        [HarmonyPostfix]
        private static void NoBorrowedArt(AbilityCardUI __instance, AbilityCardYMLData cardData)
        {
            if (cardData?.CharacterID == null || __instance.miniAbilityCard == null) return;
            CCharacterClass cls = ClassById(cardData.CharacterID);
            if (cls == null || !IsGloomhavenClass(cls.AbilityCardArt, out ECharacter model)) return;
            Transform art = __instance.miniAbilityCard.transform.Find("Container_CardArt/Mask_CardArt/Image_CardArt");
            Image image = art != null ? art.GetComponent<Image>() : null;
            if (GHCardFace.MiniArt(__instance, model)) { /* the class's banner, as the real rows show the card's picture */ }
            else if (image != null) image.enabled = false;
            else if (Reported.Add("no Image_CardArt"))
                Plugin.Log.LogWarning("Gloomhaven classes: the mini card has no Container_CardArt/Mask_CardArt/Image_CardArt; its template painting stays");
            GHCardFace.Dress(__instance, model);
        }

        // ---- the corner badge (lost / persistent / round / XP icons) of each card half

        /// <summary>FullAbilityCardAction's private array of corner badges (tools/hookcheck verifies the name).</summary>
        private static readonly FieldInfo SummaryContainers = typeof(FullAbilityCardAction).GetField("actionSummaryContainers", BindingFlags.Instance | BindingFlags.NonPublic);
        private static CardActionSummary _cornerDonor;

        /// <summary>The template the Gloomhaven cards are built from still carries an older corner badge: 20 px icons
        /// with fire effects on a class-coloured "mandatory" banner (the "!" box). Frosthaven's own cards use a newer
        /// piece (ExperienceActiveBonuses: 24-28 px icons, newer sprites, a soft dark gradient). Before the game lays a
        /// Gloomhaven card half out, swap in a copy of that piece taken from one of the stand-in class's real card
        /// prefabs, placed where the template's badge was; the game's own code then fills, fits and colours it exactly
        /// as it does on her cards.</summary>
        [HarmonyPatch(typeof(FullAbilityCardAction), nameof(FullAbilityCardAction.MakeAbilityAction))]
        [HarmonyPrefix]
        private static void CornerBadge(FullAbilityCardAction __instance, AbilityCardYMLData card)
        {
            if (card == null || card.ID == -1 || SummaryContainers == null) return;
            CCharacterClass cls = ClassById(card.CharacterID);
            if (cls == null || !IsGloomhavenClass(cls.AbilityCardArt, out _)) return;
            try
            {
                var containers = SummaryContainers.GetValue(__instance) as CardActionSummary[];
                if (containers == null || containers.Length != 1 || containers[0] == null || !containers[0].name.StartsWith("BottomCorner")) return;
                CardActionSummary donor = CornerDonor();
                if (donor == null) return;
                CardActionSummary old = containers[0];
                RectTransform oldRect = (RectTransform)old.transform;
                CardActionSummary fresh = UnityEngine.Object.Instantiate(donor, oldRect.parent);
                fresh.name = donor.name;
                RectTransform rect = (RectTransform)fresh.transform;
                rect.anchorMin = oldRect.anchorMin;
                rect.anchorMax = oldRect.anchorMax;
                rect.pivot = oldRect.pivot;
                // inside the half's frame, clear of its cut corner (GHCardFace lays the halves out like the real cards;
                // the template hangs the top badge 5 units below its content)
                bool topHalf = __instance.name.StartsWith("Top", StringComparison.OrdinalIgnoreCase);
                rect.anchoredPosition = oldRect.anchoredPosition + new Vector2(-6f, topHalf ? 5f : 4f);
                rect.localScale = Vector3.one;
                rect.SetSiblingIndex(oldRect.GetSiblingIndex());
                fresh.gameObject.SetActive(true);
                SummaryContainers.SetValue(__instance, new[] { fresh });
                old.gameObject.SetActive(false);        // the card pool clones this card this frame: hide first, destroy after
                UnityEngine.Object.Destroy(old.gameObject);
            }
            catch (Exception e)
            {
                if (Reported.Add("corner")) Plugin.Log.LogWarning($"Gloomhaven classes: could not restyle a card's corner badge: {e}");
            }
        }

        /// <summary>The corner badge of one of the stand-in class's real card prefabs (loaded the way the game loads her
        /// cards), or null if it isn't available yet; looked up again on the next card until found.</summary>
        private static CardActionSummary CornerDonor()
        {
            if (_cornerDonor != null) return _cornerDonor;
            GameObject prefab = StandInCardPrefab();
            CardActionSummary[] summaries = prefab == null ? null : prefab.GetComponentsInChildren<CardActionSummary>(true);
            _cornerDonor = summaries?.FirstOrDefault(s => s != null && !s.name.StartsWith("BottomCorner"));
            if (_cornerDonor == null && Reported.Add("no corner donor"))
                Plugin.Log.LogWarning($"Gloomhaven classes: no {StandInModel} card prefab to borrow the corner badge from; the template's stays");
            else if (_cornerDonor != null)
                Plugin.Log.LogInfo($"Gloomhaven classes: card corner badges copied from the {StandInModel}'s {prefab.name}");
            return _cornerDonor;
        }

        /// <summary>One of the stand-in class's real card prefabs, loaded the way the game loads her cards (from
        /// hero_&lt;class&gt;cards, which the game may unload again: copy what you need, keep no references). Null when
        /// the classes or the bundle aren't available yet.</summary>
        internal static GameObject StandInCardPrefab()
        {
            CCharacterClass standIn = CharacterClassManager.Classes?.FirstOrDefault(c => c != null && c.CharacterModel.ToString() == StandInModel);
            CAbilityCard sample = standIn == null ? null
                : CharacterClassManager.AllAbilityCards?.FirstOrDefault(c => c?.GetAbilityCardYML != null && c.GetAbilityCardYML.ID != -1 && c.GetAbilityCardYML.CharacterID == standIn.ID);
            return sample == null ? null : PersistentData.GetAbilityCardPrefab(sample.GetAbilityCardYML, standIn);
        }

        /// <summary>At quit the pooled cards are recycled after UIInfoTools is gone, and clearing a generated card's
        /// area hexes and enhancement slots then logs errors (caught by the game, noise only). Skip it while quitting.</summary>
        [HarmonyPatch(typeof(AbilityCardUI), "ClearEnhancements")]
        [HarmonyPrefix]
        private static bool NotWhileQuitting() => !_quitting;

        // ------------------------------------------------------------------ class definitions

        /// <summary>A class without masteries: the game's YAML reader can't read an empty list ("MasteryIDs: []" fails
        /// the whole file), and a missing one leaves the definition's MasteryIDs null, which the scenario results screen
        /// reads without a check (ScenarioResultViewService: masteryIDs.Length). Give such classes an empty array.</summary>
        [HarmonyPatch(typeof(FH.YML.YMLDefRepository), "ApplyVerticalChunkPatches")]
        [HarmonyPostfix]
        private static void NoMasteries(FH.YML.YMLDefRepository __instance)
        {
            foreach (FHRL.CharacterClassDef def in __instance.DefRepository.CharacterClasses.Values)
                if (def.MasteryIDs == null) def.MasteryIDs = new FHRL.MasteryID[0];
        }

        /// <summary>The "Hero intro" button shows for every class outside the game mode's starting list and plays
        /// StreamingAssets\Movies\HeroIntroductions\FH_HeroIntroduction_&lt;Class&gt;_4k.mov; a Gloomhaven class has
        /// none, so the button did nothing. Hide it.</summary>
        [HarmonyPatch(typeof(FHGUI.Services.CharacterDetailsViewService), nameof(FHGUI.Services.CharacterDetailsViewService.CharacterHasIntroVideo))]
        [HarmonyPostfix]
        private static void IntroVideo(FHRL.CharacterClassID classID, ref bool __result)
        {
            if (!__result) return;
            CCharacterClass cls = ClassById(classID.ID);
            if (cls != null && IsGloomhavenClass(cls.CharacterModel.ToString(), out _)) __result = false;
        }

        // ------------------------------------------------------------------ text

        private static bool TryOurs(string term, out string text)
        {
            text = null;
            if (term == null || Strings.Count == 0) return false;
            string key = term.Length > 1 && term[0] == '$' && term[term.Length - 1] == '$' ? term.Substring(1, term.Length - 2) : term;
            if (!Strings.TryGetValue(key, out text)) return false;
            if (key.StartsWith("GH_")) return true;
            if (!GameHasKey.TryGetValue(key, out bool has))
            {
                has = I2.Loc.LocalizationManager.GetTermData(key) != null;
                if (I2.Loc.LocalizationManager.Sources.Count > 0) GameHasKey[key] = has;  // only trust a loaded table
            }
            return !has;
        }

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPrefix]
        private static bool Translate(string term, ref string __result)
        {
            if (!TryOurs(term, out string text)) return true;
            __result = GLOOM.LocalizationManager.ReplaceTextVariables(text);
            return false;
        }

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.TryGetTranslation))]
        [HarmonyPrefix]
        private static bool TryTranslate(string term, ref string translation, ref bool __result)
        {
            if (!TryOurs(term, out string text)) return true;
            translation = GLOOM.LocalizationManager.ReplaceTextVariables(text);
            __result = true;
            return false;
        }
    }
}
