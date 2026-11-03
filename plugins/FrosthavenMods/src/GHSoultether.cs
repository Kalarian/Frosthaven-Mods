using System;
using System.Collections.Generic;
using System.Linq;
using AStar;
using FHRL;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Soultether's rules that its cards' data can't say:
    ///   - Living Night bottom (card 33503): one token for both effects. The hidden damage bonus BDamageAttacker carries the
    ///     two slots; when it finishes, the disadvantage bonus BDisadvantage (no slots of its own) finishes with it.
    ///     "GHRule LivingNight".
    ///   - "Summons summoned by that action" (Unending Dominance top, Otherworldly Rage bottom: MiscVars GH2E_LastSummoned):
    ///     the summons each summon ability creates are kept per summoner (CAbilitySummon.TrySpawnSummon's list); a marked
    ///     ability's valid targets are exactly the last summon ability's summons that are alive. GH2E_ResetLastSummoned
    ///     (Otherworldly Rage's replay) forgets the list first, so only the replayed card's summons count.
    ///     "GHRule LastSummoned".
    ///   - Imposed Will bottom: at the start of every attack on a Soultether's summon, the hidden resource GH2EHadWard on
    ///     the summon says whether it had Ward then (the card's triggers read it after the attack). "GHRule ImposedWill".
    ///   - Covetous Imp: when it kills a normal or elite enemy that was not summoned, its summoner gains that money token
    ///     (a loot token, Frosthaven FAQ section 10): one loot-deck draw. "GHRule CovetousImp".
    ///   - Rift Spirit (MiscVars GH2E_SummonerPicksTarget on its end-of-turn Shield): "you control the grant abilities of
    ///     this summon": with more than one ally in range the summoner's player picks (asked ally by ally).
    ///     "GHRule RiftSpiritGrant".
    ///   - Slime Spirit (MiscVars GH2E_SlimeSplit on its lethal-damage prevention): when the prevention is spent, a second
    ///     Slime Spirit is summoned for the same Soultether in an empty hex adjacent to it (the first such hex), tied to the
    ///     same card (Oozing Manifestation, 33525); the new one has no prevention of its own (the card's one character
    ///     token is placed). "GHRule SlimeSplit".
    ///   - Void Eater (MiscVars GH2E_CurseMonsterDeck on a NullTargeting step): a curse card goes straight into the monster
    ///     attack modifier deck (10-curse limit). "GHRule VoidEaterCurse".
    ///   - Masteries: FrosthavenMods_SummonCardsKeptBeforeRest (Value different cards whose summon ability was performed
    ///     before the round of the first rest, all still in the active area at the check) and
    ///     FrosthavenMods_FirstRoundSummonsAcrossScenarios (a career list of round-1 summon abilities, card + ability, in
    ///     synced campaign state: a marker in CampaignState.LootedChestTreasureTableIDs, "FHMODS:Soul2R1:<character>=...",
    ///     written from the scenario result every peer receives, as DwarfTweaks' Boneshaper count; inert in a save without
    ///     the mod).
    /// Gated on the pack's class SoultetherID, its summon class IDs, its card IDs and misc vars only the pack carries.
    /// </summary>
    [HarmonyPatch]
    internal static class GHSoultether
    {
        internal const string Soul = "SoultetherID";
        internal const int LivingNight = 33503, Oozing = 33525, ImposedWill = 33530;
        internal const string HadWard = "GH2EHadWard";
        internal const string KeptTag = "FrosthavenMods_SummonCardsKeptBeforeRest";
        internal const string CareerTag = "FrosthavenMods_FirstRoundSummonsAcrossScenarios";
        private const string CareerMarker = "FHMODS:Soul2R1:";

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Rule(string rule, int card, string detail) => Plugin.Log.LogInfo($"GHRule {rule} card {card} {detail}");

        private static void Report(string what, Exception e)
        {
            if (Reported.Add(what)) Plugin.Log.LogError($"GHSoultether ({what}): {e}");
        }

        private static bool IsSoul(CActor a) => a != null && a.Class?.ID == Soul;

        private static CActor SummonerOf(CActor a) => (a as CHeroSummonActor)?.Summoner;

        // ------------------------------------------------------------------ Living Night bottom

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.Finish))]
        [HarmonyPostfix]
        private static void BonusFinished(CActiveBonus __instance)
        {
            if (__instance?.Ability == null) return;
            try
            {
                if (__instance.BaseCard?.ID == LivingNight && __instance.Ability.Name == "BDamageAttacker")
                {
                    foreach (CActiveBonus b in __instance.Actor.ActiveBonuses.Where(x => x != null && x != __instance && x.BaseCard?.ID == LivingNight
                                                                                         && x.Ability?.Name == "BDisadvantage" && !x.Finishing()).ToList())
                    {
                        b.Finish();
                        Rule("LivingNight", LivingNight, "the two attacks on your summons are used: the disadvantage ends with the damage");
                    }
                }
            }
            catch (Exception e) { Report("Living Night", e); }
            try { SlimeSplit(__instance); } catch (Exception e) { Report("Slime Spirit", e); }
        }

        // ------------------------------------------------------------------ the summons of the last summon ability

        private static readonly Dictionary<string, List<CActor>> LastSummoned = new Dictionary<string, List<CActor>>();
        private static readonly Dictionary<string, HashSet<string>> FirstRound = new Dictionary<string, HashSet<string>>();
        private static object _firstRoundState;

        [HarmonyPatch(typeof(CAbilitySummon), nameof(CAbilitySummon.TrySpawnSummon), new[] { typeof(CActor), typeof(string), typeof(string), typeof(List<CTile>), typeof(string), typeof(string),
            typeof(CAbility), typeof(CActor), typeof(List<CActor>), typeof(bool), typeof(bool), typeof(bool), typeof(CActor) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal,
                ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Normal })]
        [HarmonyPostfix]
        private static void Summoned(CActor summoner, CAbility ability, List<CActor> summonedActors, bool __result)
        {
            try
            {
                if (!__result || !IsSoul(summoner) || summonedActors == null || summonedActors.Count == 0 || ability == null) return;
                LastSummoned[summoner.ActorGuid ?? ""] = summonedActors;
                int round = ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0;
                CBaseCard card = null;
                try { card = ability.GetAbilityBaseCard(); } catch { }
                if (round == 1 && card is CAbilityCard && card.ID >= 33501 && card.ID <= 33531)
                {
                    if (!ReferenceEquals(_firstRoundState, ScenarioManager.CurrentScenarioState)) { FirstRound.Clear(); _firstRoundState = ScenarioManager.CurrentScenarioState; }
                    if (!FirstRound.TryGetValue(Soul, out var set)) FirstRound[Soul] = set = new HashSet<string>();
                    if (set.Add($"{card.ID}/{ability.Name}")) Rule("FirstRoundSummons", card.ID, $"mastery: round 1 summon ability {card.ID}/{ability.Name}");
                }
            }
            catch (Exception e) { Report("summons kept", e); }
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.Start))]
        [HarmonyPrefix]
        private static void BeforeStart(CAbility __instance, CActor targetingActor)
        {
            try
            {
                if (__instance?.MiscVars != null && targetingActor != null && GHPlague.Marked(__instance, "GH2E_ResetLastSummoned"))
                    LastSummoned.Remove(targetingActor.ActorGuid ?? "");
            }
            catch (Exception e) { Report("summons reset", e); }
        }

        [HarmonyPatch(typeof(SharedAbilityTargeting), nameof(SharedAbilityTargeting.GetValidActorsInRange), new[] { typeof(CAbility) })]
        [HarmonyPostfix]
        private static void AfterValidActors(CAbility ability)
        {
            try
            {
                if (ability?.MiscVars == null || ability.TargetingActor == null || !GHPlague.Marked(ability, "GH2E_LastSummoned")) return;
                LastSummoned.TryGetValue(ability.TargetingActor.ActorGuid ?? "", out List<CActor> made);
                var keep = (made ?? new List<CActor>()).Where(a => a != null && !a.IsDead && ScenarioManager.Scenario.HasActor(a)).ToList();
                ability.ValidActorsInRange.RemoveAll(a => !keep.Contains(a));
                foreach (CActor a in keep)
                    if (!ability.ValidActorsInRange.Contains(a)) ability.ValidActorsInRange.Add(a);
                if (ability.TilesInRange != null)
                {
                    ability.TilesInRange.RemoveAll(t => t == null || !keep.Any(a => a.ArrayIndex == t.m_ArrayIndex));
                    foreach (CActor a in keep)
                    {
                        CTile tile = ScenarioManager.Tiles[a.ArrayIndex.X, a.ArrayIndex.Y];
                        if (tile != null && !ability.TilesInRange.Contains(tile)) ability.TilesInRange.Add(tile);
                    }
                }
                Rule("LastSummoned", GHPlague.CardOf(ability), $"{ability.Name}: the summons that summon action created: {(keep.Count == 0 ? "none" : string.Join(", ", keep.Select(GHPlague.NameOf)))}");
            }
            catch (Exception e) { Report("summons targets", e); }
        }

        // ------------------------------------------------------------------ Imposed Will bottom: Ward when the attack started

        [HarmonyPatch(typeof(CAbilityAttack), "AttackActor")]
        [HarmonyPrefix]
        private static void BeforeAttackActor(CAttackSummary.TargetSummary attackingTargetSummary)
        {
            try
            {
                CActor target = attackingTargetSummary?.ActorToAttack;
                if (!IsSoul(SummonerOf(target))) return;
                bool ward = target.Tokens.HasKey(CCondition.EPositiveCondition.Ward);
                int has = target.GetCharacterResourceAmount(HadWard);
                if (ward && has == 0) target.AddCharacterResource(HadWard, 1);
                else if (!ward && has > 0) target.RemoveCharacterResource(HadWard, has);
                if (ward && SummonerOf(target).ActiveBonuses.Any(b => b?.BaseCard?.ID == ImposedWill))
                    Rule("ImposedWill", ImposedWill, $"{GHPlague.NameOf(target)} has Ward as the attack starts: no Ward and no damage to the attacker after it");
            }
            catch (Exception e) { Report("Imposed Will", e); }
        }

        // ------------------------------------------------------------------ Covetous Imp: its kills' money token goes to the summoner

        private static readonly HashSet<string> ImpKills = new HashSet<string>();
        private static object _impState;

        [HarmonyPatch(typeof(CActor), nameof(CActor.OnDeath))]
        [HarmonyPrefix]
        private static void Dying(CActor __instance, CActor targetingActor, CActor.ECauseOfDeath causeOfDeath)
        {
            try
            {
                if (causeOfDeath == CActor.ECauseOfDeath.ActorRemovedFromMap || !(__instance is CEnemyActor e) || e.IsSummon) return;
                if (!(targetingActor is CHeroSummonActor imp) || imp.Class?.ID != "CovetousImpID" || !(imp.Summoner is CPlayerActor owner)) return;
                if (e.OriginalType != CActor.EType.Enemy && e.OriginalType != CActor.EType.Enemy2) return;
                if (e.MonsterClass != null && e.MonsterClass.Boss) return;    // a boss drops no money token
                if (!ReferenceEquals(_impState, ScenarioManager.CurrentScenarioState)) { ImpKills.Clear(); _impState = ScenarioManager.CurrentScenarioState; }
                if (!ImpKills.Add(e.ActorGuid ?? "")) return;
                owner.GainLoot(endOfScenario: false, incLootTokenCount: true);
                ScenarioRuleClient.MessageHandler(new CStackRewardsToShow_MessageData(owner)
                {
                    m_Rewards = owner.LastLoot.GetRewards().ToList(),
                    m_SourceType = "MoneyToken"
                });
                Rule("CovetousImp", 33519, $"the Covetous Imp killed {GHPlague.NameOf(e)}: {GHPlague.NameOf(owner)} gains its money token directly (one loot-deck draw)");
            }
            catch (Exception ex) { Report("Covetous Imp", ex); }
        }

        // ------------------------------------------------------------------ Rift Spirit: the summoner picks the grant's ally

        [HarmonyPatch(typeof(CAbilityTargeting), nameof(CAbilityTargeting.SortValidActorsInRange))]
        [HarmonyPostfix]
        private static void AfterSort(CAbilityTargeting __instance, CActor actorApplying, ref List<CActor> validActorsInRange)
        {
            try
            {
                if (__instance?.MiscVars == null || validActorsInRange == null || validActorsInRange.Count < 2 || !GHPlague.Marked(__instance, "GH2E_SummonerPicksTarget")) return;
                CActor owner = SummonerOf(actorApplying);
                if (owner == null) return;
                for (int i = 0; i < validActorsInRange.Count - 1; i++)
                {
                    CActor c = validActorsInRange[i];
                    if (!GHPlague.Ask(owner, false, "FHMODS_SOUL_GRANT", "FHMODS_SOUL_GRANT_YES", "FHMODS_SOUL_GRANT_NO", GHPlague.NameOf(actorApplying), GHPlague.NameOf(c))) continue;
                    validActorsInRange.RemoveAt(i);
                    validActorsInRange.Insert(0, c);
                    Rule("RiftSpiritGrant", 0, $"{GHPlague.NameOf(owner)} picks {GHPlague.NameOf(c)} for {GHPlague.NameOf(actorApplying)}'s {__instance.AbilityType}");
                    return;
                }
                CActor last = validActorsInRange[validActorsInRange.Count - 1];
                validActorsInRange.Remove(last);
                validActorsInRange.Insert(0, last);
                Rule("RiftSpiritGrant", 0, $"{GHPlague.NameOf(owner)} picks {GHPlague.NameOf(last)} (the last ally offered) for {GHPlague.NameOf(actorApplying)}'s {__instance.AbilityType}");
            }
            catch (Exception e) { Report("Rift Spirit", e); }
        }

        // ------------------------------------------------------------------ Slime Spirit's split

        [ThreadStatic] private static bool _splitting;

        private static void SlimeSplit(CActiveBonus b)
        {
            if (_splitting || !GHPlague.Marked(b.Ability, "GH2E_SlimeSplit")) return;
            if (!(b.Actor is CHeroSummonActor slime) || slime.IsDead || slime.Health <= 0 || b.Remaining > 0) return;   // only a spent prevention
            if (!(slime.Summoner is CPlayerActor soul) || !IsSoul(soul)) return;
            CAbilityCard card = soul.CharacterClass?.ActivatedAbilityCards?.FirstOrDefault(c => c != null && c.ID == Oozing);
            CAbility summon = card?.TopAction?.Abilities?.FirstOrDefault(a => a != null && a.AbilityType == CAbility.EAbilityType.Summon);
            CTile tile = GameState.GetTilesInRange(slime.ArrayIndex, 1, CAbility.EAbilityTargeting.Range, emptyTilesOnly: true)
                .FirstOrDefault(t => t != null && CPathFinder.IsAdjacent(t.m_ArrayIndex, slime.ArrayIndex));
            if (tile == null)
            {
                Rule("SlimeSplit", Oozing, "the Slime Spirit's damage was negated; no empty adjacent hex for the second Slime Spirit");
                return;
            }
            var made = new List<CActor>();
            bool done = false, summoned = false, cancel = false;
            _splitting = true;
            try
            {
                CAbilitySummon.TrySpawnSummon(soul, "SlimeSpirit2ID", null, new List<CTile> { tile }, out string error, summon?.AnimOverload, summon, null, made, out done, ref summoned, ref cancel);
                foreach (CActor a in made)
                    foreach (CActiveBonus x in a.ActiveBonuses.Where(x => x?.Ability != null && GHPlague.Marked(x.Ability, "GH2E_SlimeSplit")).ToList())
                        x.Finish();   // the card's one character token is placed: the new Slime Spirit has no prevention
                Rule("SlimeSplit", Oozing, made.Count > 0 ? $"the Slime Spirit's damage was negated: a second Slime Spirit at {tile.m_ArrayIndex}{(card == null ? " (card not in the active area: not tied to it)" : "")}"
                                                          : $"the second Slime Spirit could not be summoned ({error})");
            }
            finally { _splitting = false; }
        }

        // ------------------------------------------------------------------ Void Eater: a curse into the monster deck

        [HarmonyPatch(typeof(CAbilityNullTargeting), nameof(CAbilityNullTargeting.ApplyToActor))]
        [HarmonyPostfix]
        private static void NullApplied(CAbilityNullTargeting __instance)
        {
            try
            {
                if (__instance?.MiscVars == null || !GHPlague.Marked(__instance, "GH2E_CurseMonsterDeck")) return;
                CMonsterAttackModifierDeck deck = MonsterClassManager.EnemyMonsterAttackModifierDeck;
                int curses = deck.AttackModifierCards.Count(c => c != null && c.IsCurse);
                if (curses >= 10)
                {
                    Rule("VoidEaterCurse", 33527, "the monster deck already holds 10 curses: none added");
                    return;
                }
                MonsterClassManager.AddCurseCard(deck);
                Rule("VoidEaterCurse", 33527, $"the Void Eater killed while Dark was strong or waning: a curse shuffled into the monster attack modifier deck ({curses + 1} curses)");
            }
            catch (Exception e) { Report("Void Eater", e); }
        }

        // ------------------------------------------------------------------ the masteries

        [HarmonyPatch(typeof(MapRuleLibrary.YML.Shared.CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        private static bool Mastery(MapRuleLibrary.YML.Shared.CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                    ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null || (!tags.Contains(KeptTag) && !tags.Contains(CareerTag))) return true;
            List<CPlayerStats> stats = (comparePlayerStats ?? new List<CPlayerStats>()).Where(s => s != null).ToList();
            int needed = Math.Max(1, unlockConditionTarget.Value);
            try
            {
                if (tags.Contains(KeptTag)) Kept(needed, stats, ref totalConditions, ref conditionsMet, ref __result);
                else Career(needed, stats, ref totalConditions, ref conditionsMet, ref __result);
            }
            catch (Exception e)
            {
                Report("mastery", e);
                totalConditions = needed;
                conditionsMet = 0;
                __result = false;
            }
            return false;
        }

        private static IEnumerable<CPlayerStatsAbilities> SummonRecords(List<CPlayerStats> stats) =>
            stats.SelectMany(s => s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>())
                .Where(a => a?.ActionData != null && a.ActionData.AbilityType == CAbility.EAbilityType.Summon && a.ActionData.CardType == CBaseCard.ECardType.CharacterAbility
                            && a.ActionData.CardID >= 33501 && a.ActionData.CardID <= 33531);

        private static void Kept(int needed, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int firstRest = stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Concat<CPlayerStatsAction>(stats.SelectMany(s => s.EndTurn ?? Enumerable.Empty<CPlayerStatsEndTurn>()))
                .Where(x => x?.ActionData != null && x.ActionData.MainActorResting).Select(x => x.Round).DefaultIfEmpty(int.MaxValue).Min();
            var cards = new HashSet<int>(SummonRecords(stats).Where(a => a.Round < firstRest).Select(a => a.ActionData.CardID));
            CPlayerActor soul = ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => IsSoul(p));
            var active = new HashSet<int>((soul?.CharacterClass?.ActivatedAbilityCards ?? new List<CAbilityCard>()).Where(c => c != null).Select(c => c.ID));
            int kept = cards.Count(active.Contains);
            total = needed;
            met = Math.Min(kept, needed);
            result = kept >= needed;
            Plugin.Log.LogInfo($"GHRule SummonCardsKept card 0 mastery: {cards.Count} summon cards before the first rest{(firstRest == int.MaxValue ? " (no rest)" : $" (round {firstRest})")}, {kept} still in the active area (needs {needed}){(result ? ": met" : "")}");
        }

        private static HashSet<string> CurrentFirstRound(List<CPlayerStats> stats)
        {
            if (!ReferenceEquals(_firstRoundState, ScenarioManager.CurrentScenarioState)) { FirstRound.Clear(); _firstRoundState = ScenarioManager.CurrentScenarioState; }
            if (!FirstRound.TryGetValue(Soul, out var set)) FirstRound[Soul] = set = new HashSet<string>();
            if (stats != null)
                foreach (CPlayerStatsAbilities a in SummonRecords(stats).Where(a => a.Round == 1))
                    set.Add($"{a.ActionData.CardID}/{a.ActionData.ActingAbilityName}");
            return set;
        }

        /// <summary>The career list saved for the party's Soultether (any character of the class: a party has one).</summary>
        private static HashSet<string> Saved(CampaignState state, string characterGuid)
        {
            var set = new HashSet<string>();
            if (state?.LootedChestTreasureTableIDs == null) return set;
            foreach (string s in state.LootedChestTreasureTableIDs.Where(x => x != null && x.StartsWith(CareerMarker, StringComparison.Ordinal)))
            {
                int eq = s.IndexOf('=');
                if (eq < 0) continue;
                if (characterGuid != null && s.Substring(CareerMarker.Length, eq - CareerMarker.Length) != characterGuid) continue;
                foreach (string k in s.Substring(eq + 1).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) set.Add(k);
            }
            return set;
        }

        private static CampaignState _lastCampaign;

        private static void Career(int needed, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            var all = new HashSet<string>(Saved(GHClassesRule.LiveState ?? _lastCampaign, null));
            all.UnionWith(CurrentFirstRound(stats));
            total = needed;
            met = Math.Min(all.Count, needed);
            result = all.Count >= needed;
            Plugin.Log.LogInfo($"GHRule FirstRoundSummons card 0 mastery: {all.Count} different round-1 summon abilities over the campaign (needs {needed}){(result ? ": met" : "")}");
        }

        [HarmonyPatch(typeof(ScenarioController), nameof(ScenarioController.CompleteActiveScenario))]
        [HarmonyPrefix]
        private static void SaveCareer(ScenarioController __instance, ScenarioResult result)
        {
            try
            {
                CampaignState state = __instance?.CampaignController?.State;
                _lastCampaign = state;
                if (state == null || result == null || result.isRestarting) return;
                foreach (ScenarioResult.CharacterResult r in result.Characters)
                {
                    if (r.Character?.ID.ID != Soul) continue;
                    CharacterState c = state.PartyCharacters.FirstOrDefault(p => p.ClassDef == r.Character);
                    if (c == null) continue;
                    string prefix = CareerMarker + c.ID.ID + "=";
                    var set = Saved(state, c.ID.ID.ToString());
                    int before = set.Count;
                    set.UnionWith(CurrentFirstRound(null));
                    state.LootedChestTreasureTableIDs.RemoveAll(s => s != null && s.StartsWith(prefix, StringComparison.Ordinal));
                    state.LootedChestTreasureTableIDs.Add(prefix + string.Join(",", set.OrderBy(s => s, StringComparer.Ordinal)));
                    Rule("FirstRoundSummons", 0, $"mastery: career list of round-1 summon abilities {before} -> {set.Count}");
                }
            }
            catch (Exception e) { Report("career save", e); }
        }

        // ------------------------------------------------------------------ our text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_SOUL_GRANT"] = "You control the grant abilities of your {0}: give it to {1}?",
            ["FHMODS_SOUL_GRANT_YES"] = "This ally",
            ["FHMODS_SOUL_GRANT_NO"] = "Next ally",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_SOUL_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
