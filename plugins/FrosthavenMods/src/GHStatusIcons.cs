using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using WorldspaceUI;
using FHGUI.Common;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven class effects shown on a figure's status row, where poison and the other conditions sit (the user's
    /// manual test, 2026-10-29: "Songs should apply a music note buff style symbol to every character affected and I
    /// should be able to inspect them like any other buff"; the Favorite's mark showed nothing on the enemy).
    ///
    /// Songs: Gloomhaven adds each Soothsinger song to every figure it reaches; Frosthaven's status row and inspect panel
    /// list a figure's own bonuses only. Each song that reaches a figure (the engine's own CharacterClassManager
    /// .FindAllSongActiveBonuses, the lookup its abilities use) becomes a custom effect on the row with the Soothsinger's
    /// note icon and the card's name and text as its tooltip, and joins the figure's bonus list in the inspect panel.
    ///
    /// Pictures: a pack sprite name "GHClass_&lt;Class&gt;" (the Favorite's character resource, gh_port) is that class's
    /// own active-ability icon (the Hatchet's axe), which Frosthaven's icon table doesn't have.
    /// </summary>
    [HarmonyPatch]
    internal static class GHStatusIcons
    {
        private const string ClassIcon = "GHClass_";
        private const string SongId = "GHSong:";
        private static readonly ConditionalWeakTable<EffectsBar, HashSet<string>> Shown = new ConditionalWeakTable<EffectsBar, HashSet<string>>();
        private static readonly Dictionary<CActor, int> Logged = new Dictionary<CActor, int>();

        [HarmonyPatch(typeof(UIInfoTools), nameof(UIInfoTools.GetActiveAbilityIcon), new[] { typeof(string), typeof(string), typeof(bool) })]
        [HarmonyPrefix]
        private static bool Icon(UIInfoTools __instance, string iconName, ref Sprite __result)
        {
            if (iconName == null || !iconName.StartsWith(ClassIcon, StringComparison.Ordinal)) return true;
            __result = __instance.GetCharacterActiveAbilityIcon(iconName.Substring(ClassIcon.Length));
            return __result == null;
        }

        private static List<CActiveBonus> Songs(CActor actor)
        {
            if (actor == null || actor.IsDead || ScenarioManager.Scenario == null) return new List<CActiveBonus>();
            try { return CharacterClassManager.FindAllSongActiveBonuses(actor) ?? new List<CActiveBonus>(); }
            catch (Exception) { return new List<CActiveBonus>(); }
        }

        /// <summary>The song icons on the figure's status row, after the game has drawn its own.</summary>
        [HarmonyPatch(typeof(ActorBehaviour), nameof(ActorBehaviour.UpdateWorldspaceConditionsUI))]
        [HarmonyPostfix]
        private static void Row(ActorBehaviour __instance)
        {
            try
            {
                CActor actor = __instance.Actor;
                EffectsBar bar = __instance.m_WorldspacePanelUI?.EffectBar;
                if (actor == null || bar == null) return;
                HashSet<string> shown = Shown.GetOrCreateValue(bar);
                var now = new HashSet<string>();
                Sprite note = null;
                foreach (CActiveBonus song in Songs(actor))
                {
                    string id = SongId + song.ID;
                    now.Add(id);
                    note = note ?? UIInfoTools.Instance.GetCharacterActiveAbilityIcon(ECharacter.Soothsinger.ToString());
                    string title = "<size=+2><color=#" + UIInfoTools.DefInstance.basicTextColor.ToHex() + ">" + GLOOM.LocalizationManager.GetTranslation(song.BaseCard?.Name) + "</color></size>";
                    string text = null;
                    try { text = song.Layout?.ListLayouts?.Count > 0 ? CreateLayout.LocaliseText(song.Layout.ListLayouts[0]) : null; } catch (Exception) { }
                    bar.AddCustomEffect(id, note, title, text);
                }
                foreach (string id in shown.Where(x => !now.Contains(x)).ToList()) bar.RemoveCustomEffect(id);
                shown.Clear();
                shown.UnionWith(now);
                if (!Logged.TryGetValue(actor, out int was) || was != now.Count)
                {
                    Logged[actor] = now.Count;
                    if (now.Count > 0 || was > 0) Plugin.Log.LogInfo($"Gloomhaven songs: {actor.GetPrefabName()} is under {now.Count} song(s)");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("song row")) Plugin.Log.LogWarning("Gloomhaven songs: status row: " + e); }
        }

        /// <summary>A song starts or ends (the rules thread): every figure's row is redrawn through the game's own message.</summary>
        [HarmonyPatch(typeof(CActor), nameof(CActor.AddAugmentOrSong))]
        [HarmonyPostfix]
        private static void SongAdded(CActor __instance, CAbility ability)
        {
            if (ability?.Song == null) return;
            CBaseCard card = ability.Song.RegisteredBaseCard;
            Plugin.Log.LogInfo($"Gloomhaven songs: {__instance.GetPrefabName()} sings {card?.Name ?? "(no card)"}; {__instance.Songs.Count} song(s) held, "
                               + $"{card?.ActiveBonuses?.Count(b => b.IsSong) ?? 0} song bonus(es) on the card");
            RefreshAll();
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.RemoveSong))]
        [HarmonyPostfix]
        private static void SongRemoved() => RefreshAll();

        // a card entering or leaving the active area (songs live on the Soothsinger's active cards)
        [HarmonyPatch(typeof(Choreographer), "Handle_RefreshActiveBonusUI")]
        [HarmonyPostfix]
        private static void ActiveAreaChanged()
        {
            if (ScenarioManager.Scenario?.PlayerActors?.Any(p => p.CharacterClass.CharacterModel == ECharacter.Soothsinger) == true) RefreshAll();
        }

        private static void RefreshAll()
        {
            try
            {
                if (ScenarioManager.Scenario == null) return;
                foreach (CActor a in ScenarioManager.Scenario.AllAliveActors.ToList())
                    ScenarioRuleClient.MessageHandler(new CUpdateWorldspaceConditionsUI_MessageData(a));
            }
            catch (Exception e) { if (GHClasses.Reported.Add("song refresh")) Plugin.Log.LogWarning("Gloomhaven songs: refresh: " + e); }
        }

        /// <summary>A doomed enemy shows the doom on its status row. Frosthaven draws it only from the bonus's list layout
        /// (EffectsBar.ToggleDoomEffect, nothing when it's missing), which only Gloomhaven's Doom card element filled in:
        /// the ported card face is a plain Active, so the Algox Priest Rain of Arrows doomed showed nothing (the user's
        /// manual test, 2026-10-29). The game's own generator (the doom branch: the class icon, the card's name) runs
        /// first, with the card half's text as the doom's layout. Every Gloomhaven doom is on a bottom half.</summary>
        [HarmonyPatch(typeof(EffectsBar), nameof(EffectsBar.ToggleDoomEffect), new[] { typeof(string), typeof(CActiveBonus) })]
        [HarmonyPrefix]
        private static void DoomRow(CActiveBonus bonus)
        {
            try
            {
                if (bonus == null || !bonus.IsDoom || bonus.BaseCard == null || bonus.BaseCard.ID < 20000 || bonus.BaseCard.ID >= 30000) return;
                if (bonus.Layout?.ListLayouts != null && bonus.Layout.ListLayouts.Count > 0 && bonus.Layout.ListLayouts[0] != null) return;
                string text = "$GH_" + (bonus.BaseCard.ID - 20000) + "_B$";
                CDoom doom = bonus.Caster?.Dooms?.FirstOrDefault(d => d.DoomAbilities.Any(a => a == bonus.Ability));
                if (doom != null && string.IsNullOrEmpty(doom.ActiveBonusLayout)) doom.ActiveBonusLayout = text;
                ActiveBonusUIUtils.CheckForLayoutToGenerate(bonus, out _);
                if (bonus.Layout?.ListLayouts == null) return;
                if (bonus.Layout.ListLayouts.Count == 0) bonus.Layout.ListLayouts.Add(text);
                else if (bonus.Layout.ListLayouts[0] == null) bonus.Layout.ListLayouts[0] = text;
                if (GHClasses.Reported.Add("doom row " + bonus.BaseCard.ID)) Plugin.Log.LogInfo($"Gloomhaven dooms: {bonus.BaseCard.Name} shows on the doomed figure");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("doom row")) Plugin.Log.LogWarning("Gloomhaven dooms: status row: " + e); }
        }

        /// <summary>The inspect panel lists the songs on the figure with its other bonuses, as Gloomhaven does.</summary>
        [HarmonyPatch(typeof(ActorStatPanel), "GetAllActiveBonuses")]
        [HarmonyPostfix]
        private static void Panel(CActor actor, List<CActiveBonus> __result) => AddSongs(actor, __result);

        [HarmonyPatch(typeof(UICharacterUtils), nameof(UICharacterUtils.GetAllActiveBonuses))]
        [HarmonyPostfix]
        private static void Utils(CActor actor, List<CActiveBonus> __result) => AddSongs(actor, __result);

        private static void AddSongs(CActor actor, List<CActiveBonus> list)
        {
            if (list == null) return;
            try { foreach (CActiveBonus s in Songs(actor)) if (!list.Contains(s)) list.Add(s); }
            catch (Exception e) { if (GHClasses.Reported.Add("song panel")) Plugin.Log.LogWarning("Gloomhaven songs: inspect panel: " + e); }
        }
    }
}
