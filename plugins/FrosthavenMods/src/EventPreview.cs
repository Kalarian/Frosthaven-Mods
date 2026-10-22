using System;
using System.Collections.Generic;
using System.Linq;
using FHGUI;
using FHGUI.DTO;
using FHGUI.Modules;
using FHGUI.Services;
using FHRL;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Reveal event outcomes": on a town, road or boat event, hovering over an option shows what it will do:
    /// its outcome text, then its effects. Board game rule bent (p. 12): pick an option, then
    /// read only that outcome.
    ///
    /// Outcomes are not data: each event is a class (FHRL.OutpostEvents.*_OutpostEventDef) whose ApplyOption works the
    /// effects out when the option is chosen - checking traits, races, buildings, resources, and rolling dice. So each
    /// option is played out on a throwaway copy of the campaign (FHGlue.BinarySerializeState / BinaryDeserialzieState,
    /// the copy the host sends to joining players, run by its own CampaignController) and the copy's results are shown:
    /// the effects, the outcome text the game would show, and what comes after (choices you'll make, an outpost attack,
    /// a next step). The copy carries the dice state too, so random outcomes show the roll you'd actually get. The real
    /// campaign is never touched (display only, nothing to sync).
    /// </summary>
    [HarmonyPatch]
    internal static class EventPreview
    {
        private sealed class Preview
        {
            public string Summary;   // the effects (tooltip, after the outcome)
            public string Outcome;   // the outcome text (tooltip)
        }

        private static string _key;
        private static readonly Dictionary<uint, Preview> Previews = new Dictionary<uint, Preview>();

        [HarmonyPatch(typeof(OutpostEventsService), nameof(OutpostEventsService.GetEventDTO), new[] { typeof(OutpostEventState) })]
        [HarmonyPostfix]
        private static void AddPreviews(OutpostEventState _event, OutpostEventDTO __result)
        {
            if (!ModRules.IsOn(ModRules.RevealEvents) || __result?.Options == null || __result.Options.Count == 0) return;
            try
            {
                var map = Singleton<FHMapChoreographer>.InstanceFast;
                CampaignController live = map?.View?.GameController;
                if (live == null || _event == null || live.OutpostEventController.EventToResolve != _event) return;
                // shown in each option's tooltip (AddTooltips); the option buttons keep the game's own text
                Predict(map, _event, __result.Options.Select(o => o.ID).ToList());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Reveal event outcomes: no preview for this event: " + e);
            }
        }

        /// <summary>Plays each option out on its own copy of the campaign. Cached per event step.</summary>
        private static void Predict(FHMapChoreographer map, OutpostEventState ev, List<uint> optionIDs)
        {
            string key = $"{ev.ID.ID}/{ev.OutcomeIteration}/{ev.OptionSelectedID}/{string.Join(",", optionIDs)}";
            if (key == _key) return;
            _key = key;
            Previews.Clear();
            DefRepository repo = map.DefRepo;
            MultiplayerGameState liveState = map.View.MultiplayerGameController.GameState;
            byte[] snapshot = FHGlue.BinarySerializeState(liveState, repo);
            var service = map.View.GetService<OutpostEventsService>();
            foreach (uint id in optionIDs)
            {
                try { Previews[id] = PlayOut(snapshot, repo, id, service); }
                catch (Exception e)
                {
                    // the details go to the log; the tooltip just says so
                    Previews[id] = new Preview { Summary = "No preview for this option" };
                    Plugin.Log.LogWarning($"Reveal event outcomes: {ev.ID.ID} option {id}: {e}");
                }
            }
            Plugin.Log.LogInfo($"Reveal event outcomes: {ev.ID.ID} step {ev.OutcomeIteration}: " +
                               string.Join(" | ", Previews.Select(p => $"{p.Key}: {p.Value.Summary}")));
        }

        /// <param name="activate">null: the event on screen. Otherwise this event is made the open one on the copy first
        /// (used by <see cref="DumpAll"/>).</param>
        private static Preview PlayOut(byte[] snapshot, DefRepository repo, uint optionID, OutpostEventsService service,
            OutpostEventDef activate = null)
        {
            MultiplayerGameState copy = FHGlue.BinaryDeserialzieState(snapshot, repo);
            CampaignState state = copy.CampaignState;
            var sim = new CampaignController(state, repo);
            if (activate != null) state.Outpost.SetActiveEvent(activate);
            OutpostEventState ev = sim.OutpostEventController.EventToResolve;
            if (ev == null) return new Preview { Summary = "" };
            OutpostEventDef.Option option = ev.Def.GetOptions(state, ev).FirstOrDefault(o => o.ID == optionID);
            if (!option.IsNotEmpty) return new Preview { Summary = "" };
            if (!sim.OutpostEventController.IsValidOption(option)) return new Preview { Summary = "Requirement not met." };

            int effectsBefore = ev.AppliedEffectsFromOutcome.Count;
            var actionsBefore = new HashSet<CampaignUnresolvedAction>(state.UnresolvedActions);
            sim.OutpostEventController.ResolveCurrentEvent(option);
            string outcome = OutcomeText(ev);
            List<string> steps = Steps(copy, sim, ev, effectsBefore, actionsBefore, repo, service, 0);

            // a step in lower case ("you choose again", "if the next scenario is won...") reads on from "Then"
            return new Preview
            {
                Summary = steps.Count == 0 ? "No effect"
                    : string.Join("\n", steps.Select((s, i) => i == 0 ? Cap(s) : char.IsLower(s[0]) ? "Then " + s : "Then: " + s)),
                Outcome = outcome,
            };
        }

        /// <summary>The option's steps, in order. An option can stop at steps the party carries out first (pay 10 gold,
        /// pick a character...) and then continue: each step is worded with its amounts, then the rest is played out as
        /// the game does once it's done. A pick of one character or building is played out once per pick; where the
        /// result depends on amounts or items each player chooses, the rule is stated instead.</summary>
        private static List<string> Steps(MultiplayerGameState copy, CampaignController sim, OutpostEventState ev, int effectsBefore,
            HashSet<CampaignUnresolvedAction> actionsBefore, DefRepository repo, OutpostEventsService service, int depth)
        {
            CampaignState state = copy.CampaignState;
            var steps = new List<string>();
            for (int step = 0; ; step++)
            {
                List<CampaignEffect> effects = ev.AppliedEffectsFromOutcome.Skip(effectsBefore).ToList();
                effectsBefore = ev.AppliedEffectsFromOutcome.Count;
                List<CampaignUnresolvedAction> pending = state.UnresolvedActions.Where(a => !actionsBefore.Contains(a)).ToList();
                List<string> parts = StepText(effects, pending, state, repo, service);
                if (parts.Count > 0) steps.Add(string.Join(" • ", parts));
                if (ev.Completed) break;
                if (!ev.OptionSelected) { steps.Add("you choose again"); break; }
                if (step >= 4) { steps.Add("the event continues"); break; }

                if (Notes.TryGetValue($"{ev.ID.ID}/{ev.OptionSelectedID}", out string note)) { steps.Add(note); break; }
                int pick = pending.FindIndex(a => a.Type == CampaignInteractionType.ChooseCharacters || a.Type == CampaignInteractionType.ChooseBuilding);
                if (pick >= 0)
                {
                    // one pick at a time only (two picks in a row multiply the combinations; those events have a note)
                    bool single = pending.Count(a => a.Type == pending[pick].Type) == 1;
                    string picks = depth < 2 && single ? Picks(copy, pending[pick], pending, repo, service, depth) : null;
                    steps.Add(picks ?? (pending[pick].Type == CampaignInteractionType.ChooseCharacters ? "choose a character" : "choose a building"));
                    if (picks != null) break;
                }
                if (pending.Any(a => DependsOnChoices.Contains(a.Type))) { steps.Add("the result depends on what each of you chooses"); break; }

                // the party has done its part: on this copy, mark the steps done and let the event go on
                foreach (CampaignUnresolvedAction a in pending) state.UnresolvedActions.Remove(a);
                state.AnyHerbsToLose = state.AnyHerbsToGain = 0;
                state.MaterialsToLose = state.MaterialsToGain = default;
                try { sim.OutpostEventController.ContinueResolveCurrentEvent(); }
                catch { steps.Add("the event continues"); break; }
            }
            return steps;
        }

        /// <summary>"Choose a character:" with what each pick leads to, picks with the same result on one line.</summary>
        private static string Picks(MultiplayerGameState copy, CampaignUnresolvedAction pick, List<CampaignUnresolvedAction> pending,
            DefRepository repo, OutpostEventsService service, int depth)
        {
            CampaignState state = copy.CampaignState;
            bool characters = pick.Type == CampaignInteractionType.ChooseCharacters;
            if (characters && pick.GetInt(0) > 1) return null; // several characters at once: too many combinations
            var options = new List<(string name, Action<CampaignState> choose)>();
            if (characters)
                foreach (CharacterState c in state.PartyCharacters)
                {
                    string id = c.ClassID.ID;
                    options.Add((c.Name, s =>
                    {
                        s.UnresolvedActionChosenCharacters.Clear();
                        s.UnresolvedActionChosenCharacters.Add(s.PartyCharacters.First(x => x.ClassID.ID == id).ClassDef);
                    }));
                }
            else
                foreach (BuildingTypeID id in state.BuildingsToSelectIDs)
                {
                    string name = state.Outpost.Buildings.FirstOrDefault(b => b.ID.ID == id.ID)?.Def?.Name ?? $"building {id.ID}";
                    options.Add((name, s => { s.UnresolvedActionChosenBuildings.Clear(); s.UnresolvedActionChosenBuildings.Add(id); }));
                }
            if (options.Count == 0 || options.Count > 8) return null;

            byte[] here = FHGlue.BinarySerializeState(copy, repo);
            var results = new List<(string name, string what)>();
            foreach (var (name, choose) in options)
            {
                string what;
                try
                {
                    MultiplayerGameState c2 = FHGlue.BinaryDeserialzieState(here, repo);
                    CampaignState s2 = c2.CampaignState;
                    var sim2 = new CampaignController(s2, repo);
                    OutpostEventState ev2 = sim2.OutpostEventController.EventToResolve;
                    // the game goes on only once every step is done (a pick can come with an outpost attack, a payment...)
                    foreach (CampaignUnresolvedAction a in pending) s2.UnresolvedActions.Remove(a);
                    s2.AnyHerbsToLose = s2.AnyHerbsToGain = 0;
                    s2.MaterialsToLose = s2.MaterialsToGain = default;
                    var before2 = new HashSet<CampaignUnresolvedAction>(s2.UnresolvedActions);
                    int done = ev2.AppliedEffectsFromOutcome.Count;
                    choose(s2);
                    sim2.OutpostEventController.ContinueResolveCurrentEvent();
                    List<string> sub = Steps(c2, sim2, ev2, done, before2, repo, service, depth + 1);
                    what = sub.Count == 0 ? "no effect" : Cap(string.Join(", then ", sub));
                }
                catch (Exception e)
                {
                    what = "not available";
                    Plugin.Log.LogWarning($"Reveal event outcomes: pick '{name}': {(e.InnerException ?? e).Message}");
                }
                results.Add((name, what));
            }
            return (characters ? "choose a character:" : "choose a building:") +
                   string.Concat(results.GroupBy(r => r.what).Select(g => $"\n  {string.Join(", ", g.Select(r => r.name))}: {g.Key}"));
        }

        /// <summary>One step's effects and party steps, worded.</summary>
        private static List<string> StepText(List<CampaignEffect> effects, List<CampaignUnresolvedAction> pending, CampaignState state,
            DefRepository repo, OutpostEventsService service)
        {
            var parts = new List<string>();
            // The game merges every "event added" (and shop item / calendar section) effect into one generic line, and
            // hides some effects players need to know about: name the events, word those effects ourselves.
            List<string> added = effects.SelectMany(e => EventsAdded(e, repo)).Distinct().ToList();
            List<string> removed = effects.Where(e => e.Type == CampaignEffectType.RemoveEvent).Select(e => e.GetString(0)).Distinct().ToList();
            var shownEffects = effects.Where(e => e.Type != CampaignEffectType.TriggerAttackOutpost && e.Type != CampaignEffectType.RemoveEvent
                                                  && !EventsAdded(e, repo).Any()).ToList();
            bool OneByOne(CampaignEffect e) => e.Type == CampaignEffectType.AddItemsToShop || e.Type == CampaignEffectType.AddCalendarCampaignSection;
            var visible = new HashSet<CampaignEffectType>();
            foreach (CampaignEffect effect in service.CombineCampaignEffects(shownEffects.Where(e => !OneByOne(e))).Concat(shownEffects.Where(OneByOne)))
            {
                string text = OwnText(effect);
                if (text == null)
                {
                    OutpostEventDTO.Effect shown = service.GetFormattedEffect(effect);
                    text = shown != null && !shown.Hidden && !string.IsNullOrWhiteSpace(shown.Text) ? Clean(shown.Text) : HiddenText(effect, repo);
                }
                if (text == null) continue;
                parts.Add(text);
                visible.Add(effect.Type);
            }
            parts = parts.Distinct().ToList();
            if (added.Count > 0) parts.Add((added.Count == 1 ? "New event: " : "New events: ") + string.Join(", ", added));
            if (removed.Count > 0) parts.Add((removed.Count == 1 ? "Event removed: " : "Events removed: ") + string.Join(", ", removed));
            if (effects.Any(e => e.Type == CampaignEffectType.TriggerAttackOutpost)) parts.Add("Outpost attack!");

            // the party's steps, unless the game's own effect line above already says the same (with its amount)
            List<string> todo = pending
                .Where(a => !(ExplainedBy.TryGetValue(a.Type, out CampaignEffectType[] by) && by.Any(visible.Contains)))
                .Select(a => Describe(a, state)).Where(s => s != null).Distinct().ToList();
            List<string> pays = todo.Where(s => s.StartsWith("Pay ")).ToList();
            if (pays.Count > 0) parts.Add("Pay " + string.Join(" and ", pays.Select(s => s.Substring(4))));
            parts.AddRange(todo.Where(s => !s.StartsWith("Pay ")));
            return parts;
        }

        /// <summary>Party steps the game creates from an effect whose line already shows it.</summary>
        private static readonly Dictionary<CampaignInteractionType, CampaignEffectType[]> ExplainedBy = new Dictionary<CampaignInteractionType, CampaignEffectType[]>
        {
            [CampaignInteractionType.GainGold] = new[] { CampaignEffectType.GainGold },
            [CampaignInteractionType.LoseGold] = new[] { CampaignEffectType.LoseGold },
            [CampaignInteractionType.GainMaterials] = new[] { CampaignEffectType.GainMaterials, CampaignEffectType.GainLumber, CampaignEffectType.GainMetal, CampaignEffectType.GainHide },
            [CampaignInteractionType.LoseMaterials] = new[] { CampaignEffectType.LoseMaterials, CampaignEffectType.LoseLumber, CampaignEffectType.LoseMetal, CampaignEffectType.LoseHide },
            [CampaignInteractionType.GainAnyHerbs] = new[] { CampaignEffectType.GainHerbs },
            [CampaignInteractionType.GainItem] = new[] { CampaignEffectType.GainItem },
            [CampaignInteractionType.LoseItem] = new[] { CampaignEffectType.LoseItem },
        };

        /// <summary>Steps where what follows depends on amounts or items each player picks (not playable out).</summary>
        private static readonly HashSet<CampaignInteractionType> DependsOnChoices = new HashSet<CampaignInteractionType>
        {
            CampaignInteractionType.Event_EverybodyChooseLoseUpToGold, CampaignInteractionType.Event_EverybodyChooseLoseUpToMaterials,
            CampaignInteractionType.Event_EverybodyChooseLoseGold, CampaignInteractionType.Event_BuyItemsDiscountOptional,
            CampaignInteractionType.Event_LoseAnySnowthistle,
        };

        /// <summary>What follows those choices, from the event cards (checked against the game's event code).</summary>
        private static readonly Dictionary<string, string> Notes = new Dictionary<string, string>
        {
            ["SO-13/1"] = "gain 5 gold for each snowthistle given",
            ["SO-18/1"] = "if the next scenario is won, each character gets back double the gold they paid",
            ["SO-18/2"] = "if the next scenario is won, each character gets back double the resources they paid",
            ["SR-04/1"] = "each character who paid starts the next scenario with a bless; once 6 donations have been made in all, gain 1 prosperity",
            ["SR-15/1"] = "a two-handed weapon adds event WR-37, any other weapon adds event WR-38",
            ["SR-19/1"] = "each character who bought an item starts the next scenario with a curse",
            ["CustomEvent_142.1/1"] = "choose two characters: the first starts the next quest with half their health, the second with that much " +
                                      "extra health • The party gains an item (Tome of Life) • No outpost event next week",
        };

        /// <summary>Our wording where the game's line leaves out what players need (the calendar section).</summary>
        private static string OwnText(CampaignEffect e)
        {
            if (e.Type != CampaignEffectType.AddCalendarCampaignSection) return null;
            uint weeks = e.GetUInt(1);
            return $"Calendar: {Section(e.GetString(0))} " + (weeks == 0 ? "this week" : weeks == 1 ? "next week" : $"in {weeks} weeks");
        }

        /// <summary>Effects the game doesn't list but players need to know about.</summary>
        private static string HiddenText(CampaignEffect e, DefRepository repo)
        {
            switch (e.Type)
            {
                case CampaignEffectType.ResolveAnotherBoatEvent: return "Another boat event follows";
                case CampaignEffectType.ResolveAnotherSummerRoadEvent:
                case CampaignEffectType.ResolveAnotherWinterRoadEvent: return "Another road event follows";
                case CampaignEffectType.AddMoraleOnScenarioSucces: return $"Gain {e.GetInt(0)} morale if the next scenario is won";
                case CampaignEffectType.AddCalendarCampaignSectionNextSummer: return $"Calendar: {Section(e.GetString(0))} next summer";
                case CampaignEffectType.SkipNextOutpostEvent: return "No outpost event next week";
                case CampaignEffectType.ChangeActiveScenario:
                    return repo.Scenarios.TryGetValue(new ScenarioID(e.GetUInt(0)), out ScenarioDef s)
                        ? $"Your destination changes to #{s.ID.ID} {s.Name}" : "Your destination changes";
                default: return null;
            }
        }

        /// <summary>"section 180.1", or a story event of the digital game by its title: "CustomEvent_A_Tall_Drunken_Tale"
        /// -> "“A Tall Drunken Tale”", "CustomEvent_TheBridge" -> "“The Bridge”".</summary>
        private static string Section(string id)
        {
            const string custom = "CustomEvent_";
            if (id == null || !id.StartsWith(custom)) return "section " + id;
            string name = System.Text.RegularExpressions.Regex.Replace(id.Substring(custom.Length).Replace('_', ' '), "([a-z])([A-Z])", "$1 $2");
            return "“" + name + "”";
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>The events an effect adds to the decks (events have no names, only these IDs, as on the cards).</summary>
        private static IEnumerable<string> EventsAdded(CampaignEffect e, DefRepository repo)
        {
            if (e.Type == CampaignEffectType.EnableEvent)
            {
                string id = e.GetString(0);
                if (repo.OutpostEvents.ContainsKey(new OutpostEventID(id))) yield return id;
            }
            else if (e.Type == CampaignEffectType.EnableEventsFromTo)
            {
                // as the game expands it (CampaignController, EnableEventsFromTo): "SO-37".."SO-40", existing IDs only
                string[] from = e.GetString(0).Split('-'), to = e.GetString(1).Split('-');
                if (from.Length != 2 || to.Length != 2 || !int.TryParse(from[1], out int a) || !int.TryParse(to[1], out int b)) yield break;
                for (int i = a; i <= b; i++)
                    if (repo.OutpostEvents.ContainsKey(new OutpostEventID($"{from[0]}-{i}"))) yield return $"{from[0]}-{i}";
            }
        }

        /// <summary>Checking aid (the dev tools' Ctrl+F5 calls it): every event's options played out on copies of the
        /// current campaign, written to <game>\FrosthavenMods\event-previews.txt exactly as the tooltips would show
        /// them. Display only: the live campaign is not touched. Returns the file's path.</summary>
        public static string DumpAll()
        {
            var map = Singleton<FHMapChoreographer>.InstanceFast;
            if (map?.View?.GameController == null) throw new InvalidOperationException("only works in the outpost");
            DefRepository repo = map.DefRepo;
            byte[] snapshot = FHGlue.BinarySerializeState(map.View.MultiplayerGameController.GameState, repo);
            var service = map.View.GetService<OutpostEventsService>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Event previews for '{SaveData.Instance?.Global?.CurrentAdventureData?.PartyName}' as of {DateTime.Now:yyyy-MM-dd HH:mm} (Frosthaven Mods {Plugin.Version})");
            int events = 0, options = 0, failed = 0;
            foreach (OutpostEventDef def in repo.OutpostEvents.Values.OrderBy(d => d.ID.ID.Split('-')[0]).ThenBy(d => int.TryParse(d.ID.ID.Split('-').Last(), out int n) ? n : 0))
            {
                events++;
                sb.AppendLine().AppendLine($"{def.ID.ID} ({def.Type})");
                List<uint> ids;
                try
                {
                    CampaignState state = FHGlue.BinaryDeserialzieState(snapshot, repo).CampaignState;
                    state.Outpost.SetActiveEvent(def);
                    OutpostEventState ev = new CampaignController(state, repo).OutpostEventController.EventToResolve;
                    ids = def.GetOptions(state, ev).Select(o => o.ID).ToList();
                }
                catch (Exception e) { sb.AppendLine("  (no options: " + (e.InnerException ?? e).Message + ")"); failed++; continue; }
                foreach (uint id in ids)
                {
                    options++;
                    string letter = id >= 1 && id <= 26 ? ((char)('A' + id - 1)).ToString() : id.ToString();
                    Preview p;
                    try { p = PlayOut(snapshot, repo, id, service, def); }
                    catch (Exception e) { p = new Preview { Summary = "Not available: " + (e.InnerException ?? e).Message }; failed++; }
                    string[] lines = (p.Summary ?? "").Split('\n');
                    sb.AppendLine($"  {letter}: {lines[0]}");
                    foreach (string line in lines.Skip(1)) sb.AppendLine("     " + line);
                    string outcome = (p.Outcome ?? "").Replace("\n", " ");
                    sb.AppendLine("     outcome: " + (outcome.Length > 110 ? outcome.Substring(0, 110) + "..." : outcome));
                }
            }
            sb.AppendLine().AppendLine($"{events} events, {options} options, {failed} not available");
            string dir = System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "FrosthavenMods");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "event-previews.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            return path;
        }

        /// <summary>A step the party carries out, with its amount where the game records one. null: nothing to add (the
        /// outpost attack line, or a pick that <see cref="Picks"/> lays out).</summary>
        private static string Describe(CampaignUnresolvedAction a, CampaignState state)
        {
            int n = a.GetInt(0);
            switch (a.Type)
            {
                case CampaignInteractionType.LoseGold: return n > 0 ? $"Pay {n} gold (from any characters)" : "Pay gold";
                case CampaignInteractionType.GainGold: return n > 0 ? $"Gain {n} gold (shared as you like)" : "Gain gold";
                case CampaignInteractionType.LoseAnyHerbs: return state.AnyHerbsToLose > 0 ? $"Pay {state.AnyHerbsToLose} herbs of any kind" : "Pay herbs";
                case CampaignInteractionType.GainAnyHerbs: return state.AnyHerbsToGain > 0 ? $"Gain {state.AnyHerbsToGain} herbs of any kind" : "Gain herbs";
                case CampaignInteractionType.LoseMaterials: return "Pay " + Amounts(state.MaterialsToLose, "materials");
                case CampaignInteractionType.GainMaterials: return "Gain " + Amounts(state.MaterialsToGain, "materials");
                case CampaignInteractionType.GainItem: return "Gain an item";
                case CampaignInteractionType.LoseItem: return $"Give up {ItemKind(a.GetString(0))} (you choose which)";
                case CampaignInteractionType.Event_EverybodyChooseLoseUpToGold: return $"Each character may pay up to {n} gold";
                case CampaignInteractionType.Event_EverybodyChooseLoseUpToMaterials: return $"Each character may pay up to {n} resources";
                case CampaignInteractionType.Event_EverybodyChooseLoseGold: return $"Each character may pay {n} gold";
                case CampaignInteractionType.Event_LoseMaterialsEach: return $"Each character pays {n} material{(n == 1 ? "" : "s")} of any kind";
                case CampaignInteractionType.Event_GainAnyMaterialsEach: return $"Each character gains {n} materials of any kind";
                case CampaignInteractionType.Event_LoseAnySnowthistle: return "Give up any amount of snowthistle";
                case CampaignInteractionType.Event_BuyItemsDiscountOptional: return $"Each character may buy one item for {state.BuyItemGoldDiscount} gold less";
                case CampaignInteractionType.Event_GainOneItemForParty: return $"Choose 1 of {state.ItemToGainIDs.Count} items for the party";
                case CampaignInteractionType.Event_TradeLumberForMetal: return "Each character may trade any amount of lumber for the same amount of metal";
                case CampaignInteractionType.Event_SelectBuildingsToProtect: return $"Choose {n} building{(n == 1 ? "" : "s")} to protect from the attack";
                case CampaignInteractionType.Event_LosePet: return "Lose a pet (you choose which)";
                case CampaignInteractionType.SelectClassUnlock: return "Choose a class to unlock";
                case CampaignInteractionType.ChooseUnlockMandatoryScenario: return "Choose a scenario to unlock";
                case CampaignInteractionType.ChooseNextCampaignSection: return "Choose what happens next";
                case CampaignInteractionType.ChooseAttackModifier: return "Choose a town guard attack modifier";
                case CampaignInteractionType.AttackEvent:
                case CampaignInteractionType.ChooseCharacters:
                case CampaignInteractionType.ChooseBuilding:
                case CampaignInteractionType.Event_WR24OptionA: return null;
                default: return Words(a.Type.ToString()) + (n > 0 ? $" ({n})" : "");
            }
        }

        /// <summary>The kind of item a "lose an item" step asks for (its parameter is an ItemFilterFlags name).</summary>
        private static string ItemKind(string filter)
        {
            if (!Enum.TryParse(filter ?? "", out ItemFilterFlags f) || f == ItemFilterFlags.None) return "an item";
            if (f.HasFlag(ItemFilterFlags.Weapon)) return "a weapon";
            if (f.HasFlag(ItemFilterFlags.Potion)) return "a potion";
            if (f.HasFlag(ItemFilterFlags.Small)) return "a small item";
            if (f.HasFlag(ItemFilterFlags.FromRandomDeck)) return "an item from the random item deck";
            return "an item";
        }

        private static string Amounts(OutpostResourceChange c, string fallback)
        {
            var parts = new List<string>();
            if (c.Resources.Lumber > 0) parts.Add($"{c.Resources.Lumber} lumber");
            if (c.Resources.Metal > 0) parts.Add($"{c.Resources.Metal} metal");
            if (c.Resources.Hide > 0) parts.Add($"{c.Resources.Hide} hide");
            if (c.Resources.TotalHerbs > 0) parts.Add($"{c.Resources.TotalHerbs} herbs");
            if (c.AnyMaterial > 0) parts.Add($"{c.AnyMaterial} materials of any kind");
            if (c.AnyHerb > 0) parts.Add($"{c.AnyHerb} herbs of any kind");
            return parts.Count == 0 ? fallback : string.Join(", ", parts);
        }

        /// <summary>The outcome text the game would show after this choice.</summary>
        private static string OutcomeText(OutpostEventState ev)
        {
            try
            {
                if (ev.CustomOutcomeLocKeys != null && ev.CustomOutcomeLocKeys.Count > 0)
                    return NarrationSetting.FromCustomEventOutcome(ev);
                string term = I2.Loc.I2Utils.GetValidTermName(NarrationSetting.FromOutpostEventOutcome(ev).Text);
                return I2.Loc.LocalizationManager.GetTermTranslation("FH/" + term);
            }
            catch (Exception e)
            {
                return "(outcome text unavailable: " + e.Message + ")";
            }
        }

        /// <summary>One line, without the game's size/font/style tags (e.g. "New quest unlocked!" sets the quest name in
        /// a large title font, which overrode the small preview size and shrank the whole option button).</summary>
        private static string Clean(string text)
        {
            text = LayoutTags.Replace(text, "").Replace("\n", " ").Trim();
            // the game's own slips: "buliding", "an item.. Choose", a bold tag never closed ("<b>Black Imp<b>")
            text = text.Replace("buliding", "building").Replace(".. ", ". ");
            return UnclosedBold.Replace(text, "<b>$1</b>");
        }

        private static readonly System.Text.RegularExpressions.Regex UnclosedBold = new System.Text.RegularExpressions.Regex("<b>([^<]*)<b>");

        private static readonly System.Text.RegularExpressions.Regex LayoutTags = new System.Text.RegularExpressions.Regex(
            "</?(size|font|style|voffset|line-height|cspace|mspace|allcaps|uppercase|lowercase|smallcaps|align|indent|margin|pos|width)(=[^>]*)?>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>"Event_EverybodyChooseLoseUpToGold" -> "everybody choose lose up to gold".</summary>
        private static string Words(string name)
        {
            name = name.StartsWith("Event_") ? name.Substring(6) : name;
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (c == '_') { sb.Append(' '); continue; }
                if (char.IsUpper(c) && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                sb.Append(sb.Length == 0 ? c : char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        // ---- the outcome text on hover over each option button

        [HarmonyPatch(typeof(OutpostEventModule), "PopulateOptions")]
        [HarmonyPostfix]
        private static void AddTooltips(OutpostEventModule __instance, List<FHButton> ___m_Buttons)
        {
            GiveResourcesPatches.Safe("add the event outcome tooltips", () =>
            {
                OutpostEventDTO data = Traverse.Create(__instance).Property("Data").GetValue<OutpostEventDTO>();
                if (data?.Options == null || ___m_Buttons == null) return;
                bool on = ModRules.IsOn(ModRules.RevealEvents);
                for (int i = 0; i < ___m_Buttons.Count; i++)
                {
                    FHButton button = ___m_Buttons[i];
                    if (button == null) continue;
                    var tip = button.GetComponent<UITextTooltipTarget>();
                    Preview p = null;
                    bool has = on && i < data.Options.Count && Previews.TryGetValue(data.Options[i].ID, out p)
                               && (!string.IsNullOrEmpty(p.Outcome) || !string.IsNullOrEmpty(p.Summary));
                    if (!has) { if (tip != null) tip.enabled = false; continue; }
                    if (tip == null)
                    {
                        tip = button.gameObject.AddComponent<UITextTooltipTarget>();
                        // autoAdjustHeight false, as in all of the game's own text tooltips: for this tooltip type it
                        // means "fit the text" (true keeps the last height, so the framed background collapses).
                        tip.Initialize(UITooltipManager.Corner.Auto, Vector2.zero, false, 560f, 50f, autoAdjustHeight: false, hideBackground: false);
                    }
                    OurTips.Add(button.transform.GetInstanceID());
                    tip.enabled = true;
                    string effects = string.IsNullOrEmpty(p.Summary) ? "" : "<color=#E6D3A3>" + p.Summary + "</color>";
                    tip.SetText(string.IsNullOrEmpty(p.Outcome) ? effects : effects.Length == 0 ? p.Outcome : p.Outcome + "\n\n" + effects);
                }
            });
        }

        // ---- where the tooltip sits and its background, for these tooltips only

        /// <summary>The option buttons that carry an outcome tooltip (transform instance IDs).</summary>
        private static readonly HashSet<int> OurTips = new HashSet<int>();

        private static readonly Color PanelColor = new Color(0.075f, 0.09f, 0.12f, 1f);
        private const float Gap = 8f;

        /// <summary>The game places a tooltip by a corner of the target, so "above the option, left edges lined up"
        /// isn't one of its choices: put it there after the game positions it. The game's framed background is partly
        /// see-through, so an opaque panel goes behind the text; it's switched off again for every other tooltip.</summary>
        [HarmonyPatch(typeof(UITooltipManager), nameof(UITooltipManager.UpdatePositionAndPivot))]
        [HarmonyPostfix]
        private static void PlaceTooltip(UITooltipManager __instance, RectTransform ___m_AnchorToTarget, GameObject ___m_Background)
        {
            try
            {
                bool ours = ___m_AnchorToTarget != null && OurTips.Contains(___m_AnchorToTarget.GetInstanceID());
                SetPanel(___m_Background, ours);
                if (!ours) return;
                var rect = (RectTransform)__instance.transform;
                rect.pivot = Vector2.zero;
                var corners = new Vector3[4];
                ___m_AnchorToTarget.GetWorldCorners(corners); // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
                rect.position = corners[1] + ___m_AnchorToTarget.TransformVector(new Vector3(0f, Gap, 0f));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Reveal event outcomes: could not place the tooltip: " + e.Message);
            }
        }

        private static void SetPanel(GameObject background, bool on)
        {
            if (background == null) return;
            Transform existing = background.transform.Find("FrosthavenMods Opaque");
            if (existing == null)
            {
                if (!on) return;
                var go = new GameObject("FrosthavenMods Opaque", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(background.transform, false);
                go.transform.SetAsFirstSibling(); // over the background's own image, under its frame ornaments
                var r = (RectTransform)go.transform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = r.offsetMax = Vector2.zero;
                var image = go.GetComponent<Image>();
                image.color = PanelColor;
                image.raycastTarget = false;
                existing = go.transform;
            }
            existing.gameObject.SetActive(on);
        }
    }
}
