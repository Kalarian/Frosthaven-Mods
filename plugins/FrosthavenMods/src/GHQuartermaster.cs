using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using TMPro;
using UnityEngine;
using WorldspaceUI;
using Point = AStar.Point;

namespace FrosthavenMods
{
    /// <summary>
    /// The Gloomhaven 2nd edition Quartermaster ("Three Spears" in the GH2e FAQ), as printed on its class mat, cards, perks
    /// and masteries. Everything here acts only on the 2nd edition pack's markers (MiscVars GH2E_QM*, perk IDs
    /// Quartermaster209/210, item IDs 33201-33218, the class Quartermaster2ID): without the pack it is inert.
    ///
    /// The Quartermaster item supply (class mat): nine class items (pack ItemCards 33201-33209, no slot, one copy each, no
    /// gold cost; the user's image QM1 Sharpened Dirk .. QM9 Scroll of Foresight). An item no living character holds
    /// unused is in the supply. A give puts a fresh copy into the receiver's items (CInventory.AddItem, the game's own
    /// item bar shows it); a used item is back in the supply at once (GH2e FAQ: "Items are returned to their supply
    /// immediately when used") and leaves the holder's items at the end of the turn or at the next give. Only characters
    /// receive items (FAQ: not summons); giving is not targeted (FAQ: no line of sight). Random draws use the scenario's
    /// own random numbers (the same on every co-op copy); choices are the engine's own network-synced yes/no box,
    /// answered by the Quartermaster's player.
    ///   - GH2E_QMGive N [GH2E_QMGive2 M] [GH2E_QMGiveOr K] [GH2E_QMGiveXP X] on a NullTargeting: give item N (and M; or
    ///     the player picks N or K) to each target; X XP only when something was given (the "to gain XP 1" text).
    ///   - GH2E_QMGiveRandom N: N random items from the supply to the target (Reinforced Steel top).
    ///   - GH2E_QMBoosterPack: draw five random items, distribute among adjacent allies and self, at most three to self.
    ///   - GH2E_QMGiantClub: up to four chosen items to allies adjacent to any hex the move entered; +10 supplies per ally.
    ///   - GH2E_QMForesight (the Scroll of Foresight item): the top two modifier cards, each may go to the bottom.
    /// Supplies spend (class mat, GH2E_QMSpend): needs 10+; the track goes to 0; then the player may Recover any of the
    /// targets' spent or lost items whose combined gold cost is at most the supplies spent, one shared pool for all
    /// targets (FAQ); only items with a printed gold cost (FAQ), never the class items.
    /// Proficiency (33110): top (GH2E_QMProficiencyTop) the +1 melee bonus counts from the first lost-icon item used on
    /// each of the Quartermaster's turns to the turn's end; a supplies recover of a 30+ gold item offers a random class
    /// item. Bottom (GH2E_QMProficiencyBottom): Barbed Strip / Iron Plate are handed out (and swapped) as their +1 copies
    /// (33218 Retaliate 3, 33217 Shield 3); the rest trigger gives one of the two.
    /// Set Up Shop (33122) top (GH2E_QMSetUpShop): the Quartermaster can't perform move abilities; when an ally's move or
    /// teleport ability ends adjacent to the Quartermaster, its player may spend supplies to Recover that ally's items.
    /// Perks: Deep Pockets (Quartermaster209) one more small item; Guild Connections (Quartermaster210, the user's
    /// equivalent of the 2e Well-Connected perk, which the FAQ says has no function outside 2e): the party buys items five
    /// gold cheaper. Masteries: FrosthavenMods_QMSupplySpends, FrosthavenMods_QMItemEveryRound. The supplies amount shows
    /// as a number on the Quartermaster's supplies icon.
    /// Every action logs "GHRule &lt;Rule&gt; card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHQuartermaster
    {
        internal const string Supplies = "Supplies";
        internal const string QMClass = "Quartermaster2ID";
        internal const int ProficiencyCard = 33110, SetUpShopCard = 33122;
        private const int ItemBase = 33200, IronPlus = 33217, BarbedPlus = 33218;
        internal const string DeepPockets = "Quartermaster209", GuildConnections = "Quartermaster210";
        private static readonly string[] Keys = { null, "SharpenedDirk", "SturdySpear", "ScrollofEmbers", "WeightedBolas",
            "ScrollofRelocation", "CorrosiveCoating", "IronPlate", "BarbedStrip", "ScrollofForesight" };

        // ------------------------------------------------------------------ the item supply

        /// <summary>The class item number (1-9) of a pack item ID, 0 for any other item.</summary>
        internal static int No(int id)
        {
            if (id == IronPlus) return 7;
            if (id == BarbedPlus) return 8;
            return id > ItemBase && id <= ItemBase + 9 ? id - ItemBase : 0;
        }

        internal static bool IsClassItem(CItem item) => item != null && No(item.ID) > 0;

        private static string ItemName(int n)
        {
            if (n < 1 || n > 9) return "?";
            string key = "GH2E_QMItem_" + Keys[n] + "_1", s = null;
            try { s = GLOOM.LocalizationManager.GetTranslation(key); } catch { }
            return string.IsNullOrEmpty(s) || s.Contains(key) ? Keys[n] : s;
        }

        private static IEnumerable<CPlayerActor> Players() =>
            ScenarioManager.Scenario?.PlayerActors?.Where(p => p != null) ?? Enumerable.Empty<CPlayerActor>();

        private static IEnumerable<CPlayerActor> LivingPlayers() => Players().Where(p => !p.IsDead);

        internal static bool IsQM(CActor a) => a is CPlayerActor p && p.CharacterClass?.ID == QMClass;

        /// <summary>The living character holding item n unused, or null (in the supply).</summary>
        private static CPlayerActor Holder(int n) =>
            LivingPlayers().FirstOrDefault(p => p.Inventory?.AllItems?.Any(i => i != null && No(i.ID) == n && i.SlotState != CItem.EItemSlotState.Consumed) == true);

        private static List<int> SupplyItems() => Enumerable.Range(1, 9).Where(n => Holder(n) == null).ToList();

        /// <summary>Used class items leave their holders' items (they are back in the supply already).</summary>
        private static void Purge()
        {
            foreach (CPlayerActor p in Players().ToList())
                foreach (CItem i in (p.Inventory?.AllItems ?? new List<CItem>()).Where(i => IsClassItem(i) && (i.SlotState == CItem.EItemSlotState.Consumed || p.IsDead)).ToList())
                {
                    p.Inventory.RemoveItem(i);
                    Plugin.Log.LogInfo($"GHRule QMItemReturn card 0 {ItemName(No(i.ID))} leaves {GHPlague.NameOf(p)}'s items (back in the Quartermaster item supply)");
                }
        }

        internal static bool BonusActive(CActor a, string marker)
        {
            if (a == null || a.IsDead) return false;
            try { return CActiveBonus.FindApplicableActiveBonuses(a).Any(b => b != null && b.Actor == a && GHPlague.Marked(b.Ability, marker)); }
            catch { return false; }
        }

        private static bool ProficiencyBottom() => LivingPlayers().Any(p => IsQM(p) && BonusActive(p, "GH2E_QMProficiencyBottom"));

        private static int IdFor(int n)
        {
            if ((n == 7 || n == 8) && ProficiencyBottom()) return n == 7 ? IronPlus : BarbedPlus;
            return ItemBase + n;
        }

        /// <summary>Item n from the supply into <paramref name="target"/>'s items. False when it can't be (a summon, the
        /// item held by someone).</summary>
        internal static bool Give(CActor giver, CActor target, int n, int card, string source)
        {
            if (n < 1 || n > 9) return false;
            if (!(target is CPlayerActor receiver) || target.IsDead)
            {
                Plugin.Log.LogInfo($"GHRule QMGive card {card} {ItemName(n)}: {GHPlague.NameOf(target)} is not a character (FAQ: only characters gain the items); nothing given ({source})");
                return false;
            }
            Purge();
            CPlayerActor holder = Holder(n);
            if (holder != null)
            {
                Plugin.Log.LogInfo($"GHRule QMGive card {card} {ItemName(n)} is held by {GHPlague.NameOf(holder)}: no second copy ({source})");
                return false;
            }
            var item = new CItem(IdFor(n), CBaseCard.ECardType.Item, 0u);
            receiver.Inventory.AddItem(item);
            NoteGive(giver);
            Plugin.Log.LogInfo($"GHRule QMGive card {card} {GHPlague.NameOf(giver)} gives {GHPlague.NameOf(receiver)} {ItemName(n)}{(item.ID == IronPlus || item.ID == BarbedPlus ? " (+1, Proficiency)" : "")} ({source})");
            return true;
        }

        private static int Random(int n)
        {
            try { return ScenarioManager.CurrentScenarioState.ScenarioRNG.Next(n); }
            catch { return 0; }
        }

        private static bool GiveRandom(CActor giver, CActor target, int card, string source)
        {
            Purge();
            List<int> supply = SupplyItems();
            if (supply.Count == 0)
            {
                Plugin.Log.LogInfo($"GHRule QMGive card {card} the Quartermaster item supply is empty: nothing drawn ({source})");
                return false;
            }
            int n = supply[Random(supply.Count)];
            Plugin.Log.LogInfo($"GHRule QMDraw card {card} drew {ItemName(n)} from {supply.Count} item(s) in the supply ({source})");
            return Give(giver, target, n, card, source);
        }

        private static bool Ask(CActor asker, bool fallback, string help, string yes, string no, params object[] args) =>
            GHPlague.Ask(asker, fallback, help, yes, no, args);

        // ------------------------------------------------------------------ the abilities

        private static readonly HashSet<Guid> Spent = new HashSet<Guid>();

        [HarmonyPatch(typeof(CAbilityNullTargeting), nameof(CAbilityNullTargeting.ApplyToActor))]
        [HarmonyPostfix]
        private static void Applied(CAbilityNullTargeting __instance, CActor actor, bool __result)
        {
            if (!__result || __instance?.MiscVars == null || __instance.MiscVars.Count == 0) return;
            try
            {
                int card = GHPlague.CardOf(__instance);
                CActor giver = __instance.TargetingActor;
                if (Int(__instance, "GH2E_QMSpend") > 0)
                {
                    if (!Spent.Add(__instance.ID)) return;   // once per ability: one shared pool for all its targets
                    List<CActor> targets = (__instance.ActorsToTarget ?? new List<CActor>()).Where(a => a != null).ToList();
                    if (!targets.Contains(actor)) targets.Add(actor);
                    SpendSupplies(giver, targets, card, "spend box");
                    return;
                }
                if (Int(__instance, "GH2E_QMBoosterPack") > 0) { BoosterPack(giver, card); return; }
                if (Int(__instance, "GH2E_QMGiantClub") > 0) { GiantClub(giver, card); return; }
                if (Int(__instance, "GH2E_QMForesight") > 0) { Foresight(actor ?? giver); return; }
                int random = Int(__instance, "GH2E_QMGiveRandom");
                if (random > 0)
                {
                    for (int i = 0; i < random; i++) GiveRandom(giver, actor, card, "random draw");
                    return;
                }
                int a1 = Int(__instance, "GH2E_QMGive");
                if (a1 <= 0) return;
                int a2 = Int(__instance, "GH2E_QMGive2"), alt = Int(__instance, "GH2E_QMGiveOr"), xp = Int(__instance, "GH2E_QMGiveXP");
                bool any = false;
                int first = a1;
                if (alt > 0)
                {
                    Purge();
                    bool aFree = Holder(a1) == null, bFree = Holder(alt) == null;
                    if (!aFree && bFree) first = alt;
                    else if (aFree && bFree)
                        first = Ask(giver, true, "FHMODS_QM_CHOOSE", "FHMODS_QM_CHOOSE_A", "FHMODS_QM_CHOOSE_B",
                                    GHPlague.NameOf(actor), ItemName(a1), ItemName(alt)) ? a1 : alt;
                    if (alt > 0 && first != a1) Plugin.Log.LogInfo($"GHRule QMGive card {card} chose {ItemName(first)} over {ItemName(a1)}");
                }
                any |= Give(giver, actor, first, card, "give");
                if (a2 > 0) any |= Give(giver, actor, a2, card, "give");
                if (any && xp > 0 && giver != null)
                {
                    giver.GainXP(xp);
                    Plugin.Log.LogInfo($"GHRule QMGiveXP card {card} {GHPlague.NameOf(giver)} gains {xp} XP for the give");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("qm give")) Plugin.Log.LogWarning("GHQuartermaster (give): " + e); }
        }

        private static int Int(CAbility a, string key)
        {
            try { return a.MiscVars.Any(x => x.Item1 == key) ? a.GetMiscVar(key).GetInt() : 0; }
            catch { return 0; }
        }

        private static List<CPlayerActor> AdjacentAllies(CActor me) =>
            LivingPlayers().Where(p => p != me && me != null && me.IsAdjacentTo(p) && CActor.AreActorsAllied(me.Type, p.Type, me, p)).ToList();

        private static void BoosterPack(CActor qm, int card)
        {
            Purge();
            List<int> supply = SupplyItems();
            var drawn = new List<int>();
            while (drawn.Count < 5 && supply.Count > 0)
            {
                int n = supply[Random(supply.Count)];
                supply.Remove(n);
                drawn.Add(n);
            }
            Plugin.Log.LogInfo($"GHRule QMBoosterPack card {card} drew {drawn.Count}: {string.Join(", ", drawn.Select(ItemName))}");
            var allies = AdjacentAllies(qm);
            int toSelf = 0;
            foreach (int n in drawn)
            {
                var eligible = new List<CActor>(allies);
                if (toSelf < 3) eligible.Add(qm);
                if (eligible.Count == 0)
                {
                    Plugin.Log.LogInfo($"GHRule QMBoosterPack card {card} {ItemName(n)}: nobody may take it (three already to yourself, no adjacent ally); back to the supply");
                    continue;
                }
                CActor to = eligible[eligible.Count - 1];
                for (int i = 0; i < eligible.Count - 1; i++)
                    if (Ask(qm, i == 0, "FHMODS_QM_BOOSTER", "FHMODS_QM_GIVE", "FHMODS_QM_NEXT", ItemName(n), GHPlague.NameOf(eligible[i]), toSelf))
                    { to = eligible[i]; break; }
                if (Give(qm, to, n, card, "Booster Pack") && to == qm) toSelf++;
            }
        }

        private static void GiantClub(CActor qm, int card)
        {
            var path = new List<Point>();
            if (CAbilityMove.s_LastMover == qm && CAbilityMove.AllPlayerArrayIndexOnPath.Count > 0
                && CAbilityMove.AllPlayerArrayIndexOnPath[CAbilityMove.AllPlayerArrayIndexOnPath.Count - 1].Equals(qm.ArrayIndex))
                path.AddRange(CAbilityMove.AllPlayerArrayIndexOnPath);
            var allies = LivingPlayers().Where(p => p != qm && CActor.AreActorsAllied(qm.Type, p.Type, qm, p) && path.Any(pt => ScenarioManager.IsTileAdjacent(pt.X, pt.Y, p.ArrayIndex.X, p.ArrayIndex.Y))).ToList();
            Plugin.Log.LogInfo($"GHRule QMGiantClub card {card} the move entered {path.Count} hex(es); {allies.Count} ally(ies) next to them");
            int given = 0;
            var receivers = new HashSet<CActor>();
            foreach (CPlayerActor ally in allies)
            {
                if (given >= 4) break;
                Purge();
                if (SupplyItems().Count == 0) break;
                if (!Ask(qm, true, "FHMODS_QM_CLUB_ALLY", "FHMODS_QM_YES", "FHMODS_QM_NO", GHPlague.NameOf(ally), given)) continue;
                foreach (int n in SupplyItems())
                {
                    if (given >= 4) break;
                    if (!Ask(qm, false, "FHMODS_QM_CLUB_ITEM", "FHMODS_QM_GIVE", "FHMODS_QM_NEXT", ItemName(n), GHPlague.NameOf(ally), given)) continue;
                    if (Give(qm, ally, n, card, "Giant Club")) { given++; receivers.Add(ally); }
                }
            }
            if (receivers.Count > 0)
            {
                qm.AddCharacterResource(Supplies, 10 * receivers.Count);
                Plugin.Log.LogInfo($"GHRule QMGiantClub card {card} {given} item(s) to {receivers.Count} ally(ies): +{10 * receivers.Count} supplies ({qm.GetCharacterResourceAmount(Supplies)})");
            }
        }

        private static void Foresight(CActor user)
        {
            List<AttackModifierYMLData> deck = CustomAbility.GetAttackModifierDeck(user);
            if (deck == null || deck.Count == 0)
            {
                Plugin.Log.LogInfo($"GHRule QMForesight card 0 {GHPlague.NameOf(user)}'s modifier deck is empty");
                return;
            }
            List<AttackModifierYMLData> top = deck.Take(2).ToList();
            var bottom = new List<AttackModifierYMLData>();
            for (int i = 0; i < top.Count; i++)
                if (Ask(user, false, "FHMODS_QM_FORESIGHT", "FHMODS_QM_BOTTOM", "FHMODS_QM_KEEP", i + 1, ModifierText(top[i]), top.Count))
                    bottom.Add(top[i]);
            foreach (AttackModifierYMLData c in bottom) { deck.Remove(c); deck.Add(c); }
            try { CShowNextAttackModifierBehaviour.ValidateAllShowNextModifierBonuses(user); } catch { }
            Plugin.Log.LogInfo($"GHRule QMForesight card 0 {GHPlague.NameOf(user)} revealed {string.Join(", ", top.Select(ModifierText))}; to the bottom: {(bottom.Count == 0 ? "none" : string.Join(", ", bottom.Select(ModifierText)))}");
        }

        private static string ModifierText(AttackModifierYMLData d)
        {
            string s = null;
            try { s = d?.Card?.AttackModifierLogString(); } catch { }
            if (string.IsNullOrEmpty(s)) return d?.Name ?? "?";
            try
            {
                string t = GLOOM.LocalizationManager.GetTranslation(s);
                if (!string.IsNullOrEmpty(t) && !t.Contains(s)) return t;
            }
            catch { }
            return s;
        }

        // ------------------------------------------------------------------ supplies

        /// <summary>The class mat's spend: all supplies (10+ needed), the track to 0, then the targets' spent / lost items with
        /// a printed gold cost, combined cost at most the amount spent, one pool for all targets (FAQ).</summary>
        internal static void SpendSupplies(CActor qm, List<CActor> targets, int card, string source)
        {
            if (qm == null) return;
            int budget = qm.GetCharacterResourceAmount(Supplies);
            if (budget < 10)
            {
                Plugin.Log.LogInfo($"GHRule QMSpend card {card} only {budget} supplies (10 needed): nothing spent ({source})");
                return;
            }
            qm.RemoveCharacterResource(Supplies, budget, true);
            Plugin.Log.LogInfo($"GHRule QMSpend card {card} {GHPlague.NameOf(qm)} spends {budget} supplies (track to 0) on {string.Join(", ", targets.Select(GHPlague.NameOf))} ({source})");
            int remaining = budget, lostGold = 0, recovered = 0;
            var candidates = targets.OfType<CPlayerActor>().Distinct()
                .SelectMany(t => (t.Inventory?.AllItems ?? new List<CItem>()).Where(i => i != null && i.YMLData != null && !IsClassItem(i) && i.YMLData.PermanentlyConsumed != true
                                                                && (i.SlotState == CItem.EItemSlotState.Spent || i.SlotState == CItem.EItemSlotState.Consumed))
                                    .Select(i => (owner: t, item: i)))
                .OrderByDescending(x => x.item.YMLData.Cost).ToList();
            int noCost = candidates.Count(x => x.item.YMLData.Cost <= 0);
            if (noCost > 0) Plugin.Log.LogInfo($"GHRule QMSpend card {card} {noCost} used item(s) without a gold cost can't be recovered with supplies (FAQ)");
            foreach (var (owner, item) in candidates.Where(x => x.item.YMLData.Cost > 0))
            {
                int cost = item.YMLData.Cost;
                if (cost > remaining) continue;
                string name = GLOOM.LocalizationManager.GetTranslation(item.Name);
                if (!Ask(qm, true, "FHMODS_QM_SPEND", "FHMODS_QM_RECOVER", "FHMODS_QM_SKIP", GHPlague.NameOf(owner), name, cost, remaining)) continue;
                bool wasLost = item.SlotState == CItem.EItemSlotState.Consumed;
                owner.Inventory.ReactivateItem(item, owner);
                remaining -= cost;
                recovered += cost;
                if (wasLost) lostGold += cost;
                Plugin.Log.LogInfo($"GHRule QMSpend card {card} recovers {GHPlague.NameOf(owner)}'s {name} ({cost} gold, {(wasLost ? "lost" : "spent")}); {remaining} of {budget} left");
                if (cost >= 30 && BonusActive(qm, "GH2E_QMProficiencyTop")
                    && Ask(qm, true, "FHMODS_QM_PROFICIENCY", "FHMODS_QM_YES", "FHMODS_QM_NO", name, cost))
                {
                    Plugin.Log.LogInfo($"GHRule QMProficiency card {ProficiencyCard} a {cost} gold item recovered with supplies: a random class item");
                    GiveRandom(qm, qm, ProficiencyCard, "Proficiency");
                }
            }
            if (budget >= 50 && lostGold >= 50)
            {
                Count(LedgerSpends, qm);
                LedgerSpends[qm.ActorGuid ?? ""] = Math.Max(LedgerSpends[qm.ActorGuid ?? ""], GHSavedCounts.Add(qm, SlotLedger));
                Plugin.Log.LogInfo($"GHRule QMMastery card 0 Quartermaster's Ledger: 50 supplies spent for {lostGold} gold of lost items ({LedgerSpends[qm.ActorGuid ?? ""]} so far)");
            }
            Plugin.Log.LogInfo($"GHRule QMSpend card {card} done: {recovered} gold recovered of {budget}");
        }

        // ------------------------------------------------------------------ Proficiency top: the lost-item turn flag

        private static string _profTurn;   // "<round>/<actor guid>" of the Quartermaster turn in which the bonus counts

        [HarmonyPatch(typeof(CInventory), "OnItemConsumed")]
        [HarmonyPostfix]
        private static void Consumed(CInventory __instance, CItem item)
        {
            try
            {
                CActor owner = __instance?.InventoryActor;
                if (item == null || owner == null) return;
                if (IsClassItem(item))
                    Plugin.Log.LogInfo($"GHRule QMItemReturn card 0 {GHPlague.NameOf(owner)} used {ItemName(No(item.ID))}: back in the Quartermaster item supply");
                if (IsQM(owner) && GameState.InternalCurrentActor == owner && BonusActive(owner, "GH2E_QMProficiencyTop"))
                {
                    string key = TurnKey(owner);
                    if (_profTurn != key)
                    {
                        _profTurn = key;
                        Plugin.Log.LogInfo($"GHRule QMProficiency card {ProficiencyCard} first lost-icon item this turn ({GLOOM.LocalizationManager.GetTranslation(item.Name)}): +1 on melee attacks for the rest of the turn");
                    }
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("qm consumed")) Plugin.Log.LogWarning("GHQuartermaster (item used): " + e); }
        }

        private static string TurnKey(CActor a) => (ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0) + "/" + (a?.ActorGuid ?? "");

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), nameof(CAttackActiveBonus_BuffAttack.ReferenceStrength))]
        [HarmonyPostfix]
        private static void ProficiencyStrength(CAttackActiveBonus_BuffAttack __instance, ref int __result)
        {
            try
            {
                if (__result == 0) return;
                CActiveBonus b = __instance.ActiveBonus;
                if (b == null || !GHPlague.Marked(b.Ability, "GH2E_QMProficiencyTop")) return;
                if (_profTurn != TurnKey(b.Actor) || GameState.InternalCurrentActor != b.Actor) __result = 0;
            }
            catch { }
        }

        // ------------------------------------------------------------------ Proficiency bottom: swap in the +1 copies

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void Ended(CAbility __instance)
        {
            try
            {
                if (__instance == null) return;
                if (__instance.AbilityHasHappened && GHPlague.Marked(__instance, "GH2E_QMProficiencyBottom")) Upgrade();
                if (__instance.AbilityType == CAbility.EAbilityType.Move || __instance.AbilityType == CAbility.EAbilityType.Teleport
                    || __instance.AbilityType == CAbility.EAbilityType.Teleport2)
                    AllyEndedMove(__instance);
            }
            catch (Exception e) { if (GHClasses.Reported.Add("qm ended")) Plugin.Log.LogWarning("GHQuartermaster (ability end): " + e); }
        }

        private static void Upgrade()
        {
            foreach (CPlayerActor p in LivingPlayers().ToList())
                foreach (CItem i in (p.Inventory?.AllItems ?? new List<CItem>()).Where(i => i != null && (i.ID == ItemBase + 7 || i.ID == ItemBase + 8) && i.SlotState != CItem.EItemSlotState.Consumed).ToList())
                {
                    int plus = i.ID == ItemBase + 7 ? IronPlus : BarbedPlus;
                    p.Inventory.RemoveItem(i);
                    p.Inventory.AddItem(new CItem(plus, CBaseCard.ECardType.Item, 0u));
                    Plugin.Log.LogInfo($"GHRule QMProficiency card {ProficiencyCard} {GHPlague.NameOf(p)}'s {ItemName(No(plus))} gains +1 ({(plus == IronPlus ? "Shield 3" : "Retaliate 3")})");
                }
        }

        // ------------------------------------------------------------------ Set Up Shop

        private static readonly HashSet<Guid> NoMoveLogged = new HashSet<Guid>();

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.ImmuneToAbility))]
        [HarmonyPostfix]
        private static void CannotMove(CActor actor, CAbility ability, ref bool __result)
        {
            try
            {
                if (__result || ability == null || ability.AbilityType != CAbility.EAbilityType.Move || !IsQM(actor)) return;
                if (ability.ActiveBonusData != null && ability.ActiveBonusData.Duration != CActiveBonus.EActiveBonusDurationType.NA) return;
                if (!BonusActive(actor, "GH2E_QMSetUpShop")) return;
                __result = true;
                if (NoMoveLogged.Add(ability.ID))
                    Plugin.Log.LogInfo($"GHRule SetUpShop card {SetUpShopCard} {GHPlague.NameOf(actor)} cannot perform move abilities ({ability.Name})");
            }
            catch { }
        }

        private static void AllyEndedMove(CAbility ability)
        {
            CActor ally = ability.TargetingActor;
            if (!(ally is CPlayerActor) || ally.IsDead || !ability.AbilityHasHappened || IsQM(ally)) return;
            if (ability.AbilityType != CAbility.EAbilityType.Move && ability.ActorsTargeted != null && !ability.ActorsTargeted.Contains(ally)) return;
            foreach (CPlayerActor qm in LivingPlayers().Where(p => p != ally && IsQM(p)).ToList())
            {
                if (!BonusActive(qm, "GH2E_QMSetUpShop") || !qm.IsAdjacentTo(ally)) continue;
                if (!CActor.AreActorsAllied(qm.Type, ally.Type, qm, ally)) continue;
                int have = qm.GetCharacterResourceAmount(Supplies);
                if (have < 10)
                {
                    Plugin.Log.LogInfo($"GHRule SetUpShop card {SetUpShopCard} {GHPlague.NameOf(ally)} ended a move next to {GHPlague.NameOf(qm)}: {have} supplies, 10 needed");
                    continue;
                }
                if (!Ask(qm, false, "FHMODS_QM_SHOP", "FHMODS_QM_YES", "FHMODS_QM_NO", GHPlague.NameOf(ally), have))
                {
                    Plugin.Log.LogInfo($"GHRule SetUpShop card {SetUpShopCard} {GHPlague.NameOf(ally)} ended a move next to {GHPlague.NameOf(qm)}: declined");
                    continue;
                }
                Plugin.Log.LogInfo($"GHRule SetUpShop card {SetUpShopCard} {GHPlague.NameOf(ally)} ended a move next to {GHPlague.NameOf(qm)}: supplies spent on their items");
                SpendSupplies(qm, new List<CActor> { ally }, SetUpShopCard, "Set Up Shop");
            }
        }

        // ------------------------------------------------------------------ end of turn

        [HarmonyPatch(typeof(GameState), nameof(GameState.LogEndTurn))]
        [HarmonyPostfix]
        private static void TurnEnded(CActor actor)
        {
            try
            {
                Purge();
                Spent.Clear();
                NoMoveLogged.Clear();
                if (IsQM(actor))
                {
                    if (_profTurn == TurnKey(actor)) _profTurn = null;
                    if (((CPlayerActor)actor).CharacterClass.LongRest) { Note(LongRests, actor); GHSavedCounts.Set(actor, SlotRestRound, Round); }
                    GHSavedCounts.Set(actor, SlotTurnRound, Round);
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("qm turn end")) Plugin.Log.LogWarning("GHQuartermaster (turn end): " + e); }
        }

        // ------------------------------------------------------------------ masteries

        private static readonly Dictionary<string, int> LedgerSpends = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<int>> GiveRounds = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<int>> LongRests = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        private static ScenarioState _scenario;

        private static void Fresh()
        {
            ScenarioState s = ScenarioManager.CurrentScenarioState;
            // no scenario state (results screen, map): keep the last scenario's counts for the mastery check
            if (s == null || ReferenceEquals(s, _scenario)) return;
            _scenario = s;
            LedgerSpends.Clear();
            GiveRounds.Clear();
            LongRests.Clear();
        }

        private static void Count(Dictionary<string, int> d, CActor a)
        {
            Fresh();
            string g = a?.ActorGuid ?? "";
            d[g] = (d.TryGetValue(g, out int n) ? n : 0) + 1;
        }

        private static void Note(Dictionary<string, HashSet<int>> d, CActor a)
        {
            Fresh();
            string g = a?.ActorGuid ?? "";
            if (!d.TryGetValue(g, out HashSet<int> set)) d[g] = set = new HashSet<int>();
            set.Add(ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0);
        }

        private static void NoteGive(CActor giver)
        {
            if (!IsQM(giver)) return;
            Note(GiveRounds, giver);
            GHSavedCounts.Set(giver, SlotGiveRound, Round);
        }

        private static int Round => ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0;

        // The mastery records are also saved with the scenario (GHSavedCounts slots on the Quartermaster), so a save/reload,
        // a rewind or a rejoin keeps them: 1 = Ledger spends this scenario, 2 = "missed a round" (a round in which she took a
        // turn, didn't long rest and gave no item), 3 = last round with an item given, 4 = last round with a long rest,
        // 5 = last round in which she ended a turn. Slot 2 is judged at each round's end from slots 3-5.
        internal const int SlotLedger = 1, SlotMissed = 2, SlotGiveRound = 3, SlotRestRound = 4, SlotTurnRound = 5;

        [HarmonyPatch(typeof(ElementInfusionBoardManager), nameof(ElementInfusionBoardManager.EndRound))]
        [HarmonyPrefix]
        private static void RoundEnded()
        {
            try
            {
                int r = Round;
                if (r <= 0 || ScenarioManager.Scenario?.PlayerActors == null) return;
                foreach (CPlayerActor qm in ScenarioManager.Scenario.PlayerActors.Where(p => p != null && IsQM(p)))
                {
                    if (GHSavedCounts.Get(qm, SlotTurnRound) != r || GHSavedCounts.Get(qm, SlotMissed) > 0) continue;
                    if (GHSavedCounts.Get(qm, SlotGiveRound) == r || GHSavedCounts.Get(qm, SlotRestRound) == r) continue;
                    GHSavedCounts.Set(qm, SlotMissed, 1);
                    Plugin.Log.LogInfo($"GHRule QMMastery card 0 Never Short: round {r} ended without an item given (no long rest): the mastery is missed this scenario");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("qm round end")) Plugin.Log.LogWarning("GHQuartermaster (round end): " + e); }
        }

        [HarmonyPatch(typeof(CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Mastery(CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                    ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null) return true;
            bool ledger = tags.Contains("FrosthavenMods_QMSupplySpends"), everyRound = tags.Contains("FrosthavenMods_QMItemEveryRound");
            if (!ledger && !everyRound) return true;
            try
            {
                Fresh();
                List<CPlayerStats> stats = (comparePlayerStats ?? new List<CPlayerStats>()).Where(s => s != null).ToList();
                string self = stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>())
                    .Select(x => x?.ActionData?.MainActorGUID).FirstOrDefault(g => !string.IsNullOrEmpty(g)) ?? "";
                if (ledger)
                {
                    int need = Math.Max(1, unlockConditionTarget.Value);
                    int have = Math.Max(LedgerSpends.TryGetValue(self, out int n) ? n : 0, GHSavedCounts.Get(self, SlotLedger));
                    totalConditions = need;
                    conditionsMet = Math.Min(have, need);
                    __result = have >= need;
                }
                else
                {
                    var rounds = stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>())
                        .Where(x => x != null && x.Round > 0).Select(x => x.Round).Distinct().ToList();
                    HashSet<int> rests = LongRests.TryGetValue(self, out HashSet<int> r) ? r : new HashSet<int>();
                    HashSet<int> gives = GiveRounds.TryGetValue(self, out HashSet<int> g2) ? g2 : new HashSet<int>();
                    List<int> judged = rounds.Where(x => !rests.Contains(x)).ToList();
                    totalConditions = Math.Max(1, judged.Count);
                    conditionsMet = judged.Count(gives.Contains);
                    __result = judged.Count > 0 && conditionsMet == judged.Count;
                    // after a reload the memory sets miss the earlier rounds: the saved record decides (no round missed
                    // at a round's end, and the last round given or rested, as the check may run before its end)
                    CPlayerActor me = GHSavedCounts.Player(self);
                    if (me != null && judged.Count > 0)
                    {
                        int last = rounds.Max();
                        bool lastOk = !judged.Contains(last) || gives.Contains(last) || GHSavedCounts.Get(me, SlotGiveRound) == last || GHSavedCounts.Get(me, SlotRestRound) == last;
                        __result = GHSavedCounts.Get(me, SlotMissed) == 0 && lastOk;
                        if (__result) conditionsMet = totalConditions;
                        else conditionsMet = Math.Min(conditionsMet, totalConditions - 1);
                    }
                }
                Plugin.Log.LogInfo($"GHRule QMMastery card 0 {(ledger ? "Quartermaster's Ledger" : "Never Short")}: {conditionsMet}/{totalConditions}{(__result ? " met" : "")}");
            }
            catch (Exception e)
            {
                if (GHClasses.Reported.Add("qm mastery")) Plugin.Log.LogError("GHQuartermaster (mastery): " + e);
                totalConditions = 1; conditionsMet = 0; __result = false;
            }
            return false;
        }

        // ------------------------------------------------------------------ perks (campaign)

        private static readonly HashSet<string> PerkLogged = new HashSet<string>();

        private static bool Has(FHRL.CharacterState c, string perk) => c?.PerkIDs != null && c.PerkIDs.Any(p => p.ID == perk);

        [HarmonyPatch(typeof(FHRL.TradingController), nameof(FHRL.TradingController.GetDiscountedCost))]
        [HarmonyPostfix]
        private static void Discount(FHRL.TradingController __instance, FHRL.ItemDef item, ref int __result)
        {
            try
            {
                var party = __instance?.CampaignController?.State?.PartyCharacters;
                if (party == null || __result <= 0 || !party.Any(c => Has(c, GuildConnections))) return;
                __result = Math.Max(0, __result - 5);
                if (PerkLogged.Add("discount " + item?.ID.ID))
                    Plugin.Log.LogInfo($"GHRule GuildConnections card 0 Guild Connections: {item?.Name} costs {__result} gold (5 less)");
            }
            catch { }
        }

        [HarmonyPatch(typeof(FHRL.CharacterController), nameof(FHRL.CharacterController.GetMaxUnlockedSmallItems))]
        [HarmonyPostfix]
        private static void ExtraSmallItem(FHRL.CharacterState character, ref uint __result)
        {
            if (!Has(character, DeepPockets)) return;
            __result++;
            if (PerkLogged.Add("pockets " + character.Name))
                Plugin.Log.LogInfo($"GHRule DeepPockets card 0 Deep Pockets: {character.Name} may bring {__result} small items");
        }

        [HarmonyPatch(typeof(FHRL.CharacterController), nameof(FHRL.CharacterController.GetMaxSmallItems))]
        [HarmonyPostfix]
        private static void ExtraSmallSlot(FHRL.CharacterState character, ref uint __result)
        {
            if (Has(character, DeepPockets)) __result++;
        }

        // ------------------------------------------------------------------ the supplies number on the status row

        [HarmonyPatch(typeof(ActorBehaviour), nameof(ActorBehaviour.UpdateWorldspaceConditionsUI))]
        [HarmonyPostfix]
        private static void SuppliesNumber(ActorBehaviour __instance)
        {
            try
            {
                CActor actor = __instance.Actor;
                EffectsBar bar = __instance.m_WorldspacePanelUI?.EffectBar;
                if (!IsQM(actor) || bar == null) return;
                var refs = Traverse.Create(bar).Field("m_CustomEffectsRefs").GetValue<Dictionary<string, CustomEffect>>();
                if (refs == null || !refs.TryGetValue(Supplies, out CustomEffect effect) || effect == null) return;
                Transform t = effect.transform.Find("GHSuppliesCount");
                TextMeshProUGUI text = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
                if (text == null)
                {
                    TextMeshProUGUI template = bar.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault()
                                               ?? __instance.m_WorldspacePanelUI.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
                    var go = new GameObject("GHSuppliesCount", typeof(RectTransform));
                    go.transform.SetParent(effect.transform, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                    text = go.AddComponent<TextMeshProUGUI>();
                    if (template != null) { text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial; }
                    text.color = Color.white;
                    text.fontStyle = FontStyles.Bold;
                    text.enableAutoSizing = true;
                    text.fontSizeMin = 6f;
                    text.fontSizeMax = 40f;
                    text.alignment = TextAlignmentOptions.BottomRight;
                    text.outlineWidth = 0.25f;
                    text.outlineColor = Color.black;
                    text.raycastTarget = false;
                }
                int amount = actor.GetCharacterResourceAmount(Supplies);
                text.text = amount.ToString();
                string tip = GLOOM.LocalizationManager.GetTranslation("GH_GH2E_SUPPLIES_TOOLTIP");   // the resource's LocKey as the porter ships it
                Traverse.Create(effect).Field("tooltip").GetValue<UITextTooltipTarget>()?.SetText($"Supplies: {amount}", refreshTooltip: false, tip);
            }
            catch (Exception e) { if (GHClasses.Reported.Add("qm supplies number")) Plugin.Log.LogWarning("GHQuartermaster (supplies number): " + e); }
        }

        // ------------------------------------------------------------------ refreshes never bring a class item back

        [HarmonyPatch(typeof(CAbilityRefreshItemCards), "ValidItems")]
        [HarmonyPostfix]
        private static void NoClassItemRefresh(List<CItem> __result)
        {
            try { __result?.RemoveAll(IsClassItem); } catch { }
        }

        // ------------------------------------------------------------------ our text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_QM_CHOOSE"] = "Give {0} the {1} (first) or the {2} (second)?",
            ["FHMODS_QM_CHOOSE_A"] = "The first",
            ["FHMODS_QM_CHOOSE_B"] = "The second",
            ["FHMODS_QM_BOOSTER"] = "Booster Pack: give the {0} to {1}? ({2} of 3 given to yourself so far)",
            ["FHMODS_QM_GIVE"] = "Give",
            ["FHMODS_QM_NEXT"] = "Next",
            ["FHMODS_QM_YES"] = "Yes",
            ["FHMODS_QM_NO"] = "No",
            ["FHMODS_QM_CLUB_ALLY"] = "Giant Club: give items from the Quartermaster item supply to {0}? ({1} of 4 given so far)",
            ["FHMODS_QM_CLUB_ITEM"] = "Giant Club: give the {0} to {1}? ({2} of 4 given so far)",
            ["FHMODS_QM_FORESIGHT"] = "Scroll of Foresight: card {0} of {2} on top of your attack modifier deck is {1}. Place it on the bottom of the deck?",
            ["FHMODS_QM_BOTTOM"] = "To the bottom",
            ["FHMODS_QM_KEEP"] = "Keep",
            ["FHMODS_QM_SPEND"] = "Supplies: recover {0}'s {1} ({2} gold)? {3} gold of supplies left to spend.",
            ["FHMODS_QM_RECOVER"] = "Recover",
            ["FHMODS_QM_SKIP"] = "Skip",
            ["FHMODS_QM_PROFICIENCY"] = "Proficiency: you recovered the {0} ({1} gold). Gain one random item from the Quartermaster item supply?",
            ["FHMODS_QM_SHOP"] = "Set Up Shop: {0} ended their move next to you. Spend your {1} supplies to recover their items?",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_QM_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
