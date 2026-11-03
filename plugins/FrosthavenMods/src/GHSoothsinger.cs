using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using UnityEngine;
using WorldspaceUI;

namespace FrosthavenMods
{
    /// <summary>
    /// The Gloomhaven 2nd edition Soothsinger's Notes and Songs, as printed on its class mat, cards and perks (GH2e FAQ
    /// 8.13 "Music Note"). Everything here acts only on the 2nd edition pack's markers (the class Soothsinger2ID, its perk
    /// IDs Soothsinger208/209/210, MiscVars GH2E_* on its cards, the modifier AMC_GH2E_Soothsinger2_Fermata_P0): without
    /// the pack it is inert. Notes are the character resources SoothsingerHighNote / SoothsingerLowNote (2 of each at most).
    ///   - Song cost (class mat: "In order to perform a Song, you must have the corresponding notes on your mat"): every
    ///     song top's TSongNotes carries its printed cost (GH2E_SongHigh / GH2E_SongLow). The card hand greys out the song
    ///     top's action while the Notes on the mat don't cover it (the basic Attack 2 stays playable), the engine's own
    ///     per-action switch (FullAbilityCard.ToggleActionInteractivity, as it does for SelectAndPlayCard). Plays through
    ///     Symphonic Medley / Grand Finale bottoms ignore the requirement (their SelectAndPlayCard is running).
    ///   - Paying: a song removes all Notes (the cards' TSongNotes bonus). The Notes that matched the cost are "used in the
    ///     Song"; the rest are wasted (mastery 2, the approved default). Harmonics (Soothsinger209): with a wasted Note, a
    ///     Note of the player's choice right after, outside the once-a-round limit.
    ///   - Song tokens: Grand Finale bottom (GH2E_SkipSongTokens on a NullTargeting) and the Fermata modifier hold every
    ///     active song's token at the end of this round: the song trackers (TSong of the end-of-turn songs, TSongTimer of
    ///     the aura songs) don't advance and award no slot XP this round (all or nothing: GH2e FAQ).
    ///   - Ode to the Gambler (GH2E_OdeRolling aura on you and all allies): each figure's first modifier card drawn in a
    ///     turn, once revealed, may be treated as rolling (the engine's own rolling copy; the figure's player answers).
    ///   - Thrilling Solo (GH2E_ThrillingSolo): right after your first attack ability from an ability card each round,
    ///     the whole ability is repeated with +1 Attack and new targets (Frosthaven's repeat behaviour copy), skippable.
    ///   - Soothing Lullaby bottom (GH2E_DisarmAtTurnEnd on its Disarm): the Disarm is held as a token on the monster
    ///     (shown on its status row) and given at the end of that monster's turn this round; gone at the round's end.
    ///   - Perks: Storyteller (Soothsinger208) 8 XP to every character when its holder completed their battle goal;
    ///     Lullaby (Soothsinger210) when initiative is ordered in a round its holder long rests: a Note of choice and each
    ///     ally character may lower their initiative by 10.
    ///   - Masteries: FrosthavenMods_SSDistinctSongs (Value different songs performed), FrosthavenMods_SSNotesAllUsed
    ///     (Value Notes created, none wasted, none left at the end); the counts are hidden character resources on the
    ///     Soothsinger (GH2ESongsSung bit mask, GH2ENotesMade, GH2ENotesWasted), saved with the scenario.
    ///   - The 2e songs on the status rows (GHStatusIcons asks SongsReaching) and the card face "Song" tag (GHCardStyle).
    /// Every action logs "GHRule &lt;Rule&gt; card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHSoothsinger
    {
        internal const string ClassId = "Soothsinger2ID";
        internal const string High = "SoothsingerHighNote", Low = "SoothsingerLowNote";
        internal const string StorytellerPerk = "Soothsinger208", HarmonicsPerk = "Soothsinger209", LullabyPerk = "Soothsinger210";
        private const string FermataCard = "AMC_GH2E_Soothsinger2_Fermata_P0";
        internal const int SoothingLullabyCard = 33416, OdeCard = 33418, ThrillingSoloCard = 33422, GrandFinaleCard = 33429;
        internal const string DistinctSongsTag = "FrosthavenMods_SSDistinctSongs", NotesAllUsedTag = "FrosthavenMods_SSNotesAllUsed";

        // ------------------------------------------------------------------ helpers

        internal static bool Is(CActor a) => a is CPlayerActor p && p.Class?.ID == ClassId;

        internal static bool HasPerk(CPlayerActor p, string id) =>
            p?.CharacterClass?.Perks != null && p.CharacterClass.Perks.Any(x => x != null && x.ID == id);

        private static int Int(CAbility a, string key)
        {
            try { return a?.MiscVars != null && a.MiscVars.Any(x => x.Item1 == key) ? a.GetMiscVar(key).GetInt() : 0; }
            catch { return 0; }
        }

        internal static bool Marked(CAbility a, string key) => GHPlague.Marked(a, key);

        private static int CardOf(CAbility a) => GHPlague.CardOf(a);

        private static string Guid(CActor a) => a?.ActorGuid ?? "";

        private static int Round => ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0;

        private static ScenarioState _scenario;

        /// <summary>Per-scenario records are dropped when a new scenario state appears (a new scenario or a reload).</summary>
        private static void Fresh()
        {
            ScenarioState s = ScenarioManager.CurrentScenarioState;
            // no scenario state (results screen, map): keep the records (Storyteller must not pay twice)
            if (s == null || ReferenceEquals(s, _scenario)) return;
            _scenario = s;
            SkipRound.Clear(); SoloRound.Clear(); OdeTurn.Clear(); LullabyMarks.Clear(); Held.Clear(); LullabyRound = -1;
            StorytellerDone = false;
        }

        /// <summary>The song half's TSongNotes ability, or null when the card's top isn't a 2e song.</summary>
        internal static CAbility SongNotes(CAction top)
        {
            if (top == null) return null;
            try { return CAbilityCard.GetActionAbilities(top).FirstOrDefault(a => a != null && a.Name == "TSongNotes"); }
            catch { return null; }
        }

        internal static bool IsSongCard(CBaseCard card) => card is CAbilityCard c && c.ID >= 20000 && c.ID < 40000 && SongNotes(c.TopAction) != null;

        private static int Has(CActor a, string note) { try { return a?.GetCharacterResourceAmount(note) ?? 0; } catch { return 0; } }

        private static string NameOf(CActor a) => GHPlague.NameOf(a);

        private static string CardName(CBaseCard c)
        {
            string n = null;
            try { n = GLOOM.LocalizationManager.GetTranslation(c?.Name); } catch { }
            return string.IsNullOrEmpty(n) ? c?.Name ?? "?" : n;
        }

        /// <summary>The player picks a High (true) or a Low Note; with nobody to ask, the kind with fewer Notes.</summary>
        private static bool AskHigh(CActor singer, string helpKey, params object[] args)
        {
            bool fallback = Has(singer, High) <= Has(singer, Low);
            return GHPlague.Ask(singer, fallback, helpKey, "FHMODS_SOOTH_HIGH", "FHMODS_SOOTH_LOW", args);
        }

        private static void GainChosenNote(CActor singer, string rule, int card, string helpKey, params object[] args)
        {
            bool high = AskHigh(singer, helpKey, args);
            string note = high ? High : Low;
            s_FreeGain = true;
            try { singer.AddCharacterResource(note, 1); }
            finally { s_FreeGain = false; }
            Plugin.Log.LogInfo($"GHRule {rule} card {card} {NameOf(singer)} gains a {(high ? "High" : "Low")} Note (High {Has(singer, High)}, Low {Has(singer, Low)})");
        }

        [ThreadStatic] private static bool s_FreeGain;

        // ------------------------------------------------------------------ song cost: the card hand

        private static readonly AccessTools.FieldRef<CardsActionControlller, FullAbilityCard> FirstCard = AccessTools.FieldRefAccess<CardsActionControlller, FullAbilityCard>("_firstCard");
        private static readonly AccessTools.FieldRef<CardsActionControlller, FullAbilityCard> SecondCard = AccessTools.FieldRefAccess<CardsActionControlller, FullAbilityCard>("_secondCard");
        private static readonly AccessTools.FieldRef<CardsActionControlller, FullAbilityCard> ThirdCard = AccessTools.FieldRefAccess<CardsActionControlller, FullAbilityCard>("_thirdCard");
        private static readonly AccessTools.FieldRef<CardsActionControlller, CActor> ControllerActor = AccessTools.FieldRefAccess<CardsActionControlller, CActor>("_actor");
        private static readonly HashSet<string> CostLogged = new HashSet<string>();

        /// <summary>Whether the Notes on the mat pay for the song top of <paramref name="card"/> (true for any other top).</summary>
        internal static bool Affordable(CActor singer, CAbilityCard card, out int needHigh, out int needLow)
        {
            needHigh = needLow = 0;
            CAbility notes = SongNotes(card?.TopAction);
            if (notes == null) return true;
            needHigh = Int(notes, "GH2E_SongHigh");
            needLow = Int(notes, "GH2E_SongLow");
            return Has(singer, High) >= needHigh && Has(singer, Low) >= needLow;
        }

        [HarmonyPatch(typeof(CardsActionControlller), "DisableForgoneActions")]
        [HarmonyPostfix]
        private static void GreyUnpaidSongs(CardsActionControlller __instance)
        {
            try
            {
                CActor actor = ControllerActor(__instance);
                if (!Is(actor) || GameState.CurrentSelectAndPlayCardAbility != null) return;   // Medley / Grand Finale: requirements ignored
                foreach (FullAbilityCard ui in new[] { FirstCard(__instance), SecondCard(__instance), ThirdCard(__instance) })
                {
                    CAbilityCard card = ui?.AbilityCard;
                    if (card == null || Affordable(actor, card, out int h, out int l)) continue;
                    ui.ToggleActionInteractivity(active: false, CBaseCard.ActionType.TopAction);
                    string key = $"{Round}:{card.ID}:{Has(actor, High)}:{Has(actor, Low)}";
                    if (CostLogged.Add(key))
                        Plugin.Log.LogInfo($"GHRule SongCost card {card.ID} {CardName(card)} top greyed out: needs High {h} Low {l}, has High {Has(actor, High)} Low {Has(actor, Low)}");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth cost")) Plugin.Log.LogError("GHSoothsinger (song cost): " + e); }
        }

        // ------------------------------------------------------------------ paying for a song; Harmonics; the Note ledger

        // the masteries' counts live in hidden character resources on the Soothsinger, saved with the scenario (a reload, a
        // co-op rejoin or a round restart keeps them; a rewind rolls them back with the state): the songs performed as a
        // bit mask (bit = card ID - 33400), the Notes created and the Notes wasted by Songs
        internal const string SongsSungRes = "GH2ESongsSung", NotesMadeRes = "GH2ENotesMade", NotesWastedRes = "GH2ENotesWasted";

        private static void SetRes(CActor a, string id, int value)
        {
            if (ScenarioRuleClient.SRLYML?.CharacterResources?.Any(r => r.ID == id) != true) return;   // pack data missing: never the error screen
            int has = Has(a, id);
            if (has == value) return;
            if (has > 0) a.RemoveCharacterResource(id, has);
            if (value > 0) a.AddCharacterResource(id, value);
        }

        private static int SongsSung(CActor a)
        {
            int mask = Has(a, SongsSungRes), n = 0;
            for (; mask != 0; mask &= mask - 1) n++;
            return n;
        }

        private struct Paying { public CActor Singer; public int Card; public int Unused; }

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.Finish))]
        [HarmonyPrefix]
        private static void SongPaying(CActiveBonus __instance, out Paying __state)
        {
            __state = default;
            try
            {
                if (__instance?.Ability?.Name != "TSongNotes" || !Is(__instance.Actor) || __instance.Finishing() || __instance.Finished()) return;
                Fresh();
                CActor singer = __instance.Actor;
                int card = __instance.BaseCard?.ID ?? 0;
                CAbility notes = __instance.Ability;
                int needH = Int(notes, "GH2E_SongHigh"), needL = Int(notes, "GH2E_SongLow");
                if (needH + needL == 0 && __instance.BaseCard is CAbilityCard ac && SongNotes(ac.TopAction) is CAbility top)
                {
                    needH = Int(top, "GH2E_SongHigh");
                    needL = Int(top, "GH2E_SongLow");
                }
                int h = Has(singer, High), l = Has(singer, Low);
                int paidH = Math.Min(h, needH), paidL = Math.Min(l, needL);
                int unused = h - paidH + l - paidL;
                if (card > 33400 && card < 33431) SetRes(singer, SongsSungRes, Has(singer, SongsSungRes) | (1 << (card - 33400)));
                if (unused > 0) SetRes(singer, NotesWastedRes, Has(singer, NotesWastedRes) + unused);
                bool ignored = GameState.CurrentSelectAndPlayCardAbility != null;
                Plugin.Log.LogInfo($"GHRule SongPay card {card} {CardName(__instance.BaseCard)}: cost High {needH} Low {needL}, mat High {h} Low {l}; "
                                   + $"{paidH + paidL} used, {unused} wasted, all Notes removed{(ignored ? " (played ignoring the Note requirements)" : "")}; "
                                   + $"{SongsSung(singer)} different song(s) this scenario, {Has(singer, NotesWastedRes)} Note(s) wasted so far");
                __state = new Paying { Singer = singer, Card = card, Unused = unused };
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth pay")) Plugin.Log.LogError("GHSoothsinger (song pay): " + e); }
        }

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.Finish))]
        [HarmonyPostfix]
        private static void SongPaid(Paying __state)
        {
            try
            {
                if (__state.Singer == null || __state.Unused <= 0 || !HasPerk(__state.Singer as CPlayerActor, HarmonicsPerk)) return;
                GainChosenNote(__state.Singer, "Harmonics", 0, "FHMODS_SOOTH_HARMONICS", __state.Unused);
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth harmonics")) Plugin.Log.LogError("GHSoothsinger (Harmonics): " + e); }
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.AddCharacterResource))]
        [HarmonyPrefix]
        private static void NoteGaining(CActor __instance, string resourceID, out int __state)
        {
            __state = -1;
            if ((resourceID == High || resourceID == Low) && Is(__instance)) __state = Has(__instance, resourceID);
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.AddCharacterResource))]
        [HarmonyPostfix]
        private static void NoteGained(CActor __instance, string resourceID, int __state)
        {
            try
            {
                if (__state < 0) return;
                Fresh();
                int made = Has(__instance, resourceID) - __state;
                if (made <= 0) return;
                SetRes(__instance, NotesMadeRes, Has(__instance, NotesMadeRes) + made);
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth ledger")) Plugin.Log.LogError("GHSoothsinger (Note ledger): " + e); }
        }

        // ------------------------------------------------------------------ perk Call and Response: "Grant one ally: Move 2 OR Attack 2"

        private const string CallAndResponseCard = "AMC_GH2E_Soothsinger2_GrantAlly_R";

        /// <summary>The perk modifier's grant carries both abilities (AAllyMove, AAllyAttack); a Choose on a modifier stopped
        /// the game (2026-11-01). When the card is used in an attack, before the engine copies and stacks the grant, the
        /// drawer's player picks one and the grant keeps only that ability (a copy: the card's own data is untouched).
        /// Logs "GHRule CallAndResponse card 0 ...".</summary>
        [HarmonyPatch(typeof(CAbilityAttack), "ProcessModifierAbilities")]
        [HarmonyPrefix]
        private static void CallAndResponse(CAbilityAttack __instance, List<CAbilityAttack.AttackModifierAbilityInfo> modifierAbilities)
        {
            try
            {
                if (modifierAbilities == null) return;
                foreach (CAbilityAttack.AttackModifierAbilityInfo info in modifierAbilities)
                {
                    if (info?.Card?.Name != CallAndResponseCard || !(info.Ability is CAbilityControlActor grant)) continue;
                    List<CAbility> both = grant.ControlActorData?.ControlAbilities;
                    if (both == null || both.Count < 2) continue;
                    CActor me = info.TargetingActor ?? __instance.TargetingActor;
                    bool move = GHPlague.Ask(me, true, "FHMODS_SOOTH_CALLRESPONSE", "FHMODS_SOOTH_CALLRESPONSE_MOVE", "FHMODS_SOOTH_CALLRESPONSE_ATTACK");
                    CAbility chosen = both.FirstOrDefault(a => a != null && (move ? a.AbilityType == CAbility.EAbilityType.Move : a.AbilityType == CAbility.EAbilityType.Attack)) ?? both[0];
                    var copy = CAbility.CopyAbility(grant, generateNewID: false) as CAbilityControlActor;
                    if (copy?.ControlActorData == null) continue;
                    copy.ControlActorData.ControlAbilities = new List<CAbility> { chosen };
                    info.Ability = copy;
                    Plugin.Log.LogInfo($"GHRule CallAndResponse card 0 Call and Response drawn by {NameOf(me)}: grant one ally {(move ? "Move 2" : "Attack 2")} (not both)");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth call")) Plugin.Log.LogError("GHSoothsinger (Call and Response): " + e); }
        }

        // ------------------------------------------------------------------ Captivating Performance bottom: EACH active song back

        internal const int CaptivatingCard = 33430;

        /// <summary>"Move your character tokens on each of your active Song abilities backward one slot": the engine's tracker
        /// picker is an "up to" selection (End selection after one card), so whatever the player picked, every song card
        /// with a movable tracker moves (the class's non-song trackers are ImmuneMoveActiveBonusTracker). OnTrackerUpdated
        /// runs on every machine (the picker's result is synced), so all of them widen the list the same way.</summary>
        [HarmonyPatch(typeof(CAbilityMoveActiveBonusTracker), nameof(CAbilityMoveActiveBonusTracker.OnTrackerUpdated))]
        [HarmonyPrefix]
        private static void EverySongBack(CAbilityMoveActiveBonusTracker __instance, ref List<CAbilityCard> selectedCards, int moveStrength)
        {
            try
            {
                if (__instance?.Name != "BTrackersBack" || CardOf(__instance) != CaptivatingCard || !Is(__instance.TargetingActor)) return;
                var all = CAbilityMoveActiveBonusTracker.GetActiveBonusesWithMovableTrackers(__instance.TargetingActor, moveStrength)
                    .Where(b => IsSongCard(b.BaseCard)).Select(b => b.BaseCard as CAbilityCard).Where(c => c != null).Distinct().ToList();
                int picked = selectedCards?.Count ?? 0;
                selectedCards = all;
                foreach (CAbilityCard c in all)
                    Plugin.Log.LogInfo($"GHRule SongTokenBack card {CaptivatingCard} {CardName(c)}: token back one slot (every active song; {picked} picked in the engine's picker)");
                if (all.Count == 0) Plugin.Log.LogInfo($"GHRule SongTokenBack card {CaptivatingCard} no active song token can move back");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth captivating")) Plugin.Log.LogError("GHSoothsinger (Captivating Performance): " + e); }
        }

        // ------------------------------------------------------------------ song tokens held this round (Grand Finale, Fermata)

        private static readonly Dictionary<string, int> SkipRound = new Dictionary<string, int>();
        private static readonly HashSet<string> Held = new HashSet<string>();
        [ThreadStatic] private static CActiveBonus s_Hold;

        private static void SkipTokens(CActor singer, int card, string source)
        {
            Fresh();
            if (!Is(singer)) return;
            SkipRound[Guid(singer)] = Round;
            Plugin.Log.LogInfo($"GHRule SongTokenSkip card {card} {source}: {NameOf(singer)}'s song tokens stay put at the end of round {Round}");
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void MarkerEnded(CAbility __instance)
        {
            try
            {
                if (!Marked(__instance, "GH2E_SkipSongTokens") || !Is(__instance.TargetingActor)) return;
                SkipTokens(__instance.TargetingActor, CardOf(__instance), "Grand Finale bottom");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth skip")) Plugin.Log.LogError("GHSoothsinger (Grand Finale): " + e); }
        }

        [HarmonyPatch(typeof(CAbilityAttack), "ProcessModifierAbilities")]
        [HarmonyPrefix]
        private static void FermataDrawn(CAbilityAttack __instance, List<CAbilityAttack.AttackModifierAbilityInfo> modifierAbilities)
        {
            try
            {
                if (modifierAbilities == null || !modifierAbilities.Any(i => i?.Card?.Name == FermataCard || Marked(i?.Ability, "GH2E_SkipSongTokens"))) return;
                CActor singer = __instance.TargetingActor;
                if (Is(singer) && !(SkipRound.TryGetValue(Guid(singer), out int r) && r == Round && ReferenceEquals(_scenario, ScenarioManager.CurrentScenarioState)))
                    SkipTokens(singer, 0, "Fermata perk");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth fermata")) Plugin.Log.LogError("GHSoothsinger (Fermata): " + e); }
        }

        /// <summary>A Soothsinger's song tracker: TSong (end-of-turn songs) or TSongTimer (aura songs) on a 2e song card.</summary>
        private static bool IsSongTracker(CActiveBonus b) =>
            b != null && b.HasTracker && Is(b.Actor) && (b.Ability?.Name == "TSong" || b.Ability?.Name == "TSongTimer") && IsSongCard(b.BaseCard);

        private static void HoldIfSkipped(CActiveBonus b)
        {
            try
            {
                if (!IsSongTracker(b)) return;
                Fresh();
                if (!SkipRound.TryGetValue(Guid(b.Actor), out int r) || r != Round) return;
                s_Hold = b;
                if (Held.Add($"{Round}:{b.ID}"))
                    Plugin.Log.LogInfo($"GHRule SongTokenSkip card {b.BaseCard?.ID ?? 0} {CardName(b.BaseCard)}: token held this round (slot {b.TrackerIndex})");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth hold")) Plugin.Log.LogError("GHSoothsinger (hold token): " + e); }
        }

        [HarmonyPatch(typeof(CEndTurnAbilityActiveBonus), nameof(CEndTurnAbilityActiveBonus.TriggerAbility))]
        [HarmonyPrefix]
        private static void EndTurnSong(CEndTurnAbilityActiveBonus __instance) => HoldIfSkipped(__instance);

        [HarmonyPatch(typeof(CEndTurnAbilityActiveBonus), nameof(CEndTurnAbilityActiveBonus.TriggerAbility))]
        [HarmonyFinalizer]
        private static void EndTurnSongDone() => s_Hold = null;

        [HarmonyPatch(typeof(CEndRoundAbilityActiveBonus), nameof(CEndRoundAbilityActiveBonus.TriggerAbility))]
        [HarmonyPrefix]
        private static void EndRoundSong(CEndRoundAbilityActiveBonus __instance) => HoldIfSkipped(__instance);

        [HarmonyPatch(typeof(CEndRoundAbilityActiveBonus), nameof(CEndRoundAbilityActiveBonus.TriggerAbility))]
        [HarmonyFinalizer]
        private static void EndRoundSongDone() => s_Hold = null;

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.UpdateXPTracker))]
        [HarmonyPrefix]
        private static bool TrackerMoves(CActiveBonus __instance) => !ReferenceEquals(__instance, s_Hold);

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.FinishIfNoRemaining))]
        [HarmonyPrefix]
        private static bool TrackerEnds(CActiveBonus __instance) => !ReferenceEquals(__instance, s_Hold);

        // ------------------------------------------------------------------ Ode to the Gambler

        private static readonly HashSet<string> OdeTurn = new HashSet<string>();
        [ThreadStatic] private static CActor s_Drawer;
        [ThreadStatic] private static bool s_FirstOfTurn;
        [ThreadStatic] private static AttackModifierYMLData s_Revealed;

        private static string TurnKey(CActor drawer) => $"{Round}:{Guid(GameState.TurnActor ?? GameState.InternalCurrentActor)}:{Guid(drawer)}";

        [HarmonyPatch(typeof(CActor), "DrawAttackModifierCard")]
        [HarmonyPrefix]
        private static void Drawing(CActor actor)
        {
            s_Drawer = actor;
            s_FirstOfTurn = false;
            s_Revealed = null;
            try
            {
                if (actor == null || ScenarioManager.Scenario == null) return;
                Fresh();
                s_FirstOfTurn = OdeTurn.Add(TurnKey(actor));   // the first card this figure draws in this turn
            }
            catch { }
        }

        [HarmonyPatch(typeof(CActor), "DrawAttackModifierCard")]
        [HarmonyFinalizer]
        private static void Drawn() { s_Drawer = null; s_FirstOfTurn = false; s_Revealed = null; }

        [HarmonyPatch(typeof(GameState), nameof(GameState.CacheCurrentModifierChoice))]
        [HarmonyPostfix]
        private static void Revealed(AttackModifierYMLData __0) { if (__0 != null && s_Drawer != null) s_Revealed = __0; }

        [HarmonyPatch(typeof(CActor), nameof(CActor.TreatFirstAttackModifierAsRolling))]
        [HarmonyPrefix]
        private static bool OdeRolling(CActor __instance, ref bool __result)
        {
            try
            {
                if (!s_FirstOfTurn || !ReferenceEquals(__instance, s_Drawer)) return true;
                CActiveBonus ode = __instance.FindApplicableActiveBonuses()?.FirstOrDefault(b => Marked(b?.Ability, "GH2E_OdeRolling"));
                if (ode == null) return true;
                s_FirstOfTurn = false;
                string card = s_Revealed?.MathModifier ?? s_Revealed?.Name ?? "?";
                bool yes = GHPlague.Ask(__instance, false, "FHMODS_SOOTH_ODE", "FHMODS_SOOTH_YES", "FHMODS_SOOTH_NO", NameOf(__instance), card);
                Plugin.Log.LogInfo($"GHRule OdeRolling card {ode.BaseCard?.ID ?? OdeCard} {NameOf(__instance)}'s first modifier this turn ({card}) {(yes ? "treated as rolling" : "kept as drawn")}");
                if (!yes) return true;
                __result = true;
                return false;
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth ode")) Plugin.Log.LogError("GHSoothsinger (Ode to the Gambler): " + e); return true; }
        }

        // ------------------------------------------------------------------ Thrilling Solo

        private static readonly Dictionary<string, int> SoloRound = new Dictionary<string, int>();
        private const string SoloSuffix = "_GHSolo";

        [HarmonyPatch(typeof(CAbilityAttack), nameof(CAbilityAttack.AbilityEnded))]
        [HarmonyPostfix]
        private static void AttackEnded(CAbilityAttack __instance)
        {
            try
            {
                CActor me = __instance?.TargetingActor;
                if (!Is(me) || !__instance.AbilityHasHappened) return;
                if (!(__instance.GetAbilityBaseCard() is CAbilityCard card) || card.ID < 20000 || card.ID >= 40000) return;   // ability cards only
                if (__instance.Name != null && __instance.Name.EndsWith(SoloSuffix, StringComparison.Ordinal)) return;
                Fresh();
                string g = Guid(me);
                if (SoloRound.TryGetValue(g, out int r) && r == Round) return;   // not the first attack ability this round
                SoloRound[g] = Round;
                CActiveBonus solo = me.ActiveBonuses?.FirstOrDefault(b => b != null && !b.Finished() && Marked(b.Ability, "GH2E_ThrillingSolo"));
                if (solo == null || !(PhaseManager.Phase is CPhaseAction action)) return;
                CAbility from = __instance.AbilityInitialState ?? __instance;
                CAbility copy = CAbility.CopyAbility(from, generateNewID: false);
                copy.ActiveBonusYML = __instance.ActiveBonusYML;
                copy.Name = __instance.Name + SoloSuffix;
                copy.AbilityInitialState = __instance.AbilityInitialState;
                copy.TargetingActor = me;
                copy.ParentAbility = __instance.ParentAbility;
                copy.Strength = copy.Strength + 1;
                action.InjectAbility(copy, me, card);
                Plugin.Log.LogInfo($"GHRule ThrillingSolo card {solo.BaseCard?.ID ?? ThrillingSoloCard} repeats {CardName(card)}'s {__instance.Name} with Attack {copy.Strength} (was {from.Strength}), new targets");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth solo")) Plugin.Log.LogError("GHSoothsinger (Thrilling Solo): " + e); }
        }

        // ------------------------------------------------------------------ Soothing Lullaby bottom

        private struct Mark { public int Round; public CActor Caster; public int Card; public int Duration; public EConditionDecTrigger Trigger; }
        private static readonly Dictionary<CActor, Mark> LullabyMarks = new Dictionary<CActor, Mark>();
        [ThreadStatic] private static CAbilityDisarm s_Marking;

        [HarmonyPatch(typeof(CAbilityDisarm), nameof(CAbilityDisarm.ApplyToActor))]
        [HarmonyPrefix]
        private static void DisarmApplying(CAbilityDisarm __instance) => s_Marking = Marked(__instance, "GH2E_DisarmAtTurnEnd") ? __instance : null;

        [HarmonyPatch(typeof(CAbilityDisarm), nameof(CAbilityDisarm.ApplyToActor))]
        [HarmonyFinalizer]
        private static void DisarmApplied() => s_Marking = null;

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyCondition), new[] { typeof(CActor), typeof(CCondition.ENegativeCondition), typeof(int), typeof(EConditionDecTrigger),
            typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        private static bool DisarmLater(CActor __instance, CActor actorApplying, CCondition.ENegativeCondition condition, int duration, EConditionDecTrigger decTrigger)
        {
            CAbilityDisarm marking = s_Marking;
            if (marking == null || condition != CCondition.ENegativeCondition.Disarm) return true;
            try
            {
                Fresh();
                LullabyMarks[__instance] = new Mark { Round = Round, Caster = actorApplying, Card = CardOf(marking), Duration = duration, Trigger = decTrigger };
                Plugin.Log.LogInfo($"GHRule LullabyToken card {CardOf(marking)} token on {NameOf(__instance)}: Disarm at the end of its turn this round (round {Round})");
                ScenarioRuleClient.MessageHandler(new CUpdateWorldspaceConditionsUI_MessageData(__instance));
                return false;
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth lullaby")) Plugin.Log.LogError("GHSoothsinger (Soothing Lullaby): " + e); return true; }
        }

        [HarmonyPatch(typeof(CPhaseEndTurn), MethodType.Constructor)]
        [HarmonyPostfix]
        private static void TurnEnding()
        {
            try
            {
                if (LullabyMarks.Count == 0) return;
                Fresh();
                CActor actor = GameState.InternalCurrentActor;
                foreach (CActor stale in LullabyMarks.Where(kv => kv.Value.Round != Round || kv.Key.IsDead).Select(kv => kv.Key).ToList())
                {
                    LullabyMarks.Remove(stale);
                    ScenarioRuleClient.MessageHandler(new CUpdateWorldspaceConditionsUI_MessageData(stale));
                }
                if (actor == null || !LullabyMarks.TryGetValue(actor, out Mark m)) return;
                LullabyMarks.Remove(actor);
                actor.ApplyCondition(m.Caster, CCondition.ENegativeCondition.Disarm, m.Duration, m.Trigger);
                Plugin.Log.LogInfo($"GHRule LullabyToken card {m.Card} end of {NameOf(actor)}'s turn: token removed, it gains Disarm ({(actor.Tokens.HasKey(CCondition.ENegativeCondition.Disarm) ? "disarmed" : "not disarmed: immune")})");
                ScenarioRuleClient.MessageHandler(new CUpdateWorldspaceConditionsUI_MessageData(actor));
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth lullaby end")) Plugin.Log.LogError("GHSoothsinger (Soothing Lullaby, turn end): " + e); }
        }

        private static readonly ConditionalWeakTable<EffectsBar, object> TokenShown = new ConditionalWeakTable<EffectsBar, object>();
        private const string TokenId = "GHLullabyToken";

        [HarmonyPatch(typeof(ActorBehaviour), nameof(ActorBehaviour.UpdateWorldspaceConditionsUI))]
        [HarmonyPostfix]
        private static void TokenRow(ActorBehaviour __instance)
        {
            try
            {
                CActor actor = __instance.Actor;
                EffectsBar bar = __instance.m_WorldspacePanelUI?.EffectBar;
                if (actor == null || bar == null) return;
                bool marked = LullabyMarks.TryGetValue(actor, out Mark m) && m.Round == Round && ReferenceEquals(_scenario, ScenarioManager.CurrentScenarioState);
                bool shown = TokenShown.TryGetValue(bar, out _);
                if (marked && !shown)
                {
                    Sprite icon = UIInfoTools.Instance.GetCharacterActiveAbilityIcon(ECharacter.Soothsinger.ToString());
                    string title = "<size=+2><color=#" + UIInfoTools.DefInstance.basicTextColor.ToHex() + ">" + Text("FHMODS_SOOTH_TOKEN_TITLE") + "</color></size>";
                    bar.AddCustomEffect(TokenId, icon, title, Text("FHMODS_SOOTH_TOKEN_TEXT"));
                    TokenShown.Add(bar, new object());
                }
                else if (!marked && shown)
                {
                    bar.RemoveCustomEffect(TokenId);
                    TokenShown.Remove(bar);
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth token row")) Plugin.Log.LogWarning("GHSoothsinger (token row): " + e); }
        }

        // ------------------------------------------------------------------ the 2e songs on the status rows

        /// <summary>The 2e songs that reach <paramref name="actor"/>: every song of the Soothsinger herself, and an aura song's
        /// effect on each figure in its aura. One bonus per song card (its TSong).</summary>
        internal static List<CActiveBonus> SongsReaching(CActor actor)
        {
            var list = new List<CActiveBonus>();
            if (actor == null || ScenarioManager.Scenario == null) return list;
            foreach (CPlayerActor singer in ScenarioManager.Scenario.PlayerActors.Where(p => Is(p) && !p.IsDead))
            {
                foreach (IGrouping<int, CActiveBonus> song in singer.ActiveBonuses.Where(b => b != null && !b.Finished() && b.Ability?.Name != null
                             && b.Ability.Name.StartsWith("TSong", StringComparison.Ordinal) && b.Ability.Name != "TSongNotes" && IsSongCard(b.BaseCard))
                             .GroupBy(b => b.BaseCard.ID))
                {
                    bool reaches = ReferenceEquals(actor, singer) || song.Any(b => b.IsAura && b.ValidActorsInRangeOfAura != null && b.ValidActorsInRangeOfAura.Contains(actor));
                    if (reaches) list.Add(song.FirstOrDefault(b => b.Ability.Name == "TSong") ?? song.First());
                }
            }
            return list;
        }

        // ------------------------------------------------------------------ perk Lullaby: initiative ordering

        private static int LullabyRound = -1;

        [HarmonyPatch(typeof(GameState), nameof(GameState.SortIntoInitiativeAndIDOrder))]
        [HarmonyPrefix]
        private static void OrderingInitiative()
        {
            try
            {
                if (PhaseManager.PhaseType != CPhase.PhaseType.MonsterClassesSelectAbilityCards || ScenarioManager.Scenario == null) return;
                Fresh();
                if (LullabyRound == Round) return;
                CPlayerActor singer = ScenarioManager.Scenario.PlayerActors.FirstOrDefault(p => Is(p) && !p.IsDead && HasPerk(p, LullabyPerk)
                                                                                                && (p.IsLongRestSelected || p.CharacterClass.LongRest));
                if (singer == null) return;
                LullabyRound = Round;
                GainChosenNote(singer, "LullabyPerk", 0, "FHMODS_SOOTH_LULLABY_NOTE");
                foreach (CPlayerActor ally in ScenarioManager.Scenario.PlayerActors.Where(p => p != null && !ReferenceEquals(p, singer) && !p.IsDead).ToList())
                {
                    int before = ally.Initiative();
                    bool yes = GHPlague.Ask(ally, true, "FHMODS_SOOTH_LULLABY_INIT", "FHMODS_SOOTH_YES", "FHMODS_SOOTH_NO", NameOf(ally), before, Math.Max(0, before - 10));
                    if (yes) ally.CharacterClass.UpdateInitiativeBonusPreview(-10);
                    Plugin.Log.LogInfo($"GHRule LullabyPerk card 0 {NameOf(ally)} {(yes ? $"lowers initiative by 10 ({before} -> {ally.Initiative()})" : $"keeps initiative {before}")}");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth lullaby perk")) Plugin.Log.LogError("GHSoothsinger (Lullaby perk): " + e); }
        }

        // ------------------------------------------------------------------ perk Storyteller: the scenario's end

        private static bool StorytellerDone;

        private static void Storyteller(EResult result)
        {
            Fresh();
            if (StorytellerDone || ScenarioManager.Scenario == null) return;
            StorytellerDone = true;
            CPlayerActor singer = ScenarioManager.Scenario.AllPlayers.FirstOrDefault(p => Is(p) && HasPerk(p, StorytellerPerk));
            if (singer == null) return;
            if (result != EResult.Win && !singer.CanBattleGoalsCompleteOnLoseScenario()) return;
            var goals = MapRuleLibrary.Adventure.AdventureState.MapState?.CurrentMapScenarioState?.ChosenBattleGoalStates;
            var goal = goals?.FirstOrDefault(t => t != null && t.Item1 == singer.CharacterClass.CharacterID)?.Item2;
            var s = goal?.BattleGoalConditionState;
            bool done = s != null && !s.Failed && (s.NegativeCondition ? s.TotalConditionsAndTargets >= s.CurrentProgress : s.TotalConditionsAndTargets <= s.CurrentProgress);
            if (!done)
            {
                Plugin.Log.LogInfo("GHRule Storyteller card 0 battle goal not completed: no experience");
                return;
            }
            foreach (CPlayerActor p in ScenarioManager.Scenario.AllPlayers.ToList()) p.GainXP(8, "Storyteller");
            Plugin.Log.LogInfo($"GHRule Storyteller card 0 battle goal completed: 8 experience to each of {ScenarioManager.Scenario.AllPlayers.Count} character(s)");
        }

        [HarmonyPatch(typeof(FHGUI.Services.ScenarioResultViewService), nameof(FHGUI.Services.ScenarioResultViewService.GetBattleResultDTO))]
        [HarmonyPrefix]
        private static void BeforeResults(EResult outcome)
        {
            try { Storyteller(outcome); }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth storyteller")) Plugin.Log.LogError("GHSoothsinger (Storyteller): " + e); }
        }

        [HarmonyPatch(typeof(PartyAdventureData), nameof(PartyAdventureData.EndCurrentScenario))]
        [HarmonyPrefix]
        private static void BeforeEnd(EResult result)
        {
            try { Storyteller(result); }
            catch (Exception e) { if (GHClasses.Reported.Add("sooth storyteller")) Plugin.Log.LogError("GHSoothsinger (Storyteller): " + e); }
        }

        // ------------------------------------------------------------------ masteries

        [HarmonyPatch(typeof(MapRuleLibrary.YML.Shared.CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Mastery(MapRuleLibrary.YML.Shared.CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                    ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null) return true;
            bool distinct = tags.Contains(DistinctSongsTag), allUsed = tags.Contains(NotesAllUsedTag);
            if (!distinct && !allUsed) return true;
            try
            {
                Fresh();
                List<CPlayerStats> stats = (comparePlayerStats ?? new List<CPlayerStats>()).Where(x => x != null).ToList();
                string self = stats.SelectMany(x => x.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>())
                    .Select(x => x?.ActionData?.MainActorGUID).FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";
                int need = Math.Max(1, unlockConditionTarget.Value);
                CActor singer = ScenarioManager.Scenario?.AllPlayers?.FirstOrDefault(p => p.ActorGuid == self)
                                ?? ScenarioManager.Scenario?.AllPlayers?.FirstOrDefault(p => Is(p));
                if (distinct)
                {
                    int have = singer != null ? SongsSung(singer) : 0;
                    totalConditions = need;
                    conditionsMet = Math.Min(have, need);
                    __result = have >= need;
                }
                else
                {
                    int made = singer != null ? Has(singer, NotesMadeRes) : 0;
                    int wasted = singer != null ? Has(singer, NotesWastedRes) : 0;
                    int left = singer != null ? Has(singer, High) + Has(singer, Low) : 0;
                    totalConditions = need;
                    conditionsMet = wasted + left == 0 ? Math.Min(made, need) : 0;
                    __result = made >= need && wasted == 0 && left == 0;
                    GHMasteryProgress.Once("SSMastery", $"Perfect Pitch: {made} Notes created, {wasted} wasted by Songs, {left} left{(__result ? ": met" : "")}");
                    return false;
                }
                GHMasteryProgress.Once("SSMastery", $"Medley: {conditionsMet}/{totalConditions} different Songs{(__result ? ": met" : "")}");
            }
            catch (Exception e)
            {
                if (GHClasses.Reported.Add("sooth mastery")) Plugin.Log.LogError("GHSoothsinger (mastery): " + e);
                totalConditions = 1; conditionsMet = 0; __result = false;
            }
            return false;
        }

        // ------------------------------------------------------------------ our text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_SOOTH_HIGH"] = "High Note",
            ["FHMODS_SOOTH_CALLRESPONSE"] = "Call and Response: grant one ally Move 2 or Attack 2?",
            ["FHMODS_SOOTH_CALLRESPONSE_MOVE"] = "Move 2",
            ["FHMODS_SOOTH_CALLRESPONSE_ATTACK"] = "Attack 2",
            ["FHMODS_SOOTH_LOW"] = "Low Note",
            ["FHMODS_SOOTH_YES"] = "Yes",
            ["FHMODS_SOOTH_NO"] = "No",
            ["FHMODS_SOOTH_HARMONICS"] = "Harmonics: the Song removed {0} Note(s) it didn't need. Gain one Note of your choice.",
            ["FHMODS_SOOTH_LULLABY_NOTE"] = "Lullaby: you declared a long rest. Gain one Note of your choice.",
            ["FHMODS_SOOTH_LULLABY_INIT"] = "Lullaby: {0}, decrease your initiative by 10 this round ({1} -> {2})?",
            ["FHMODS_SOOTH_ODE"] = "Ode to the Gambler: {0} drew {1} as the first modifier card this turn. Treat it as rolling?",
            ["FHMODS_SOOTH_TOKEN_TITLE"] = "Soothing Lullaby",
            ["FHMODS_SOOTH_TOKEN_TEXT"] = "The Soothsinger's token: at the end of this enemy's turn this round, the token is removed and it gains Disarm.",
        };

        private static string Text(string key) => Strings.TryGetValue(key, out string t) ? t : key;

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_SOOTH_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
