using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FHGUI;
using FHRL;
using HarmonyLib;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// The outpost command behind the "Gloomhaven classes (content pack)" house rule: unlocks or locks the pack's classes
    /// in the campaign. UnlockedClassIDs is campaign state, and the host holds three copies of it (its own view's copy,
    /// the host-check copy that joining players are served, and the save), so it only changes inside a host-checked
    /// multiplayer command, like the mod's Give commands (GiveResources.cs): every copy on every PC applies it in command
    /// order, the game's own rollback replays it, and a player who joins later receives the result with the state. The
    /// class list comes from the issuing PC, so every copy applies the same list; a PC whose rules lack one of the IDs
    /// skips it (adding it would make its map fail at the next lookup) and the join gate (PackGate) is what keeps such a
    /// PC out of the game in the first place. The unlock adds the IDs the way the game's own unlock_class console command
    /// does, without the "class unlocked" event: that event would put a line in the outpost event log that a PC without
    /// the pack can't display.
    /// </summary>
    public sealed class PackClassCommands
    {
        public const string CommandPath = "FrosthavenMods.PackClasses";

        private static readonly MethodInfo BeginResult = AccessTools.Method(typeof(CampaignController), "BeginResult");
        private static readonly MethodInfo EndResult = AccessTools.Method(typeof(CampaignController), "EndResult");

        private readonly MultiplayerGameController _game;

        public PackClassCommands(MultiplayerGameController game) { _game = game; }

        /// <summary>Unlock (on) or lock (off) the classes named in <paramref name="idList"/> ("BruteID,..."). Reads campaign
        /// state only, so every copy reaches the same verdict; locking a class the campaign still uses throws, which
        /// fails the host check and rolls everyone back.</summary>
        [MultiplayerCommand(MultiplayerCommandExecType.Everybody)]
        public CampaignController.Result PackClasses(bool on, string idList)
        {
            CampaignController campaign = _game.CampaignController;
            CampaignState state = campaign.State;
            DefRepository repo = campaign.DefRepository;
            string copy = CoopLog.Copy(_game);
            List<CharacterClassID> ids = (idList ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => new CharacterClassID(s.Trim())).ToList();
            List<CharacterClassID> unknown = ids.Where(id => !repo.CharacterClasses.ContainsKey(id)).ToList();
            if (unknown.Count > 0)
            {
                CoopLog.Write($"PACK CLASSES on={copy}: this PC's rules have no {string.Join(",", unknown.Select(i => i.ID))}: skipped (install the pack)");
                ids = ids.Except(unknown).ToList();
            }
            if (!on)
            {
                string why = ids.Select(id => GHClassesRule.ClassInUse(state, repo, id)).FirstOrDefault(w => w != null);
                if (why != null)
                {
                    CoopLog.Write($"PACK CLASSES lock REJECTED on={copy}: {why}");
                    throw new FHGameException("Gloomhaven classes: " + why);
                }
            }

            BeginResult.Invoke(campaign, null);
            int changed = 0;
            foreach (CharacterClassID id in ids)
            {
                if (on)
                {
                    if (state.UnlockedClassIDs.Contains(id)) continue;
                    state.UnlockedClassIDs.Add(id);
                    changed++;
                }
                else changed += state.UnlockedClassIDs.RemoveAll(u => u == id);
            }
            var result = (CampaignController.Result)EndResult.Invoke(campaign, null);
            CoopLog.Write($"PACK CLASSES {(on ? "unlocked" : "locked")} on={copy} changed={changed} " +
                          $"now=[{string.Join(",", state.UnlockedClassIDs.Select(i => i.ID))}] role={CoopLog.Role()}");
            return result;
        }
    }

    /// <summary>
    /// The campaign side of the "Gloomhaven classes (content pack)" switch (GHClasses.cs is the data and art side).
    /// Keeps the campaign's unlocked classes in step with the switch: when the rule changes (on every PC, through
    /// ModRules.Applied) and each time the map is shown (load, join, back from a scenario), the host or solo player
    /// compares the switch with the unlocked list and, if they disagree, issues the PackClasses command once nobody is
    /// joining. Clients never act: they receive the host's list with the state. Also the switch's guard: it can't go
    /// on without the pack on this PC (or with a connected player who lacks it), and can't go off while anything in
    /// the campaign still names a pack class (a save that does only opens where the class exists).
    /// </summary>
    [HarmonyPatch]
    internal static class GHClassesRule
    {
        public const ERulesFlag Flag = ModRules.GloomhavenClasses;
        private const float CheckEvery = 1f;   // seconds between attempts while a check is due
        private const int MaxFailures = 3;

        private static readonly ConditionalWeakTable<MultiplayerGameController, PackClassCommands> Handlers = new ConditionalWeakTable<MultiplayerGameController, PackClassCommands>();
        private static readonly MethodInfo Command = typeof(PackClassCommands).GetMethod(nameof(PackClassCommands.PackClasses));

        private static bool _checkDue, _commandInFlight;
        private static float _nextCheck;
        private static int _failures;
        private static string _lastSkip;

        public static bool On => ModRules.IsOn(Flag);

        // ------------------------------------------------------------------ the pack's classes

        /// <summary>The pack classes as campaign IDs ("BruteID") the given rules know.</summary>
        public static List<CharacterClassID> PackClassIDs(DefRepository repo)
        {
            var list = new List<CharacterClassID>();
            if (repo == null) return list;
            foreach (ScenarioRuleLibrary.CCharacterClass c in GHClasses.PackClasses())
            {
                if (string.IsNullOrEmpty(c.ID)) continue;
                var id = new CharacterClassID(c.ID);
                if (repo.CharacterClasses.ContainsKey(id) && !list.Contains(id)) list.Add(id);
            }
            return list;
        }

        /// <summary>The pack classes that open the moment the switch is on: a pack's "starting" classes, or all of a
        /// pack's classes when it doesn't deal them out through envelopes.</summary>
        public static List<CharacterClassID> StartingClassIDs(DefRepository repo) => Select(repo, starting: true);

        /// <summary>The pack classes that wait in retirement envelopes (GHEnvelopes).</summary>
        public static List<CharacterClassID> EnvelopeClassIDs(DefRepository repo) => Select(repo, starting: false);

        private static List<CharacterClassID> Select(DefRepository repo, bool starting)
        {
            var list = new List<CharacterClassID>();
            if (repo == null) return list;
            foreach (ScenarioRuleLibrary.CCharacterClass c in GHClasses.PackClasses())
            {
                if (string.IsNullOrEmpty(c.ID)) continue;
                string model = c.CharacterModel.ToString();
                GHClasses.Pack pack = GHClasses.PackOf(c.CharacterModel);
                bool opensAtStart = pack == null || !pack.Envelopes || pack.Starting.Any(s => string.Equals(s, model, StringComparison.OrdinalIgnoreCase));
                if (opensAtStart != starting) continue;
                var id = new CharacterClassID(c.ID);
                if (repo.CharacterClasses.ContainsKey(id) && !list.Contains(id)) list.Add(id);
            }
            return list;
        }

        private static string ShortName(CharacterClassID id) =>
            id.ID != null && id.ID.EndsWith("ID", StringComparison.Ordinal) ? id.ID.Substring(0, id.ID.Length - 2) : id.ID;

        /// <summary>The campaign state this PC plays with: the map's copy, else the save's (outside the map).</summary>
        internal static CampaignState LiveState =>
            Singleton<FHMapChoreographer>.InstanceFast?.GameState ?? SaveData.Instance?.Global?.CurrentAdventureData?.FrosthavenSaveData?.GameState;

        internal static DefRepository LiveRepo =>
            Singleton<FHMapChoreographer>.InstanceFast?.DefRepo ?? FH.YML.YMLDefRepository.Repo?.DefRepository;

        // ------------------------------------------------------------------ "class in use" guard

        /// <summary>Why <paramref name="id"/> can't be locked (null: it can): everything in a campaign save that names the
        /// class and makes a game without its definition throw. Characters in any party state (the map resolves their
        /// class, perks and cards as it loads), retired characters (the retired roster resolves theirs), enhanced cards of
        /// the class (every character's rebuild resolves every enhanced card), an unlock line in the outpost event log
        /// (its text resolves the class), a pending class choice or chosen class. Campaign state only, so every copy
        /// agrees.</summary>
        public static string ClassInUse(CampaignState state, DefRepository repo, CharacterClassID id)
        {
            if (state == null) return null;
            string name = ShortName(id);
            CharacterState c = state.AllCharacters?.FirstOrDefault(x => x != null && x.ClassID == id);
            if (c != null) return $"{GiveRules.Name(c)} the {name} is in this campaign ({c.PartyState})";
            CharacterState r = state.RetiredCharacters?.FirstOrDefault(x => x != null && x.ClassID == id);
            if (r != null) return $"{GiveRules.Name(r)} the {name} retired in week {r.WeekRetired}";
            if (repo != null && state.ClassCardModifications != null &&
                state.ClassCardModifications.Any(m => m != null && repo.ClassCards.TryGetValue(m.CardID, out ClassCardDef card) && card != null && card.ClassID == id))
                return $"{name} cards carry enhancements";
            if (state.Outpost?.EventLog != null &&
                state.Outpost.EventLog.GetAllEventsOfType<OutpostEventLogState.EventLogData.ClassUnlocked>().Any(e => e != null && e.ClassID == id))
                return $"the outpost event log records the {name} unlock";
            if (state.UnresolvedActions != null &&
                state.UnresolvedActions.Any(a => a.Type == CampaignInteractionType.SelectClassUnlock && a.GetStringParams().Contains(id.ID)))
                return $"a class choice offering the {name} is pending";
            if (state.UnresolvedActionChosenCharacters != null && state.UnresolvedActionChosenCharacters.Any(d => d != null && d.ID == id))
                return $"the {name} was chosen in a pending action";
            return null;
        }

        /// <summary>The switch's guard (ModRules.Rule.Blocked): why it can't move to <paramref name="wantOn"/> now, or null.</summary>
        internal static string WhyBlocked(bool wantOn)
        {
            if (wantOn)
            {
                if (GHClasses.PackClasses().Count == 0)
                    return GHClasses.Packs.Count == 0
                        ? "Install the Gloomhaven classes content pack on this PC first (see the mod guide)."
                        : "The installed content pack brought no classes into the game's rules: reinstall it (fh_mods.py install --packs) and restart the game.";
                return PackGate.WhyCantTurnOn();   // null unless another connected PC lacks this pack
            }
            DefRepository repo = LiveRepo;
            CampaignState state = LiveState;
            if (repo == null || state == null) return null;
            string why = PackClassIDs(repo).Select(id => ClassInUse(state, repo, id)).FirstOrDefault(w => w != null);
            return why == null ? null : "Locked: " + why + ". A campaign that ever had a character of a pack class needs the pack for good.";
        }

        // ------------------------------------------------------------------ when to act

        /// <summary>From ModRules.Applied: once per rule change on every PC. Only notes that a check is due; the check
        /// decides whether this PC may act (host or solo, map shown, nobody joining).</summary>
        internal static void OnRulesApplied(ERulesFlag before, ERulesFlag after)
        {
            if (((before ^ after) & Flag) == 0) return;
            CoopLog.Write($"PACK CLASSES rule -> {((after & Flag) == Flag ? "on" : "off")} role={CoopLog.Role()}");
            RequestCheck();
        }

        internal static void RequestCheck()
        {
            _checkDue = true;
            _nextCheck = 0f;
            _failures = 0;
        }

        /// <summary>The map scene is built (once per load, join or return from a scenario, on every PC) with the campaign
        /// state in place and, on the host, the host-check copy created. The work itself waits for the poll.</summary>
        [HarmonyPatch(typeof(FHMapView), nameof(FHMapView.InitUI))]
        [HarmonyPostfix]
        private static void MapShown() => RequestCheck();

        [HarmonyPatch(typeof(InputFHManager), "LateUpdate")]
        [HarmonyPostfix]
        private static void Poll()
        {
            if (!_checkDue || _commandInFlight || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckEvery;
            try
            {
                if (Reconcile()) _checkDue = false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Gloomhaven classes: the unlock check failed: " + e);
                if (++_failures >= MaxFailures) _checkDue = false;
            }
        }

        /// <summary>Make the unlocked list match the rule. True when done (or nothing for this PC to do), false to try
        /// again at the next poll (map not shown yet, players joining, command results pending).</summary>
        private static bool Reconcile()
        {
            FHMapChoreographer map = Singleton<FHMapChoreographer>.InstanceFast;
            FHMapView view = map?.View;
            if (view == null || !view.HasInit || view.CurrentState == null) return false;
            if (SaveData.Instance?.Global == null || SaveData.Instance.Global.CurrentGameState != EGameState.Map) return false;
            if (FFSNetwork.IsOnline && !FFSNetwork.IsHost) return true;   // clients get the host's list with the state
            if (!CanIssueNow(map)) return false;

            CampaignState state = view.GameState;
            DefRepository repo = view.GameController.DefRepository;
            List<CharacterClassID> pack = PackClassIDs(repo);
            if (pack.Count == 0) return true;
            bool on = On;
            // on: open the pack's starting classes (its envelope classes arrive through retirements, GHEnvelopes); off: lock them all
            List<CharacterClassID> want = on ? StartingClassIDs(repo) : pack;
            bool allIn = want.All(state.UnlockedClassIDs.Contains);
            bool anyIn = pack.Any(state.UnlockedClassIDs.Contains);
            if (on ? allIn : !anyIn) return true;
            if (!on)
            {
                string why = pack.Select(id => ClassInUse(state, repo, id)).FirstOrDefault(w => w != null);
                if (why != null)
                {
                    if (_lastSkip != why) { _lastSkip = why; CoopLog.Write("PACK CLASSES stay unlocked: " + why); }
                    return true;
                }
            }
            if (_failures >= MaxFailures) { CoopLog.Write("PACK CLASSES: giving up after repeated command failures"); return true; }
            Issue(view, on, want);
            return true;
        }

        /// <summary>The game's own conditions for an outpost change (minus the map-only one, checked by the caller):
        /// not starting or shutting the network down, no command results waiting, no scenario entered; online: the
        /// host, with its host-check copy up and nobody joining or connecting.</summary>
        private static bool CanIssueNow(FHMapChoreographer map)
        {
            if (FFSNetwork.IsShuttingDown || FFSNetwork.IsStartingUp) return false;
            if (map.GameClient == null || map.GameClient.HasUnprocessedResults) return false;
            if (map.GameState.ActiveScenario != null && map.GameState.ActiveScenario.HasEnteredScenario) return false;
            if (!FFSNetwork.IsOnline) return true;
            return FFSNetwork.IsHost && map.GameServer != null && FFSNet.PlayerRegistry.JoiningPlayers.Count == 0 && FFSNet.PlayerRegistry.ConnectingUsers.Count == 0;
        }

        private static void Issue(FHMapView view, bool on, List<CharacterClassID> pack)
        {
            _commandInFlight = true;
            string ids = string.Join(",", pack.Select(i => i.ID));
            CoopLog.Write($"PACK CLASSES issuing {(on ? "unlock" : "lock")} of {ids} role={CoopLog.Role()}");
            try
            {
                FHMultiplayerClient.Promise promise = view.Choreographer.ExecuteCommand(PackClassCommands.CommandPath, on, ids);
                promise.OnComplete(() =>
                {
                    _commandInFlight = false;
                    if (view != null) view.RefreshView();
                });
                promise.OnFailed(() =>
                {
                    _commandInFlight = false;
                    _failures++;
                    _checkDue = true;   // the list is as it was: check again
                    CoopLog.Write("PACK CLASSES command rejected by the host check");
                });
                if (!FFSNetwork.IsOnline) _commandInFlight = false;   // solo: ran synchronously, no confirmation follows
                view.RefreshView();
            }
            catch (Exception e)
            {
                // solo (or the host's own copy) threw: the lock guard, or something unexpected
                _commandInFlight = false;
                _failures++;
                Plugin.Log.LogError("Gloomhaven classes: the unlock command failed: " + e);
                GiveResourcesPatches.Message(view, "Gloomhaven classes: " + (e.InnerException ?? e).Message);
            }
        }

        // ------------------------------------------------------------------ command resolution (see GiveResourcesPatches.ResolveCommand)

        /// <summary>Resolve our command name on every controller copy (the host runs two: its own and the host check).</summary>
        [HarmonyPatch(typeof(MultiplayerGameController), "GetExectionContext")]
        [HarmonyPrefix]
        private static bool ResolveCommand(MultiplayerGameController __instance, string path, ref ValueTuple<object, MethodInfo> __result)
        {
            if (path != PackClassCommands.CommandPath) return true;
            __result = (Handlers.GetValue(__instance, g => new PackClassCommands(g)), Command);
            return false;
        }
    }

    /// <summary>
    /// One call of ModRules.Applied per rule change on every PC. Every change of the campaign's rules number goes
    /// through PartyAdventureData.ChangeCampaignRules: the host's settings screen (and our switches) call it with a
    /// network action, and each client's copy of that action lands in HouseRulesSettings.ProxySetHouseRuleSettings ->
    /// GameSettingsService.ClientSetRules -> ChangeCampaignRules(rules, networkAction: false). The host doesn't receive
    /// its own side action, so neither side runs twice; the game's forced call for a brand-new campaign passes the
    /// unchanged number and is skipped by the comparison.
    /// </summary>
    [HarmonyPatch]
    internal static class RuleChangePatches
    {
        [HarmonyPatch(typeof(PartyAdventureData), nameof(PartyAdventureData.ChangeCampaignRules))]
        [HarmonyPrefix]
        private static void Before(PartyAdventureData __instance, out ERulesFlag __state) => __state = __instance.Rules;

        [HarmonyPatch(typeof(PartyAdventureData), nameof(PartyAdventureData.ChangeCampaignRules))]
        [HarmonyPostfix]
        private static void After(PartyAdventureData __instance, ERulesFlag __state)
        {
            if (__state == __instance.Rules) return;
            try { ModRules.Applied(__state, __instance.Rules); }
            catch (Exception e) { Plugin.Log.LogError("House rules: a rule-change hook failed: " + e); }
        }
    }
}
