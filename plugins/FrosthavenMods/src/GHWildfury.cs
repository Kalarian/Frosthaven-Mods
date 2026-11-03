using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;
using AStar;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Wildfury's companion rules the engine has no data shape for (2e card-accuracy pass, task
    /// wildfury-mindthief). Every rule is keyed on a marker only pack data carries (MiscVars on the card's ability), on
    /// the Bear's summon class WildfuryBearID or on the class WildfuryID and its perk IDs, so without the 2nd edition
    /// pack this file is inert. Each act logs "GHRule &lt;Rule&gt; card &lt;id&gt; ...".
    ///   - BySummonerFromHex (Invocation of Spirit top: "after each of the Bear's next three attacks ... perform the
    ///     following as if you occupied the same hex as the Bear: Attack 2"): the trigger's added ability, marked
    ///     GHBySummonerFromHex, is stacked for the Wildfury instead of the Bear, with Frosthaven's own "perform from a
    ///     hex" (TargetingOverrideArrayIndex: the engine treats her as standing in the Bear's hex for that ability only;
    ///     the Deathwalker's shadows use it), so range and line of sight count from the Bear (rulebook p21).
    ///   - TargetHealBonus (Invocation of Spirit bottom: "+1 Heal if the target is the Bear"): Frosthaven evaluates a
    ///     non-self conditional override per target only for attacks; for a pack heal it is added here, per healed
    ///     figure, when the override's filter (and requirements) match that figure.
    ///   - RageTokens (Concentrated Rage bottom, marker GHRageTokens): the card's damage tokens are the Wildfury's
    ///     character resource WildfuryRage (saved, on her status row): one per Bear attack (each target is an attack)
    ///     and per Retaliate damage the Bear suffers; a Bear attack with three or more tokens removes three and gets +3
    ///     Attack instead. Tokens go when the card's bonus ends.
    ///   - LoseIfIdle (Blood Hunger bottom, marker GHLoseIfIdle): at the end of a round in which the Bear neither
    ///     attacked nor was attacked, the card's bonuses finish, so the card goes to the lost pile.
    ///   - UnleashedFury (Unleashed Fury top, marker GHDoubleCardBonuses on the commanded attack): every positive Attack
    ///     bonus from an ability card of the Wildfury on that attack counts twice (not the summons' auras, not modifiers).
    ///   - JawsRepeat (Jaws of Death top, marker GHRepeatOnKill on the commanded attack): each time the attack kills,
    ///     the Wildfury gains 1 XP and the commanded action (the control's abilities up to the attack) is performed again.
    ///   - PackBond (perk "Pack Bond", card AMC_GH2E_Wildfury_P2_Bond): the +2 counts as +3 while the Wildfury and the Bear
    ///     are within range 4 of each other (at the game's modifier rewrite step, as GHModifierRewrites does).
    ///   - LickWounds (perk "Lick Wounds", card AMC_GH2E_Wildfury_Heal2Bear_R: "Heal 2, target Bear"): drawn with no Bear
    ///     on the map, the card is drawn without its heal (a copy with no abilities), so the heal's custom target list is
    ///     never empty (the printed card heals the Bear only).
    ///   - ThickHide (perk Wildfury08): a 2x an enemy draws for an attack on the Bear counts as +0.
    ///   - CallOfTheWild (perk Wildfury09, marker GHCallOfTheWild on the commanded move): if the Bear exited a hex
    ///     adjacent to her during the move, the move's teleport sub-ability is offered to her afterwards.
    ///   - LifeBond (perk Wildfury10, marker GHLifeBond): the player chooses X (at least 1), her current hit points go
    ///     down by X (not damage) and the commanded heal heals X; once per long rest (temp var GHLifeBondUsed).
    ///   - CommandKills (mastery "kill 4 or more enemies with Command attacks of 7 or greater"): kills by the Bear's
    ///     commanded attacks with their value before modifiers, for the GHMasteries tag FrosthavenMods_CommandKillsAtValue.
    /// </summary>
    [HarmonyPatch]
    internal static class GHWildfury
    {
        internal const string ClassId = "WildfuryID";
        internal const string BearId = "WildfuryBearID";
        internal const string Rage = "WildfuryRage";
        private const string PackBondCard = "AMC_GH2E_Wildfury_P2_Bond";
        private const string LickWoundsCard = "AMC_GH2E_Wildfury_Heal2Bear_R";
        private const string ThickHidePerk = "Wildfury08";
        private const string LifeBondPerk = "Wildfury10";

        private static readonly AccessTools.FieldRef<CAttackSummary.TargetSummary, int> PreModifier =
            AccessTools.FieldRefAccess<CAttackSummary.TargetSummary, int>("m_PreModifierAttackStrength");

        private static bool Marked(CAbility a, string key)
        {
            try { return a?.MiscVars != null && a.GetMiscVar(key).GetBool(); }
            catch { return false; }
        }

        private static int CardId(CAbility a)
        {
            try { return a?.GetAbilityBaseCard()?.ID ?? 0; } catch { return 0; }
        }

        private static void Log(string rule, int card, string detail) => Plugin.Log.LogInfo($"GHRule {rule} card {card} {detail}");

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Report(string what, Exception e)
        {
            if (Reported.Add(what)) Plugin.Log.LogError($"Gloomhaven 2e Wildfury: {what}: {e}");
        }

        internal static bool IsBear(CActor a) => a is CHeroSummonActor s && s.Class?.ID == BearId;

        internal static CPlayerActor SummonerOf(CActor bear) => (bear as CHeroSummonActor)?.Summoner;

        internal static CHeroSummonActor BearOf(CActor wildfury) =>
            ScenarioManager.Scenario?.HeroSummons?.FirstOrDefault(s => s != null && !s.IsDead && s.Class?.ID == BearId && s.Summoner == wildfury);

        private static bool HasPerk(CActor a, string perk) =>
            a is CPlayerActor p && p.CharacterClass?.Perks != null && p.CharacterClass.Perks.Any(x => x != null && x.ID == perk);

        /// <summary>An active (not finished) bonus of one of her cards whose ability carries the marker, or null.</summary>
        internal static CActiveBonus CardBonus(CActor her, string marker)
        {
            try
            {
                CCharacterClass cls = (her as CPlayerActor)?.CharacterClass;
                if (cls == null) return null;
                return cls.ActivatedCards.Cast<CBaseCard>().Concat(cls.RoundAbilityCards)
                    .Where(c => c?.ActiveBonuses != null).SelectMany(c => c.ActiveBonuses)
                    .FirstOrDefault(b => b != null && !b.Finished() && !b.Finishing() && Marked(b.Ability, marker));
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ per-scenario state

        private static object _scenario;
        private static readonly Dictionary<CActor, int> BearAttackedRound = new Dictionary<CActor, int>();   // bear -> round it attacked or was attacked
        private static readonly List<int> CommandKillValues = new List<int>();
        private static readonly Dictionary<string, int> PendingValue = new Dictionary<string, int>();       // "ability target" -> value before modifiers

        private static void NewScenarioCheck()
        {
            object now = ScenarioManager.CurrentScenarioState;
            if (ReferenceEquals(now, _scenario)) return;
            _scenario = now;
            BearAttackedRound.Clear();
            CommandKillValues.Clear();
            PendingValue.Clear();
            CountedKills.Clear();
        }

        // the mastery's qualifying kills (value 7+ before modifiers, the mastery's Amount) are also kept in a hidden
        // character resource on the Wildfury, saved with the scenario: a reload or a co-op rejoin keeps them
        internal const string Kills7Res = "GH2ECommandKills7";
        private const int MasteryValue = 7;

        private static CPlayerActor Wildfury()
        {
            CScenario sc = ScenarioManager.Scenario;
            return sc?.PlayerActors?.Concat(sc.ExhaustedPlayers ?? new List<CPlayerActor>()).FirstOrDefault(p => p?.Class?.ID == ClassId);
        }

        // ------------------------------------------------------------------ Invocation of Spirit top: the Wildfury attacks from the Bear's hex

        [HarmonyPatch(typeof(CPhaseAction), nameof(CPhaseAction.StackNextAbilities))]
        [HarmonyPrefix]
        private static void BySummonerFromHex(List<CAbility> nextAbilities, ref CActor targetingActor)
        {
            try
            {
                if (nextAbilities == null || nextAbilities.Count == 0 || !IsBear(targetingActor)) return;
                CAbility ab = nextAbilities[0];
                if (!Marked(ab, "GHBySummonerFromHex")) return;
                CPlayerActor her = SummonerOf(targetingActor);
                if (her == null || her.IsDead || !ScenarioManager.Scenario.HasActor(her)) return;
                foreach (CAbility a in nextAbilities) a.TargetingOverrideArrayIndex = targetingActor.ArrayIndex;
                Log("BySummonerFromHex", CardId(ab), $"{ab.Name} performed by {GHPlague.NameOf(her)} as if in the Bear's hex ({targetingActor.ArrayIndex.X},{targetingActor.ArrayIndex.Y})");
                targetingActor = her;
            }
            catch (Exception e) { Report("as if from the Bear's hex", e); }
        }

        // ------------------------------------------------------------------ Invocation of Spirit bottom: per-target heal override

        [HarmonyPatch(typeof(CActor), nameof(CActor.Healed), new[] { typeof(int), typeof(CAbility), typeof(CActor), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(bool), typeof(bool), typeof(bool) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
        [HarmonyPrefix]
        private static void TargetHealBonus(CActor __instance, ref int strength, CAbility healAbility, CActor actorHealing)
        {
            try
            {
                if (strength <= 0 || !(healAbility is CAbilityHeal heal) || heal.ConditionalOverrides == null || heal.ConditionalOverrides.Count == 0) return;
                CBaseCard card = heal.GetAbilityBaseCard();
                if (card == null || card.ID < 20000 || card.ID >= 40000) return;
                CActor healer = actorHealing ?? heal.TargetingActor;
                foreach (CConditionalOverride co in heal.ConditionalOverrides)
                {
                    if (co == null || co.Self || co.AbilityOverrides == null) continue;
                    CVarsContext before = CAbilityFilter.CurrentVarsContext;
                    CAbilityFilter.CurrentVarsContext = heal.VarsContext;
                    bool valid;
                    try
                    {
                        valid = co.Filter != null && co.Filter.IsValidTarget(__instance, healer, heal.IsTargetedAbility, useTargetOriginalType: false, false)
                                && (co.Requirements == null || co.Requirements.MeetsAbilityRequirements(healer, heal))
                                && (co.TargetRequirements == null || co.TargetRequirements.MeetsAbilityRequirements(__instance, heal));
                    }
                    finally { CAbilityFilter.CurrentVarsContext = before; }
                    if (!valid) continue;
                    int add = co.AbilityOverrides.Sum(o => o?.Strength ?? 0);
                    if (add == 0) continue;
                    strength += add;
                    Log("TargetHealBonus", card.ID, $"{heal.Name} on {GHPlague.NameOf(__instance)}: +{add} Heal ({co.Name}), heals {strength}");
                }
            }
            catch (Exception e) { Report("target heal bonus", e); }
        }

        // ------------------------------------------------------------------ the Bear's attacks: Rage tokens, Blood Hunger, mastery values

        [HarmonyPatch(typeof(CAttackSummary.TargetSummary), nameof(CAttackSummary.TargetSummary.DrawAttackModifiers))]
        [HarmonyPrefix]
        private static void BeforeDraw(CAttackSummary.TargetSummary __instance, CActor actor, CAbilityAttack attackAbility)
        {
            try
            {
                NewScenarioCheck();
                CActor target = __instance?.ActorToAttack;
                int round = GameState.RoundCount;
                if (IsBear(target)) BearAttackedRound[target] = round;
                if (!IsBear(actor)) return;
                BearAttackedRound[actor] = round;
                CPlayerActor her = SummonerOf(actor);
                CActiveBonus rage = CardBonus(her, "GHRageTokens");
                if (rage != null)
                {
                    int tokens = her.GetCharacterResourceAmount(Rage);
                    if (tokens >= 3)
                    {
                        bool doubled = Marked(attackAbility, "GHDoubleCardBonuses") || Marked(__instance.OriginalAbility, "GHDoubleCardBonuses");
                        int add = doubled ? 6 : 3;
                        her.RemoveCharacterResource(Rage, 3, true);
                        PreModifier(__instance) += add;
                        __instance.CacheFinalAttackStrength(initial: false, beforeModifiers: true);
                        Log("RageTokens", rage.BaseCard?.ID ?? 0, $"Bear attack on {GHPlague.NameOf(target)}: 3 tokens removed for +{add} Attack{(doubled ? " (doubled by Unleashed Fury)" : "")} ({tokens - 3} left)");
                    }
                    else
                    {
                        her.AddCharacterResource(Rage, 1);
                        Log("RageTokens", rage.BaseCard?.ID ?? 0, $"Bear attack on {GHPlague.NameOf(target)}: token added ({tokens + 1})");
                    }
                }
                // the mastery: the value of a commanded attack before modifiers
                CAbility orig = __instance.OriginalAbility ?? attackAbility;
                if (her != null && (attackAbility?.ControlledByClassID == ClassId || orig?.ControlledByClassID == ClassId) && target != null)
                    PendingValue[(orig?.ID.ToString() ?? "") + " " + target.ActorGuid] = PreModifier(__instance);
            }
            catch (Exception e) { Report("Bear attack", e); }
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyRetaliateToAttack))]
        [HarmonyPrefix]
        private static void BeforeRetaliate(CActor __instance, out int __state) => __state = __instance?.Health ?? 0;

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyRetaliateToAttack))]
        [HarmonyPostfix]
        private static void AfterRetaliate(CActor __instance, int __state)
        {
            try
            {
                if (!IsBear(__instance) || __instance.Health >= __state) return;
                CPlayerActor her = SummonerOf(__instance);
                CActiveBonus rage = CardBonus(her, "GHRageTokens");
                if (rage == null) return;
                her.AddCharacterResource(Rage, 1);
                Log("RageTokens", rage.BaseCard?.ID ?? 0, $"Bear suffered {__state - __instance.Health} retaliate damage: token added ({her.GetCharacterResourceAmount(Rage)})");
            }
            catch (Exception e) { Report("Rage retaliate", e); }
        }

        /// <summary>Concentrated Rage's bonus ends: its tokens go with the card.</summary>
        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.Finish))]
        [HarmonyPostfix]
        private static void RageEnded(CActiveBonus __instance)
        {
            try
            {
                if (!Marked(__instance?.Ability, "GHRageTokens")) return;
                CActor her = __instance.Caster ?? __instance.Actor;
                if (her == null || her.Class?.ID != ClassId) return;
                int tokens = her.GetCharacterResourceAmount(Rage);
                if (tokens > 0) her.RemoveCharacterResource(Rage, tokens, true);
                Log("RageTokens", __instance.BaseCard?.ID ?? 0, $"card left play: {tokens} token(s) removed");
            }
            catch (Exception e) { Report("Rage ended", e); }
        }

        // ------------------------------------------------------------------ Blood Hunger bottom: lost after an idle round

        [HarmonyPatch(typeof(CClass), nameof(CClass.MarkRoundActiveBonusesFinished))]
        [HarmonyPrefix]
        private static void LoseIfIdle(CActor actor)
        {
            try
            {
                if (!(actor is CPlayerActor her) || her.Class?.ID != ClassId) return;
                NewScenarioCheck();
                CActiveBonus marker = CardBonus(her, "GHLoseIfIdle");
                if (marker?.BaseCard == null) return;
                int round = GameState.RoundCount;
                CHeroSummonActor bear = BearOf(her);
                if (bear != null && BearAttackedRound.TryGetValue(bear, out int r) && r == round) return;
                foreach (CActiveBonus b in marker.BaseCard.ActiveBonuses.ToList())
                    if (b != null && !b.Finished()) b.Finish();
                Log("LoseIfIdle", marker.BaseCard.ID, bear == null ? "the Bear is gone: the card goes to the lost pile" : "the Bear neither attacked nor was attacked this round: the card goes to the lost pile");
            }
            catch (Exception e) { Report("Blood Hunger", e); }
        }

        // ------------------------------------------------------------------ Unleashed Fury top: card bonuses count twice

        private static readonly HashSet<string> FuryLogged = new HashSet<string>();

        [HarmonyPatch(typeof(CAttackSummary.TargetSummary), MethodType.Constructor,
            new[] { typeof(CAttackSummary), typeof(CActor), typeof(CAbility), typeof(int) })]
        [HarmonyPostfix]
        private static void UnleashedFury(CAttackSummary.TargetSummary __instance, CActor target, CAbility ability)
        {
            try
            {
                if (!Marked(ability, "GHDoubleCardBonuses") || __instance?.ThisTargetAttackActiveBonuses == null) return;
                CPlayerActor her = SummonerOf(ability.TargetingActor) ?? ability.TargetingActor as CPlayerActor;
                if (her == null) return;
                var defaults = new List<(CActiveBonus b, int v, bool card)>();
                var typed = new List<(CActiveBonus b, int v)>();
                foreach (CActiveBonus b in __instance.ThisTargetAttackActiveBonuses)
                {
                    if (b?.Ability?.ActiveBonusData == null || b.Ability.ActiveBonusData.HasBehaviour(CActiveBonus.EActiveBonusBehaviourType.BuffIncomingAttacks)) continue;
                    if (!b.WillBeValidOnReachTargetActor(ability, target)) continue;
                    if (b.SingleTarget != null && b.SingleTarget != target) continue;
                    int v = b.ReferenceStrength(ability, target);
                    bool card = b.Caster == her && b.BaseCard is CAbilityCard && b.BaseCard.CardType == CBaseCard.ECardType.CharacterAbility;
                    if (b.Ability.ActiveBonusData.AttackType == CAbility.EAttackType.Default) defaults.Add((b, v, card));
                    else if (card && v > 0) typed.Add((b, v));
                }
                int extra = typed.Sum(t => t.v);
                var names = typed.Select(t => t.b.BaseCard?.Name).ToList();
                if (ModRules.StackingAttackBonuses)
                {
                    foreach (var d in defaults.Where(d => d.card && d.v > 0)) { extra += d.v; names.Add(d.b.BaseCard?.Name); }
                }
                else if (defaults.Count > 0)
                {
                    var top = defaults.OrderByDescending(d => d.v).First();   // only the largest plain bonus counts: double it if it's a card's
                    if (top.card && top.v > 0) { extra += top.v; names.Add(top.b.BaseCard?.Name); }
                }
                if (extra <= 0) return;
                __instance.ActiveBonusStrengthBuffs?.Add(extra);
                PreModifier(__instance) += extra;
                __instance.CacheFinalAttackStrength(initial: true, beforeModifiers: true);
                string key = ability.ID + " " + target?.ActorGuid + " " + extra;
                if (!FuryLogged.Add(key)) return;
                if (FuryLogged.Count > 500) { FuryLogged.Clear(); FuryLogged.Add(key); }
                Log("UnleashedFury", CardId(ability), $"card bonuses doubled on the attack on {GHPlague.NameOf(target)}: +{extra} more ({string.Join(" + ", names.Select(GHMindthief2.KeyText))})");
            }
            catch (Exception e) { Report("Unleashed Fury", e); }
        }

        // ------------------------------------------------------------------ ability ends: Jaws of Death, Call of the Wild, mastery kills

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void Ended(CAbility __instance)
        {
            try
            {
                if (__instance == null || !IsBear(__instance.TargetingActor)) return;
                if (__instance is CAbilityAttack) { CommandKills(__instance); JawsRepeat(__instance); }
                else if (__instance is CAbilityMove move) CallOfTheWild(move);
            }
            catch (Exception e) { Report("ability ended", e); }
        }

        private static List<CActor> Killed(CAbility ab) =>
            ab.ActorsTargeted?.Where(t => t != null && t.IsDead && !CActor.AreActorsAllied(ab.TargetingActor.Type, t.Type, ab.TargetingActor, t)).Distinct().ToList() ?? new List<CActor>();

        private static readonly HashSet<string> CountedKills = new HashSet<string>();

        private static void CommandKills(CAbility attack)
        {
            if (attack.ControlledByClassID != ClassId) return;
            NewScenarioCheck();
            foreach (CActor t in Killed(attack))
            {
                string key = attack.ID + " " + t.ActorGuid;
                if (!PendingValue.TryGetValue(key, out int value) || !CountedKills.Add(key)) continue;
                CommandKillValues.Add(value);
                CPlayerActor her = (attack.TargetingActor as CHeroSummonActor)?.Summoner as CPlayerActor ?? Wildfury();
                if (value >= MasteryValue && her != null && ScenarioRuleClient.SRLYML?.CharacterResources?.Any(r => r.ID == Kills7Res) == true)
                    her.AddCharacterResource(Kills7Res, 1);
                Log("CommandKills", CardId(attack), $"{GHPlague.NameOf(t)} killed by a commanded Bear attack of {value} before modifiers ({her?.GetCharacterResourceAmount(Kills7Res) ?? CommandKillValues.Count(v => v >= MasteryValue)} at 7+ this scenario, saved)");
            }
        }

        /// <summary>Kills this scenario by the Bear's commanded attacks whose value before modifiers was at least
        /// <paramref name="amount"/> (the GHMasteries tag's answer).</summary>
        internal static int CommandKillsAtValue(int amount)
        {
            NewScenarioCheck();
            if (amount == MasteryValue && Wildfury() is CPlayerActor her) return her.GetCharacterResourceAmount(Kills7Res);
            return CommandKillValues.Count(v => v >= amount);
        }

        private static void JawsRepeat(CAbility attack)
        {
            if (!Marked(attack, "GHRepeatOnKill")) return;
            List<CActor> killed = Killed(attack);
            if (killed.Count == 0) return;
            CActor bear = attack.TargetingActor;
            CPlayerActor her = SummonerOf(bear);
            CPhaseAction pa = PhaseManager.CurrentPhaseAction;
            if (her == null || pa == null || bear.IsDead) return;
            // "this action": the commanded abilities of the card before this attack in the control (the Move 4), then the
            // attack itself; the performed copies (they carry the card and the control's context), else the control's own
            List<CAbility> control = bear.MindControlAbility?.ControlActorData?.ControlAbilities;
            List<string> names = new List<string>();
            if (control != null)
            {
                int at = control.FindIndex(a => a != null && a.Name == attack.Name);
                if (at > 0) names.AddRange(control.Take(at).Select(a => a?.Name));
            }
            else names.Add("TAllyMove");
            List<CAbility> performed = pa.CurrentAndPreviousPhaseAbilities.Select(x => x?.m_Ability).Where(a => a != null && a.TargetingActor == bear).ToList();
            List<CAbility> repeat = new List<CAbility>();
            foreach (string n in names)
            {
                CAbility done = performed.LastOrDefault(a => a.Name == n) ?? control?.FirstOrDefault(a => a?.Name == n);
                if (done != null) repeat.Add(done);
            }
            repeat.Add(attack);
            List<CAbility> copies = repeat.Select(a =>
            {
                CAbility c = CAbility.CopyAbility(a, generateNewID: false, fullCopy: true);
                c.ControlledByClassID = her.Class.ID;
                if (c.ParentAbilityBaseCard == null) c.ParentAbilityBaseCard = attack.ParentAbilityBaseCard;
                return c;
            }).ToList();
            her.GainXP(1, attack.XPSourceKey);
            pa.StackNextAbilities(copies, bear, killAfter: false, stackToNextCurrent: true, copyCurrentActionID: true);
            Log("JawsRepeat", CardId(attack), $"{string.Join(", ", killed.Select(GHPlague.NameOf))} killed: {GHPlague.NameOf(her)} gains 1 XP and the Bear repeats {string.Join(" + ", copies.Select(c => c.Name))}");
        }

        private static void CallOfTheWild(CAbilityMove move)
        {
            if (!Marked(move, "GHCallOfTheWild")) return;
            CActor bear = move.TargetingActor;
            CPlayerActor her = SummonerOf(bear);
            if (her == null || her.IsDead || move.TilesMoved == null || move.TilesMoved.Count == 0) return;
            // the hexes the Bear left: its start and every hex of the path but the last
            List<Point> left = new List<Point> { move.StartingPoint };
            left.AddRange(move.TilesMoved.Take(move.TilesMoved.Count - 1));
            Point mine = her.ArrayIndex;
            bool exited = left.Any(p => p != mine && CActor.GetDistanceTo(p, mine, viaPathfinding: true) == 1);
            if (!exited)
            {
                Log("CallOfTheWild", 0, "Call of the Wild: the Bear never left a hex adjacent to the Wildfury: no teleport");
                return;
            }
            CAbility teleport = move.SubAbilities?.FirstOrDefault(s => Marked(s, "GHCallOfTheWildTeleport"));
            CPhaseAction pa = PhaseManager.CurrentPhaseAction;
            if (teleport == null || pa == null) return;
            CAbility copy = CAbility.CopyAbility(teleport, generateNewID: false, fullCopy: true);
            pa.StackNextAbilities(new List<CAbility> { copy }, her, killAfter: false, stackToNextCurrent: true, copyCurrentActionID: true);
            Log("CallOfTheWild", 0, "Call of the Wild: the Bear left a hex adjacent to the Wildfury: she may teleport next to it");
        }

        // ------------------------------------------------------------------ Pack Bond and Thick Hide: modifier rewrites

        private static AttackModifierYMLData Rewritten(AttackModifierYMLData card, string math)
        {
            AttackModifierYMLData copy = card.Copy(createCard: true);
            copy.OriginalModifier = card.MathModifier;
            copy.MathModifier = math;
            copy.ModifiedFromModifier = card.Card;
            ScenarioRuleClient.MessageHandler(new CChangeModifier_MessageData(GameState.InternalCurrentActor) { m_OriginalModifier = copy.OriginalModifier });
            return copy;
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.ChangeModifierValues))]
        [HarmonyPostfix]
        private static void ModifierRewrites(CActor actor, List<AttackModifierYMLData> modifiers, CActor currentTarget)
        {
            try
            {
                if (modifiers == null || modifiers.Count == 0 || actor == null) return;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    AttackModifierYMLData card = modifiers[i];
                    if (card == null) continue;
                    if (card.Name == LickWoundsCard && card.Abilities != null && card.Abilities.Count > 0
                        && !(ScenarioManager.Scenario?.HeroSummons?.Any(s => s != null && !s.IsDead && s.Class?.ID == BearId) ?? false))
                    {
                        AttackModifierYMLData copy = card.Copy(createCard: true);
                        copy.Abilities = new List<CAbility>();   // a fresh list: the deck card keeps its heal
                        modifiers[i] = copy;
                        Log("LickWounds", 0, $"Lick Wounds drawn by {GHPlague.NameOf(actor)} with no Bear on the map: the heal has no target and is skipped");
                        continue;
                    }
                    if (card.Name == PackBondCard && card.MathModifier == "+2")
                    {
                        CPlayerActor her = actor as CPlayerActor ?? SummonerOf(actor);
                        CHeroSummonActor bear = her != null ? BearOf(her) : null;
                        int d = bear != null ? SharedAbilityTargeting.GetDistanceBetweenActorsInHexes(bear, her) : -1;
                        if (d >= 0 && d <= 4)
                        {
                            modifiers[i] = Rewritten(card, "+3");
                            Log("PackBond", 0, $"Pack Bond: the Wildfury and the Bear are {d} hexes apart: +2 counts as +3");
                        }
                        continue;
                    }
                    if (card.MathModifier == "*2" && IsBear(currentTarget) && !CActor.AreActorsAllied(actor.Type, currentTarget.Type, actor, currentTarget)
                        && HasPerk(SummonerOf(currentTarget), ThickHidePerk))
                    {
                        modifiers[i] = Rewritten(card, "+0");
                        Log("ThickHide", 0, $"Thick Hide: {GHPlague.NameOf(actor)} drew 2x attacking the Bear: counts as +0");
                    }
                }
            }
            catch (Exception e) { Report("modifier rewrites", e); }
        }

        // ------------------------------------------------------------------ Life Bond perk

        [HarmonyPatch(typeof(GameState), nameof(GameState.QueuePreRestBonuses))]
        [HarmonyPrefix]
        private static void LongRestStarts(CPlayerActor playerActor)
        {
            try
            {
                if (playerActor?.Class?.ID == ClassId && HasPerk(playerActor, LifeBondPerk)) playerActor.SetTempVar("GHLifeBondUsed", new TValue(false));
            }
            catch (Exception e) { Report("Life Bond rest", e); }
        }

        [HarmonyPatch(typeof(CAbilityNull), nameof(CAbilityNull.Start))]
        [HarmonyPostfix]
        private static void LifeBond(CAbilityNull __instance)
        {
            try
            {
                if (!Marked(__instance, "GHLifeBond") || !(__instance.TargetingActor is CPlayerActor her)) return;
                if (her.Health <= 1)
                {
                    // X must be at least 1 and she can't go below 1 hit point: nothing can be paid (the perk's requirement
                    // keeps it from being offered at 1 hit point; this is the safety net)
                    her.SetTempVar("GHLifeBondX", new TValue(0));
                    Log("LifeBond", 0, $"Life Bond: {GHPlague.NameOf(her)} has 1 hit point: X can't be paid, no heal");
                    return;
                }
                int max = Math.Max(1, her.Health - 1);
                int x = 1;
                while (x < max && GHPlague.Ask(her, false, "FHMODS_WF_LIFEBOND", "FHMODS_WF_LIFEBOND_MORE", "FHMODS_WF_LIFEBOND_DONE", x, x + 1, her.Health))
                    x++;
                x = Math.Min(x, Math.Max(0, her.Health - 1));
                if (x < 1) x = 1;
                int before = her.Health;
                her.Health = Math.Max(1, her.Health - x);
                her.SetTempVar("GHLifeBondX", new TValue(x));
                her.SetTempVar("GHLifeBondUsed", new TValue(true));
                Log("LifeBond", 0, $"Life Bond: X = {x}: hit points {before} -> {her.Health} (not damage), the Bear heals {x}");
            }
            catch (Exception e) { Report("Life Bond", e); }
        }

        // ------------------------------------------------------------------ our text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_WF_LIFEBOND"] = "Life Bond: X is {0} (you lose {0} hit points, the Bear heals {0}; you have {2}). Raise X to {1}?",
            ["FHMODS_WF_LIFEBOND_MORE"] = "Raise X",
            ["FHMODS_WF_LIFEBOND_DONE"] = "Done",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_WF_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
