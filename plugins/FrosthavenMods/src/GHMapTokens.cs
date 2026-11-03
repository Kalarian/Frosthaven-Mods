using System;
using System.Collections.Generic;
using System.Linq;
using AStar;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Tinkerer's devices as printed: map tokens, not figures.
    ///   - Empty hexes (class mat: "Unlike traps, hexes containing one or more of these tokens are still empty and are not
    ///     considered negative hexes for the purposes of monster movement"; GH2e FAQ: tokens, "Tinkerer devices, etc. won't
    ///     make a hex non-empty", so figures may be summoned or spawned there). The tokens are hero summons that can't be
    ///     targeted, hurt or block, with Frosthaven's IgnoreActorCollision (a figure may stop on them: CanStopOnTopOf, the
    ///     move-tile lists, push / pull ends, teleport ends). Every "who stands on this hex" lookup of the engine
    ///     (CScenario.FindActorAt / FindActorsAt: empty-hex tests, summon placement, spawns, targets on a tile, adjacency
    ///     counts) skips them, so a token hex is empty and the figure sharing it is the one found. Logged once per
    ///     scenario load: "GHRule MapTokenEmptyHex".
    ///   - Teleportation Pad top: "You and all allies may treat the two hexes containing the tokens as being adjacent to
    ///     each other during move abilities." While both pads of a Tinkerer stand, every move of that Tinkerer or of a figure
    ///     allied to them gets a cost-1 path link between the two pad hexes, in both directions: Frosthaven's own
    ///     Deathwalker shadow links (CAbilityMove.RefreshShadowNavLinks adds node links at the start of each move step,
    ///     ClearShadowNavLinks takes them away when the move ends); the player's reachable hexes, an ally summon's AI path
    ///     and the move cost all read them. Monsters' moves never get them. "GHRule TeleportPad card 32518 ...".
    ///   - Gravity Bomb top: "At the end of your next turn, perform: Pull 2, target all enemies within Range 2 of the token.
    ///     Pull toward the token. Then any enemy occupying the hex containing the token suffers damage 5, all enemies
    ///     occupying hexes adjacent to the token suffer damage 1, and you discard this card." At the end of the Tinkerer's
    ///     turn in a later round than the token was placed, the token's three abilities (summons/GravityBomb.yml, carried as
    ///     its OnDeathAbilities) are queued as the token's own end-of-turn abilities (Frosthaven's pending scenario-modifier
    ///     abilities, the Regenerate / Wound mechanism: each runs as an action of the token, so the pull is toward the token),
    ///     then the token is taken off the map, which ends the summon and sends the card to the discard pile. The abilities
    ///     never run on a death (exhaustion must not set the bomb off). "GHRule GravityBomb card 32530 ...".
    /// Only the four pack token summons (IDs below) are touched: no Frosthaven summon has these IDs, so without the 2nd
    /// edition pack this is inert.
    /// </summary>
    [HarmonyPatch]
    internal static class GHMapTokens
    {
        internal const string TinkererClass = "Tinkerer2ID";
        internal const string PadID = "TeleportationPadID";
        internal const string BombID = "GravityBombID";
        internal const int PadCard = 32518, BombCard = 32530;

        private static readonly HashSet<string> TokenIds = new HashSet<string>
        {
            "EnhancementFieldID", "PerniciousFoggerID", PadID, BombID,
        };

        internal static bool IsToken(CActor a) =>
            a is CHeroSummonActor s && s.SummonData != null && s.SummonData.ID != null && TokenIds.Contains(s.SummonData.ID);

        internal static bool IsToken(CActor a, string id) =>
            a is CHeroSummonActor s && s.SummonData != null && s.SummonData.ID == id;

        // ---- empty hexes: the engine's position lookups skip the tokens ----

        private static object s_ReportedFor;

        [HarmonyPatch(typeof(CScenario), nameof(CScenario.FindActorsAt))]
        [HarmonyPostfix]
        private static void AfterFindActorsAt(List<CActor> __result)
        {
            if (__result == null || __result.Count == 0) return;
            for (int i = __result.Count - 1; i >= 0; i--)
                if (IsToken(__result[i])) { __result.RemoveAt(i); ReportEmpty(); }
        }

        [HarmonyPatch(typeof(CScenario), nameof(CScenario.FindActorAt), new[] { typeof(Point), typeof(CActor), typeof(bool), typeof(bool) })]
        [HarmonyPostfix]
        private static void AfterFindActorAt(CScenario __instance, Point position, CActor actorToIgnore, bool includeInitial, ref CActor __result)
        {
            if (!IsToken(__result)) return;
            // the figure sharing the token's hex, if any (FindActorsAt already leaves the tokens out); else the hex is empty
            CActor other = null;
            try { other = __instance.FindActorsAt(position, actorToIgnore, includeInitial)?.FirstOrDefault(); } catch { }
            __result = other;
            ReportEmpty();
        }

        private static void ReportEmpty()
        {
            object state = ScenarioManager.CurrentScenarioState;
            if (state == null || ReferenceEquals(state, s_ReportedFor)) return;
            s_ReportedFor = state;
            Plugin.Log.LogInfo("GHRule MapTokenEmptyHex card 0 a Tinkerer token's hex counts as empty (the figure sharing it is found, not the token)");
        }

        // ---- Teleportation Pad: the two pad hexes are adjacent for allied moves ----

        private static readonly List<(CNode node, CNavLink link)> s_PadLinks = new List<(CNode, CNavLink)>();
        private static string s_LastPadLog;

        [HarmonyPatch(typeof(CAbilityMove), nameof(CAbilityMove.ClearShadowNavLinks))]
        [HarmonyPostfix]
        private static void AfterClearLinks() => ClearPadLinks();

        [HarmonyPatch(typeof(CAbilityMove), nameof(CAbilityMove.RefreshShadowNavLinks))]
        [HarmonyPostfix]
        private static void AfterRefreshLinks(CAbilityMove __instance)
        {
            try { AddPadLinks(__instance?.TargetingActor); }
            catch (Exception e) { Report("TeleportPad", e); }
        }

        /// <summary>"Place two tokens in empty hexes": two different hexes. A token hex counts as empty (above), so without
        /// this the second pad could go on the first pad's hex, and two pads in one hex link nothing (bench 20261102-214859:
        /// both pads at (6,10), no TeleportPad rule). A pad summon's position choice leaves out the hexes of the summoner's
        /// standing pads. Other tokens may still share a pad's hex (they are empty hexes).</summary>
        [HarmonyPatch(typeof(SharedAbilityTargeting), nameof(SharedAbilityTargeting.RemoveInvalidTilesForAbilityMirrored))]
        [HarmonyPostfix]
        private static void AfterRemoveInvalidTiles(List<CTile> tiles, CAbility ability)
        {
            try
            {
                if (tiles == null || tiles.Count == 0 || !(ability is CAbilitySummon summon)) return;
                if (summon.SummonIDs == null || summon.SelectedSummonID != PadID) return;
                CActor owner = ability.TargetingActor;
                CScenario scenario = ScenarioManager.Scenario;
                if (owner == null || scenario?.HeroSummons == null) return;
                var taken = new HashSet<Point>(scenario.HeroSummons
                    .Where(s => s != null && !s.IsDead && IsToken(s, PadID) && s.Summoner == owner).Select(s => s.ArrayIndex));
                if (taken.Count == 0) return;
                int removed = tiles.RemoveAll(t => t != null && taken.Contains(t.m_ArrayIndex));
                if (removed > 0)
                    Plugin.Log.LogInfo($"GHRule TeleportPadHexes card {PadCard} the second pad goes in another hex ({removed} pad hex(es) left out for {GHPlague.NameOf(owner)})");
            }
            catch (Exception e) { Report("TeleportPad hexes", e); }
        }

        private static void ClearPadLinks()
        {
            if (s_PadLinks.Count == 0) return;
            foreach ((CNode node, CNavLink link) in s_PadLinks) node?.NavLinks?.Remove(link);
            s_PadLinks.Clear();
        }

        private static void AddPadLinks(CActor mover)
        {
            ClearPadLinks();
            CScenario scenario = ScenarioManager.Scenario;
            if (mover == null || scenario?.HeroSummons == null || IsToken(mover)) return;
            foreach (IGrouping<CPlayerActor, CHeroSummonActor> pads in scenario.HeroSummons
                         .Where(s => s != null && !s.IsDead && IsToken(s, PadID) && s.Summoner != null).GroupBy(s => s.Summoner))
            {
                List<CHeroSummonActor> two = pads.ToList();
                if (two.Count != 2) continue;
                CPlayerActor owner = pads.Key;
                if (mover != owner && !CActor.AreActorsAllied(owner.Type, mover.Type, owner, mover)) continue;
                Point a = two[0].ArrayIndex, b = two[1].ArrayIndex;
                if (a == b) continue;
                CNode na = ScenarioManager.PathFinder.GetNode(a), nb = ScenarioManager.PathFinder.GetNode(b);
                if (na == null || nb == null) continue;
                var ab = new CNavLink(1, nb);
                var ba = new CNavLink(1, na);
                if (!na.NavLinks.Contains(ab)) { na.NavLinks.Add(ab); s_PadLinks.Add((na, ab)); }
                if (!nb.NavLinks.Contains(ba)) { nb.NavLinks.Add(ba); s_PadLinks.Add((nb, ba)); }
                string key = $"{ScenarioManager.CurrentScenarioState?.RoundNumber}/{mover.ActorGuid}/{a}/{b}";
                if (key != s_LastPadLog)
                {
                    s_LastPadLog = key;
                    Plugin.Log.LogInfo($"GHRule TeleportPad card {PadCard} pads at {a} and {b} adjacent for {GHPlague.NameOf(mover)}'s move");
                }
            }
        }

        // ---- Gravity Bomb: at the end of the Tinkerer's next turn ----

        private static readonly Dictionary<string, int> s_BombArmedRound = new Dictionary<string, int>();
        private static object s_BombState;
        private static CPhaseEndTurn s_LastEndTurn;
        private static readonly List<Tuple<CAbility, CActor>> s_Queued = new List<Tuple<CAbility, CActor>>();
        private static CHeroSummonActor s_ToRemove;

        /// <summary>Called inside PhaseManager's EndTurn step, right after the turn actor's end-of-turn bonuses are collected
        /// and before the engine starts the pending abilities (see GHTinkerer.AfterFindBonuses).</summary>
        internal static void OnEndTurn(CActor actor)
        {
            if (!(actor is CPlayerActor tinkerer) || tinkerer.CharacterClass?.ID != TinkererClass) return;
            if (!(PhaseManager.CurrentPhase is CPhaseEndTurn phase) || ReferenceEquals(phase, s_LastEndTurn) || phase.m_SentEndTurnMessage) return;
            s_LastEndTurn = phase;
            CScenario scenario = ScenarioManager.Scenario;
            if (scenario?.HeroSummons == null || GameState.PendingScenarioModifierAbilities == null) return;
            if (!ReferenceEquals(s_BombState, ScenarioManager.CurrentScenarioState)) { s_BombState = ScenarioManager.CurrentScenarioState; s_BombArmedRound.Clear(); }
            int round = ScenarioManager.CurrentScenarioState.RoundNumber;
            foreach (CHeroSummonActor bomb in scenario.HeroSummons.Where(s => s != null && !s.IsDead && IsToken(s, BombID) && s.Summoner == tinkerer).ToList())
            {
                if (!s_BombArmedRound.TryGetValue(bomb.ActorGuid, out int armed))
                {
                    // first end of turn seen for this token: placed this round (the card is among this round's cards) = arm it;
                    // else it was placed in an earlier round (a reloaded save): its "next turn" is this one
                    bool placedNow = PlayedThisRound(tinkerer);
                    if (placedNow)
                    {
                        s_BombArmedRound[bomb.ActorGuid] = round;
                        Plugin.Log.LogInfo($"GHRule GravityBomb card {BombCard} placed at {bomb.ArrayIndex} in round {round}: goes off at the end of {GHPlague.NameOf(tinkerer)}'s next turn");
                        continue;
                    }
                }
                else if (armed >= round) continue;    // an extra turn in the same round is not "your next turn"
                Detonate(tinkerer, bomb);
            }
        }

        private static bool PlayedThisRound(CPlayerActor p)
        {
            CCharacterClass c = p.CharacterClass;
            if (c == null) return true;
            bool In(IEnumerable<CAbilityCard> cards) => cards != null && cards.Any(x => x != null && x.ID == BombCard);
            return In(c.RoundAbilityCardsSelectedInCardSelection) || In(c.RoundAbilityCards) || In(c.ExtraTurnCards);
        }

        private static void Detonate(CPlayerActor tinkerer, CHeroSummonActor bomb)
        {
            List<CAbility> abilities = bomb.SummonData?.OnDeathAbilities;
            if (abilities == null || abilities.Count == 0) { Plugin.Log.LogWarning("GHMapTokens: the Gravity Bomb has no abilities to perform"); return; }
            s_Queued.Clear();
            foreach (CAbility a in abilities)
            {
                CAbility copy = CAbility.CopyAbility(a, generateNewID: false);
                copy.ProcessIfDead = false;
                var entry = new Tuple<CAbility, CActor>(copy, bomb);
                s_Queued.Add(entry);
                GameState.PendingScenarioModifierAbilities.Add(entry);
            }
            s_ToRemove = bomb;
            s_BombArmedRound.Remove(bomb.ActorGuid);
            Plugin.Log.LogInfo($"GHRule GravityBomb card {BombCard} goes off at {bomb.ArrayIndex} (end of {GHPlague.NameOf(tinkerer)}'s turn, round {ScenarioManager.CurrentScenarioState.RoundNumber}): Pull 2 toward it on enemies within range 2, damage 5 in its hex, damage 1 adjacent");
        }

        /// <summary>Between two pending abilities: once the bomb's three have all run, the token leaves the map.</summary>
        [HarmonyPatch(typeof(PhaseManager), nameof(PhaseManager.NextActiveBonusOrScenarioModifier))]
        [HarmonyPrefix]
        private static void BeforeNextPending()
        {
            if (s_ToRemove == null) return;
            List<Tuple<CAbility, CActor>> pending = GameState.PendingScenarioModifierAbilities;
            if (pending != null && s_Queued.Any(q => pending.Contains(q))) return;
            CHeroSummonActor bomb = s_ToRemove;
            s_ToRemove = null;
            s_Queued.Clear();
            try
            {
                if (bomb.IsDead || !ScenarioManager.Scenario.HasActor(bomb)) return;
                GameState.KillActor(bomb.Summoner, bomb, CActor.ECauseOfDeath.SummonActiveBonusCancelled, out bool _);
                Plugin.Log.LogInfo($"GHRule GravityBomb card {BombCard} token removed: the card goes to the discard pile");
            }
            catch (Exception e) { Report("GravityBomb", e); }
        }

        /// <summary>The bomb's abilities are carried as OnDeathAbilities; they never run on a death.</summary>
        [HarmonyPatch(typeof(CHeroSummonActor), nameof(CHeroSummonActor.OnDeath))]
        [HarmonyPrefix]
        private static void BeforeSummonDeath(CHeroSummonActor __instance)
        {
            if (IsToken(__instance, BombID)) OnDeathUsed(__instance) = true;
        }

        private static readonly AccessTools.FieldRef<CActor, bool> OnDeathUsed = AccessTools.FieldRefAccess<CActor, bool>("m_OnDeathAbilityUsed");

        // ---- the bomb's damage targets: its own hex, the hexes adjacent ----

        [HarmonyPatch(typeof(SharedAbilityTargeting), nameof(SharedAbilityTargeting.GetValidActorsInRange), new[] { typeof(CAbility) })]
        [HarmonyPostfix]
        private static void AfterValidActors(CAbility ability)
        {
            try
            {
                if (ability?.MiscVars == null || ability.TargetingActor == null) return;
                bool hexOnly = GHPlague.Marked(ability, "GHTokenHexOnly"), adjacentOnly = GHPlague.Marked(ability, "GHTokenAdjacentOnly");
                if (!hexOnly && !adjacentOnly) return;
                CActor me = ability.TargetingActor;
                Point at = me.ArrayIndex;
                if (adjacentOnly)
                {
                    ability.ValidActorsInRange.RemoveAll(a => a == null || a.ArrayIndex == at);
                    Plugin.Log.LogInfo($"GHRule GravityBomb card {BombCard} damage 1 on {ability.ValidActorsInRange.Count} enemy(ies) adjacent to the token");
                    return;
                }
                var inHex = new List<CActor>();
                foreach (CActor a in ScenarioManager.Scenario.FindActorsAt(at) ?? new List<CActor>())
                    if (a != null && a != me && !a.IsDead && ability.AbilityFilter.IsValidTarget(a, me, ability.IsTargetedAbility, useTargetOriginalType: false, ability.MiscAbilityData?.CanTargetInvisible))
                        inHex.Add(a);
                ability.ValidActorsInRange.Clear();
                ability.ValidActorsInRange.AddRange(inHex);
                CTile tile = ScenarioManager.Tiles[at.X, at.Y];
                if (ability.TilesInRange != null && tile != null && !ability.TilesInRange.Contains(tile)) ability.TilesInRange.Add(tile);
                Plugin.Log.LogInfo($"GHRule GravityBomb card {BombCard} damage 5 on {inHex.Count} enemy(ies) in the token's hex");
            }
            catch (Exception e) { Report("GravityBomb targets", e); }
        }

        private static readonly HashSet<string> Reported = new HashSet<string>();

        internal static void Report(string what, Exception e)
        {
            if (Reported.Add(what)) Plugin.Log.LogError($"GHMapTokens: {what} failed: {e}");
        }
    }
}
