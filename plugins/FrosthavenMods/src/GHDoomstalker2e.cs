using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.CustomLevels;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Doomstalker's printed rules that Frosthaven's doom and summon code has no shape for (2e card
    /// accuracy pass, task rest-a). Frosthaven's hero summons are AI-driven (HeroSummonYMLData.PlayerControlled defaults
    /// to false), so the summon rules hook the summon's turn:
    ///   - DoomFocus: "Your summons focus on this enemy" (The Hunt Begins bottom 32707, Give Chase top 32712, Walking
    ///     Bastion bottom 32717, Charging Beast bottom 32725). While one of those Dooms is on a living enemy, each of the
    ///     Doomstalker's summons' turns (CHeroSummonActor.ActionSelection) gets the engine's own focus override on that
    ///     enemy (CAIFocusOverrideDetails OverrideFocus on the actor, as CustomAbility.SetFocusOverride builds for monsters;
    ///     temporary, so the next move without it clears it).
    ///   - Heel (perk Doomstalker210): "On each of your summons' turns, you may have them skip their turn and instead
    ///     perform Move +0 treating you as their focus." The Doomstalker's player is asked (the engine's synced yes/no box)
    ///     at the start of each summon turn; yes keeps only the summon's own move, focused on the Doomstalker (benign focus,
    ///     the engine's summon-return shape), no attack.
    ///   - GiveChase (32712 top): "Your summons add +2 Move and Jump to all their move abilities" while that Doom is on a
    ///     living enemy: every move ability the summon performs (its own and granted ones) gets +2 and Jump when the move
    ///     collects its bonuses (CAbilityMove.GetBonuses, run at the move's start and restart).
    ///   - WalkingBastion (32717 bottom): "After one of your summons performs an attack targeting this enemy, triggering
    ///     no more than once each round, grant that summon: Attack 1, Pierce 2. This attack must target the enemy that has
    ///     Doom." After a summon attack ability that targeted the doomed enemy ends, once per round, the summon performs an
    ///     Attack 1 (no base attack added), Pierce 2, whose only valid target is the doomed enemy.
    ///   - HornedHristek (Charging Beast top 32725): "After moving for the first time each round, add +2 Attack to all
    ///     attacks for the round." After the Hristek's first move ability that moved it this round, each attack ability it
    ///     starts later in the round gets +2.
    ///   - ArchinotlExtraTurn (Predator and Prey top 32729): "At initiative 100 each round, this summon performs an
    ///     additional turn." When the turn order runs out (every figure with initiative 1-99 has acted), each living Great
    ///     Archinotl that was not summoned this round takes one more turn (initiative shown as 100 for the rest of the
    ///     round), then the round ends.
    ///   - HeartOfDoom (32730 bottom): "If you discard this card, you must discard all but one active Doom." When the
    ///     Doomstalker's doom slots drop (the AddDoomSlot bonus ends) the player picks the Dooms to keep (asked doom by
    ///     doom; the last one left is kept), instead of the engine dropping the oldest.
    ///   - SlotSequence (Coated Arrows top 32720, Impending End bottom 32724): one tracker, a different effect per slot.
    ///     Coated Arrows: Poison, then Wound, then Immobilize, then Disarm on the next four ranged attacks. Impending End:
    ///     Immobilize on the first attack on the doomed enemy, +2 Attack on the second and the third. The bonus ability
    ///     (marked MiscVars GHSlotSequence) holds the current slot's condition (and, for Impending End, +0 on slot 1).
    /// All rules are gated on the pack's class / summon / card IDs: inert without the 2nd edition pack. Each logs
    /// "GHRule {Rule} card {id} ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHDoomstalker2e
    {
        internal const string ClassId = "Doomstalker2ID";
        private const string HeelPerk = "Doomstalker210";
        private const int HuntBegins = 32707, GiveChase = 32712, WalkingBastion = 32717, CoatedArrows = 32720,
            ImpendingEnd = 32724, ChargingBeast = 32725, PredatorAndPrey = 32729, HeartOfDoom = 32730;
        private static readonly HashSet<int> FocusDooms = new HashSet<int> { HuntBegins, GiveChase, WalkingBastion, ChargingBeast };
        private const string Hristek = "HornedHristekID", Archinotl = "GreatArchinotlID";

        private static int Round => ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0;

        private static void Log(string rule, int card, string detail) => Plugin.Log.LogInfo($"GHRule {rule} card {card} {detail}");

        internal static string NameOf(CActor a)
        {
            if (a == null) return "?";
            try { return GHPlague.NameOf(a); } catch { return a.Class?.ID ?? "?"; }
        }

        private static CPlayerActor SummonerOf(CActor a) =>
            a is CHeroSummonActor s && s.Summoner != null && s.Summoner.Class?.ID == ClassId ? s.Summoner : null;

        /// <summary>The card each of the Doomstalker's active Dooms came from, with the doom target (alive only).</summary>
        private static IEnumerable<int> ActiveDoomCards(CPlayerActor doomstalker)
        {
            if (doomstalker?.Dooms == null || doomstalker.DoomTarget == null || doomstalker.DoomTarget.IsDead) yield break;
            if (!ScenarioManager.Scenario.HasActor(doomstalker.DoomTarget)) yield break;
            foreach (CDoom doom in doomstalker.Dooms.ToList())
            {
                CAbility first = doom?.DoomAbilities?.FirstOrDefault();
                int id = 0;
                try { id = first != null ? doomstalker.FindCardWithAbility(first)?.ID ?? 0 : 0; } catch { }
                if (id != 0) yield return id;
            }
        }

        private static bool HasDoom(CPlayerActor doomstalker, int card) => ActiveDoomCards(doomstalker).Contains(card);

        private static readonly AccessTools.FieldRef<CAbility, Dictionary<CCondition.ENegativeCondition, CAbility>> NegConds =
            AccessTools.FieldRefAccess<CAbility, Dictionary<CCondition.ENegativeCondition, CAbility>>("m_NegativeConditions");

        // ------------------------------------------------------------------ summon turns: focus and Heel

        [HarmonyPatch(typeof(CHeroSummonActor), nameof(CHeroSummonActor.ActionSelection))]
        [HarmonyPostfix]
        private static void SummonTurn(CHeroSummonActor __instance)
        {
            try
            {
                CPlayerActor me = SummonerOf(__instance);
                if (me == null || GameState.CurrentAction?.Action?.Abilities == null) return;
                List<CAbility> abilities = GameState.CurrentAction.Action.Abilities;
                CAbilityMove move = abilities.OfType<CAbilityMove>().FirstOrDefault();

                if (me.CharacterClass?.Perks != null && me.CharacterClass.Perks.Any(p => p != null && p.ID == HeelPerk)
                    && GHPlague.Ask(me, false, "FHMODS_DOOM2_HEEL", "FHMODS_DOOM2_YES", "FHMODS_DOOM2_NO", NameOf(__instance)))
                {
                    abilities.Clear();
                    if (move != null)
                    {
                        SetFocus(move, new CAIFocusOverrideDetails
                        {
                            OverrideType = CAIFocusOverrideDetails.EOverrideType.OverrideFocus,
                            OverrideTargetType = CAIFocusOverrideDetails.EOverrideTargetType.Actor,
                            TargetGUID = me.ActorGuid,
                            IsTemporary = true,
                            FocusBenign = true,
                        });
                        abilities.Add(move);
                    }
                    Log("Heel", 0, $"perk Heel: {NameOf(__instance)} skips its turn and performs Move +0 ({move?.Strength ?? 0}) toward {NameOf(me)}{(move == null ? " (no move: nothing)" : "")}");
                    return;
                }

                int doomCard = ActiveDoomCards(me).FirstOrDefault(FocusDooms.Contains);
                if (doomCard == 0) return;
                CActor target = me.DoomTarget;
                var focus = new CAIFocusOverrideDetails
                {
                    OverrideType = CAIFocusOverrideDetails.EOverrideType.OverrideFocus,
                    OverrideTargetType = CAIFocusOverrideDetails.EOverrideTargetType.Actor,
                    TargetGUID = target.ActorGuid,
                    IsTemporary = true,
                };
                if (move != null) SetFocus(move, focus);
                else
                {
                    ActorState state = ScenarioManager.CurrentScenarioState.ActorStates.FirstOrDefault(a => a.ActorGuid == __instance.ActorGuid);
                    state?.SetAIFocusOverride(focus);
                }
                Log("DoomFocus", doomCard, $"{NameOf(__instance)} focuses on the doomed {NameOf(target)}");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("doom2e summon turn")) Plugin.Log.LogError("Doomstalker (2e) summon turn: " + e); }
        }

        private static void SetFocus(CAbilityMove move, CAIFocusOverrideDetails focus) =>
            AccessTools.Property(typeof(CAbilityMove), nameof(CAbilityMove.AIFocusOverride)).SetValue(move, focus);

        // ------------------------------------------------------------------ Give Chase: +2 Move and Jump

        private static readonly AccessTools.FieldRef<CAbilityMove, bool> MoveJump = AccessTools.FieldRefAccess<CAbilityMove, bool>("m_Jump");
        private static readonly System.Reflection.PropertyInfo ModifiedStrength = AccessTools.Property(typeof(CAbility), "m_ModifiedStrength");

        [HarmonyPatch(typeof(CAbilityMove), nameof(CAbilityMove.GetBonuses))]
        [HarmonyPostfix]
        private static void GiveChaseMove(CAbilityMove __instance, CActor actor)
        {
            try
            {
                CActor mover = actor ?? __instance.TargetingActor;
                CPlayerActor me = SummonerOf(mover);
                if (me == null || !__instance.CanApplyActiveBonusTogglesTo() || !HasDoom(me, GiveChase)) return;
                ModifiedStrength.SetValue(__instance, (int)ModifiedStrength.GetValue(__instance) + 2);
                MoveJump(__instance) = true;
                Log("GiveChase", GiveChase, $"{NameOf(mover)}'s move {__instance.Name}: +2 Move and Jump (now {ModifiedStrength.GetValue(__instance)})");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("doom2e Give Chase")) Plugin.Log.LogError("Doomstalker (2e) Give Chase: " + e); }
        }

        // ------------------------------------------------------------------ ability ends: Walking Bastion, Hristek's move

        private static readonly Dictionary<string, int> BastionRound = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> HristekMovedRound = new Dictionary<string, int>();
        private static readonly ConditionalWeakTable<CAbility, object> Buffed = new ConditionalWeakTable<CAbility, object>();
        private static ScenarioState _scenario;

        private static void NewScenarioCheck()
        {
            ScenarioState s = ScenarioManager.CurrentScenarioState;
            if (ReferenceEquals(s, _scenario)) return;
            _scenario = s;
            BastionRound.Clear();
            HristekMovedRound.Clear();
            ExtraTurnRound.Clear();
            SummonedRound.Clear();
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void Ended(CAbility __instance)
        {
            try
            {
                if (!(__instance?.TargetingActor is CHeroSummonActor summon)) return;
                CPlayerActor me = SummonerOf(summon);
                if (me == null) return;
                NewScenarioCheck();
                if (__instance is CAbilityMove move && move.HasMoved && summon.SummonData?.ID == Hristek)
                {
                    if (!HristekMovedRound.TryGetValue(summon.ActorGuid, out int r) || r != Round)
                    {
                        HristekMovedRound[summon.ActorGuid] = Round;
                        Log("HornedHristek", ChargingBeast, $"{NameOf(summon)} moved for the first time this round: +2 Attack on its attacks for the round");
                    }
                    return;
                }
                if (__instance is CAbilityAttack attack && attack.AbilityHasHappened && HasDoom(me, WalkingBastion))
                {
                    CActor doomed = me.DoomTarget;
                    if (attack.ActorsTargeted == null || !attack.ActorsTargeted.Contains(doomed)) return;
                    if (BastionRound.TryGetValue(me.ActorGuid, out int r) && r == Round) return;
                    if (doomed.IsDead || !ScenarioManager.Scenario.HasActor(doomed) || summon.IsDead || !ScenarioManager.Scenario.HasActor(summon)) return;
                    if (!(PhaseManager.CurrentPhase is CPhaseAction phase)) return;
                    BastionRound[me.ActorGuid] = Round;
                    var grant = (CAbilityAttack)CAbilityAttack.CreateDefaultAttack(1, 1, 1, isMonster: false, "", isMultipass: true, null, null, isDefault: false);
                    grant.Pierce = 2;
                    grant.StrengthIsBase = true;
                    grant.Targeting = CAbility.EAbilityTargeting.Custom;
                    grant.TargetingVars = new List<(string, TValue)>
                    {
                        ("AddValidActorsInRange", new TValue(new List<CActor> { doomed })),
                        ("AddTilesInRange", new TValue(new List<CTile> { doomed.Tile })),
                    };
                    phase.StackNextAbilities(new List<CAbility> { grant }, summon, killAfter: false, stackToNextCurrent: true, copyCurrentActionID: true);
                    Log("WalkingBastion", WalkingBastion, $"{NameOf(summon)} attacked the doomed {NameOf(doomed)}: granted Attack 1, Pierce 2 on it (once this round)");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("doom2e ability end")) Plugin.Log.LogError("Doomstalker (2e) ability end: " + e); }
        }

        [HarmonyPatch(typeof(CAbilityAttack), nameof(CAbilityAttack.Start))]
        [HarmonyPrefix]
        private static void HristekAttack(CAbilityAttack __instance, CActor targetingActor)
        {
            try
            {
                if (!(targetingActor is CHeroSummonActor summon) || summon.SummonData?.ID != Hristek || SummonerOf(summon) == null) return;
                NewScenarioCheck();
                if (!HristekMovedRound.TryGetValue(summon.ActorGuid, out int r) || r != Round) return;
                if (Buffed.TryGetValue(__instance, out _)) return;
                Buffed.Add(__instance, null);
                __instance.Strength += 2;
                Log("HornedHristek", ChargingBeast, $"{NameOf(summon)}'s attack {__instance.Name}: +2 Attack after its first move this round (now {__instance.Strength})");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("doom2e Horned Hristek")) Plugin.Log.LogError("Doomstalker (2e) Horned Hristek: " + e); }
        }

        // ------------------------------------------------------------------ Great Archinotl: an extra turn at initiative 100

        private static readonly Dictionary<string, int> ExtraTurnRound = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> SummonedRound = new Dictionary<string, int>();
        private static readonly System.Reflection.FieldInfo SortedField = AccessTools.Field(typeof(GameState), "s_InitiativeSortedActors");
        private static List<CActor> SortedActors() => SortedField?.GetValue(null) as List<CActor>;

        [HarmonyPatch(typeof(CHeroSummonActor), nameof(CHeroSummonActor.OnSummoned))]
        [HarmonyPostfix]
        private static void Summoned(CHeroSummonActor __instance)
        {
            try
            {
                if (__instance?.SummonData?.ID != Archinotl) return;
                NewScenarioCheck();
                SummonedRound[__instance.ActorGuid] = Round;
            }
            catch { }
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.MoveToNextActor))]
        [HarmonyPrefix]
        private static void ExtraTurn()
        {
            try
            {
                List<CActor> sorted = SortedActors();
                if (sorted == null || ScenarioManager.Scenario?.HeroSummons == null) return;
                // inert unless a 2e Doomstalker's Great Archinotl is out (runs before every turn of every scenario)
                if (!ScenarioManager.Scenario.HeroSummons.Any(s => s != null && s.SummonData?.ID == Archinotl && SummonerOf(s) != null)) return;
                NewScenarioCheck();
                bool someoneLeft = sorted.Any(a => a != null && ScenarioManager.Scenario.HasActor(a) && (!a.Deactivated || !a.SkipTurnWhenDeactivated)
                                                   && (!a.PlayedThisRound || a.HasPendingExtraTurn));
                if (someoneLeft) return;
                CHeroSummonActor next = ScenarioManager.Scenario.HeroSummons.FirstOrDefault(s => s != null && !s.IsDead && s.SummonData?.ID == Archinotl
                    && SummonerOf(s) != null && sorted.Contains(s)
                    && !(ExtraTurnRound.TryGetValue(s.ActorGuid, out int r) && r == Round)
                    && !(SummonedRound.TryGetValue(s.ActorGuid, out int sr) && sr == Round));
                if (next == null) return;
                ExtraTurnRound[next.ActorGuid] = Round;
                next.PlayedThisRound = false;
                Log("ArchinotlExtraTurn", PredatorAndPrey, $"{NameOf(next)} performs its additional turn at initiative 100 (round {Round})");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("doom2e Great Archinotl")) Plugin.Log.LogError("Doomstalker (2e) Great Archinotl: " + e); }
        }

        [HarmonyPatch(typeof(CHeroSummonActor), nameof(CHeroSummonActor.Initiative))]
        [HarmonyPostfix]
        private static void ExtraTurnInitiative(CHeroSummonActor __instance, ref int __result)
        {
            try
            {
                if (__instance?.SummonData?.ID == Archinotl && ExtraTurnRound.TryGetValue(__instance.ActorGuid, out int r) && r == Round
                    && ReferenceEquals(ScenarioManager.CurrentScenarioState, _scenario)) __result = 100;
            }
            catch { }
        }

        // ------------------------------------------------------------------ Heart of Doom: the player keeps one Doom

        [HarmonyPatch(typeof(CActor), nameof(CActor.RefreshCharacterSpecialMechanicSlots))]
        [HarmonyPrefix]
        private static void ChooseDoomsToKeep(CActor __instance)
        {
            try
            {
                if (!(__instance is CPlayerActor me) || me.Class?.ID != ClassId || me.Dooms == null) return;
                int slots = Math.Max(0, me.DoomSlots);
                if (me.Dooms.Count <= slots || slots == 0) return;
                List<CDoom> dooms = me.Dooms.ToList();
                var keep = new List<CDoom>();
                for (int i = 0; i < dooms.Count && keep.Count < slots; i++)
                {
                    int undecided = dooms.Count - i;
                    if (undecided <= slots - keep.Count) { keep.Add(dooms[i]); continue; }
                    if (GHPlague.Ask(me, false, "FHMODS_DOOM2_KEEP", "FHMODS_DOOM2_KEEP_YES", "FHMODS_DOOM2_KEEP_NO", DoomName(me, dooms[i]), slots - keep.Count, dooms.Count))
                        keep.Add(dooms[i]);
                }
                // the engine drops Dooms[0] while there are too many: put the kept ones last
                me.Dooms.Clear();
                me.Dooms.AddRange(dooms.Where(d => !keep.Contains(d)));
                me.Dooms.AddRange(keep);
                Log("HeartOfDoom", HeartOfDoom, $"doom slots now {slots}: kept {string.Join(", ", keep.Select(d => DoomName(me, d)))}; discarded {dooms.Count - keep.Count}");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("doom2e Heart of Doom")) Plugin.Log.LogError("Doomstalker (2e) Heart of Doom: " + e); }
        }

        private static string DoomName(CPlayerActor me, CDoom doom)
        {
            try
            {
                CBaseCard card = me.FindCardWithAbility(doom.DoomAbilities.First());
                string name = card?.Name;
                try { if (!string.IsNullOrEmpty(name)) name = GLOOM.LocalizationManager.GetTranslation(name) ?? name; } catch { }
                return string.IsNullOrEmpty(name) ? $"card {card?.ID}" : name;
            }
            catch { return "a Doom"; }
        }

        // ------------------------------------------------------------------ slot sequences

        private const string SlotTag = "GHSlotSequence";

        private static bool SlotMarked(CActiveBonus b)
        {
            try { return b?.Ability?.MiscVars != null && b.Ability.GetMiscVar(SlotTag).GetBool(); }
            catch { return false; }
        }

        private static CCondition.ENegativeCondition SlotCondition(int card, int index)
        {
            if (card == CoatedArrows)
            {
                switch (index)
                {
                    case 0: return CCondition.ENegativeCondition.Poison;
                    case 1: return CCondition.ENegativeCondition.Wound;
                    case 2: return CCondition.ENegativeCondition.Immobilize;
                    case 3: return CCondition.ENegativeCondition.Disarm;
                }
            }
            if (card == ImpendingEnd && index == 0) return CCondition.ENegativeCondition.Immobilize;
            return CCondition.ENegativeCondition.NA;
        }

        private static int BonusCard(CActiveBonus b)
        {
            try { return b?.BaseCard?.ID ?? 0; } catch { return 0; }
        }

        /// <summary>Puts the current slot's condition (only) on the bonus ability.</summary>
        private static void Sync(CActiveBonus bonus, string when)
        {
            if (!SlotMarked(bonus)) return;
            int card = BonusCard(bonus);
            if (card != CoatedArrows && card != ImpendingEnd) return;
            Dictionary<CCondition.ENegativeCondition, CAbility> conds = NegConds(bonus.Ability);
            if (conds == null) return;
            CCondition.ENegativeCondition want = SlotCondition(card, bonus.TrackerIndex);
            if (conds.Count == (want == CCondition.ENegativeCondition.NA ? 0 : 1) && (want == CCondition.ENegativeCondition.NA || conds.ContainsKey(want))) return;
            conds.Clear();
            if (want != CCondition.ENegativeCondition.NA)
            {
                CAbility.EAbilityType type = CAbility.AbilityTypes.Single(x => x.ToString() == want.ToString());
                conds.Add(want, CAbility.CreateAbility(type, bonus.Ability.AbilityFilter, isMonster: false, true));
            }
            Log("SlotSequence", card, $"slot {bonus.TrackerIndex + 1}: {(want == CCondition.ENegativeCondition.NA ? "no condition" : want.ToString())}{(card == ImpendingEnd ? (bonus.TrackerIndex == 0 ? ", +0 Attack" : ", +2 Attack") : "")} ({when})");
        }

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), "ApplyBuffs")]
        [HarmonyPrefix]
        private static void BeforeBuffAttack(CAttackActiveBonus_BuffAttack __instance)
        {
            try { Sync(__instance.ActiveBonus, "attack"); } catch (Exception e) { if (GHClasses.Reported.Add("doom2e slot sequence")) Plugin.Log.LogError("Doomstalker (2e) slot sequence: " + e); }
        }

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), nameof(CAttackActiveBonus_BuffAttack.OnAttackStart))]
        [HarmonyPrefix]
        private static void BuffAttackStart(CAttackActiveBonus_BuffAttack __instance)
        {
            try { Sync(__instance.ActiveBonus, "attack start"); } catch { }
        }

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffIncomingAttacks), nameof(CAttackActiveBonus_BuffIncomingAttacks.OnBeingAttackedPreDamage))]
        [HarmonyPrefix]
        private static void BeforeIncoming(CAttackActiveBonus_BuffIncomingAttacks __instance)
        {
            try { Sync(__instance.ActiveBonus, "attack on the doomed enemy"); } catch (Exception e) { if (GHClasses.Reported.Add("doom2e slot sequence")) Plugin.Log.LogError("Doomstalker (2e) slot sequence: " + e); }
        }

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffIncomingAttacks), nameof(CAttackActiveBonus_BuffIncomingAttacks.ReferenceStrength))]
        [HarmonyPostfix]
        private static void IncomingStrength(CAttackActiveBonus_BuffIncomingAttacks __instance, ref int __result)
        {
            try
            {
                CActiveBonus b = __instance.ActiveBonus;
                if (__result != 0 && SlotMarked(b) && BonusCard(b) == ImpendingEnd && b.TrackerIndex == 0) __result = 0;
            }
            catch { }
        }

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.UpdateXPTracker))]
        [HarmonyPostfix]
        private static void AfterTracker(CActiveBonus __instance)
        {
            try { Sync(__instance, "slot used"); } catch { }
        }

        // ------------------------------------------------------------------ our text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_DOOM2_HEEL"] = "Heel: have {0} skip its turn and instead perform Move +0 with you as its focus?",
            ["FHMODS_DOOM2_YES"] = "Heel",
            ["FHMODS_DOOM2_NO"] = "Normal turn",
            ["FHMODS_DOOM2_KEEP"] = "Heart of Doom: keep the {0} Doom? ({1} to keep of {2})",
            ["FHMODS_DOOM2_KEEP_YES"] = "Keep",
            ["FHMODS_DOOM2_KEEP_NO"] = "Discard",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_DOOM2_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
