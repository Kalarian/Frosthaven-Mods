using System;
using System.Collections.Generic;
using System.Linq;
using FHGUI.DTO;
using FHRL;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Show personal quest rewards": each personal quest card the game offers (a new character's pick of two,
    /// and the extra quest a 15-inspiration retirement draws) gets a line under its goal with the building it unlocks on
    /// retirement. The board game prints this on the card (rulebook p. 63, "Rewards: the building envelope unlocked");
    /// the digital card leaves it out.
    ///
    /// The reward is worked out the way the game does at retirement (CharacterController.Action_ApplyPersonalQuestRewards):
    /// the quest's first envelope that isn't open yet (an envelope counts as open once its building is unlocked), or, with
    /// both open, a random side scenario and a random item blueprint. The line also says what comes next if that building
    /// gets unlocked first, and whether another party member's quest would unlock the same building first. Display only.
    /// </summary>
    [HarmonyPatch]
    internal static class QuestRewards
    {
        private const string Name = "FrosthavenMods_QuestRewards";

        private static bool On => ModRules.IsOn(ModRules.QuestRewards);

        // what each building lets you do, after its name
        private static readonly Dictionary<uint, string> Uses = new Dictionary<uint, string>
        {
            [21] = "buy materials",
            [24] = "grow herbs",
            [37] = "buy items",
            [39] = "more items to buy",
            [42] = "blessings",
            [44] = "enhance cards",
            [65] = "sell metal",
            [67] = "sell lumber",
            [72] = "sell hide",
            [74] = "festivals",
            [81] = "trials and favors",
            [83] = "respec characters",
            [85] = "cheaper building",
            [88] = "pets",
            [90] = "challenges",
        };

        [HarmonyPatch(typeof(UIPersonalQuestCard), nameof(UIPersonalQuestCard.Init))]
        [HarmonyPostfix]
        private static void AddRewardLine(UIPersonalQuestCard __instance, CharacterPersonalQuestDTO personalQuest)
        {
            try
            {
                string text = null;
                if (On && personalQuest?.Def != null)
                {
                    CampaignController campaign = Singleton<FHMapChoreographer>.InstanceFast?.View?.GameController;
                    if (campaign != null) text = Describe(campaign, personalQuest.Def);
                    Plugin.Log.LogInfo($"Show personal quest rewards: {personalQuest.Def.ID.ID}: " +
                                       (text == null ? "(nothing shown)" : text.Replace("\n", " | ")));
                }
                Show(__instance, text);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Show personal quest rewards: " + e.Message);
            }
        }

        /// <summary>The reward line for a quest, as of now (null if it has none we can name).</summary>
        internal static string Describe(CampaignController campaign, PersonalQuestDef def)
        {
            List<uint> rewards = def.GetAvailableRewardIDs().ToList();
            if (!rewards.All(PersonalQuestEnvelope.envelopes.ContainsKey))
            {
                // a quest with its own reward (PQ 23 upgrades the Hall of Revelry)
                var effects = new List<CampaignEffect>();
                foreach (uint r in rewards) def.GetRewards(r, effects);
                List<string> parts = effects.Select(e =>
                        e.Type == CampaignEffectType.UpgradeBuilding ? "upgrades " + Building(e.Param1)
                        : e.Type == CampaignEffectType.UnlockBuilding ? "unlocks " + Building(e.Param1)
                        : null)
                    .Where(p => p != null).ToList();
                return parts.Count == 0 ? null : Gold("On retirement:") + " " + string.Join(", ", parts) + ".";
            }

            List<uint> open = rewards.Where(campaign.IsEnvelopeOpened).ToList();
            List<uint> left = rewards.Where(r => !open.Contains(r)).ToList();
            if (left.Count == 0)
                return Gold("On retirement:") + " no new building (" + string.Join(" and ", rewards.Select(Building)) + " already unlocked): " +
                       "a random side scenario and a random item blueprint instead.";

            var lines = new List<string> { Gold("On retirement:") + " unlocks " + Building(left[0]) + "." };
            string next = left.Count > 1 ? Building(left[1]) : "a random side scenario and item blueprint";
            if (open.Count > 0)
                lines.Add(Dim($"({string.Join(" and ", open.Select(Building))} already unlocked.)"));

            // another party member's quest that would open the same envelope first (this says what the fallback is)
            bool clash = false;
            foreach (CharacterState c in campaign.State.ActiveCharacters)
            {
                PersonalQuestDef other = c.PersonalQuestDef;
                if (other == null || other == def || campaign.State.CompletedPersonalQuests.Contains(c.PersonalQuest)) continue;
                uint theirs = other.GetAvailableRewardIDs().FirstOrDefault(r => !campaign.IsEnvelopeOpened(r));
                if (theirs != left[0]) continue;
                lines.Add(Dim($"{c.Name}'s quest also unlocks {Building(left[0], false)}: whoever retires second gets {next}."));
                clash = true;
            }
            if (open.Count == 0 && !clash)
                lines.Add(Dim($"If {Building(left[0], false)} gets unlocked first: {next} instead."));
            return string.Join("\n", lines);
        }

        private static string Gold(string s) => "<color=#E3C98B>" + s + "</color>";

        private static string Dim(string s) => "<color=#BBB0A3><size=85%>" + s + "</size></color>";

        private static string Building(string id) => uint.TryParse(id, out uint n) ? Building(n) : id;

        private static string Building(uint id) => Building(id, true);

        /// <summary>The game's name for the building, then what it's for.</summary>
        private static string Building(uint id, bool withUse)
        {
            string name = null;
            try
            {
                string key = UIInfoTools.Instance?.GetBuildingConfig(new BuildingTypeID(id))?.GetName();
                if (!string.IsNullOrEmpty(key)) name = GLOOM.LocalizationManager.GetTranslation(key);
            }
            catch (Exception)
            {
                // fall back to the number
            }
            if (string.IsNullOrEmpty(name)) name = "building " + id;
            return withUse && Uses.TryGetValue(id, out string use) ? $"{name} ({use})" : name;
        }

        /// <summary>Puts the line under the goal text, inside the card's text group (the card resizes to fit).</summary>
        private static void Show(UIPersonalQuestCard card, string text)
        {
            TMP_Text goal = card.TextGoalDescription;
            if (goal == null) return;
            Transform group = goal.transform.parent;
            Transform existing = group.Find(Name);
            if (string.IsNullOrEmpty(text))
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }
            TextMeshProUGUI line = existing != null ? existing.GetComponent<TextMeshProUGUI>() : Create(group, goal);
            if (line == null) return;
            line.gameObject.SetActive(true);
            line.transform.SetSiblingIndex(goal.transform.GetSiblingIndex() + 1);
            line.text = text;
            UiDump.Once("Personal quest card (with rewards)", card.transform);
        }

        private static TextMeshProUGUI Create(Transform group, TMP_Text goal)
        {
            var go = new GameObject(Name, typeof(RectTransform));
            go.transform.SetParent(group, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = goal.font;
            text.fontSharedMaterial = goal.fontSharedMaterial;
            text.color = goal.color;
            text.fontSize = Math.Max(14f, goal.fontSize * 0.85f);
            text.enableWordWrapping = true;
            text.alignment = goal.alignment;
            text.raycastTarget = false;
            text.margin = new Vector4(0f, 8f, 0f, 0f);
            // as wide as the goal text; the group's layout may or may not size its children, so set both ways
            float width = ((RectTransform)goal.transform).rect.width > 0 ? ((RectTransform)goal.transform).rect.width : 287f;
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, 0f);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            VerticalLayoutGroup layout = group.GetComponent<VerticalLayoutGroup>();
            if (layout == null || !layout.childControlHeight)
                go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return text;
        }
    }
}
