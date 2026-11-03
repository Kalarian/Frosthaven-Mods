using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// A snapshot of the scenario taken when it ends, for the 2nd edition career masteries that are written from the
    /// scenario result (ScenarioController.CompleteActiveScenario prefixes: Silent Knife money tokens, Soultether round-1
    /// summons, Elementalist all six elements, Bruiser push kill).
    ///
    /// Engine timing (decompiled): the scenario's own mastery checks (CUnlockCondition.CheckUnlockConditionTarget) run
    /// during play (StatsDataStorage.ScrapeEventLogAndCheckBattleGoals) and on the results screen
    /// (UINewAdventureResultsManager.ShowNewAdventureModeResults -> CMapScenarioState.CheckBattleGoals), and
    /// PartyAdventureData.EndCurrentScenario builds the FHScenarioResult from ScenarioManager.Scenario.AllPlayers; the
    /// scenario is alive for all of these. CompleteActiveScenario runs later, from FHMapChoreographer.Update
    /// (ProcessScenarioResult) once the outpost map has loaded. ScenarioManager.Scenario is only cleared by
    /// ScenarioManager.Unload (main menu), so in a straight run it is still there, but: after a quit or a trip to the main
    /// menu between the scenario's end and the outpost (the result is kept in the save and processed on the next load)
    /// it is null; and readers that looked only at PlayerActors missed an exhausted character (exhausted players move to
    /// ExhaustedPlayers). So the values are copied here, in an EndCurrentScenario prefix, from every player (alive or
    /// exhausted), and the career writers read the copy; when there is none (or it belongs to an older scenario) they
    /// fall back to the live scenario, and say which in their GHRule line.
    /// Inert without the packs: it only copies character resources; only the pack plugins read it.
    /// </summary>
    [HarmonyPatch]
    internal static class GHScenarioEnd
    {
        private sealed class Snap
        {
            internal ScenarioState State;
            internal string ScenarioId;
            internal readonly Dictionary<string, Dictionary<string, int>> Resources = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            internal HashSet<string> SoulFirstRound;
        }

        private static Snap s_Last;

        internal const string FromSnapshot = "the scenario-end snapshot";
        internal const string FromLive = "the live scenario";
        internal const string FromNothing = "nothing (scenario gone, no snapshot)";

        [HarmonyPatch(typeof(PartyAdventureData), nameof(PartyAdventureData.EndCurrentScenario))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Take()
        {
            try
            {
                CScenario sc = ScenarioManager.Scenario;
                if (sc == null) { s_Last = null; return; }
                var snap = new Snap { State = ScenarioManager.CurrentScenarioState, ScenarioId = ScenarioManager.CurrentScenarioState?.ID };
                foreach (CPlayerActor p in AllPlayers(sc))
                {
                    string cls = p.CharacterClass?.ID;
                    if (cls == null || p.CharacterResources == null) continue;
                    var res = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (CCharacterResource r in p.CharacterResources)
                        if (r?.ID != null) res[r.ID] = r.Amount;
                    snap.Resources[cls] = res;
                }
                if (snap.Resources.ContainsKey(GHSoultether.Soul)) snap.SoulFirstRound = GHSoultether.FirstRoundForSnapshot();
                s_Last = snap;
            }
            catch (Exception e) { s_Last = null; if (GHClasses.Reported.Add("scenario end snapshot")) Plugin.Log.LogError("GHScenarioEnd (snapshot): " + e); }
        }

        /// <summary>The result has been written: the snapshot is spent (a later result without a fresh one reads live).</summary>
        [HarmonyPatch(typeof(FHRL.ScenarioController), nameof(FHRL.ScenarioController.CompleteActiveScenario))]
        [HarmonyPostfix]
        private static void Spent() => s_Last = null;

        private static IEnumerable<CPlayerActor> AllPlayers(CScenario sc) =>
            (sc.PlayerActors ?? new List<CPlayerActor>()).Concat(sc.ExhaustedPlayers ?? new List<CPlayerActor>()).Where(p => p != null);

        /// <summary>The snapshot, unless a different scenario is loaded now (then it is stale).</summary>
        private static Snap Current()
        {
            Snap s = s_Last;
            if (s == null) return null;
            ScenarioState live = ScenarioManager.CurrentScenarioState;
            return live == null || ReferenceEquals(live, s.State) ? s : null;
        }

        internal static string ScenarioId(out string from)
        {
            Snap s = Current();
            if (s?.ScenarioId != null) { from = FromSnapshot; return s.ScenarioId; }
            string live = ScenarioManager.CurrentScenarioState?.ID;
            from = live != null ? FromLive : FromNothing;
            return live ?? "?";
        }

        /// <summary>A character resource of the player of this class when the scenario ended (alive or exhausted).</summary>
        internal static int Resource(string classId, string resourceId, out string from)
        {
            Snap s = Current();
            if (s != null)
            {
                from = FromSnapshot;
                return s.Resources.TryGetValue(classId, out var res) && res.TryGetValue(resourceId, out int n) ? n : 0;
            }
            CScenario sc = ScenarioManager.Scenario;
            CPlayerActor p = sc == null ? null : AllPlayers(sc).FirstOrDefault(x => x.CharacterClass?.ID == classId);
            if (p == null) { from = FromNothing; return 0; }
            from = FromLive;
            try { return p.GetCharacterResourceAmount(resourceId); } catch { return 0; }
        }

        /// <summary>The Soultether's round-1 summon abilities copied at the scenario's end, or null without a snapshot.</summary>
        internal static HashSet<string> SoulFirstRound()
        {
            HashSet<string> set = Current()?.SoulFirstRound;
            return set == null ? null : new HashSet<string>(set, StringComparer.Ordinal);
        }
    }
}
