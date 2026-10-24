using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FHGUI;
using FHGUI.DTO.Outpost;
using FHRL;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// Codex pages for the pack's classes. The codex builds its monster, boss, building and item pages from the loaded
    /// definitions, but its hero pages are a hand-made list in a Unity asset (CodexConfig, "CodexDataSO": one entry per
    /// Frosthaven class, "DrifterInfo" and so on, each pointing at a page prefab with the class ID baked into its
    /// FHUICodexHeroEntry). Nothing in a data pack can add to that list, so this does: when the codex gathers its entries,
    /// each pack class gets one, a copy of the stand-in's page with the class ID swapped. The page then shows what the
    /// pack provides for the class: its illustration, description, health table, hand size, complexity, stats, traits,
    /// every card, and the "mechanics" text (CODEX_HeroInfo_&lt;Class&gt;, Gloomhaven digital's own role, difficulty and
    /// play notes). The entry follows the rule Frosthaven's own classes follow: a pack's starting classes are listed
    /// openly like Frosthaven's six, the envelope classes read "???" until a campaign has unlocked them (the map unlocks
    /// the codex tag of every unlocked class, GHClassesRule's unlock included).
    ///
    /// The copy is a template kept under an inactive holder, so its scripts never run, and the codex instantiates it like
    /// any page prefab. The codex pools page instances by prefab, so each class needs its own template rather than the
    /// shared prefab with the ID set at display time.
    /// </summary>
    [HarmonyPatch]
    internal static class GHCodex
    {
        private static readonly FieldInfo HeroId = AccessTools.Field(typeof(FHUICodexHeroEntry), "HeroId");
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>();
        private static GameObject _holder;

        [HarmonyPatch(typeof(CodexConfig), nameof(CodexConfig.AllEntries))]
        [HarmonyPostfix]
        private static void AddPackClasses(List<CodexConfig.CodexEntry> __result)
        {
            if (__result == null || HeroId == null) return;
            try
            {
                // the stand-in's page is the template; any hero page would do
                CodexConfig.CodexEntry source = __result.FirstOrDefault(e => IsHeroPage(e) && e.ID == GHClasses.StandInModel + "Info")
                                                ?? __result.FirstOrDefault(IsHeroPage);
                if (source == null) return;
                DefRepository repo = GHClassesRule.LiveRepo;
                foreach (CCharacterClass c in GHClasses.PackClasses())
                {
                    if (c == null || string.IsNullOrEmpty(c.ID)) continue;
                    string cls = c.CharacterModel.ToString();
                    GameObject template = Template(cls, c.ID, source.EntryContent);
                    if (template == null) continue;
                    CodexConfig.CodexEntry mine = __result.FirstOrDefault(e => e != null && e.ID == cls + "Info");
                    if (mine != null)
                    {
                        mine.EntryContent = template;   // listed already; the template may have been rebuilt
                        continue;
                    }
                    GHClasses.Pack pack = GHClasses.PackOf(c.CharacterModel);
                    bool starting = pack != null && pack.Starting.Any(s => string.Equals(s, cls, StringComparison.OrdinalIgnoreCase));
                    mine = new CodexConfig.CodexEntry
                    {
                        ID = cls + "Info",
                        EntryContent = template,
                        Category = source.Category,
                        LocalizationKey = TitleKey(c, repo),
                        keywords = new List<string>(),
                        unlockTag = starting ? null : FHGUI.Common.FHUICodexUtils.CodexHeroIDToUnlockTag(new CharacterClassID(c.ID)),
                    };
                    __result.Insert(Position(__result, mine), mine);
                    Plugin.Log.LogInfo($"Content packs: codex page for the {cls}" + (starting ? "" : " (unlocks with the class)"));
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Content packs: codex pages failed: " + e); }
        }

        private static bool IsHeroPage(CodexConfig.CodexEntry e) =>
            e != null && e.EntryContent != null && e.EntryContent.GetComponent<FHUICodexHeroEntry>() != null;

        /// <summary>The class's page template: a copy of <paramref name="prefab"/> with its hero ID set to the class, kept
        /// inactive and alive across scenes. Rebuilt if anything destroyed it.</summary>
        private static GameObject Template(string cls, string classId, GameObject prefab)
        {
            if (Templates.TryGetValue(cls, out GameObject template) && template != null) return template;
            if (prefab == null) return null;
            if (_holder == null)
            {
                _holder = new GameObject("FrosthavenMods codex pages");
                _holder.SetActive(false);   // its children never run: prefabs in all but name
                UnityEngine.Object.DontDestroyOnLoad(_holder);
            }
            template = UnityEngine.Object.Instantiate(prefab, _holder.transform);
            template.name = cls + "Info";
            FHUICodexHeroEntry page = template.GetComponent<FHUICodexHeroEntry>();
            if (page == null)
            {
                UnityEngine.Object.Destroy(template);
                return null;
            }
            HeroId.SetValue(page, classId);
            Templates[cls] = template;
            return template;
        }

        /// <summary>The page's title key: the pack's CODEX_Hero_&lt;Class&gt;_Title when it brings one (gh_port writes it from
        /// the class's name), else the class's own name key.</summary>
        private static string TitleKey(CCharacterClass c, DefRepository repo)
        {
            string key = "CODEX_Hero_" + c.CharacterModel + "_Title";
            if (GHClasses.HasString(key)) return key;
            string name = repo != null && repo.CharacterClasses.TryGetValue(new CharacterClassID(c.ID), out CharacterClassDef def) && def != null
                ? def.ClassName : null;
            return string.IsNullOrEmpty(name) ? c.CharacterModel.ToString() : name.Replace("$", "");
        }

        /// <summary>Among the pages of its category, in ID order (the asset lists the heroes alphabetically); after the
        /// last of them otherwise.</summary>
        private static int Position(List<CodexConfig.CodexEntry> entries, CodexConfig.CodexEntry mine)
        {
            int last = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                CodexConfig.CodexEntry e = entries[i];
                if (e == null || e.Category != mine.Category) continue;
                if (string.CompareOrdinal(e.ID, mine.ID) > 0) return i;
                last = i;
            }
            return last + 1;
        }
    }
}
