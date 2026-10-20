using System;
using System.Collections.Generic;
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
                foreach (ModRules.Rule rule in ModRules.All)
                {
                    ModRules.Rule r = rule;
                    var dto = new CampaignSettingDTO
                    {
                        NameLocalized = r.Name,
                        DescriptionLocalized = r.Description,
                        TooltipLocalized = r.Description,
                        Value = new CampaignSettingDTO.SettingValue((rules & r.Flag) == r.Flag, false),
                        Interactable = interactable,
                        OnValueChange = result =>
                        {
                            if (result is bool on) onRuleChangeCallback?.Invoke(r.Flag, on);
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

        /// <summary>The in-game screen's own callback captures the rules as they were when the screen opened, so flipping
        /// two switches in a row would undo the first. Our switches read the current rules at click time instead.</summary>
        [HarmonyPatch(typeof(GameSettingsService), nameof(GameSettingsService.GetSettingsDTO))]
        [HarmonyPostfix]
        private static void FreshCallbacks(ref UICampaignSettingsWindow.Params __result)
        {
            if (__result.General == null) return;
            bool canChange = CanChangeOurRulesNow();
            foreach (CampaignSettingDTO dto in __result.General)
            {
                if (!Ours.TryGetValue(dto, out ModRules.Rule r)) continue;
                dto.Interactable = canChange;
                dto.OnValueChange = result =>
                {
                    if (!(result is bool on)) return;
                    PartyAdventureData data = SaveData.Instance?.Global?.CurrentAdventureData;
                    if (data == null) return;
                    ERulesFlag next = on ? data.Rules | r.Flag : data.Rules & ~r.Flag;
                    Plugin.Log.LogInfo($"House rules: '{r.Name}' -> {(on ? "on" : "off")} (rules {(int)data.Rules:X} -> {(int)next:X}: {ModRules.Describe(next)})");
                    data.ChangeCampaignRules(next);
                };
            }
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
                    Plugin.Log.LogInfo($"House rules: '{header.CampaignName}' saved with {ModRules.Describe(header.Rules)}");
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
            Plugin.Log.LogInfo($"House rules: received from the host (rules {(int)now:X}: {ModRules.Describe(now)})");
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
