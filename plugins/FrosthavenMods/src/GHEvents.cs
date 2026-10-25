using System;
using System.Collections.Generic;
using System.Linq;
using FHRL;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace FrosthavenMods
{
    /// <summary>
    /// The ported classes' retirement events (tools/gh_events.py): each Gloomhaven class's own city and road events, as
    /// Frosthaven outpost and road events. A class's retirement section enables them (EnableEvent), the way Frosthaven
    /// rewards its Blinkblade or Deepwraith; an option can enable a follow-up event the same way.
    ///
    /// Frosthaven's events are classes (FHRL.OutpostEvents.*_OutpostEventDef) that OutpostEventDef.LoadFromAssembly
    /// finds by reflection in the game's own assembly. The pack's events are data (pack.json "events": ID, deck, and per
    /// option the effects), so one generic definition per event is added to the repository right after the game's own,
    /// before the story sections that enable them are read. Its options apply their effects as a Frosthaven event's
    /// ApplyOption would; a drawn event leaves its deck for good, as the game's do unless their code returns them.
    ///
    /// The text: the event screen asks the game's translation table (I2) for "FH/&lt;ID&gt;_Front", "FH/&lt;ID&gt;_Option_&lt;n&gt;"
    /// and "FH/&lt;ID&gt;_Option_&lt;n&gt;_Outcome" (NarrationSetting, OutpostEventsService), and directly, not through the game's
    /// lookup the packs' other text answers. So the pack's strings for its events are added to the table as real terms,
    /// in every language column (English text), the first time an event is shown, and again if the table was reloaded.
    /// The picture falls back to the game's generic outpost or road art; there is no voice line.
    /// </summary>
    [HarmonyPatch]
    internal static class GHEvents
    {
        /// <summary>One pack event: options 1..n, each a list of effects ([type, params...] from pack.json).</summary>
        internal sealed class PackEventDef : OutpostEventDef
        {
            public string Class;
            public readonly List<JArray[]> Options = new List<JArray[]>();

            public override IEnumerable<Option> GetOptions(CampaignState state, OutpostEventState @event)
            {
                for (int i = 1; i <= Options.Count; i++) yield return new Option { ID = (uint)i };
            }

            public override bool ApplyOption(CampaignState state, OutpostEventState @event, CampaignController controller, List<CampaignEffect> outEffects)
            {
                int i = (int)@event.OutcomeOptionID;
                if (i < 1 || i > Options.Count) return true;
                foreach (JArray e in Options[i - 1])
                {
                    CampaignEffect? effect = Effect(e);
                    if (effect.HasValue) outEffects.Add(effect.Value);
                }
                return true;
            }
        }

        /// <summary>A CampaignEffect from [type, params...]: no params, one or two ints, or one to three strings.</summary>
        internal static CampaignEffect? Effect(JArray e)
        {
            if (e == null || e.Count == 0 || !Enum.TryParse((string)e[0], out CampaignEffectType type))
            {
                Report("bad effect " + e?.ToString(Newtonsoft.Json.Formatting.None));
                return null;
            }
            List<JToken> p = e.Skip(1).ToList();
            if (p.Count == 0) return new CampaignEffect(type);
            if (p.All(x => x.Type == JTokenType.Integer))
            {
                if (p.Count == 1) return new CampaignEffect(type, (int)p[0]);
                if (p.Count == 2) return new CampaignEffect(type, (int)p[0], (int)p[1]);
            }
            if (p.All(x => x.Type == JTokenType.String))
            {
                if (p.Count == 1) return new CampaignEffect(type, (string)p[0]);
                if (p.Count == 2) return new CampaignEffect(type, (string)p[0], (string)p[1]);
                if (p.Count == 3) return new CampaignEffect(type, (string)p[0], (string)p[1], (string)p[2]);
            }
            Report("unsupported effect parameters " + e.ToString(Newtonsoft.Json.Formatting.None));
            return null;
        }

        /// <summary>The installed packs' event IDs (what "Copy without content packs" takes out of a save).</summary>
        internal static List<OutpostEventID> PackEventIDs() =>
            GHClasses.Packs.SelectMany(p => p.Events.OfType<JObject>()).Select(e => (string)e["id"])
                .Where(id => !string.IsNullOrEmpty(id)).Distinct().Select(id => new OutpostEventID(id)).ToList();

        /// <summary>"the Brute's road event" for a pack event; null for anything else.</summary>
        internal static string Describe(OutpostEventID id, DefRepository repo)
        {
            if (!(repo.OutpostEvents.TryGetValue(id, out OutpostEventDef d) && d is PackEventDef p)) return null;
            string where = d.Type == OutpostEventType.Road ? "road" : d.Type == OutpostEventType.Boat ? "boat" : "outpost";
            return $"the {p.Class}'s {where} event {id.ID}";
        }

        [HarmonyPatch(typeof(OutpostEventDef), nameof(OutpostEventDef.LoadFromAssembly))]
        [HarmonyPostfix]
        private static void AddPackEvents(DefRepository repo)
        {
            int n = 0;
            foreach (GHClasses.Pack pack in GHClasses.Packs)
                foreach (JObject e in pack.Events.OfType<JObject>())
                {
                    try
                    {
                        string id = (string)e["id"];
                        if (string.IsNullOrEmpty(id)) continue;
                        var def = new PackEventDef
                        {
                            ID = new OutpostEventID(id),
                            Type = Enum.TryParse((string)e["type"], out OutpostEventType t) ? t : OutpostEventType.Outpost,
                            IsWinter = (bool?)e["winter"] ?? false,
                            Class = (string)e["class"] ?? "?",
                        };
                        foreach (JObject o in (e["options"] as JArray ?? new JArray()).OfType<JObject>())
                            def.Options.Add((o["effects"] as JArray ?? new JArray()).OfType<JArray>().ToArray());
                        if (repo.OutpostEvents.ContainsKey(def.ID) && !(repo.OutpostEvents[def.ID] is PackEventDef))
                        {
                            Plugin.Log.LogError($"Content packs: {pack} event {id} clashes with one of the game's own; skipped");
                            continue;
                        }
                        repo.OutpostEvents[def.ID] = def;
                        n++;
                    }
                    catch (Exception ex) { Plugin.Log.LogError($"Content packs: {pack} has a bad event: {ex.Message}"); }
                }
            if (n > 0) Plugin.Log.LogInfo($"Content packs: {n} events added");
        }

        // ------------------------------------------------------------------ text

        private static I2.Loc.LanguageSourceData _textIn;

        /// <summary>Puts the packs' event text into the game's translation table as "FH/" terms (once per loaded table).</summary>
        internal static void EnsureText()
        {
            try
            {
                List<OutpostEventID> ids = PackEventIDs();
                if (ids.Count == 0 || I2.Loc.LocalizationManager.Sources.Count == 0) return;
                string probe = "FH/" + ids[0].ID + "_Front";
                if (_textIn != null && I2.Loc.LocalizationManager.Sources.Contains(_textIn) && _textIn.GetTermData(probe) != null) return;
                // the table that holds the game's own event text
                I2.Loc.LocalizationManager.GetTermData("FH/SO-64_Front", out I2.Loc.LanguageSourceData source);
                source = source ?? I2.Loc.LocalizationManager.Sources[0];
                int added = 0;
                foreach (OutpostEventID id in ids)
                    foreach (KeyValuePair<string, string> kv in GHClasses.StringsStartingWith(id.ID + "_"))
                    {
                        string term = "FH/" + kv.Key;
                        if (source.GetTermData(term) != null) continue;
                        I2.Loc.TermData data = source.AddTerm(term, I2.Loc.eTermType.Text, false);
                        for (int i = 0; i < data.Languages.Length; i++) data.Languages[i] = kv.Value;
                        added++;
                    }
                _textIn = source;
                Plugin.Log.LogInfo($"Content packs: {added} event text terms added to the game's translation table");
            }
            catch (Exception e) { Report("could not add the event text: " + e); }
        }

        [HarmonyPatch(typeof(FHGUI.Services.OutpostEventsService), nameof(FHGUI.Services.OutpostEventsService.GetEventDTO), new[] { typeof(OutpostEventState) })]
        [HarmonyPrefix]
        private static void BeforeEventScreen() => EnsureText();

        [HarmonyPatch(typeof(NarrationSetting), nameof(NarrationSetting.FromOutpostEventOutcome))]
        [HarmonyPrefix]
        private static void BeforeOutcome() => EnsureText();

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Report(string what)
        {
            if (Reported.Add(what)) Plugin.Log.LogError("Content packs: event: " + what);
        }
    }
}
