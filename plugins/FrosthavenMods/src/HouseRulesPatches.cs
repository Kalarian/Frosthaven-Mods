using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FHGUI;
using FHGUI.DTO;
using HarmonyLib;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// Turns the game's hidden House Rules screen (Options > House Rules, campaign only) on and fills it with our rules.
    /// In this build the screen is unreachable because GameSettingsService.ValidCampaignRules is None. We leave the
    /// vanilla rules hidden (some are unfinished, e.g. MergedPartyResources has no code behind it) and append our own.
    /// </summary>
    [HarmonyPatch]
    internal static class HouseRulesPatches
    {
        // DTOs we created, so GetSettingsDTO can give them a callback that reads the rules at click time.
        private static readonly ConditionalWeakTable<CampaignSettingDTO, ModRules.Rule> Ours = new ConditionalWeakTable<CampaignSettingDTO, ModRules.Rule>();

        /// <summary>Show the House Rules entry in the options menu for campaigns.</summary>
        [HarmonyPatch(typeof(GameSettingsService), nameof(GameSettingsService.CanShowHouseRulesOptions))]
        [HarmonyPostfix]
        private static void ShowHouseRules(ref bool __result)
        {
            if (!__result && SaveData.Instance?.Global != null && SaveData.Instance.Global.GameMode == EGameMode.Campaign)
                __result = true;
        }

        /// <summary>Append our rules to the "general" (changeable mid-campaign) list. Used by the in-game screen and the
        /// new-campaign window alike.</summary>
        [HarmonyPatch(typeof(GameSettingsService), nameof(GameSettingsService.PopulateSettingsDTO))]
        [HarmonyPostfix]
        private static void AddOurRules(ERulesFlag rules, bool interactable, Action<ERulesFlag, bool> onRuleChangeCallback, ref UICampaignSettingsWindow.Params __result)
        {
            try
            {
                if (__result.General == null) __result.General = new List<CampaignSettingDTO>();
                if (__result.CampaignSpecific == null) __result.CampaignSpecific = new List<CampaignSettingDTO>();
                __result.General.Add(new CampaignSettingDTO { NameLocalized = "Mod pack rules", IsCaption = true });
                __result.General.Add(new CampaignSettingDTO
                {
                    NameLocalized = "Everyone in a co-op game needs the same mod pack version. Only the host can change these.",
                    IsDescription = true,
                });
                // listed by name, whatever order they're defined in
                foreach (ModRules.Rule rule in ModRules.All.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    ModRules.Rule r = rule;
                    bool isOn = (rules & r.Flag) == r.Flag;
                    string blocked = Blocked(r, !isOn);
                    var dto = new CampaignSettingDTO
                    {
                        NameLocalized = r.Name,
                        DescriptionLocalized = r.Description + Note(blocked),
                        TooltipLocalized = r.Description + Note(blocked),
                        Value = new CampaignSettingDTO.SettingValue(isOn, false),
                        Interactable = interactable && blocked == null,
                        OnValueChange = result =>
                        {
                            if (result is bool on && Blocked(r, on) == null) onRuleChangeCallback?.Invoke(r.Flag, on);
                        },
                    };
                    Ours.Add(dto, r);
                    __result.General.Add(dto);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("House rules: could not add the mod rules to the settings screen: " + e);
            }
        }

        /// <summary>The game only allows house-rule changes on the map/outpost, but the prologue is scenarios back to back.
        /// Our rules only matter in the outpost and travel as a side action outside the scenario's lockstep state, so
        /// they can change any time in a game: solo, or by the host while nobody is joining (the game's own conditions
        /// minus the map-only one).</summary>
        private static bool CanChangeOurRulesNow()
        {
            if (SaveData.Instance?.Global == null || SaveData.Instance.Global.CurrentGameState == EGameState.None) return false;
            if (FFSNetwork.IsShuttingDown || FFSNetwork.IsStartingUp) return false;
            if (!FFSNetwork.IsOnline) return true;
            return FFSNetwork.IsHost && FFSNet.PlayerRegistry.JoiningPlayers.Count == 0 && FFSNet.PlayerRegistry.ConnectingUsers.Count == 0;
        }

        /// <summary>In a scenario of an online game. Rules that change play are locked then: a rule change travels outside
        /// the scenario's lockstep, so it could take effect at different moments on different PCs.</summary>
        private static bool OnlineScenario()
        {
            try { return FFSNetwork.IsOnline && SaveData.Instance?.Global?.CurrentGameState == EGameState.Scenario; }
            catch { return false; }
        }

        /// <summary>The in-game screen's own callback captures the rules as they were when the screen opened, so flipping
        /// two switches in a row would undo the first. Our switches read the current rules at click time instead.</summary>
        [HarmonyPatch(typeof(GameSettingsService), nameof(GameSettingsService.GetSettingsDTO))]
        [HarmonyPostfix]
        private static void FreshCallbacks(ref UICampaignSettingsWindow.Params __result)
        {
            if (__result.General == null) return;
            bool canChange = CanChangeOurRulesNow();
            bool onlineScenario = OnlineScenario();
            foreach (CampaignSettingDTO dto in __result.General)
            {
                if (!Ours.TryGetValue(dto, out ModRules.Rule r)) continue;
                bool locked = r.AffectsScenarios && onlineScenario;
                string blocked = locked ? null : Blocked(r, !ModRules.IsOn(r.Flag));
                dto.Interactable = canChange && !locked && blocked == null;
                // AddOurRules built these texts from the rules as they were then; rebuild them for now
                string note = locked ? ModRules.LockedNote : Note(blocked);
                dto.DescriptionLocalized = r.Description + note;
                dto.TooltipLocalized = r.Description + note;
                dto.OnValueChange = result =>
                {
                    if (!(result is bool on)) return;
                    if (r.AffectsScenarios && OnlineScenario()) return; // locked (see ModRules.Rule.AffectsScenarios)
                    if (Blocked(r, on) != null) return;                  // greyed out (see ModRules.Rule.Blocked)
                    Change(r, on);
                };
            }
        }

        /// <summary>Why <paramref name="r"/> can't be switched to <paramref name="wantOn"/> now, or null (never throws).</summary>
        private static string Blocked(ModRules.Rule r, bool wantOn)
        {
            try { return r.Blocked?.Invoke(wantOn); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"House rules: the guard of '{r.Name}' failed: {e}");
                return "This switch can't be checked right now.";
            }
        }

        private static string Note(string blocked) => blocked == null ? "" : "\n\n" + ModRules.Red("(" + blocked + ")");

        /// <summary>Flip a rule in the current campaign, after the backup save a rule may ask for when it goes on.</summary>
        private static async void Change(ModRules.Rule r, bool on)
        {
            try
            {
                PartyAdventureData data = SaveData.Instance?.Global?.CurrentAdventureData;
                if (data == null) return;
                if (on && r.BackupBeforeOn != null && !data.IsIronMan)
                {
                    try
                    {
                        await SaveData.Instance.ManualSaveCurrentAdventureData(r.BackupBeforeOn);
                        CoopLog.Write($"BACKUP SAVE '{r.BackupBeforeOn}' before '{r.Name}' goes on");
                    }
                    catch (Exception e) { Plugin.Log.LogWarning($"House rules: the backup save before '{r.Name}' failed: {e.Message}"); }
                    data = SaveData.Instance?.Global?.CurrentAdventureData;
                    if (data == null) return;
                }
                ERulesFlag next = on ? data.Rules | r.Flag : data.Rules & ~r.Flag;
                CoopLog.Write($"RULES CHANGED here: '{r.Name}' -> {(on ? "on" : "off")} (rules {(int)data.Rules:X} -> {(int)next:X}: {ModRules.Describe(next)}) role={CoopLog.Role()}");
                data.ChangeCampaignRules(next);
                CoopLog.Snapshot("after rule change");
            }
            catch (Exception e) { Plugin.Log.LogError($"House rules: could not change '{r.Name}': {e}"); }
        }

        /// <summary>Game bug: the rules are written to the save header but ApplyHeader never reads them back, so every
        /// house rule would reset to off on reload. Restore them.</summary>
        [HarmonyPatch(typeof(PartyAdventureData), "ApplyHeader")]
        [HarmonyPostfix]
        private static void RestoreRulesFromHeader(PartyAdventureData __instance, PartyAdventureData.Header header)
        {
            try
            {
                if (header.Rules == __instance.Rules) return;
                AccessTools.PropertySetter(typeof(PartyAdventureData), nameof(PartyAdventureData.Rules)).Invoke(__instance, new object[] { header.Rules });
                if ((header.Rules & ModRules.Mask) != 0)
                    CoopLog.Write($"RULES LOADED '{header.CampaignName}': {ModRules.Describe(header.Rules)} (rules {(int)header.Rules:X})");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("House rules: could not restore the saved rules: " + e);
            }
        }

        /// <summary>Clients get rule changes through this network action; log them so a co-op test can be checked.</summary>
        [HarmonyPatch(typeof(HouseRulesSettings), nameof(HouseRulesSettings.ProxySetHouseRuleSettings))]
        [HarmonyPostfix]
        private static void LogReceived()
        {
            ERulesFlag now = ModRules.Current;
            CoopLog.Write($"RULES RECEIVED from the host: rules {(int)now:X}: {ModRules.Describe(now)}");
            CoopLog.Snapshot("after rules received");
        }

        /// <summary>Game bug: the settings window destroys its rows on every rebuild but never empties its row list, so the
        /// list grows with destroyed objects each time a switch is flipped.</summary>
        [HarmonyPatch(typeof(UICampaignSettingsWindow), "ClearSettingsContainer")]
        [HarmonyPostfix]
        private static void ClearRowList(List<GameObject> ___m_SettingRows)
        {
            ___m_SettingRows?.Clear();
        }
    }
}
