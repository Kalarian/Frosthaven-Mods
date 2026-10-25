using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FHGUI;
using FHGUI.Elements;
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
    /// The way out of a campaign that used a content pack: a copy of the campaign with every trace of pack content
    /// removed, saved as a NEW campaign "&lt;name&gt; (no content packs)" that a game without the pack (or without the
    /// mods at all) loads. Needs the mods, never the pack: it strips whatever the installed rules don't know, so it
    /// works after the pack is gone, on a save the game itself can no longer open. Three doors:
    /// - the Save Game window in the outpost: "Copy without content packs" copies the live campaign (host or solo);
    /// - the Load Game window: the same button on any saved campaign that carries pack content, read from its file;
    /// - a guard on loading: a save that names content the rules lack is stopped with an offer to make the copy, instead
    ///   of failing inside the game, whose error button moves the save to the corrupted folder.
    ///
    /// What a pack-less game chokes on is every lookup of a pack definition: a character's class (CharacterState.ClassDef
    /// indexes the definition repository), its cards, perks and masteries, enhancements on pack cards, the "class
    /// unlocked" line of the outpost log, a pending class choice, the unlocked-class list, and the legacy party mirror
    /// (CMapCharacter, keyed by class ID) the save also carries. The copy drops them all: pack characters are deleted the
    /// way the game deletes one (items back to the shop, gold and resources gone, their personal quest back in the deck),
    /// retired pack characters leave the retired roster, everything else is filtered. The copy is then checked against a
    /// repository with the installed packs removed (every definition the save can reach is resolved), written as a new
    /// campaign through the game's own save writer, read back from disk and checked again. The copy's rules keep only
    /// the game's own switches. The source campaign is never changed.
    /// </summary>
    [HarmonyPatch]
    internal static class GHSaveStrip
    {
        public const string Suffix = " (no content packs)";
        public const string FileName = "No content packs";
        private const string SaveButtonName = "FrosthavenMods_StripSave";
        private const string LoadButtonName = "FrosthavenMods_StripLoad";
        private const string Marker = "FHMODS:";   // the mods' own markers in LootedChestTreasureTableIDs (GHLooks, Bone kills)

        // the game's own rule bits: everything else is a mod switch and goes
        private static readonly ERulesFlag VanillaRules = Enum.GetValues(typeof(ERulesFlag)).Cast<ERulesFlag>().Aggregate(ERulesFlag.None, (a, b) => a | b);

        private static readonly FieldInfo NewSaveField = AccessTools.Field(typeof(FHUISaveGame), "newSave");
        private static readonly FieldInfo SaveInfoField = AccessTools.Field(typeof(FHUILoadGame), "saveInfo");
        private static readonly FieldInfo InvalidateField = AccessTools.Field(typeof(FHUILoadGame), "_invalidate");
        private static readonly FieldInfo ActionButtonField = AccessTools.Field(typeof(FHUIGameSaveData), "actionButton");
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
        private static readonly FieldInfo RepoSections = AccessTools.Field(typeof(DefRepository), nameof(DefRepository.CampaignSections));
        private static readonly FieldInfo RepoEvents = AccessTools.Field(typeof(DefRepository), nameof(DefRepository.OutpostEvents));
        private static readonly MethodInfo ShallowClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool _bypass;        // the load guard lets a load it has already checked through
        private static int _selectSeq;      // the Load window's selection the file check belongs to
        private static Transform _loadButton;
        private static PartyAdventureData _loadButtonSave;

        internal sealed class Report
        {
            public readonly List<string> Removed = new List<string>();
            public string Campaign;
            public string Path;
        }

        private static DefRepository Repo => GHClassesRule.LiveRepo;

        // ================================================================== the Save Game window (live campaign)

        [HarmonyPatch(typeof(FHUISaveGame), nameof(FHUISaveGame.Show), typeof(IGameModeService))]
        [HarmonyPostfix]
        private static void AddSaveButton(FHUISaveGame __instance)
        {
            GiveResourcesPatches.Safe("add the Copy without content packs button (save window)", () =>
            {
                var template = NewSaveField.GetValue(__instance) as FHButton;
                if (template == null) return;
                Transform parent = template.transform.parent;
                bool show = WantedLive();
                Transform existing = parent.Find(SaveButtonName);
                if (existing == null)
                {
                    if (!show) return;
                    UiDump.Once("Save window buttons", parent);
                    FHButton button = ButtonCopy.Make(template, parent, SaveButtonName, Text.Get("FHMODS_STRIP"), OnClickLive);
                    if (button == null) return;
                    PlaceBeside((RectTransform)button.transform, (RectTransform)template.transform);
                    existing = button.transform;
                }
                existing.gameObject.SetActive(show);
            });
        }

        /// <summary>Show the button to the host or solo player in the outpost when the campaign has (or can get) pack content.</summary>
        private static bool WantedLive()
        {
            FHMapChoreographer map = Singleton<FHMapChoreographer>.InstanceFast;
            CampaignState state = map?.GameState;
            DefRepository repo = map?.GameController?.DefRepository;
            if (state == null || repo == null) return false;
            if (SaveData.Instance?.Global == null || SaveData.Instance.Global.CurrentGameState != EGameState.Map) return false;
            if (FFSNetwork.IsOnline && !FFSNetwork.IsHost) return false;
            return GHClassesRule.On || Traces(state, repo).Any();
        }

        private static void OnClickLive()
        {
            string why = WhyNotLive(out FHMapChoreographer map);
            if (why != null) { Info(why); return; }
            PartyAdventureData live = SaveData.Instance.Global.CurrentAdventureData;
            Confirm(live, Traces(map.GameState, map.GameController.DefRepository).ToList(), () => RunLive(map));
        }

        /// <summary>Why the live copy can't be made right now, or null.</summary>
        private static string WhyNotLive(out FHMapChoreographer map)
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

        private static async void RunLive(FHMapChoreographer map)
        {
            Singleton<FHUISaveGame>.Instance?.Hide();
            await Run(() => CopyLive(map));
        }

        /// <summary>The live campaign, after the game's own pre-save sync, as a deep copy.</summary>
        private static async Task<Report> CopyLive(FHMapChoreographer map)
        {
            PartyAdventureData live = SaveData.Instance.Global.CurrentAdventureData;
            CampaignController controller = map.GameController;
            DefRepository repo = controller.DefRepository;
            try { map.GameClient.SyncCharacters(); }   // the legacy party mirror, the party slots and the save's state reference
            catch (Exception e) { Plugin.Log.LogWarning("Copy without content packs: the pre-save sync failed, continuing: " + e.Message); }
            FHSaveData data = await live.FrosthavenSaveData.DeepCopySerializableObject<FHSaveData>();
            var serializer = new StateSerializer<CampaignState>(repo) { StateResolveAction = s => s.ResolveDefs(repo) };
            data.GameState = serializer.Clone(controller.State);
            return await Finish(data, live, repo);
        }

        // ================================================================== the Load Game window (any saved campaign)

        [HarmonyPatch(typeof(FHUILoadGame), "SelectSave")]
        [HarmonyPostfix]
        private static void LoadWindowSelected(FHUILoadGame __instance, IGameSaveData saveData)
        {
            GiveResourcesPatches.Safe("the Load window's content-pack check", () =>
            {
                int seq = ++_selectSeq;
                if (_loadButton != null) _loadButton.gameObject.SetActive(false);
                _loadButtonSave = null;
                PartyAdventureData save = saveData?.PartyAdventureData;
                if (save == null || saveData.GameMode != EGameMode.Campaign || Repo == null) return;
                var panel = SaveInfoField.GetValue(__instance) as FHUIGameSaveData;
                var template = panel == null ? null : ActionButtonField.GetValue(panel) as Button;
                if (template == null) return;
                CheckSelected(save, template, seq);
            });
        }

        private static async void CheckSelected(PartyAdventureData save, Button template, int seq)
        {
            List<string> traces;
            try
            {
                FHSaveData data = await ReadSave(save);
                traces = Traces(data.GameState, Repo).ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Copy without content packs: could not check '{save.PartyName}/{save.SaveFileName}': {e.Message}");
                return;
            }
            if (seq != _selectSeq || traces.Count == 0) return;   // another save was picked meanwhile, or nothing to strip
            GiveResourcesPatches.Safe("add the Copy without content packs button (load window)", () =>
            {
                // The Load button sits in a row (Delete | Load) laid out horizontally; a third button in that row pushes
                // the Load button out of the panel. Ours goes under the row, as wide as the row.
                Transform row = template.transform.parent, panel = row.parent;
                if (_loadButton == null || _loadButton.parent != panel)
                {
                    UiDump.Once("Load window save panel buttons", row);
                    FHButton made = ButtonCopy.Make(template as FHButton ?? template.GetComponent<FHButton>(), panel, LoadButtonName,
                                                    Text.Get("FHMODS_STRIP"), OnClickFile);
                    if (made == null) return;
                    PlaceBelow((RectTransform)made.transform, (RectTransform)row);
                    _loadButton = made.transform;
                }
                _loadButtonSave = save;
                _loadButton.gameObject.SetActive(true);
            });
        }

        private static void OnClickFile()
        {
            PartyAdventureData save = _loadButtonSave;
            if (save == null) return;
            ConfirmFile(save);
        }

        private static async void ConfirmFile(PartyAdventureData save)
        {
            List<string> traces;
            try { traces = Traces((await ReadSave(save)).GameState, Repo).ToList(); }
            catch (Exception e) { Info("This save could not be read: " + e.Message); return; }
            Confirm(save, traces, () => RunFile(save));
        }

        private static async void RunFile(PartyAdventureData save)
        {
            await Run(() => CopyFile(save));
            if (Singleton<FHUILoadGame>.Instance != null) InvalidateField.SetValue(Singleton<FHUILoadGame>.Instance, true);   // relist
        }

        /// <summary>A saved campaign, read from its file; outpost saves only (a save made during a scenario carries
        /// the scenario's actors as well).</summary>
        private static async Task<Report> CopyFile(PartyAdventureData save)
        {
            if (save.CurrentScenarioID.HasValue)
                throw new Exception("this save was made during a scenario; pick one made in the outpost (an autosave or a Mod backup after the scenario)");
            FHSaveData data = await ReadSave(save);
            return await Finish(data, save, Repo);
        }

        private static async Task<FHSaveData> ReadSave(PartyAdventureData save)
        {
            string path = await save.PartyMainSaveFile;
            var loaded = await (Task<(FHSaveData data, bool gameClosedWhileSaving)>)LoadFromFile.Invoke(null, new object[] { path, false, save.IsOldSaveFileFormat });
            if (loaded.data?.GameState == null) throw new Exception("the save file could not be read");
            return loaded.data;
        }

        // ================================================================== the load guard

        [HarmonyPatch(typeof(CampaignLoadService), nameof(CampaignLoadService.LoadGame))]
        [HarmonyPrefix]
        private static bool GuardLoad(CampaignLoadService __instance, IGameSaveData gameData, ref Task __result)
        {
            if (_bypass || gameData?.PartyAdventureData == null || gameData.GameMode != EGameMode.Campaign || Repo == null) return true;
            PartyAdventureData save = gameData.PartyAdventureData;
            __result = Guarded(save, () =>
            {
                _bypass = true;
                try { return __instance.LoadGame(gameData); }
                finally { _bypass = false; }
            });
            return false;
        }

        [HarmonyPatch(typeof(CampaignModeService), nameof(CampaignModeService.ResumeLastGame))]
        [HarmonyPrefix]
        private static bool GuardResume(CampaignModeService __instance)
        {
            if (_bypass || Repo == null) return true;
            GuardedResume(__instance);
            return false;
        }

        private static async void GuardedResume(CampaignModeService service)
        {
            PartyAdventureData resume = null;
            try { resume = await SaveData.Instance.Global.GetResumeCampaign(); }
            catch (Exception e) { Plugin.Log.LogWarning("Copy without content packs: no resume campaign to check: " + e.Message); }
            await Guarded(resume, () =>
            {
                _bypass = true;
                try { service.ResumeLastGame(); }
                finally { _bypass = false; }
                return Task.CompletedTask;
            });
        }

        /// <summary>Let the load go ahead unless the save names content the installed rules lack; then offer the copy.</summary>
        private static async Task Guarded(PartyAdventureData save, Func<Task> proceed)
        {
            List<string> missing = new List<string>();
            if (save != null && save.GameMode == EGameMode.Campaign)
            {
                try { missing = Missing((await ReadSave(save)).GameState, Repo); }
                catch (Exception e) { Plugin.Log.LogWarning($"Copy without content packs: could not check '{save.PartyName}' before loading: {e.Message}"); }
            }
            if (missing.Count == 0)
            {
                await proceed();
                return;
            }
            CoopLog.Write($"STRIP GUARD '{save.PartyName}/{save.SaveFileName}' names missing content: {string.Join(", ", missing)}");
            string text = $"\"{save.PartyName}\" uses content that isn't installed in this game: {string.Join(", ", missing)}.\n\n" +
                          "Loaded as it is, it would fail, and the game would move the save to its corrupted folder. Install that " +
                          "content pack again, or make a copy of the campaign without it: it appears in Load game as " +
                          $"\"{save.PartyName}{Suffix}\", with those characters and unlocks removed.";
            Singleton<UIConfirmationBoxManager>.Instance.ShowGenericConfirmation(Text.Get("FHMODS_STRIP_MISSING"), text,
                () => RunFile(save), null, "FHMODS_STRIP_DO", "GUI_CANCEL");
        }

        // ================================================================== shared UI

        private static void Confirm(PartyAdventureData save, List<string> traces, Action go)
        {
            string what = traces.Count == 0
                ? "This campaign has no content-pack characters or unlocks, so the copy only loses the pack's switch."
                : "The copy will not have:\n- " + string.Join("\n- ", traces) + "\n\nTheir items go back to the shop; their gold and resources are lost.";
            string text = $"A copy of the campaign is saved as a new campaign, \"{save.PartyName}{Suffix}\", that loads in a game without " +
                          $"content packs (or without the mods). The campaign itself is not changed.\n\n{what}\n\n" +
                          "The copy keeps only the game's own house rules.";
            Singleton<UIConfirmationBoxManager>.Instance.ShowGenericConfirmation(Text.Get("FHMODS_STRIP_TITLE"), text, () => go());
        }

        private static void Info(string text) =>
            Singleton<UIConfirmationBoxManager>.Instance.ShowGenericInfo(Text.Get("FHMODS_STRIP_TITLE"), text, null, "GUI_CONTINUE");

        private static async Task Run(Func<Task<Report>> make)
        {
            var gate = SavingLock.GetValue(SaveData.Instance) as SemaphoreSlim;
            bool held = false;
            try
            {
                if (gate != null)
                {
                    held = await gate.WaitAsync(TimeSpan.FromSeconds(10));
                    if (!held) throw new Exception("another save is still running");
                }
                Report report = await make();
                CoopLog.Write($"STRIP COPY written: {report.Path} removed=[{string.Join("; ", report.Removed)}]");
                string removed = report.Removed.Count == 0 ? "Nothing had to be removed." : "Removed from the copy:\n- " + string.Join("\n- ", report.Removed);
                Info($"Saved as the campaign \"{report.Campaign}\" (save \"{FileName}\"). It is in Load game, and opens in a game " +
                     $"without content packs.\n\n{removed}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Copy without content packs: failed: " + e);
                Info("The copy was not made: " + (e.InnerException ?? e).Message + "\n\nNothing was changed.");
            }
            finally
            {
                if (held) gate.Release();
            }
        }

        /// <summary>Left of the template: in a layout group by sibling order, else by position. Wide enough for the label.</summary>
        private static void PlaceBeside(RectTransform rt, RectTransform template)
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
            NoWrap(rt);
        }

        /// <summary>Above the template (the Load window's Load button has Delete beside it), same width as the label needs.</summary>
        /// <summary>Under <paramref name="row"/> (the Load window's Delete | Load row), with the row's anchors and width,
        /// one row height plus a gap lower; outside any layout the panel may run.</summary>
        private static void PlaceBelow(RectTransform rt, RectTransform row)
        {
            rt.anchorMin = row.anchorMin;
            rt.anchorMax = row.anchorMax;
            rt.pivot = row.pivot;
            rt.sizeDelta = new Vector2(row.sizeDelta.x, row.rect.height);
            rt.anchoredPosition = row.anchoredPosition - new Vector2(0f, row.rect.height + 10f);
            LayoutElement le = rt.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;
            NoWrap(rt);
        }

        private static void NoWrap(RectTransform rt)
        {
            foreach (TMP_Text t in rt.GetComponentsInChildren<TMP_Text>(true))
            {
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
            }
        }

        // ================================================================== the copy

        /// <summary>Strip, check, write and re-check. Throws with a plain-English reason on any failure; a written file
        /// that fails its read-back check is deleted again.</summary>
        private static async Task<Report> Finish(FHSaveData data, PartyAdventureData source, DefRepository repo)
        {
            data.ResolveDefs(repo);
            List<CharacterClassID> pack = GHClassesRule.PackClassIDs(repo);
            var report = new Report();
            Strip(data, repo, pack, report);

            DefRepository packless = Packless(repo, pack);
            string leftover = Verify(data, packless);
            if (leftover != null) throw new Exception("the copy would still refer to pack content (" + leftover + ")");
            data.ResolveDefs(repo);

            string name = source.PartyName + Suffix;
            if (name.Length >= PartyAdventureData.Header.CampaignNameSizeLimit)
                name = source.PartyName.Substring(0, PartyAdventureData.Header.CampaignNameSizeLimit - Suffix.Length - 1) + Suffix;
            var copy = new PartyAdventureData(data, name, source.Owner ?? new SaveOwner(), source.GameMode, source.Rules & VanillaRules, source.IsIronMan)
            {
                SaveFileName = FileName,
                TimeActiveSecRunLTD = source.TimeActiveSecRunLTD,
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
                try { File.Delete(path); } catch (Exception e) { Plugin.Log.LogWarning("Copy without content packs: could not delete the bad copy: " + e.Message); }
                throw new Exception(problem);
            }

            // the Load screen's picture, if the source has one
            try
            {
                string shot = await source.ScreenShotFile;
                if (File.Exists(shot)) File.Copy(shot, await copy.ScreenShotFile, true);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Copy without content packs: no screenshot for the copy: " + e.Message); }

            report.Campaign = name;
            report.Path = path;
            return report;
        }

        private static string Short(CharacterClassID id) =>
            id.ID != null && id.ID.EndsWith("ID", StringComparison.Ordinal) ? id.ID.Substring(0, id.ID.Length - 2) : id.ID ?? "?";

        private static string Short(string id) => Short(new CharacterClassID(id));

        /// <summary>A class the pack-less game lacks: one of the installed packs', or one these rules don't know at all.</summary>
        private static bool Foreign(CharacterClassID id, List<CharacterClassID> pack, DefRepository repo) =>
            id.ID != null && (pack.Contains(id) || !repo.CharacterClasses.ContainsKey(id));

        /// <summary>The campaign sections the installed packs add: the ones their manifests list (gh_port writes them) and
        /// the retirement sections of their classes from the class definitions, for a manifest that predates the list.</summary>
        private static List<CampaignSectionID> PackSections(DefRepository repo, List<CharacterClassID> pack)
        {
            var ids = new List<CampaignSectionID>();
            void Add(string s) { if (!string.IsNullOrEmpty(s) && !ids.Contains(new CampaignSectionID(s))) ids.Add(new CampaignSectionID(s)); }
            foreach (GHClasses.Pack p in GHClasses.Packs) foreach (string s in p.Sections) Add(s);
            foreach (CharacterClassID id in pack)
                if (repo.CharacterClasses.TryGetValue(id, out CharacterClassDef d) && d != null) Add(d.RetirementCampaignSection);
            return ids;
        }

        /// <summary>A campaign section the pack-less game lacks: one of the installed packs', or one these rules don't know.</summary>
        private static bool ForeignSection(CampaignSectionID id, List<CampaignSectionID> pack, DefRepository repo) =>
            id.ID != null && (pack.Contains(id) || !repo.CampaignSections.ContainsKey(id));

        /// <summary>Every campaign section a save names: activated and queued, on the calendar, posted as a job, in the log.</summary>
        private static IEnumerable<CampaignSectionID> Sections(CampaignState state)
        {
            foreach (CampaignSectionID s in state.ActivatedCampaignSectionIDs) yield return s;
            foreach (CampaignSectionID s in state.PendingCampaignSectionIDs) yield return s;
            if (state.Outpost == null) yield break;
            foreach (CalendarEventState e in state.Outpost.CalendarEvents) if (e != null) yield return e.SectionID;
            foreach (CampaignSectionID s in state.Outpost.JobPostings) yield return s;
            if (state.Outpost.EventLog == null) yield break;
            foreach (OutpostEventLogState.EventLogData.CampaignSectionTriggered e in state.Outpost.EventLog.GetAllEventsOfType<OutpostEventLogState.EventLogData.CampaignSectionTriggered>())
                if (e != null) yield return e.SectionID;
            foreach (OutpostEventLogState.EventLogData.NewJobPosting e in state.Outpost.EventLog.GetAllEventsOfType<OutpostEventLogState.EventLogData.NewJobPosting>())
                if (e != null) yield return e.CampaignSectionID;
        }

        /// <summary>Every event a save names: in the five decks, drawn so far, being resolved, in the log.</summary>
        private static IEnumerable<OutpostEventID> Events(CampaignState state)
        {
            OutpostState o = state.Outpost;
            if (o == null) yield break;
            foreach (List<OutpostEventID> deck in Decks(o)) foreach (OutpostEventID id in deck) yield return id;
            foreach (OutpostEventState e in o.ActivatedEvents) if (e != null) yield return e.ID;
            if (o.ActiveEventID.HasValue) yield return o.ActiveEventID.Value;
            if (o.EventLog == null) yield break;
            foreach (OutpostEventLogState.EventLogData.OutpostEventTriggered e in o.EventLog.GetAllEventsOfType<OutpostEventLogState.EventLogData.OutpostEventTriggered>())
                if (e != null) yield return e.EventID;
        }

        private static List<OutpostEventID>[] Decks(OutpostState o) => new[]
        {
            o.SummerOutpostEventDeckIDs, o.WinterOutpostEventDeckIDs, o.SummerRoadEventDeckIDs, o.WinterRoadEventDeckIDs, o.BoatEventDeckIDs,
        };

        /// <summary>An event the pack-less game lacks: one of the installed packs' (their classes' retirement events), or
        /// one these rules don't know.</summary>
        private static bool ForeignEvent(OutpostEventID id, List<OutpostEventID> pack, DefRepository repo) =>
            id.ID != null && (pack.Contains(id) || !repo.OutpostEvents.ContainsKey(id));

        private static string EventName(OutpostEventID id, DefRepository repo) => GHEvents.Describe(id, repo) ?? "the event " + id.ID;

        /// <summary>"the Brute's retirement story" for a class's retirement section, else the section by ID.</summary>
        private static string StoryName(CampaignSectionID s, DefRepository repo)
        {
            CharacterClassDef d = repo.CharacterClasses.Values.FirstOrDefault(x => x != null && x.RetirementCampaignSection == s.ID);
            return d != null ? $"the {Short(d.ID)}'s retirement story" : $"the story section {s.ID}";
        }

        /// <summary>The classes (and story sections) a save names that the installed rules don't know (the load guard's question).</summary>
        private static List<string> Missing(CampaignState state, DefRepository repo)
        {
            var none = new List<CharacterClassID>();
            var noSections = new List<CampaignSectionID>();
            var ids = new List<string>();
            void Add(CharacterClassID id) { if (Foreign(id, none, repo) && !ids.Contains(Short(id))) ids.Add(Short(id)); }
            foreach (CampaignSectionID s in Sections(state))
                if (ForeignSection(s, noSections, repo) && !ids.Contains("story section " + s.ID)) ids.Add("story section " + s.ID);
            var noEvents = new List<OutpostEventID>();
            foreach (OutpostEventID e in Events(state))
                if (ForeignEvent(e, noEvents, repo) && !ids.Contains("event " + e.ID)) ids.Add("event " + e.ID);
            foreach (CharacterState c in state.AllCharacters.Concat(state.RetiredCharacters)) if (c != null) Add(c.ClassID);
            foreach (CharacterClassID id in state.UnlockedClassIDs) Add(id);
            foreach (CharacterClassDef d in state.UnresolvedActionChosenCharacters) if (d != null) Add(d.ID);
            foreach (CampaignUnresolvedAction a in state.UnresolvedActions.Where(a => a.Type == CampaignInteractionType.SelectClassUnlock))
                foreach (string p in a.GetStringParams()) if (!string.IsNullOrEmpty(p)) Add(new CharacterClassID(p));
            if (state.Outpost?.EventLog != null)
                foreach (OutpostEventLogState.EventLogData.ClassUnlocked e in state.Outpost.EventLog.GetAllEventsOfType<OutpostEventLogState.EventLogData.ClassUnlocked>())
                    if (e != null) Add(e.ClassID);
            if (state.ClassCardModifications.Any(m => m != null && !repo.ClassCards.ContainsKey(m.CardID)) && !ids.Contains("cards of a missing class"))
                ids.Add("cards of a missing class");
            return ids;
        }

        /// <summary>What the copy will lose, for the confirmation and the buttons' visibility: installed packs' content
        /// and content these rules don't know.</summary>
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
                                                                 (!repo.ClassCards.TryGetValue(m.CardID, out ClassCardDef card) || card == null || IsPack(card.ClassID)));
            if (enhanced > 0) yield return $"enhancements on {enhanced} of their cards";
            if (state.Outpost?.EventLog != null &&
                state.Outpost.EventLog.GetAllEventsOfType<OutpostEventLogState.EventLogData.ClassUnlocked>().Any(e => e != null && IsPack(e.ClassID)))
                yield return "the class unlocks in the outpost log";
            if (state.UnresolvedActions.Any(a => a.Type == CampaignInteractionType.SelectClassUnlock &&
                                                a.GetStringParams().Any(p => !string.IsNullOrEmpty(p) && IsPack(new CharacterClassID(p)))))
                yield return "a pending class choice";
            List<CampaignSectionID> sections = PackSections(repo, pack);
            foreach (string s in Sections(state).Where(s => ForeignSection(s, sections, repo)).Select(s => s.ID).Distinct().ToList())
                yield return StoryName(new CampaignSectionID(s), repo);
            List<OutpostEventID> events = GHEvents.PackEventIDs();
            foreach (string e in Events(state).Where(e => ForeignEvent(e, events, repo)).Select(e => e.ID).Distinct().ToList())
                yield return EventName(new OutpostEventID(e), repo);
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
            // the packs' story sections (a class's retirement story): activated, queued, on the calendar, posted, logged
            List<CampaignSectionID> sections = PackSections(repo, pack);
            bool IsPackSection(CampaignSectionID id) => ForeignSection(id, sections, repo);
            foreach (string s in Sections(state).Where(IsPackSection).Select(s => s.ID).Distinct().ToList())
                report.Removed.Add(StoryName(new CampaignSectionID(s), repo));
            state.ActivatedCampaignSectionIDs.RemoveAll(IsPackSection);
            state.PendingCampaignSectionIDs.RemoveAll(IsPackSection);
            state.Outpost?.CalendarEvents.RemoveAll(e => e == null || IsPackSection(e.SectionID));
            state.Outpost?.JobPostings.RemoveAll(IsPackSection);
            // the packs' events (a class's retirement events): in the decks, drawn, logged; one being resolved can't go
            List<OutpostEventID> events = GHEvents.PackEventIDs();
            bool IsPackEvent(OutpostEventID id) => ForeignEvent(id, events, repo);
            OutpostState outpost = state.Outpost;
            if (outpost != null)
            {
                if (outpost.ActiveEventID.HasValue && IsPackEvent(outpost.ActiveEventID.Value))
                    throw new Exception("one of the pack's events is being resolved in this save; finish it in the game first");
                foreach (string e in Events(state).Where(IsPackEvent).Select(e => e.ID).Distinct().ToList())
                    report.Removed.Add(EventName(new OutpostEventID(e), repo));
                foreach (List<OutpostEventID> deck in Decks(outpost)) deck.RemoveAll(IsPackEvent);
                outpost.ActivatedEvents.RemoveAll(e => e == null || IsPackEvent(e.ID));
            }
            int enhanced = state.ClassCardModifications.Count(m => m != null && m.EnhancedSlots.Count > 0 &&
                                                                 (!repo.ClassCards.TryGetValue(m.CardID, out ClassCardDef card) || card == null || IsPack(card.ClassID)));
            if (enhanced > 0) report.Removed.Add($"enhancements on {enhanced} of their cards");
            state.ClassCardModifications.RemoveAll(m => m == null || !repo.ClassCards.TryGetValue(m.CardID, out ClassCardDef card) || card == null || IsPack(card.ClassID));
            state.UnresolvedActionChosenCharacters.RemoveAll(d => d == null || IsPack(d.ID));
            state.LootedChestTreasureTableIDs.RemoveAll(s => s != null && s.StartsWith(Marker, StringComparison.Ordinal));
            if (state.Outpost?.EventLog != null && EventList.GetValue(state.Outpost.EventLog) is List<OutpostEventLogState.EventLogData> log)
                log.RemoveAll(e => e == null || (e is OutpostEventLogState.EventLogData.ClassUnlocked cu && IsPack(cu.ClassID))
                                   || (e is OutpostEventLogState.EventLogData.CampaignSectionTriggered st && IsPackSection(st.SectionID))
                                   || (e is OutpostEventLogState.EventLogData.NewJobPosting jp && IsPackSection(jp.CampaignSectionID))
                                   || (e is OutpostEventLogState.EventLogData.OutpostEventTriggered et && IsPackEvent(et.EventID))
                                   || Mentions(e, removed));
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

        /// <summary>The installed definitions minus the installed packs: a shallow clone with the five dictionaries a pack
        /// extends replaced by filtered copies (classes, their cards, perks, masteries, story sections and events). With no pack
        /// installed it is the installed rules as they are, which is exactly what a pack-less game has.</summary>
        private static DefRepository Packless(DefRepository repo, List<CharacterClassID> pack)
        {
            var clone = (DefRepository)ShallowClone.Invoke(repo, null);
            var classes = new HashSet<CharacterClassID>(pack);
            var sections = new HashSet<CampaignSectionID>(PackSections(repo, pack));
            RepoSections.SetValue(clone, repo.CampaignSections.Where(kv => !sections.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            var events = new HashSet<OutpostEventID>(GHEvents.PackEventIDs());
            RepoEvents.SetValue(clone, repo.OutpostEvents.Where(kv => !events.Contains(kv.Key) && !(kv.Value is GHEvents.PackEventDef)).ToDictionary(kv => kv.Key, kv => kv.Value));
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
                where = "story sections"; foreach (CampaignSectionID s in Sections(state)) _ = vanilla.CampaignSections[s];
                foreach (OutpostEventID e in Events(state)) { where = "event " + e.ID; _ = vanilla.OutpostEvents[e]; }
                where = "quest deck"; state.PersonalQuestDeck.ToList();
                where = "shop"; state.Outpost.PurchasableItems.ToList(); state.Outpost.CraftableItems.ToList();
                where = "mod markers";
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
