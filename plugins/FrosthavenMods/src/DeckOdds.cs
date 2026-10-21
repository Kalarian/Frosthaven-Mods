using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Modifier deck odds": adds the odds of the next attack modifier draw to the game's own modifier panel
    /// (the pop-out by the hand, ModifiersDisplayController, which already shows each value as left / used): for the
    /// character, and for the monster deck.
    ///
    /// Odds are for the card that settles the attack: rolling cards keep the draw going, so the settling card is the
    /// first non-rolling card left in the deck, and by symmetry every non-rolling card left is equally likely to be it.
    /// "Rolling" is the chance the draw starts with a rolling card (an extra bonus on top). If no non-rolling card is
    /// left, the deck is reshuffled mid-draw, so the whole deck counts. Curses count as nulls and blesses as x2s, as they
    /// play. Display only: reads the decks, changes nothing.
    /// </summary>
    [HarmonyPatch]
    internal static class DeckOdds
    {
        private const string Name = "FrosthavenMods_DeckOdds";

        internal static bool On => ModRules.IsOn(ModRules.DeckOdds);

        private static readonly AccessTools.FieldRef<CMonsterAttackModifierDeck, bool> MonsterReshuffle =
            AccessTools.FieldRefAccess<CMonsterAttackModifierDeck, bool>("m_ShuffleAttackModifierCards");

        [HarmonyPatch(typeof(ModifiersDisplayController), nameof(ModifiersDisplayController.Display))]
        [HarmonyPostfix]
        private static void AddOdds(ModifiersDisplayController __instance, CCharacterClass characterClass, VerticalLayoutGroup ___popupGroup)
        {
            try
            {
                Transform existing = ___popupGroup != null ? ___popupGroup.transform.Find(Name) : null;
                if (!On || characterClass == null || ___popupGroup == null)
                {
                    if (existing != null) existing.gameObject.SetActive(false);
                    return;
                }
                OddsLine line = existing != null ? existing.GetComponent<OddsLine>() : Create(___popupGroup.transform);
                if (line == null) return;
                line.gameObject.SetActive(true);
                line.transform.SetAsLastSibling();
                line.Show(__instance, characterClass);
                UiDump.Once("Modifier panel (with odds)", ___popupGroup.transform);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Modifier deck odds: " + e.Message);
            }
        }

        private static OddsLine Create(Transform parent)
        {
            TextMeshProUGUI template = parent.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
            var go = new GameObject(Name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
                text.color = template.color;
                text.fontSize = Math.Max(12f, template.fontSize * 0.75f);
            }
            else text.fontSize = 16f;
            text.enableWordWrapping = true;
            text.alignment = TextAlignmentOptions.Left;
            text.raycastTarget = false;
            var layout = go.AddComponent<LayoutElement>();
            layout.minWidth = 220f;
            return go.AddComponent<OddsLine>();
        }

        // ---- the odds

        internal struct Odds
        {
            public int Cards, Null, X2, Minus, Zero, Plus, Rolling;
            public bool Reshuffle;
        }

        internal static Odds Compute(List<AttackModifierYMLData> pile, List<AttackModifierYMLData> discard, bool reshuffle)
        {
            IEnumerable<AttackModifierYMLData> cards = pile ?? new List<AttackModifierYMLData>();
            if (!cards.Any(c => c != null && !c.Rolling)) cards = cards.Concat(discard ?? new List<AttackModifierYMLData>());
            var o = new Odds { Reshuffle = reshuffle };
            foreach (AttackModifierYMLData c in cards)
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

        internal static string Describe(string who, Odds o)
        {
            int settling = o.Cards - o.Rolling;
            if (settling <= 0) return $"<b>{who}:</b> no cards";
            string P(int n) => $"{Math.Round(100.0 * n / settling):0}%";
            string line = $"<b>{who}</b> ({o.Cards} left): Null {P(o.Null)} | x2 {P(o.X2)} | -1/-2 {P(o.Minus)} | +0 {P(o.Zero)} | +1/+2 {P(o.Plus)}";
            if (o.Rolling > 0) line += $" | Rolling {Math.Round(100.0 * o.Rolling / o.Cards):0}%";
            if (o.Reshuffle) line += " <i>(reshuffles at the end of the round)</i>";
            return line;
        }

        internal static string Text(CCharacterClass character)
        {
            string text = Describe("Next draw", Compute(character.AttackModifierCards, character.DiscardedAttackModifierCards,
                character.m_ShuffleAttackModifierCards));
            CMonsterAttackModifierDeck monsters = MonsterClassManager.EnemyMonsterAttackModifierDeck;
            if (monsters != null)
                text += "\n" + Describe("Monsters", Compute(monsters.AttackModifierCards, monsters.DiscardedAttackModifierCards,
                    MonsterReshuffle(monsters)));
            return text;
        }

        /// <summary>Keeps the line current while the panel is open (the game fills its counters only when it opens).</summary>
        internal sealed class OddsLine : MonoBehaviour
        {
            private ModifiersDisplayController _panel;
            private CCharacterClass _character;
            private TextMeshProUGUI _text;
            private string _key;

            public void Show(ModifiersDisplayController panel, CCharacterClass character)
            {
                _panel = panel;
                _character = character;
                _key = null;
                Refresh();
            }

            private void Update()
            {
                if (_panel != null && _panel.IsOpen()) Refresh();
            }

            private void Refresh()
            {
                try
                {
                    if (_character == null) return;
                    if (_text == null) _text = GetComponent<TextMeshProUGUI>();
                    CMonsterAttackModifierDeck m = MonsterClassManager.EnemyMonsterAttackModifierDeck;
                    string key = $"{_character.AttackModifierCards.Count}/{_character.DiscardedAttackModifierCards.Count}/{_character.m_ShuffleAttackModifierCards}" +
                                 (m == null ? "" : $"/{m.AttackModifierCards.Count}/{m.DiscardedAttackModifierCards.Count}/{MonsterReshuffle(m)}");
                    if (key == _key) return;
                    _key = key;
                    _text.text = Text(_character);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Modifier deck odds: " + e.Message);
                    enabled = false;
                }
            }
        }
    }
}
