using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FHGUI;
using FHRL;
using GLOOM.MainMenu;
using HarmonyLib;
using MapRuleLibrary.Party;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// The way out of a campaign that used the Gloomhaven pack: a copy of the current campaign with every trace of the
    /// pack removed, saved as a NEW campaign "&lt;name&gt; (no Gloomhaven)" that a game without the pack (or without the
    /// mods at all) loads. A "Copy without Gloomhaven" button in the Save Game window does it (host or solo, in the
    /// outpost). The live campaign is never changed.
    ///
    /// What a pack-less game chokes on is every lookup of a pack definition: a character's class (CharacterState.ClassDef
    /// indexes the definition repository), its cards, perks and masteries, enhancements on pack cards, the "class
    /// unlocked" line of the outpost log, a pending class choice, the unlocked-class list, and the legacy party mirror
    /// (CMapCharacter, keyed by class ID) the save also carries. The copy drops them all: pack characters are deleted the
    /// way the game deletes one (items back to the shop, gold and resources gone, their personal quest back in the deck),
    /// retired pack characters leave the retired roster, everything else is filtered. The copy is then checked against a
    /// repository with the pack removed (every definition the save can reach is resolved), written as a new campaign
    /// through the game's own save writer, read back from disk and checked again. The copy's rules keep only the game's
    /// own switches.
    /// </summary>
    [HarmonyPatch]
    internal static class GHSaveStrip
    {
        public const string Suffix = " (no Gloomhaven)";
        public const string FileName = "No Gloomhaven";
        private const string ButtonName = "FrosthavenMods_StripSave";
        private const string Marker = "FHMODS:";   // the pack's own markers in LootedChestTreasureTableIDs (GHLooks)

        // the game's own rule bits: everything else is a mod switch and goes
        private static readonly ERulesFlag VanillaRules = Enum.GetValues(typeof(ERulesFlag)).Cast<ERulesFlag>().Aggregate(ERulesFlag.None, (a, b) => a | b);

        private static readonly FieldInfo NewSaveField = AccessTools.Field(typeof(FHUISaveGame), "newSave");
        private static readonly MethodInfo SaveToFile = AccessTools.Method(typeof(PartyAdventureData), "SaveGameStateToFile");
        private static readonly MethodInfo LoadFromFile = AccessTools.Method(typeof(PartyAdventureData), "LoadGameStateFromFile");
        private static readonly MethodInfo BeginResult = AccessTools.Method(typeof(CampaignController), "BeginResult");
        private static readonly MethodInfo EndResult = AccessTools.Method(typeof(CampaignController), "EndResult");
        private static readonly MethodInfo DeleteCharacter = AccessTools.Method(typeof(CharacterController), "Action_DeleteCharacter");
        private static readonly MethodInfo UnlockClass = AccessTools.Method(typeof(CharacterController), "Action_UnlockClass");
        private static readonly FieldInfo EventList = AccessTools.Field(typeof(OutpostEventLogState), "_eventList");
        private static readonly FieldInfo SavingLock = AccessTools.Field(typeof(SaveData), "_savingLock");
        private static readonly FieldInfo RepoClasses = AccessTools.Field(typeof(DefRepository), nameof(DefRepository.CharacterClasses));
        private static readonly FieldInfo RepoCards = AccessTools.Field(typeof(DefRepository), nameof(DefRepository.ClassCards));
        private static readonly FieldInfo RepoPerks = AccessTools.Field(typeof(DefRepository), nameof(DefRepository.Perks));
        private static readonly FieldInfo RepoMasteries = AccessTools.Field(typeof(DefRepository), nameof(DefRepository.MasteryDefs));
        private static readonly MethodInfo ShallowClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        internal sealed class Report
        {
            public readonly List<string> Removed = new List<string>();
            public string Campaign;
            public string Path;
        }

        // ------------------------------------------------------------------ the button in the Save Game window

        [HarmonyPatch(typeof(FHUISaveGame), nameof(FHUISaveGame.Show), typeof(IGameModeService))]
        [HarmonyPostfix]
        private static void AddButton(FHUISaveGame __instance)
        {
            GiveResourcesPatches.Safe("add the Copy without Gloomhaven button", () =>
            {
                var template = NewSaveField.GetValue(__instance) as FHButton;
                if (template == null) return;
                Transform parent = template.transform.parent;
                bool show = Wanted();
                Transform existing = parent.Find(ButtonName);
                if (existing == null)
                {
                    if (!show) return;
                    UiDump.Once("Save window buttons", parent);
                    FHButton button = ButtonCopy.Make(template, parent, ButtonName, Text.Get("FHMODS_STRIP"), OnClick);
                    if (button == null) return;
                    Place((RectTransform)button.transform, (RectTransform)template.transform);
                    existing = button.transform;
                }
                existing.gameObject.SetActive(show);
            });
        }

        /// <summary>Left of "New save": in a layout group by sibling order, else by position. Wide enough for the label.</summary>
        private static void Place(RectTransform rt, RectTransform template)
        {
            rt.SetSiblingIndex(template.GetSiblingIndex());
            float width = Mathf.Max(template.rect.width * 2.4f, 300f);
            if (template.parent.GetComponent<LayoutGroup>() != null)
            {
                LayoutElement le = rt.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = width;
                le.minWidth = width;
            }
            else
            {
                rt.sizeDelta = new Vector2(width, template.sizeDelta.y);
                rt.anchoredPosition = template.anchoredPosition - new Vector2((template.rect.width + width) / 2f + 16f, 0f);
            }
            foreach (TMP_Text t in rt.GetComponentsInChildren<TMP_Text>(true))
            {
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
            }
        }

        /// <summary>Show the button to the host or solo player in the outpost when the campaign has (or can get) pack content.</summary>
        private static bool Wanted()
        {
            FHMapChoreographer map = Singleton<FHMapChoreographer>.InstanceFast;
            CampaignState state = map?.GameState;
            DefRepository repo = map?.GameController?.DefRepository;
            if (state == null || repo == null) return false;
            if (SaveData.Instance?.Global == null || SaveData.Instance.Global.CurrentGameState != EGameState.Map) return false;
            if (FFSNetwork.IsOnline && !FFSNetwork.IsHost) return false;
            return GHClassesRule.On || Traces(state, repo).Any();
        }

        private static void OnClick()
        {
            string why = WhyNot(out FHMapChoreographer map);
            if (why != null) { Info(why); return; }
            PartyAdventureData live = SaveData.Instance.Global.CurrentAdventureData;
            List<string> traces = Traces(map.GameState, map.GameController.DefRepository).ToList();
            string what = traces.Count == 0
                ? "This campaign has no Gloomhaven characters or unlocks yet, so the copy only loses the pack's switch."
                : "The copy will not have:\n- " + string.Join("\n- ", traces) + "\n\nTheir items go back to the shop; their gold and resources are lost.";
            string text = $"A copy of this campaign is saved as a new campaign, \"{live.PartyName}{Suffix}\", that loads in a game without the " +
                          $"Gloomhaven pack (or without the mods). This campaign itself is not changed.\n\n{what}\n\n" +
                          "The copy keeps only the game's own house rules.";
            Singleton<UIConfirmationBoxManager>.Instance.ShowGenericConfirmation(Text.Get("FHMODS_STRIP_TITLE"), text, () => Run(map));
        }

        private static void Info(string text) =>
            Singleton<UIConfirmationBoxManager>.Instance.ShowGenericInfo(Text.Get("FHMODS_STRIP_TITLE"), text, null, "GUI_CONTINUE");

        /// <summary>Why the copy can't be made right now, or null.</summary>
        private static string WhyNot(out FHMapChoreographer map)
        {
            map = Singleton<FHMapChoreographer>.InstanceFast;
            if (SaveData.Instance?.Global?.CurrentAdventureData?.FrosthavenSaveData == null) return "No campaign is open.";
            if (SaveData.Instance.Global.CurrentGameState != EGameState.Map || map?.GameState == null || map.GameController == null)
                return "This works in the outpost, between scenarios.";
            if (FFSNetwork.IsOnline && !FFSNetwork.IsHost) return "Only the host can make the copy.";
            if (SaveData.Instance.IsSavingData || SaveData.Instance.IsSavingThreadActive) return "The game is saving; try again in a moment.";
            if (map.GameClient == null || map.GameClient.HasUnprocessedResults) return "The outpost is still catching up; try again in a moment.";
            CampaignState state = map.GameState;
            if (state.ActiveScenario != null && state.ActiveScenario.HasEnteredScenario) return "Finish the current scenario first.";
            if (state.PendingScenarioCompletionEvent != null) return "Let the scenario's results finish first.";
            return null;
        }

        private static async void Run(FHMapChoreographer map)
        {
            var gate = SavingLock.GetValue(SaveData.Instance) as SemaphoreSlim;
            bool held = false;
            try
            {
                Singleton<FHUISaveGame>.Instance?.Hide();
                if (gate != null)
                {
                    held = await gate.WaitAsync(TimeSpan.FromSeconds(10));
                    if (!held) throw new Exception("another save is still running");
                }
                Report report = await Copy(map);
                CoopLog.Write($"STRIP COPY written: {report.Path} removed=[{string.Join("; ", report.Removed)}]");
                string removed = report.Removed.Count == 0 ? "Nothing had to be removed." : "Removed from the copy:\n- " + string.Join("\n- ", report.Removed);
                Info($"Saved as the campaign \"{report.Campaign}\" (save \"{FileName}\"). It is in Load game, and opens in a game " +
                     $"without the Gloomhaven pack.\n\n{removed}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Copy without Gloomhaven: failed: " + e);
                Info("The copy was not made: " + (e.InnerException ?? e).Message + "\n\nThis campaign was not changed.");
            }
            finally
            {
                if (held) gate.Release();
            }
        }

        // ------------------------------------------------------------------ the copy

        /// <summary>Make, check, write and re-check the pack-free copy. Throws with a plain-English reason on any failure;
        /// a written file that fails its read-back check is deleted again.</summary>
        internal static async Task<Report> Copy(FHMapChoreographer map)
        {
            PartyAdventureData live = SaveData.Instance.Global.CurrentAdventureData;
            CampaignController controller = map.GameController;
            DefRepository repo = controller.DefRepository;

            // the game's own pre-save sync: the legacy party mirror, the party slots and the save's state reference
            try { map.GameClient.SyncCharacters(); }
            catch (Exception e) { Plugin.Log.LogWarning("Copy without Gloomhaven: the pre-save sync failed, continuing: " + e.Message); }

            FHSaveData data = await live.FrosthavenSaveData.DeepCopySerializableObject<FHSaveData>();
            var serializer = new StateSerializer<CampaignState>(repo) { StateResolveAction = s => s.ResolveDefs(repo) };
            data.GameState = serializer.Clone(controller.State);
            data.ResolveDefs(repo);

            List<CharacterClassID> pack = GHClassesRule.PackClassIDs(repo);
            var report = new Report();
            Strip(data, repo, pack, report);

            DefRepository packless = Packless(repo, pack);
            string leftover = Verify(data, packless);
            if (leftover != null) throw new Exception("the copy would still refer to the pack (" + leftover + ")");
            data.ResolveDefs(repo);

            string name = live.PartyName + Suffix;
            if (name.Length >= PartyAdventureData.Header.CampaignNameSizeLimit)
                name = live.PartyName.Substring(0, PartyAdventureData.Header.CampaignNameSizeLimit - Suffix.Length - 1) + Suffix;
            var copy = new PartyAdventureData(data, name, live.Owner ?? new SaveOwner(), live.GameMode, live.Rules & VanillaRules, live.IsIronMan)
            {
                SaveFileName = FileName,
                TimeActiveSecRunLTD = live.TimeActiveSecRunLTD,
            };
            string path = await copy.PartyMainSaveFile;
            bool ok = await (Task<bool>)SaveToFile.Invoke(copy, new object[] { path });
            if (!ok || !File.Exists(path)) throw new Exception("the game's save writer did not write the file");

            // read it back the way a game would, and check again against the pack-less definitions
            var loaded = await (Task<(FHSaveData data, bool gameClosedWhileSaving)>)LoadFromFile.Invoke(null, new object[] { path, false, false });
            FHSaveData back = loaded.data;
            string problem = back == null ? "the written file could not be read back" : Verify(back, packless);
            if (problem != null)
            {
                try { File.Delete(path); } catch (Exception e) { Plugin.Log.LogWarning("Copy without Gloomhaven: could not delete the bad copy: " + e.Message); }
                throw new Exception(problem);
            }

            // the Load screen's picture, if the live campaign has one
            try
            {
                string shot = await live.ScreenShotFile;
                if (File.Exists(shot)) File.Copy(shot, await copy.ScreenShotFile, true);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Copy without Gloomhaven: no screenshot for the copy: " + e.Message); }

            report.Campaign = name;
            report.Path = path;
            return report;
        }

        private static string Short(CharacterClassID id) =>
            id.ID != null && id.ID.EndsWith("ID", StringComparison.Ordinal) ? id.ID.Substring(0, id.ID.Length - 2) : id.ID ?? "?";

        /// <summary>A class the pack-less game lacks: one of the pack's, or one these rules don't know either.</summary>
        private static bool Foreign(CharacterClassID id, List<CharacterClassID> pack, DefRepository repo) =>
            id.ID != null && (pack.Contains(id) || !repo.CharacterClasses.ContainsKey(id));

        private static string Short(string id) => Short(new CharacterClassID(id));

        /// <summary>What the copy will lose, for the confirmation and the button's visibility.</summary>
        private static IEnumerable<string> Traces(CampaignState state, DefRepository repo)
        {
            List<CharacterClassID> pack = GHClassesRule.PackClassIDs(repo);
            bool IsPack(CharacterClassID id) => Foreign(id, pack, repo);
            foreach (CharacterState c in state.AllCharacters.Where(c => c != null && IsPack(c.ClassID)))
                yield return $"{GiveRules.Name(c)} the {Short(c.ClassID)} (level {c.Level})";
            foreach (CharacterState c in state.RetiredCharacters.Where(c => c != null && IsPack(c.ClassID)))
                yield return $"{GiveRules.Name(c)} the {Short(c.ClassID)} (retired)";
            List<string> unlocked = state.UnlockedClassIDs.Where(IsPack).Select(Short).ToList();
            if (unlocked.Count > 0) yield return "the unlocked class" + (unlocked.Count > 1 ? "es " : " ") + string.Join(", ", unlocked);
            int enhanced = state.ClassCardModifications.Count(m => m != null && m.EnhancedSlots.Count > 0 &&
                                                                 repo.ClassCards.TryGetValue(m.CardID, out ClassCardDef card) && card != null && IsPack(card.ClassID));
            if (enhanced > 0) yield return $"enhancements on {enhanced} of their cards";
            if (state.Outpost?.EventLog != null &&
                state.Outpost.EventLog.GetAllEventsOfType<OutpostEventLogState.EventLogData.ClassUnlocked>().Any(e => e != null && IsPack(e.ClassID)))
                yield return "the class unlocks in the outpost log";
            if (state.UnresolvedActions.Any(a => a.Type == CampaignInteractionType.SelectClassUnlock &&
                                                a.GetStringParams().Any(p => !string.IsNullOrEmpty(p) && IsPack(new CharacterClassID(p)))))
                yield return "a pending class choice";
        }

        /// <summary>Remove every pack reference from the copy. Characters go through the game's own delete action inside
        /// a result (its notifications need one), the rest is list surgery.</summary>
        private static void Strip(FHSaveData data, DefRepository repo, List<CharacterClassID> pack, Report report)
        {
            CampaignState state = data.GameState;
            bool IsPack(CharacterClassID id) => Foreign(id, pack, repo);
            bool IsPackName(string id) => !string.IsNullOrEmpty(id) && IsPack(new CharacterClassID(id));

            var removed = new List<CharacterID>();
            var sim = new CampaignController(state, repo);
            BeginResult.Invoke(sim, null);
            try
            {
                foreach (CharacterState c in state.AllCharacters.Where(c => c != null && IsPack(c.ClassID)).ToList())
                {
                    report.Removed.Add($"{GiveRules.Name(c)} the {Short(c.ClassID)} (level {c.Level}, {Describe(c.PartyState)})");
                    removed.Add(c.ID);
                    DeleteCharacter.Invoke(sim.CharacterController, new object[] { c });
                }
                foreach (CharacterState c in state.RetiredCharacters.Where(c => c != null && IsPack(c.ClassID)).ToList())
                {
                    report.Removed.Add($"{GiveRules.Name(c)} the {Short(c.ClassID)} (retired in week {c.WeekRetired})");
                    removed.Add(c.ID);
                    state.RetiredCharacters.Remove(c);
                }
                // a pending "choose a class to unlock" offering pack classes: offer the rest, or unlock the one left
                foreach (CampaignUnresolvedAction a in state.UnresolvedActions.Where(a => a.Type == CampaignInteractionType.SelectClassUnlock).ToList())
                {
                    List<string> all = a.GetStringParams().Where(p => !string.IsNullOrEmpty(p)).ToList();
                    List<string> keep = all.Where(p => !IsPackName(p)).ToList();
                    if (keep.Count == all.Count) continue;
                    state.UnresolvedActions.Remove(a);
                    report.Removed.Add("a pending class choice offering " + string.Join(", ", all.Where(IsPackName).Select(Short)));
                    if (keep.Count >= 2) state.AddUniqueUnresolvedAction(CampaignInteractionType.SelectClassUnlock, a.Optional, keep.Cast<object>());
                    else if (keep.Count == 1) UnlockClass.Invoke(sim.CharacterController, new object[] { repo.CharacterClasses[new CharacterClassID(keep[0])] });
                }
            }
            finally
            {
                EndResult.Invoke(sim, null);
            }

            List<string> unlocked = state.UnlockedClassIDs.Where(IsPack).Select(Short).ToList();
            if (unlocked.Count > 0)
            {
                report.Removed.Add("the unlocked class" + (unlocked.Count > 1 ? "es " : " ") + string.Join(", ", unlocked));
                state.UnlockedClassIDs.RemoveAll(IsPack);
            }
            int enhanced = state.ClassCardModifications.Count(m => m != null && m.EnhancedSlots.Count > 0 &&
                                                                 (!repo.ClassCards.TryGetValue(m.CardID, out ClassCardDef card) || card == null || IsPack(card.ClassID)));
            if (enhanced > 0) report.Removed.Add($"enhancements on {enhanced} of their cards");
            state.ClassCardModifications.RemoveAll(m => m == null || !repo.ClassCards.TryGetValue(m.CardID, out ClassCardDef card) || card == null || IsPack(card.ClassID));
            state.UnresolvedActionChosenCharacters.RemoveAll(d => d == null || IsPack(d.ID));
            state.LootedChestTreasureTableIDs.RemoveAll(s => s != null && s.StartsWith(Marker, StringComparison.Ordinal));
            if (state.Outpost?.EventLog != null && EventList.GetValue(state.Outpost.EventLog) is List<OutpostEventLogState.EventLogData> log)
                log.RemoveAll(e => e == null || (e is OutpostEventLogState.EventLogData.ClassUnlocked cu && IsPack(cu.ClassID)) || Mentions(e, removed));
            state.ActiveScenario?.CharacterScenarioSettings.RemoveAll(cs => cs?.CharacterID != null && removed.Any(r => r == cs.CharacterID));

            // the save's own extras
            if (data.SlotAssignments != null)
                for (int i = 0; i < data.SlotAssignments.Count; i++)
                    if (data.SlotAssignments[i] != null && removed.Any(r => r == data.SlotAssignments[i])) data.SlotAssignments[i] = null;
            data.ScenarioResult?.Characters?.RemoveAll(ch => ch == null || IsPackName(ch.ClassID));
            data.FastReloadState = null;

            // the legacy party mirror (class-ID keyed)
            CMapParty party = data.MapState?.MapParty;
            if (party != null)
            {
                foreach (CMapCharacter mc in party.CheckCharacters.Where(mc => mc == null || IsPackName(mc.CharacterID)).ToList())
                {
                    if (mc != null) party.RemoveSelectedCharacter(mc);
                    party.RemoveCharacterFromCharactersList(mc);
                }
                CMapCharacter[] slots = party.SelectedCharactersArray;
                if (slots != null)
                    for (int i = 0; i < slots.Length; i++)
                        if (slots[i] != null && IsPackName(slots[i].CharacterID)) slots[i] = null;
                party.RetiredCharacterRecords?.RemoveAll(r => r == null || IsPackName(r.CharacterID));
                party.CharactersRetiredThisSession?.RemoveAll(mc => mc == null || IsPackName(mc.CharacterID));
                party.UnlockedCharacterIDs?.RemoveAll(IsPackName);
                party.NewUnlockedCharacterIDs?.RemoveAll(IsPackName);
                if (party.ClassPersistentEnhancements != null)
                    foreach (string key in party.ClassPersistentEnhancements.Keys.Where(IsPackName).ToList())
                        party.ClassPersistentEnhancements.Remove(key);
            }
        }

        private static string Describe(CharacterPartyStateType s)
        {
            switch (s)
            {
                case CharacterPartyStateType.InParty: return "in the party";
                case CharacterPartyStateType.Removed: return "on the bench";
                case CharacterPartyStateType.CreationProcess: return "being created";
                case CharacterPartyStateType.RetireProcess: return "retiring";
                default: return s.ToString();
            }
        }

        private static bool Mentions(OutpostEventLogState.EventLogData e, List<CharacterID> ids)
        {
            CharacterID who;
            switch (e)
            {
                case OutpostEventLogState.EventLogData.CharacterRetired r: who = r.CharacterID; break;
                case OutpostEventLogState.EventLogData.CharacterPersonalQuestCompleted q: who = q.CharacterID; break;
                case OutpostEventLogState.EventLogData.CharacterPersonalQuestCancelCompleted q: who = q.CharacterID; break;
                case OutpostEventLogState.EventLogData.CharacterLeveledUp l: who = l.CharacterID; break;
                case OutpostEventLogState.EventLogData.CharacterDonated d: who = d.CharacterID; break;
                default: return false;
            }
            return who != null && ids.Any(i => i == who);
        }

        /// <summary>The live definitions minus the pack: a shallow clone with the four dictionaries the pack extends replaced
        /// by filtered copies (classes, their cards, perks and masteries).</summary>
        private static DefRepository Packless(DefRepository repo, List<CharacterClassID> pack)
        {
            var clone = (DefRepository)ShallowClone.Invoke(repo, null);
            var classes = new HashSet<CharacterClassID>(pack);
            List<CharacterClassDef> defs = pack.Where(repo.CharacterClasses.ContainsKey).Select(id => repo.CharacterClasses[id]).Where(d => d != null).ToList();
            var perks = new HashSet<PerkID>(defs.SelectMany(d => d.Perks ?? new List<PerkID>()));
            var masteries = new HashSet<MasteryID>(defs.SelectMany(d => d.MasteryIDs ?? new MasteryID[0]));
            RepoClasses.SetValue(clone, repo.CharacterClasses.Where(kv => !classes.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            RepoCards.SetValue(clone, repo.ClassCards.Where(kv => kv.Value == null || !classes.Contains(kv.Value.ClassID)).ToDictionary(kv => kv.Key, kv => kv.Value));
            RepoPerks.SetValue(clone, repo.Perks.Where(kv => !perks.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            RepoMasteries.SetValue(clone, repo.MasteryDefs.Where(kv => !masteries.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            return clone;
        }

        /// <summary>Resolve everything the save can reach against the pack-less definitions, as a pack-less game would:
        /// null when it all resolves, else where the first leftover is.</summary>
        private static string Verify(FHSaveData data, DefRepository vanilla)
        {
            CampaignState state = data.GameState;
            if (state == null) return "no campaign state";
            string where = "campaign";
            try
            {
                state.ResolveDefs(vanilla);
                foreach (CharacterState c in state.AllCharacters.Concat(state.RetiredCharacters))
                {
                    if (c == null) continue;
                    string who = GiveRules.Name(c);
                    where = $"{who}'s class {c.ClassID.ID}"; _ = c.ClassDef;
                    where = $"{who}'s perks"; c.Perks.ToList();
                    where = $"{who}'s attack modifiers"; c.AttackModifiers.ToList();
                    where = $"{who}'s masteries"; c.CompletedMasteries.ToList();
                    where = $"{who}'s cards"; c.OwnedCards.ToList(); c.AllCards.ToList();
                    where = $"{who}'s items"; c.OwnedItems.ToList();
                    where = $"{who}'s quest"; _ = c.PersonalQuestDef;
                }
                where = "unlocked classes"; state.UnlockedClasses.ToList();
                where = "enhanced cards"; foreach (ClassCardState m in state.ClassCardModifications) _ = m.CardDef;
                where = "chosen classes"; foreach (CharacterClassDef d in state.UnresolvedActionChosenCharacters) _ = vanilla.CharacterClasses[d.ID];
                where = "pending class choice";
                foreach (CampaignUnresolvedAction a in state.UnresolvedActions.Where(a => a.Type == CampaignInteractionType.SelectClassUnlock))
                    foreach (string p in a.GetStringParams().Where(p => !string.IsNullOrEmpty(p)))
                        _ = vanilla.CharacterClasses[new CharacterClassID(p)];
                where = "outpost log";
                if (state.Outpost?.EventLog != null)
                    foreach (OutpostEventLogState.EventLogData.ClassUnlocked e in state.Outpost.EventLog.GetAllEventsOfType<OutpostEventLogState.EventLogData.ClassUnlocked>())
                        _ = vanilla.CharacterClasses[e.ClassID];
                where = "quest deck"; state.PersonalQuestDeck.ToList();
                where = "shop"; state.Outpost.PurchasableItems.ToList(); state.Outpost.CraftableItems.ToList();
                where = "look markers";
                if (state.LootedChestTreasureTableIDs.Any(s => s != null && s.StartsWith(Marker, StringComparison.Ordinal))) return where;
                where = "party slots";
                if (data.SlotAssignments != null)
                    foreach (CharacterID id in data.SlotAssignments)
                        if (id != null && id.IsValid && !state.AllCharacters.Any(c => c != null && c.ID == id)) return where + ": a slot names a character that is gone";
                where = "scenario result";
                foreach (FHScenarioResult.Character ch in data.ScenarioResult?.Characters ?? new List<FHScenarioResult.Character>())
                    if (ch != null && !string.IsNullOrEmpty(ch.ClassID) && !vanilla.CharacterClasses.ContainsKey(new CharacterClassID(ch.ClassID))) return where + ": " + ch.ClassID;
                where = "party mirror";
                CMapParty party = data.MapState?.MapParty;
                if (party != null)
                {
                    foreach (CMapCharacter mc in party.CheckCharacters.Concat(party.SelectedCharactersArray ?? new CMapCharacter[0]))
                        if (mc != null && !string.IsNullOrEmpty(mc.CharacterID) && !vanilla.CharacterClasses.ContainsKey(new CharacterClassID(mc.CharacterID))) return where + ": " + mc.CharacterID;
                    foreach (CPlayerRecords r in party.RetiredCharacterRecords ?? new List<CPlayerRecords>())
                        if (r != null && !string.IsNullOrEmpty(r.CharacterID) && !vanilla.CharacterClasses.ContainsKey(new CharacterClassID(r.CharacterID))) return where + " (retired records): " + r.CharacterID;
                    foreach (string id in party.UnlockedCharacterIDs ?? new List<string>())
                        if (!string.IsNullOrEmpty(id) && !vanilla.CharacterClasses.ContainsKey(new CharacterClassID(id))) return where + " (unlocked): " + id;
                }
            }
            catch (Exception e)
            {
                return where + ": " + (e.InnerException ?? e).Message;
            }
            return null;
        }
    }
}
