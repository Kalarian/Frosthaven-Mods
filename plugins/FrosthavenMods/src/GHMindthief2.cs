using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Mindthief's card rules the engine has no data shape for (2e card-accuracy pass, task
    /// wildfury-mindthief). Every rule is keyed on a marker only pack data carries, or on the class Mindthief2ID, so
    /// without the 2nd edition pack this file is inert. Each act logs "GHRule &lt;Rule&gt; card &lt;id&gt; ...".
    ///   - SingleTargetAugment (Psychic Blade top, "your single-target melee attack abilities may target enemies within
    ///     2 hexes"): an augment override marked MiscVars GHSingleTargetOnly is skipped on an attack with more than one
    ///     target, Targeting All / all targets, or an area of effect (CAbilityAttack.Start applies augment overrides
    ///     through CAbility.OverrideAbilityValues).
    ///   - TargetsAdjacent (Domination top, "control two enemies ... that are adjacent to each other"): on an ability
    ///     marked GHTargetsAdjacent, once a target is picked, a further pick that is not adjacent to a picked one is
    ///     refused (SharedAbilityTargeting.TileSelected, where every targeting ability's picks land).
    ///   - AugmentChoice (Domination bottom, "if you play a third AUGMENT, you must discard either of the other two; if
    ///     you discard this card and you have two active AUGMENTS, you must discard one"): Frosthaven's own slot choice
    ///     (Choreographer.Handle_ActiveBonusAugmentSlotChoice) always drops Augments[0] and the slot refresh trims the
    ///     oldest; here the player is asked which one goes (the engine's yes/no box, GHPlague.Ask).
    ///   - AugmentShare (Many as One bottom, marker GHAugmentShare): each round, the first melee attack ability of one of
    ///     her allies gets her active augments for that attack: added to the attacker's augments while the attack runs,
    ///     exactly as the engine's TransferAugments does for a controlled figure (GameState.OverrideCurrentActorForOneAction).
    ///   - PuppetStrings (perk Mindthief208, "you may have the enemy use your attack modifier deck"): the perk's data makes
    ///     every controlled figure use her deck; before a controlled action with an attack the player is asked.
    ///   - KillsByControl (mastery "kill 5 or more enemies with control abilities"): enemies killed by an ability she
    ///     controlled are counted per scenario for the GHMasteries tag FrosthavenMods_KillsByControl.
    /// </summary>
    [HarmonyPatch]
    internal static class GHMindthief2
    {
        internal const string ClassId = "Mindthief2ID";
        // Domination (bottom: the second augment slot and its "discard one" rule): the card AugmentChoice is logged under
        // (2026-11-03: it was logged under the new or the discarded augment's card, so the checker could never tie it to the half)
        private const int DominationCard = 32224;
        private const string PuppetStringsPerk = "Mindthief208";

        private static bool Marked(CAbility a, string key)
        {
            try { return a?.MiscVars != null && a.GetMiscVar(key).GetBool(); }
            catch { return false; }
        }

        private static int CardId(CAbility a)
        {
            try { return a?.GetAbilityBaseCard()?.ID ?? 0; } catch { return 0; }
        }

        internal static string CardName(CBaseCard card) => card == null ? "?" : KeyText(card.Name);

        /// <summary>A card name key in the player's language, or the key itself.</summary>
        internal static string KeyText(string key)
        {
            key = key ?? "";
            string name = null;
            try { name = GLOOM.LocalizationManager.GetTranslation(key.Trim('$')); } catch { }
            return string.IsNullOrEmpty(name) ? key : name;
        }

        private static void Log(string rule, int card, string detail) => Plugin.Log.LogInfo($"GHRule {rule} card {card} {detail}");

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Report(string what, Exception e)
        {
            if (Reported.Add(what)) Plugin.Log.LogError($"Gloomhaven 2e Mindthief: {what}: {e}");
        }

        // ------------------------------------------------------------------ Psychic Blade: single-target augment

        private static bool IsSingleTarget(CAbility attack) =>
            attack.AreaEffect == null && !attack.AllTargets && attack.NumberTargets == 1
            && attack.Targeting != CAbility.EAbilityTargeting.All && attack.Targeting != CAbility.EAbilityTargeting.Room;

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.OverrideAbilityValues))]
        [HarmonyPrefix]
        private static bool SingleTargetAugment(CAbility __instance, CAbilityOverride abilityOverride, ref CAbilityOverride __result)
        {
            try
            {
                if (!(__instance is CAbilityAttack attack) || abilityOverride?.MiscVars == null) return true;
                if (!abilityOverride.MiscVars.Any(v => v.Item1 == "GHSingleTargetOnly")) return true;
                if (IsSingleTarget(attack)) return true;
                __result = null;
                // (2026-11-03: OriginalAbility is null on the stored override (the engine sets it only on a later copy), so the
                // card came out 0: the two cards carrying GHSingleTargetOnly are told apart by the attacker's class)
                int card = CardId(abilityOverride.OriginalAbility);
                if (card == 0)
                {
                    string cls = ((attack.TargetingActor ?? GameState.InternalCurrentActor) as CPlayerActor)?.CharacterClass?.ID ?? "";   // (TargetingActor is unset this early: night46 still logged 0)
                    card = cls.StartsWith("Mindthief2") ? 32210 : cls.StartsWith("Cragheart2") ? 32106 : 0;
                }
                Log("SingleTargetAugment", card,
                    $"augment reach skipped on {attack.Name}: {attack.NumberTargets} target(s), targeting {attack.Targeting}{(attack.AreaEffect != null ? ", area" : "")}");
                return false;
            }
            catch (Exception e) { Report("single-target augment", e); return true; }
        }

        // ------------------------------------------------------------------ Domination top: adjacent targets

        [HarmonyPatch(typeof(SharedAbilityTargeting), nameof(SharedAbilityTargeting.TileSelected))]
        [HarmonyPrefix]
        private static bool TargetsAdjacent(CAbility ability, CTile selectedTile, ref List<CActor> actorsToTarget, ref bool __result)
        {
            try
            {
                if (ability == null || selectedTile == null || actorsToTarget == null || actorsToTarget.Count == 0) return true;
                if (!Marked(ability, "GHTargetsAdjacent")) return true;
                CActor picked = selectedTile.LivingActor ?? ScenarioManager.Scenario?.FindActorAt(selectedTile.m_ArrayIndex);
                if (picked == null || actorsToTarget.Contains(picked)) return true;
                if (actorsToTarget.Any(a => a != null && a.IsAdjacentTo(picked))) return true;
                __result = false;
                Log("TargetsAdjacent", CardId(ability), $"{GHPlague.NameOf(picked)} refused: not adjacent to {string.Join(", ", actorsToTarget.Select(GHPlague.NameOf))}");
                return false;
            }
            catch (Exception e) { Report("adjacent targets", e); return true; }
        }

        // ------------------------------------------------------------------ Domination bottom: which augment goes

        private static string AugmentName(CAugment a) => CardName(a?.RegisteredBaseCard);

        private static CAugment AskWhichAugment(CActor me, List<CAugment> options, string help)
        {
            // one yes/no per option but the last: "discard this one?"
            for (int i = 0; i < options.Count - 1; i++)
                if (GHPlague.Ask(me, i == 0, help, "FHMODS_MT_DISCARD", "FHMODS_MT_KEEP", AugmentName(options[i]), AugmentName(options[options.Count - 1])))
                    return options[i];
            return options[options.Count - 1];
        }

        /// <summary>A new augment with all slots full (two or more slots): she picks which active one is discarded.</summary>
        [HarmonyPatch(typeof(CActor), nameof(CActor.AddAugmentOrSong))]
        [HarmonyPrefix]
        private static bool ThirdAugment(CActor __instance, CAbility ability, CActor caster, int? id)
        {
            try
            {
                // id: a saved bonus being restored (CCharacterClass load path): never ask while a save loads
                if (id.HasValue || ability?.Augment == null || __instance?.Class?.ID != ClassId || !ScenarioRuleClient.IsWorkerThread) return true;
                if (__instance.Augments.Contains(ability.Augment) || __instance.Augments.Count < __instance.AugmentSlots || __instance.AugmentSlots == 1) return true;
                List<CAugment> options = __instance.Augments.Where(a => a != null).ToList();
                if (options.Count < 2) return true;   // nothing to choose between (0 slots, a null entry): the engine's own path
                CAugment chosen = AskWhichAugment(__instance, options, "FHMODS_MT_THIRD");
                Log("AugmentChoice", DominationCard, $"new augment {CardName(ability.GetAbilityBaseCard())}: {AugmentName(chosen)} discarded (her choice of {options.Count})");
                __instance.ReplaceAugment(ability, chosen, caster ?? __instance);
                return false;
            }
            catch (Exception e) { Report("third augment", e); return true; }
        }

        /// <summary>Domination's extra slot leaves with two augments active: she picks the one that goes.</summary>
        [HarmonyPatch(typeof(CActor), nameof(CActor.RefreshCharacterSpecialMechanicSlots))]
        [HarmonyPrefix]
        private static void SlotLost(CActor __instance)
        {
            try
            {
                if (__instance?.Class?.ID != ClassId || !ScenarioRuleClient.IsWorkerThread) return;
                while (__instance.Augments.Count > __instance.AugmentSlots && __instance.Augments.Count > 1)
                {
                    List<CAugment> options = __instance.Augments.ToList();
                    CAugment chosen = AskWhichAugment(__instance, options, "FHMODS_MT_SLOT");
                    Log("AugmentChoice", DominationCard, $"augment slot lost: {AugmentName(chosen)} discarded (her choice of {options.Count})");
                    __instance.RemoveAugment(chosen);
                    // RemoveAugment returns quietly when it finds nothing to remove: never loop (and ask) forever; the
                    // engine's own trim (oldest first) then runs
                    if (__instance.Augments.Count >= options.Count) break;
                }
            }
            catch (Exception e) { Report("augment slot lost", e); }
        }

        // ------------------------------------------------------------------ Many as One bottom: augment sharing

        private class Shared { public CActor Ally; public CActor Mindthief; public List<CAugment> Augments; public List<CAbilityCard> Cards; public int Card; }
        private static readonly Dictionary<CAbility, Shared> SharedNow = new Dictionary<CAbility, Shared>();
        private static readonly Dictionary<CActor, int> SharedRound = new Dictionary<CActor, int>();

        private static CActiveBonus ShareBonus(CActor mindthief)
        {
            try
            {
                CCharacterClass cls = (mindthief as CPlayerActor)?.CharacterClass;
                if (cls == null) return null;
                return cls.ActivatedCards.Cast<CBaseCard>().Concat(cls.RoundAbilityCards)
                    .Where(c => c?.ActiveBonuses != null).SelectMany(c => c.ActiveBonuses)
                    .FirstOrDefault(b => b != null && !b.Finished() && !b.Finishing() && Marked(b.Ability, "GHAugmentShare"));
            }
            catch (Exception) { return null; }
        }

        private static void Unshare(CAbility attack)
        {
            if (!SharedNow.TryGetValue(attack, out Shared s)) return;
            SharedNow.Remove(attack);
            foreach (CAugment a in s.Augments) s.Ally.Augments.Remove(a);
            s.Ally.OverrideAugmentSlots(null);
            List<CAbilityCard> temp = s.Ally.Class?.TemporaryCards;
            if (temp != null) foreach (CAbilityCard c in s.Cards) temp.Remove(c);
        }

        [HarmonyPatch(typeof(CAbilityAttack), nameof(CAbilityAttack.Start))]
        [HarmonyPrefix]
        private static void ShareAugments(CAbilityAttack __instance, CActor targetingActor)
        {
            try
            {
                if (__instance == null || targetingActor == null || SharedNow.ContainsKey(__instance)) return;
                if (__instance.IsAttackAbilityWithOverrides || !__instance.IsMeleeAttack) return;
                if (ScenarioManager.Scenario?.PlayerActors == null) return;
                int round = GameState.RoundCount;
                foreach (CPlayerActor me in ScenarioManager.Scenario.PlayerActors)
                {
                    if (me == null || me == targetingActor || me.IsDead || me.Class?.ID != ClassId || me.Augments.Count == 0) continue;
                if (targetingActor.MindControlAbility != null || GameState.OverridingCurrentActor && targetingActor is CEnemyActor) continue;   // a controlled enemy is not an ally
                    if (SharedRound.TryGetValue(me, out int r) && r == round) continue;
                    if (!CActor.AreActorsAllied(me.Type, targetingActor.Type, me, targetingActor)) continue;
                    CActiveBonus bonus = ShareBonus(me);
                    if (bonus == null) continue;
                    SharedRound[me] = round;
                    // stale shares (an attack that never ended) go first
                    foreach (CAbility old in SharedNow.Where(kv => kv.Value.Ally == targetingActor).Select(kv => kv.Key).ToList()) Unshare(old);
                    var s = new Shared { Ally = targetingActor, Mindthief = me, Augments = new List<CAugment>(), Cards = new List<CAbilityCard>(), Card = bonus.BaseCard?.ID ?? 0 };
                    List<CAugment> give = me.Augments.Where(a => a != null && !targetingActor.Augments.Contains(a)).ToList();
                    if (give.Count == 0) return;
                    targetingActor.OverrideAugmentSlots(targetingActor.Augments.Count + give.Count);
                    foreach (CAugment a in give)
                    {
                        if (a.RegisteredBaseCard is CAbilityCard card && targetingActor.Class != null)
                        {
                            targetingActor.Class.AddAugmentBaseCardToTemporaryCards(card);
                            s.Cards.Add(card);
                        }
                        targetingActor.Augments.Add(a);
                        s.Augments.Add(a);
                    }
                    SharedNow[__instance] = s;
                    Log("AugmentShare", s.Card, $"{GHPlague.NameOf(targetingActor)}'s melee attack {__instance.Name} gains {s.Augments.Count} augment(s) of {GHPlague.NameOf(me)}: {string.Join(", ", s.Augments.Select(AugmentName))}");
                    return;
                }
            }
            catch (Exception e) { Report("augment share", e); }
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void ShareEnded(CAbility __instance)
        {
            try
            {
                if (__instance != null && SharedNow.Count > 0) Unshare(__instance);
                ControlKill(__instance);
            }
            catch (Exception e) { Report("ability ended", e); }
        }

        /// <summary>Round end: any share still open (an attack undone or cancelled before it ended) is taken back.</summary>
        [HarmonyPatch(typeof(CClass), nameof(CClass.MarkRoundActiveBonusesFinished))]
        [HarmonyPrefix]
        private static void ShareRoundEnd()
        {
            try { foreach (CAbility a in SharedNow.Keys.ToList()) Unshare(a); }
            catch (Exception e) { Report("augment share round end", e); }
        }

        // ------------------------------------------------------------------ Puppet Strings perk: may use her deck

        private static bool HasPerk(CActor a, string perk) =>
            a is CPlayerActor p && p.CharacterClass?.Perks != null && p.CharacterClass.Perks.Any(x => x != null && x.ID == perk);

        [HarmonyPatch(typeof(GameState), nameof(GameState.OverrideCurrentActorForOneAction))]
        [HarmonyPrefix]
        private static void PuppetStrings(CActor actor, CActor controllingActor, ref bool useControllingActorModifierDeck)
        {
            try
            {
                if (!useControllingActorModifierDeck || controllingActor?.Class?.ID != ClassId || !HasPerk(controllingActor, PuppetStringsPerk)) return;
                CAbilityControlActor control = actor?.MindControlAbility
                    ?? PhaseManager.CurrentPhaseAction?.CurrentPhaseAbility?.m_Ability as CAbilityControlActor;
                List<CAbility> abilities = control?.ControlActorData?.ControlAbilities;
                if (abilities != null && !abilities.Any(a => a is CAbilityAttack || a?.AbilityType == CAbility.EAbilityType.Attack)) return;
                bool hers = GHPlague.Ask(controllingActor, true, "FHMODS_MT_PUPPET", "FHMODS_MT_PUPPET_MINE", "FHMODS_MT_PUPPET_OWN", GHPlague.NameOf(actor));
                useControllingActorModifierDeck = hers;
                Log("PuppetStrings", 0, $"Puppet Strings: {GHPlague.NameOf(actor)}'s controlled attack uses {(hers ? "the Mindthief's" : "its own")} attack modifier deck");
            }
            catch (Exception e) { Report("Puppet Strings", e); }
        }

        // ------------------------------------------------------------------ mastery: kills with control abilities

        private static object _scenario;
        private static readonly Dictionary<string, HashSet<string>> ControlKills = new Dictionary<string, HashSet<string>>();

        private static void NewScenarioCheck()
        {
            object now = ScenarioManager.CurrentScenarioState;
            if (!ReferenceEquals(now, _scenario)) { _scenario = now; ControlKills.Clear(); }
        }

        private static void ControlKill(CAbility ab)
        {
            if (ab == null || ab.ControlledByClassID != ClassId || ab.ActorsTargeted == null || ab.ActorsTargeted.Count == 0) return;
            // "with control abilities": an enemy she controls. The engine sets ControlledByClassID on the abilities her own
            // summons perform when a card grants them (Feedback Loop, Possession, Telepathic Command): those kills are not
            // control-ability kills (re-audit Mindthief2-12)
            if (!(ab.TargetingActor is CEnemyActor)) return;
            CPlayerActor me = ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => p?.Class?.ID == ClassId);
            if (me == null) return;
            NewScenarioCheck();
            if (!ControlKills.TryGetValue(me.ActorGuid, out HashSet<string> set)) ControlKills[me.ActorGuid] = set = new HashSet<string>();
            foreach (CActor t in ab.ActorsTargeted)
            {
                if (t == null || !t.IsDead || CActor.AreActorsAllied(me.Type, t.Type, me, t)) continue;
                if (!set.Add(t.ActorGuid)) continue;
                int saved = GHSavedCounts.Add(me, SlotControlKills);
                Log("KillsByControl", CardId(ab), $"{GHPlague.NameOf(t)} killed by a controlled {ab.AbilityType} ({Math.Max(saved, set.Count)} this scenario)");
            }
        }

        /// <summary>Enemies killed this scenario by abilities the Mindthief controlled (the GHMasteries tag's answer).</summary>
        internal static int KillsByControl()
        {
            NewScenarioCheck();
            int saved = (ScenarioManager.Scenario?.PlayerActors ?? new List<CPlayerActor>()).Concat(ScenarioManager.Scenario?.ExhaustedPlayers ?? new List<CPlayerActor>())
                .Where(p => p?.Class?.ID == ClassId).Select(p => GHSavedCounts.Get(p, SlotControlKills)).DefaultIfEmpty(0).Max();
            return Math.Max(saved, ControlKills.Values.Select(s => s.Count).DefaultIfEmpty(0).Max());
        }

        // Saved with the scenario (GHSavedCounts slots on the Mindthief, so a reload keeps the mastery counts): slot 1 =
        // enemies killed with control abilities; slots 2-9 = fires of each of her 8 augments, in AugmentCards order
        // (Thought Thief).
        internal const int SlotControlKills = 1;
        private static readonly int[] AugmentCards = { 32204, 32206, 32210, 32211, 32219, 32224, 32225, 32229 };

        internal static void SaveAugmentFire(CActor me, int card, int count)
        {
            if (me?.Class?.ID != ClassId) return;
            int i = Array.IndexOf(AugmentCards, card);
            if (i >= 0 && GHSavedCounts.Get(me, 2 + i) < count) GHSavedCounts.Set(me, 2 + i, count);
        }

        /// <summary>The saved augment fires of the Mindthief with this guid (card id -> fires).</summary>
        internal static Dictionary<int, int> SavedAugmentFires(string guid)
        {
            var d = new Dictionary<int, int>();
            CPlayerActor me = GHSavedCounts.Player(guid);
            if (me?.Class?.ID != ClassId) return d;
            for (int i = 0; i < AugmentCards.Length; i++)
            {
                int n = GHSavedCounts.Get(me, 2 + i);
                if (n > 0) d[AugmentCards[i]] = n;
            }
            return d;
        }

        // ------------------------------------------------------------------ our text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_MT_THIRD"] = "A new augment, but both augment slots are full: discard {0}? (No: discard {1} instead.)",
            ["FHMODS_MT_SLOT"] = "Domination leaves: only one augment can stay active. Discard {0}? (No: discard {1} instead.)",
            ["FHMODS_MT_DISCARD"] = "Discard it",
            ["FHMODS_MT_KEEP"] = "Keep it",
            ["FHMODS_MT_PUPPET"] = "Puppet Strings: should {0} use your attack modifier deck for this controlled attack?",
            ["FHMODS_MT_PUPPET_MINE"] = "My deck",
            ["FHMODS_MT_PUPPET_OWN"] = "Its own deck",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_MT_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
