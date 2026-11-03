using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven 2nd edition money tokens in Frosthaven (the Silent Knife's Flintlock, Hired Help, Pickpocket and Parting
    /// Gift perks; the Mindthief's Pilfer). The Frosthaven FAQ, section 10: "If a mechanic refers to loot tokens or money
    /// tokens, it can refer to either", and "Any time you are supposed to return a Frosthaven loot token to a scenario,
    /// you must also place one of your loot cards (except for the random item) back to the top of the loot deck." So:
    ///   - The Silent Knife's money tokens are the loot tokens she has looted this scenario: every loot token she picks
    ///     up (Frosthaven's own pick-up, CActor.ApplyLoot with incLootTokenCount) adds one to her character resource
    ///     SilentKnifeMoney, shown on her like the other pack class tokens, saved with the scenario.
    ///   - Gaining a money token (MiscVars GHMoneyGain on a pack NullTargeting step; GHMoneyGainAdjacent on a modifier
    ///     card's step, only when the attack's target is adjacent) = picking up a loot token: one loot-deck draw.
    ///   - Placing one (GHMoneyPlace on a NullTargeting step aimed at an enemy) or losing one (GHMoneyLose on Hired Help's
    ///     conversion): one token comes off her count, one of her loot cards (her choice when they differ; never the
    ///     random item or a card that opened a section) goes back on TOP of the loot deck and its value leaves her loot;
    ///     a placed token is a real loot token in that hex (CObjectGoldPile, Frosthaven's prop), so a later loot of the
    ///     hex draws that card again.
    ///   - Hired Help: the converted enemy carries the resource SilentKnifeHiredHelp (her character token on it); when it
    ///     dies, one loot token drops in its hex ("an additional money token": Frosthaven monsters drop none).
    ///   - Every hex where a loot picked up a loot token is added to the looter's temp var GHMoneyLootedTiles when a card
    ///     made it a list (Flintlock bottom's trap: "the empty hex of one looted money token").
    /// Each act logs "GHRule Money... card N". Only pack data carries the markers and the resources, and only the Silent
    /// Knife (class SilentKnifeID) counts tokens: without the 2nd edition pack this is inert.
    /// </summary>
    [HarmonyPatch]
    internal static class GHMoneyTokens
    {
        internal const string Money = "SilentKnifeMoney";
        internal const string Hired = "SilentKnifeHiredHelp";
        internal const string LootedTilesVar = "GHMoneyLootedTiles";
        private const string SilentKnifeClass = "SilentKnifeID";
        private const string ReturnHelpKey = "FHMODS_MONEY_RETURN";   // Text.cs

        // the loot cards (indexes into CLootDeck.AllCards) each Silent Knife drew from her loot tokens; after a reload this
        // is empty and the return falls back to the deck's drawn cards she can pay back
        private static readonly Dictionary<string, List<byte>> Drawn = new Dictionary<string, List<byte>>();

        private static bool HoldsTokens(CActor a) => a != null && a.Class?.ID == SilentKnifeClass;

        private static bool Marked(CAbility a, string key)
        {
            try { return a?.MiscVars != null && a.GetMiscVar(key).GetBool(); }
            catch { return false; }
        }

        private static int CardOf(CAbility a)
        {
            try { return a?.GetAbilityBaseCard()?.ID ?? 0; } catch { return 0; }
        }

        // ---- gaining: every loot token she picks up is one of her money tokens ----

        [HarmonyPatch(typeof(CActor), "ApplyLoot", new[] { typeof(LootCard), typeof(ScenarioState), typeof(bool) })]
        [HarmonyPostfix]
        private static void TokenLooted(CActor __instance, LootCard lc, bool incLootTokenCount)
        {
            try
            {
                if (!incLootTokenCount || !HoldsTokens(__instance)) return;
                __instance.AddCharacterResource(Money, 1);
                int index = Array.FindIndex(CLootDeck.AllCards, x => x == lc);
                if (lc.Type != LootCard.EType.None && index > 0)
                {
                    if (!Drawn.TryGetValue(__instance.ActorGuid, out var mine)) Drawn[__instance.ActorGuid] = mine = new List<byte>();
                    mine.Add((byte)index);
                }
                Plugin.Log.LogInfo($"GHRule MoneyTokenGain card 0 {__instance.Class.ID} looted a loot token ({lc.Type}): {__instance.GetCharacterResourceAmount(Money)} money tokens");
            }
            catch (Exception e) { Plugin.Log.LogWarning("GHMoneyTokens (gain): " + e.Message); }
        }

        // ---- the hexes a loot picked a loot token up from (Flintlock bottom) ----

        [HarmonyPatch(typeof(CActor), nameof(CActor.LootTile))]
        [HarmonyPrefix]
        private static void BeforeLootTile(CTile tile, out bool __state)
        {
            __state = false;
            try { __state = tile?.m_Props != null && tile.m_Props.Any(p => p.ObjectType == ScenarioManager.ObjectImportType.MoneyToken); }
            catch { }
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.LootTile))]
        [HarmonyPostfix]
        private static void AfterLootTile(CActor __instance, CTile tile, bool __state)
        {
            try
            {
                if (!__state || tile == null || tile.m_Props.Any(p => p.ObjectType == ScenarioManager.ObjectImportType.MoneyToken)) return;
                IList list = __instance.GetVar(LootedTilesVar).GetObject<IList>();
                if (list == null) return;   // only a card that asked for the list (Flintlock bottom's loot) gets one
                list.Add(tile);
                Plugin.Log.LogInfo($"GHRule MoneyTokenLootedHex card 0 {__instance.Class?.ID} picked a loot token up at {tile.m_ArrayIndex}");
            }
            catch (Exception e) { Plugin.Log.LogWarning("GHMoneyTokens (looted hex): " + e.Message); }
        }

        // ---- the marked steps ----

        [HarmonyPatch(typeof(CAbilityNullTargeting), nameof(CAbilityNullTargeting.ApplyToActor))]
        [HarmonyPostfix]
        private static void NullTargetingApplied(CAbilityNullTargeting __instance, CActor actor, bool __result)
        {
            try
            {
                if (!__result || actor == null) return;
                CActor me = __instance.TargetingActor;
                if (me == null) return;
                if (Marked(__instance, "GHMoneyPlace"))
                {
                    if (Spend(me, CardOf(__instance), "MoneyTokenPlace"))
                    {
                        CTile tile = ScenarioManager.Tiles[actor.ArrayIndex.X, actor.ArrayIndex.Y];
                        PlaceToken(tile);
                        Plugin.Log.LogInfo($"GHRule MoneyTokenPlace card {CardOf(__instance)} loot token placed in the hex of {actor.Class?.ID} at {tile.m_ArrayIndex}");
                    }
                }
                else if (Marked(__instance, "GHMoneyGain"))
                {
                    Gain(me, CardOf(__instance), "target not killed");
                }
                else if (Marked(__instance, "GHMoneyGainAdjacent"))
                {
                    CActor target = __instance.GetVar("TargetActor").GetObject<CActor>();
                    if (target == null && __instance.ParentAbility?.ActorsTargeted != null)
                        target = __instance.ParentAbility.ActorsTargeted.LastOrDefault();
                    if (target != null && me.IsAdjacentTo(target))
                        Gain(me, 0, "Pickpocket perk: the attack's target was adjacent");
                    else
                        Plugin.Log.LogInfo($"GHRule MoneyTokenGain card 0 Pickpocket perk: target not adjacent, no token");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("GHMoneyTokens (step): " + e.Message); }
        }

        // CAbilityChangeAllegiance.ApplyToActor always returns false (decompiled: "return false" after the conversion), so
        // the conversion is told by the actor's type before and after, not by the result (the bench of 2026-11-02 20:54:
        // the enemy turned but the token was never spent)
        [HarmonyPatch(typeof(CAbilityChangeAllegiance), nameof(CAbilityChangeAllegiance.ApplyToActor))]
        [HarmonyPrefix]
        private static void BeforeConvert(CActor actor, out CActor.EType __state)
        {
            __state = actor != null ? actor.OriginalType : CActor.EType.Unknown;
        }

        [HarmonyPatch(typeof(CAbilityChangeAllegiance), nameof(CAbilityChangeAllegiance.ApplyToActor))]
        [HarmonyPostfix]
        private static void Converted(CAbilityChangeAllegiance __instance, CActor actor, CActor.EType __state)
        {
            try
            {
                if (actor == null || actor.IsDead || actor.OriginalType == __state || !Marked(__instance, "GHMoneyLose")) return;
                CActor me = __instance.TargetingActor;
                if (!Spend(me, CardOf(__instance), "MoneyTokenLose")) return;
                if (actor.GetCharacterResourceAmount(Hired) == 0) actor.AddCharacterResource(Hired, 1);
                Plugin.Log.LogInfo($"GHRule HiredHelp card {CardOf(__instance)} {actor.Class?.ID} is hired (drops a loot token when it dies)");
            }
            catch (Exception e) { Plugin.Log.LogWarning("GHMoneyTokens (Hired Help): " + e.Message); }
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.OnDeath))]
        [HarmonyPrefix]
        private static void Dying(CActor __instance, CActor.ECauseOfDeath causeOfDeath)
        {
            try
            {
                if (__instance == null || causeOfDeath == CActor.ECauseOfDeath.ActorRemovedFromMap) return;
                if (__instance.GetCharacterResourceAmount(Hired) <= 0) return;
                __instance.RemoveCharacterResource(Hired, __instance.GetCharacterResourceAmount(Hired), true);
                CTile tile = ScenarioManager.Tiles[__instance.ArrayIndex.X, __instance.ArrayIndex.Y];
                PlaceToken(tile);
                Plugin.Log.LogInfo($"GHRule HiredHelpDrop card 0 {__instance.Class?.ID} died: an additional loot token dropped at {tile.m_ArrayIndex}");
            }
            catch (Exception e) { Plugin.Log.LogWarning("GHMoneyTokens (Hired Help death): " + e.Message); }
        }

        // ---- the acts ----

        private static void Gain(CActor me, int cardId, string why)
        {
            if (me.OriginalType != CActor.EType.Player) return;
            me.GainLoot(endOfScenario: false, incLootTokenCount: true);
            ScenarioRuleClient.MessageHandler(new CStackRewardsToShow_MessageData(me)
            {
                m_Rewards = me.LastLoot.GetRewards().ToList(),
                m_SourceType = "MoneyToken"
            });
            Plugin.Log.LogInfo($"GHRule MoneyTokenGain card {cardId} {me.Class?.ID} gains a money token ({why}): one loot-deck draw");
        }

        /// <summary>One of her money tokens leaves her: the count drops and one of her loot cards goes back on top of the
        /// loot deck. False when she has none (the card's requirement should have kept the step from starting).</summary>
        private static bool Spend(CActor me, int cardId, string rule)
        {
            if (me == null || me.GetCharacterResourceAmount(Money) < 1)
            {
                Plugin.Log.LogInfo($"GHRule {rule} card {cardId} no money token to spend");
                return false;
            }
            me.RemoveCharacterResource(Money, 1, true);
            string back = ReturnLootCard(me);
            Plugin.Log.LogInfo($"GHRule {rule} card {cardId} {me.Class?.ID} spent a money token ({me.GetCharacterResourceAmount(Money)} left); {back}");
            return true;
        }

        private static bool Returnable(LootCard c) =>
            c.Type != LootCard.EType.None && c.Type != LootCard.EType.RandomItem &&
            (c.AvailableCampaignSectionIDs == null || c.AvailableCampaignSectionIDs.Length == 0);

        private static int Value(LootCard c, ScenarioState css)
        {
            int amount = CLootDeck.CalcLootAmount(c, css);
            if (c.Type != LootCard.EType.Money) return amount;
            return amount * (int)ScenarioManager.GoldConversion + (int)css.ScenarioSetup.ExtraGoldOnLootCard;
        }

        private static bool CanPayBack(CActor me, LootCard c, ScenarioState css) =>
            me.TotalLoot.ResourceAmount(c.Type) >= Value(c, css);

        private static string ReturnLootCard(CActor me)
        {
            ScenarioState css = ScenarioManager.CurrentScenarioState;
            List<byte> deck = css.LootDeck;
            int delimiter = CLootDeck.GetDelimiterID(deck);
            var drawnSection = deck.Skip(delimiter + 1).ToList();
            Drawn.TryGetValue(me.ActorGuid, out var mine);
            var candidates = (mine ?? new List<byte>()).Where(b => drawnSection.Contains(b) && Returnable(CLootDeck.AllCards[b]) &&
                                                                    CanPayBack(me, CLootDeck.AllCards[b], css)).Distinct().ToList();
            if (candidates.Count == 0)
                candidates = drawnSection.Where(b => Returnable(CLootDeck.AllCards[b]) && CanPayBack(me, CLootDeck.AllCards[b], css)).Distinct().ToList();
            if (candidates.Count == 0) return "no loot card of hers to put back";
            // one card per different value: the choice is hers only when the values differ
            var choices = candidates.GroupBy(b => (CLootDeck.AllCards[b].Type, Value(CLootDeck.AllCards[b], css))).Select(g => g.First()).ToList();
            byte chosen = choices[0];
            if (choices.Count > 1)
            {
                var cards = choices.Select(b => CLootDeck.AllCards[b]).ToList();
                if (GameState.ShowLootCardSelection(me, cards, out LootCard picked, ReturnHelpKey))
                {
                    int i = Array.FindIndex(CLootDeck.AllCards, x => x == picked);
                    if (i > 0 && choices.Contains((byte)i)) chosen = (byte)i;
                }
            }
            LootCard card = CLootDeck.AllCards[chosen];
            deck.Remove(chosen);
            deck.Insert(0, chosen);
            int value = Value(card, css);
            me.StartLooting();   // as Frosthaven's own CustomAbility.DiscardLootCard
            if (card.Type == LootCard.EType.Money) me.GainLootMoney(-value);
            else me.GainResource(card.Type, -value);
            me.LootFromLootCards = new LootState();
            me.LootFromLootCards.AccumulateLootFrom(me.TotalLoot);
            mine?.Remove(chosen);
            return $"loot card {card.Type} {value} back on top of the loot deck";
        }

        private static void PlaceToken(CTile tile)
        {
            var prop = new CObjectGoldPile("LootToken", ScenarioManager.ObjectImportType.MoneyToken, new TileIndex(tile.m_ArrayIndex),
                                           null, null, null, tile.m_HexMap.MapGuid);
            tile.SpawnProp(prop);
        }
    }
}
