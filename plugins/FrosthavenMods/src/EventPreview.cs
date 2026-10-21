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
    /// House rule "Reveal event outcomes": on a town, road or boat event, hovering over an option shows what it will do:
    /// its outcome text, then its effects. Board game rule bent (p. 12): pick an option, then
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
            public string Summary;   // the effects (tooltip, after the outcome)
            public string Outcome;   // the outcome text (tooltip)
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
                // shown in each option's tooltip (AddTooltips); the option buttons keep the game's own text
                Predict(map, _event, __result.Options.Select(o => o.ID).ToList());
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
            var actionsBefore = new HashSet<CampaignUnresolvedAction>(state.UnresolvedActions);
            sim.OutpostEventController.ResolveCurrentEvent(option);
            string outcome = OutcomeText(ev);

            // An option can stop at steps the party carries out first (pay 10 gold, pick herbs to give up...) and then
            // continue. Show each step's amounts, then play the rest out as the game does once they're done.
            var steps = new List<string>();
            for (int step = 0; ; step++)
            {
                List<CampaignEffect> effects = ev.AppliedEffectsFromOutcome.Skip(effectsBefore).ToList();
                effectsBefore = ev.AppliedEffectsFromOutcome.Count;
                var parts = new List<string>();
                foreach (CampaignEffect effect in service.CombineCampaignEffects(effects.Where(e => e.Type != CampaignEffectType.TriggerAttackOutpost)))
                {
                    OutpostEventDTO.Effect shown = service.GetFormattedEffect(effect);
                    if (shown != null && !shown.Hidden && !string.IsNullOrWhiteSpace(shown.Text)) parts.Add(Clean(shown.Text));
                }
                if (effects.Any(e => e.Type == CampaignEffectType.TriggerAttackOutpost)) parts.Add("Outpost attack!");
                List<CampaignUnresolvedAction> pending = state.UnresolvedActions.Where(a => !actionsBefore.Contains(a)).ToList();
                parts.AddRange(pending.Select(a => Describe(a, state)).Distinct());
                if (parts.Count > 0) steps.Add((steps.Count == 0 ? "" : "Then: ") + string.Join(" • ", parts));
                if (ev.Completed) break;
                if (!ev.OptionSelected) { steps.Add("Then you choose again"); break; }
                if (step >= 4) { steps.Add("The event continues"); break; }

                // the party has done its part: on this copy, mark the steps done and let the event go on
                foreach (CampaignUnresolvedAction a in pending) state.UnresolvedActions.Remove(a);
                state.AnyHerbsToLose = state.AnyHerbsToGain = 0;
                state.MaterialsToLose = state.MaterialsToGain = default;
                try { sim.OutpostEventController.ContinueResolveCurrentEvent(); }
                catch { steps.Add("The event continues"); break; }
            }

            return new Preview
            {
                Summary = steps.Count == 0 ? "No effect" : string.Join("\n", steps),
                Outcome = outcome,
            };
        }

        /// <summary>A step the party carries out, with its amount where the game records one.</summary>
        private static string Describe(CampaignUnresolvedAction a, CampaignState state)
        {
            int n = a.GetInt(0);
            switch (a.Type)
            {
                case CampaignInteractionType.LoseGold: return n > 0 ? $"Pay {n} gold (from any characters)" : "Pay gold";
                case CampaignInteractionType.GainGold: return n > 0 ? $"Gain {n} gold (shared as you like)" : "Gain gold";
                case CampaignInteractionType.LoseAnyHerbs: return state.AnyHerbsToLose > 0 ? $"Pay {state.AnyHerbsToLose} herbs of any kind" : "Pay herbs";
                case CampaignInteractionType.GainAnyHerbs: return state.AnyHerbsToGain > 0 ? $"Gain {state.AnyHerbsToGain} herbs of any kind" : "Gain herbs";
                case CampaignInteractionType.LoseMaterials: return "Pay " + Amounts(state.MaterialsToLose, "materials");
                case CampaignInteractionType.GainMaterials: return "Gain " + Amounts(state.MaterialsToGain, "materials");
                default: return Words(a.Type.ToString()) + (n > 0 ? $" ({n})" : "");
            }
        }

        private static string Amounts(OutpostResourceChange c, string fallback)
        {
            var parts = new List<string>();
            if (c.Resources.Lumber > 0) parts.Add($"{c.Resources.Lumber} lumber");
            if (c.Resources.Metal > 0) parts.Add($"{c.Resources.Metal} metal");
            if (c.Resources.Hide > 0) parts.Add($"{c.Resources.Hide} hide");
            if (c.Resources.TotalHerbs > 0) parts.Add($"{c.Resources.TotalHerbs} herbs");
            if (c.AnyMaterial > 0) parts.Add($"{c.AnyMaterial} materials of any kind");
            if (c.AnyHerb > 0) parts.Add($"{c.AnyHerb} herbs of any kind");
            return parts.Count == 0 ? fallback : string.Join(", ", parts);
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

        /// <summary>One line, without the game's size/font/style tags (e.g. "New quest unlocked!" sets the quest name in
        /// a large title font, which overrode the small preview size and shrank the whole option button).</summary>
        private static string Clean(string text) =>
            LayoutTags.Replace(text, "").Replace("\n", " ").Trim();

        private static readonly System.Text.RegularExpressions.Regex LayoutTags = new System.Text.RegularExpressions.Regex(
            "</?(size|font|style|voffset|line-height|cspace|mspace|allcaps|uppercase|lowercase|smallcaps|align|indent|margin|pos|width)(=[^>]*)?>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
                    bool has = on && i < data.Options.Count && Previews.TryGetValue(data.Options[i].ID, out p)
                               && (!string.IsNullOrEmpty(p.Outcome) || !string.IsNullOrEmpty(p.Summary));
                    if (!has) { if (tip != null) tip.enabled = false; continue; }
                    if (tip == null)
                    {
                        tip = button.gameObject.AddComponent<UITextTooltipTarget>();
                        // autoAdjustHeight false, as in all of the game's own text tooltips: for this tooltip type it
                        // means "fit the text" (true keeps the last height, so the framed background collapses).
                        tip.Initialize(UITooltipManager.Corner.Auto, Vector2.zero, false, 560f, 50f, autoAdjustHeight: false, hideBackground: false);
                    }
                    OurTips.Add(button.transform.GetInstanceID());
                    tip.enabled = true;
                    string effects = string.IsNullOrEmpty(p.Summary) ? "" : "<color=#E6D3A3>" + p.Summary + "</color>";
                    tip.SetText(string.IsNullOrEmpty(p.Outcome) ? effects : effects.Length == 0 ? p.Outcome : p.Outcome + "\n\n" + effects);
                }
            });
        }

        // ---- where the tooltip sits and its background, for these tooltips only

        /// <summary>The option buttons that carry an outcome tooltip (transform instance IDs).</summary>
        private static readonly HashSet<int> OurTips = new HashSet<int>();

        private static readonly Color PanelColor = new Color(0.075f, 0.09f, 0.12f, 1f);
        private const float Gap = 8f;

        /// <summary>The game places a tooltip by a corner of the target, so "above the option, left edges lined up"
        /// isn't one of its choices: put it there after the game positions it. The game's framed background is partly
        /// see-through, so an opaque panel goes behind the text; it's switched off again for every other tooltip.</summary>
        [HarmonyPatch(typeof(UITooltipManager), nameof(UITooltipManager.UpdatePositionAndPivot))]
        [HarmonyPostfix]
        private static void PlaceTooltip(UITooltipManager __instance, RectTransform ___m_AnchorToTarget, GameObject ___m_Background)
        {
            try
            {
                bool ours = ___m_AnchorToTarget != null && OurTips.Contains(___m_AnchorToTarget.GetInstanceID());
                SetPanel(___m_Background, ours);
                if (!ours) return;
                var rect = (RectTransform)__instance.transform;
                rect.pivot = Vector2.zero;
                var corners = new Vector3[4];
                ___m_AnchorToTarget.GetWorldCorners(corners); // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
                rect.position = corners[1] + ___m_AnchorToTarget.TransformVector(new Vector3(0f, Gap, 0f));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Reveal event outcomes: could not place the tooltip: " + e.Message);
            }
        }

        private static void SetPanel(GameObject background, bool on)
        {
            if (background == null) return;
            Transform existing = background.transform.Find("FrosthavenMods Opaque");
            if (existing == null)
            {
                if (!on) return;
                var go = new GameObject("FrosthavenMods Opaque", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(background.transform, false);
                go.transform.SetAsFirstSibling(); // over the background's own image, under its frame ornaments
                var r = (RectTransform)go.transform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = r.offsetMax = Vector2.zero;
                var image = go.GetComponent<Image>();
                image.color = PanelColor;
                image.raycastTarget = false;
                existing = go.transform;
            }
            existing.gameObject.SetActive(on);
        }
    }
}
