using System;
using System.Collections.Generic;
using System.Linq;
using FHGUI;
using FHGUI.DTO;
using FHGUI.Modules;
using FHGUI.Services;
using FHRL;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Reveal event outcomes": on a town, road or boat event, each option shows what it will do (a line of
    /// effects under the option) and its outcome text (on hover). Board game rule bent (p. 12): pick an option, then
    /// read only that outcome.
    ///
    /// Outcomes are not data: each event is a class (FHRL.OutpostEvents.*_OutpostEventDef) whose ApplyOption works the
    /// effects out when the option is chosen - checking traits, races, buildings, resources, and rolling dice. So each
    /// option is played out on a throwaway copy of the campaign (FHGlue.BinarySerializeState / BinaryDeserialzieState,
    /// the copy the host sends to joining players, run by its own CampaignController) and the copy's results are shown:
    /// the effects, the outcome text the game would show, and what comes after (choices you'll make, an outpost attack,
    /// a next step). The copy carries the dice state too, so random outcomes show the roll you'd actually get. The real
    /// campaign is never touched (display only, nothing to sync).
    /// </summary>
    [HarmonyPatch]
    internal static class EventPreview
    {
        private sealed class Preview
        {
            public string Summary;   // one line under the option
            public string Outcome;   // the outcome text (hover)
        }

        private static string _key;
        private static readonly Dictionary<uint, Preview> Previews = new Dictionary<uint, Preview>();

        [HarmonyPatch(typeof(OutpostEventsService), nameof(OutpostEventsService.GetEventDTO), new[] { typeof(OutpostEventState) })]
        [HarmonyPostfix]
        private static void AddPreviews(OutpostEventState _event, OutpostEventDTO __result)
        {
            if (!ModRules.IsOn(ModRules.RevealEvents) || __result?.Options == null || __result.Options.Count == 0) return;
            try
            {
                var map = Singleton<FHMapChoreographer>.InstanceFast;
                CampaignController live = map?.View?.GameController;
                if (live == null || _event == null || live.OutpostEventController.EventToResolve != _event) return;
                Predict(map, _event, __result.Options.Select(o => o.ID).ToList());
                foreach (OutpostEventDTO.Option option in __result.Options)
                    if (Previews.TryGetValue(option.ID, out Preview p) && !string.IsNullOrEmpty(p.Summary))
                        option.Text += "\n<size=72%><color=#E6D3A3>" + p.Summary + "</color></size>";
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Reveal event outcomes: no preview for this event: " + e);
            }
        }

        /// <summary>Plays each option out on its own copy of the campaign. Cached per event step.</summary>
        private static void Predict(FHMapChoreographer map, OutpostEventState ev, List<uint> optionIDs)
        {
            string key = $"{ev.ID.ID}/{ev.OutcomeIteration}/{ev.OptionSelectedID}/{string.Join(",", optionIDs)}";
            if (key == _key) return;
            _key = key;
            Previews.Clear();
            DefRepository repo = map.DefRepo;
            MultiplayerGameState liveState = map.View.MultiplayerGameController.GameState;
            byte[] snapshot = FHGlue.BinarySerializeState(liveState, repo);
            var service = map.View.GetService<OutpostEventsService>();
            foreach (uint id in optionIDs)
            {
                try { Previews[id] = PlayOut(snapshot, repo, id, service); }
                catch (Exception e)
                {
                    string why = (e.InnerException ?? e).Message;
                    Previews[id] = new Preview { Summary = "Not available: " + why };
                    Plugin.Log.LogWarning($"Reveal event outcomes: {ev.ID.ID} option {id}: {e}");
                }
            }
            Plugin.Log.LogInfo($"Reveal event outcomes: {ev.ID.ID} step {ev.OutcomeIteration}: " +
                               string.Join(" | ", Previews.Select(p => $"{p.Key}: {p.Value.Summary}")));
        }

        private static Preview PlayOut(byte[] snapshot, DefRepository repo, uint optionID, OutpostEventsService service)
        {
            MultiplayerGameState copy = FHGlue.BinaryDeserialzieState(snapshot, repo);
            CampaignState state = copy.CampaignState;
            var sim = new CampaignController(state, repo);
            OutpostEventState ev = sim.OutpostEventController.EventToResolve;
            if (ev == null) return new Preview { Summary = "" };
            OutpostEventDef.Option option = ev.Def.GetOptions(state, ev).FirstOrDefault(o => o.ID == optionID);
            if (!option.IsNotEmpty) return new Preview { Summary = "" };
            if (!sim.OutpostEventController.IsValidOption(option)) return new Preview { Summary = "Requirement not met." };

            int effectsBefore = ev.AppliedEffectsFromOutcome.Count;
            var actionsBefore = new HashSet<CampaignInteractionType>(state.UnresolvedActions.Select(a => a.Type));
            sim.OutpostEventController.ResolveCurrentEvent(option);

            List<CampaignEffect> effects = ev.AppliedEffectsFromOutcome.Skip(effectsBefore).ToList();
            var parts = new List<string>();
            foreach (CampaignEffect effect in service.CombineCampaignEffects(effects.Where(e => e.Type != CampaignEffectType.TriggerAttackOutpost)))
            {
                OutpostEventDTO.Effect shown = service.GetFormattedEffect(effect);
                if (shown != null && !shown.Hidden && !string.IsNullOrWhiteSpace(shown.Text)) parts.Add(Clean(shown.Text));
            }
            if (effects.Any(e => e.Type == CampaignEffectType.TriggerAttackOutpost)) parts.Add("Outpost attack!");
            foreach (var action in state.UnresolvedActions.Select(a => a.Type).Distinct().Where(t => !actionsBefore.Contains(t)))
                parts.Add("Then: " + Words(action.ToString()));
            if (!ev.Completed) parts.Add("The event continues");

            return new Preview
            {
                Summary = parts.Count == 0 ? "No effect" : string.Join(" • ", parts),
                Outcome = OutcomeText(ev),
            };
        }

        /// <summary>The outcome text the game would show after this choice.</summary>
        private static string OutcomeText(OutpostEventState ev)
        {
            try
            {
                if (ev.CustomOutcomeLocKeys != null && ev.CustomOutcomeLocKeys.Count > 0)
                    return NarrationSetting.FromCustomEventOutcome(ev);
                string term = I2.Loc.I2Utils.GetValidTermName(NarrationSetting.FromOutpostEventOutcome(ev).Text);
                return I2.Loc.LocalizationManager.GetTermTranslation("FH/" + term);
            }
            catch (Exception e)
            {
                return "(outcome text unavailable: " + e.Message + ")";
            }
        }

        private static string Clean(string text) => text.Replace("\n", " ").Trim();

        /// <summary>"Event_EverybodyChooseLoseUpToGold" -> "everybody choose lose up to gold".</summary>
        private static string Words(string name)
        {
            name = name.StartsWith("Event_") ? name.Substring(6) : name;
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (c == '_') { sb.Append(' '); continue; }
                if (char.IsUpper(c) && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                sb.Append(sb.Length == 0 ? c : char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        // ---- the outcome text on hover over each option button

        [HarmonyPatch(typeof(OutpostEventModule), "PopulateOptions")]
        [HarmonyPostfix]
        private static void AddTooltips(OutpostEventModule __instance, List<FHButton> ___m_Buttons)
        {
            GiveResourcesPatches.Safe("add the event outcome tooltips", () =>
            {
                OutpostEventDTO data = Traverse.Create(__instance).Property("Data").GetValue<OutpostEventDTO>();
                if (data?.Options == null || ___m_Buttons == null) return;
                bool on = ModRules.IsOn(ModRules.RevealEvents);
                for (int i = 0; i < ___m_Buttons.Count; i++)
                {
                    FHButton button = ___m_Buttons[i];
                    if (button == null) continue;
                    var tip = button.GetComponent<UITextTooltipTarget>();
                    Preview p = null;
                    bool has = on && i < data.Options.Count && Previews.TryGetValue(data.Options[i].ID, out p) && !string.IsNullOrEmpty(p.Outcome);
                    if (!has) { if (tip != null) tip.enabled = false; continue; }
                    if (tip == null)
                    {
                        tip = button.gameObject.AddComponent<UITextTooltipTarget>();
                        tip.Initialize(UITooltipManager.Corner.Auto, Vector2.zero, false, 560f, 50f, true, false);
                    }
                    tip.enabled = true;
                    tip.SetText(p.Outcome + "\n\n<color=#E6D3A3>" + p.Summary + "</color>");
                }
            });
        }
    }
}
