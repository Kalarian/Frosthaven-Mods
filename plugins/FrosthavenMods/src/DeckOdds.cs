using System;
using System.Collections.Generic;
using System.Linq;
using FHGUI.Common;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Modifier deck odds": adds the odds of the next attack modifier draw under the Attack Modifiers section
    /// of the game's info panel (right-click a character or monster in a scenario; UIGeneralInfoCharacter /
    /// UIGeneralInfoMonster, which already show each value as left / used): a character's own deck, or the deck that
    /// monster draws from (enemy, ally, neutral...), plus a tag on each modifier icon with that card's own chance.
    ///
    /// Odds are for the card that settles the attack: rolling cards keep the draw going, so the settling card is the
    /// first non-rolling card left in the deck, and by symmetry every non-rolling card left is equally likely to be it.
    /// "Rolling" is the chance the draw starts with a rolling card (an extra bonus on top). If no non-rolling card is
    /// left, the deck is reshuffled mid-draw, so the whole deck counts. Curses count as nulls and blesses as x2s, as they
    /// play. Display only: reads the decks, changes nothing.
    ///
    /// (Until v0.6.5 the line went in the pop-out by the hand, ModifiersDisplayController, but the game keeps that
    /// pop-out in a hidden group, so it never showed.)
    /// </summary>
    [HarmonyPatch]
    internal static class DeckOdds
    {
        private const string Name = "FrosthavenMods_DeckOdds";

        internal static bool On => ModRules.IsOn(ModRules.DeckOdds);

        private static readonly AccessTools.FieldRef<CMonsterAttackModifierDeck, bool> MonsterReshuffle =
            AccessTools.FieldRefAccess<CMonsterAttackModifierDeck, bool>("m_ShuffleAttackModifierCards");

        [HarmonyPatch(typeof(UIGeneralInfoCharacter), "ShowStatusTab")]
        [HarmonyPostfix]
        private static void CharacterOdds(UIGeneralInfoCharacter.PlayerCharacterGeneralInfoData ____data,
            UIGeneralInfoSection ____attackModifiersContainer)
        {
            try
            {
                CCharacterClass c = ____data.Actor?.CharacterClass;
                string text = c == null ? null : Describe("Next draw", Compute(c.AttackModifierCards, c.DiscardedAttackModifierCards,
                    c.m_ShuffleAttackModifierCards));
                Show(____attackModifiersContainer, text);
                ShowPerCard(____attackModifiersContainer, ____data.AttackModifiers, c?.AttackModifierCards, c?.DiscardedAttackModifierCards);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Modifier deck odds: " + e.Message);
            }
        }

        [HarmonyPatch(typeof(UIGeneralInfoMonster), "ShowStatusTab")]
        [HarmonyPostfix]
        private static void MonsterOdds(UIGeneralInfoMonster.MonsterGeneralInfoData ____data, UIGeneralInfoSection ____attackModifiersContainer)
        {
            try
            {
                CMonsterAttackModifierDeck deck = null;
                if (On && !string.IsNullOrEmpty(____data.ActorGuid) && ScenarioManager.Scenario != null)
                    deck = ScenarioManager.Scenario.AllActors.FirstOrDefault(a => a.ActorGuid == ____data.ActorGuid) is CEnemyActor enemy
                        ? MonsterClassManager.GetAttackModifiersDeck(enemy)
                        : MonsterClassManager.EnemyMonsterAttackModifierDeck;
                string text = deck == null ? null : Describe("Next monster draw", Compute(deck.AttackModifierCards,
                    deck.DiscardedAttackModifierCards, MonsterReshuffle(deck)));
                Show(____attackModifiersContainer, text);
                ShowPerCard(____attackModifiersContainer, ____data.AttackModifiers, deck?.AttackModifierCards, deck?.DiscardedAttackModifierCards);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Modifier deck odds: " + e.Message);
            }
        }

        /// <summary>Puts the odds text as the last line of the section (a vertical layout: label, then the modifier grid).</summary>
        private static void Show(UIGeneralInfoSection section, string text)
        {
            if (section == null) return;
            Transform existing = section.transform.Find(Name);
            if (!On || string.IsNullOrEmpty(text))
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }
            TextMeshProUGUI line = existing != null ? existing.GetComponent<TextMeshProUGUI>() : Create(section);
            if (line == null) return;
            line.gameObject.SetActive(true);
            line.transform.SetAsLastSibling();
            line.text = text;
            UiDump.Once("Info panel attack modifiers (with odds)", section.transform);
        }

        /// <summary>
        /// A small tag across the top edge of each modifier icon with that card's own chance: for a normal card, the chance
        /// it settles the next attack; for a rolling card, the chance the draw starts with it. The container's elements
        /// are in the same order as the panel data's AttackModifiers (ListContainerBase.Init), one per card name.
        /// </summary>
        private static void ShowPerCard(UIGeneralInfoSection section, AttackModifierInfo[] infos, List<AttackModifierYMLData> pile,
            List<AttackModifierYMLData> discard)
        {
            UIAttackModifiersContainer container = section != null ? section.GetComponentInChildren<UIAttackModifiersContainer>(true) : null;
            if (container == null) return;
            bool show = On && infos != null && pile != null;
            List<AttackModifierYMLData> cards = show ? Drawable(pile, discard) : null;
            int all = cards?.Count ?? 0, settling = cards?.Count(c => c != null && !c.Rolling) ?? 0;
            int i = 0;
            foreach (UIAttackModifierElement element in container.Elements)
            {
                Transform existing = element.transform.Find(Name);
                if (!show || i >= infos.Length)
                {
                    if (existing != null) existing.gameObject.SetActive(false);
                    i++;
                    continue;
                }
                string name = infos[i++].DescriptionLocKey;
                List<AttackModifierYMLData> same = cards.Where(c => c != null && c.Name == name).ToList();
                int of = same.Count > 0 && same[0].Rolling ? all : settling;
                TextMeshProUGUI tag = existing != null ? existing.GetComponentInChildren<TextMeshProUGUI>() : CreateTag(element.transform, section);
                if (tag == null) continue;
                tag.transform.parent.gameObject.SetActive(true);
                tag.transform.parent.SetAsLastSibling();
                tag.text = of <= 0 ? "-" : $"{Math.Round(100.0 * same.Count / of):0}%";
                tag.alpha = same.Count > 0 ? 1f : 0.45f;
            }
        }

        private static TextMeshProUGUI CreateTag(Transform element, UIGeneralInfoSection section)
        {
            TextMeshProUGUI template = section.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
            // a dark pill centred on the icon's top edge: half in the gap above the row, half over the icon's rim
            var pill = new GameObject(Name, typeof(RectTransform));
            pill.transform.SetParent(element, false);
            var rt = (RectTransform)pill.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(40f, 15f);
            rt.anchoredPosition = new Vector2(0f, 1f);
            pill.AddComponent<LayoutElement>().ignoreLayout = true;
            var bg = pill.AddComponent<Image>();
            bg.color = new Color(0.075f, 0.09f, 0.12f, 0.92f);
            bg.raycastTarget = false;
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(pill.transform, false);
            var trt = (RectTransform)go.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var text = go.AddComponent<TextMeshProUGUI>();
            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
            }
            text.color = template != null ? template.color : Color.white;
            text.fontSize = 12f;
            text.fontStyle = FontStyles.Bold;
            text.enableWordWrapping = false;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static TextMeshProUGUI Create(UIGeneralInfoSection section)
        {
            TextMeshProUGUI template = section.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
            var go = new GameObject(Name, typeof(RectTransform));
            go.transform.SetParent(section.transform, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
                text.color = template.color;
                text.fontSize = Math.Max(14f, template.fontSize * 0.8f);
            }
            else text.fontSize = 18f;
            text.enableWordWrapping = true;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            // as wide as the modifier grid; the section's layout may or may not size its children, so set both ways
            float width = section.Content != null && section.Content.rect.width > 0 ? section.Content.rect.width : 350f;
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, 0f);
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            VerticalLayoutGroup group = section.GetComponent<VerticalLayoutGroup>();
            if (group == null || !group.childControlHeight)
                go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return text;
        }

        // ---- the odds

        internal struct Odds
        {
            public int Cards, Null, X2, Minus, Zero, Plus, Rolling;
            public bool Reshuffle;
        }

        internal static Odds Compute(List<AttackModifierYMLData> pile, List<AttackModifierYMLData> discard, bool reshuffle)
        {
            var o = new Odds { Reshuffle = reshuffle };
            foreach (AttackModifierYMLData c in Drawable(pile, discard))
            {
                if (c == null) continue;
                o.Cards++;
                if (c.Rolling) { o.Rolling++; continue; }
                if (c.IsCurse || c.MathModifier == "*0") o.Null++;
                else if (c.IsBless || c.MathModifier == "*2") o.X2++;
                else if (AttackModifierYMLData.TryParseMathModifier(c.MathModifier, out char op, out int value) && op == '-' && value > 0) o.Minus++;
                else if (AttackModifierYMLData.TryParseMathModifier(c.MathModifier, out op, out value) && op == '+' && value > 0) o.Plus++;
                else o.Zero++;
            }
            return o;
        }

        /// <summary>The cards the next draw can reach: the pile, or pile and discard if the pile holds only rolling cards (it
        /// reshuffles mid-draw).</summary>
        private static List<AttackModifierYMLData> Drawable(List<AttackModifierYMLData> pile, List<AttackModifierYMLData> discard)
        {
            var cards = new List<AttackModifierYMLData>(pile ?? new List<AttackModifierYMLData>());
            if (!cards.Any(c => c != null && !c.Rolling)) cards.AddRange(discard ?? new List<AttackModifierYMLData>());
            return cards;
        }

        /// <summary>Two short lines, in the order of the game's modifier icons (null first, x2 last).</summary>
        internal static string Describe(string who, Odds o)
        {
            int settling = o.Cards - o.Rolling;
            if (settling <= 0) return $"<b>{who}:</b> no cards";
            string P(int n) => $"{Math.Round(100.0 * n / settling):0}%";
            string text = $"<b>{who}</b> ({o.Cards} cards left)\n" +
                          $"Null {P(o.Null)}   -1/-2 {P(o.Minus)}   +0 {P(o.Zero)}   +1 or more {P(o.Plus)}   x2 {P(o.X2)}";
            if (o.Rolling > 0) text += $"   Rolling {Math.Round(100.0 * o.Rolling / o.Cards):0}%";
            if (o.Reshuffle) text += "\n<i>Reshuffles at the end of the round</i>";
            return text;
        }
    }
}
