using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;
using Point = AStar.Point;

namespace FrosthavenMods
{
    /// <summary>
    /// The Gloomhaven 2nd edition Plagueherald's Plague tokens, as printed. Plague is the pack character resource
    /// "Plague" (MaxAmount 3: "an enemy may have up to 3"). Plague is not a condition (Gloomhaven 2e FAQ 8.11).
    ///   - Direct give (GHPlagueGive): Frosthaven's ResourcesToGiveToTargets moves a resource out of the giver's own stock,
    ///     so the Plagueherald used to hold a refilled supply of 3. Now any figure that performs a Plague give (the
    ///     Plagueherald, an ally whose attack carries it, a controlled monster's commanded attack) gives the token from
    ///     the class supply: while one of the engine's give sites runs (CAbility.ApplyToActor, CAbilityAttack.AttackActor,
    ///     CAbilityDamage.Perform, the push/pull/fear/teleport target loops) the giver "has" Plague and loses none.
    ///   - The 12-token pool (class mat: "You have access to 12 Plague tokens and if you would ever give an enemy a 13th,
    ///     you must first remove any one previously-placed Plague token"): every Plague added to a figure counts the
    ///     tokens on all living non-player figures; with 12 placed, the Plagueherald's player first picks the token to
    ///     remove (a confirmation per enemy that has Plague: Remove / Next; one candidate = no question). A figure
    ///     already holding 3 takes no more (no token is placed, nothing is removed).
    /// Logs "GHRule PlagueGive card N ..." and "GHRule PlaguePool card N ...". Only the 2nd edition pack has the resource:
    /// without it nothing here acts.
    /// </summary>
    [HarmonyPatch]
    internal static class GHPlague
    {
        internal const string Plague = "Plague";
        internal const int Pool = 12;
        internal const string PlagueheraldClass = "Plagueherald2ID";

        [ThreadStatic] private static CActor s_Giver;
        [ThreadStatic] private static CAbility s_Ability;
        [ThreadStatic] private static bool s_Inside;

        internal static bool Marked(CAbility a, string key)
        {
            try { return a?.MiscVars != null && a.GetMiscVar(key).GetBool(); }
            catch { return false; }
        }

        internal static int CardOf(CAbility a)
        {
            try { return a?.GetAbilityBaseCard()?.ID ?? 0; } catch { return 0; }
        }

        internal static bool GivesPlague(CAbility a) =>
            a?.ResourcesToGiveToTargets != null && a.ResourcesToGiveToTargets.ContainsKey(Plague);

        internal static CPlayerActor Plagueherald() =>
            ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => p?.Class?.ID == PlagueheraldClass && !p.IsDead)
            ?? ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => p?.Class?.ID == PlagueheraldClass);

        internal static string NameOf(CActor a)
        {
            string name = null;
            try { name = GLOOM.LocalizationManager.GetTranslation(a.ActorLocKey()); } catch { }
            if (string.IsNullOrEmpty(name)) name = a?.Class?.ID ?? "?";
            if (a is CEnemyActor e && e.StandeeID > 0) name += " " + e.StandeeID;
            return name;
        }

        internal static int Placed() =>
            ScenarioManager.Scenario?.AllAliveActors?.Where(x => !(x is CPlayerActor)).Sum(x => x.GetCharacterResourceAmount(Plague)) ?? 0;

        /// <summary>The engine's own synchronous yes/no box (network-synced, answered by the asker's player). Only on the
        /// rules thread; anywhere else the default answer is taken.</summary>
        internal static bool Ask(CActor asker, bool fallback, string helpKey, string confirmKey, string cancelKey, params object[] args)
        {
            if (asker == null || !ScenarioRuleClient.IsWorkerThread) return fallback;
            return GameState.ShowGenericConfirmButton(asker, helpKey, confirmKey, null, cancelKey, showHeader: false, helpParams: args);
        }

        // ---- the give scope: the giver holds the class supply while a give site runs ----

        internal struct Scope { public CActor Giver; public CAbility Ability; public bool Set; }

        internal static Scope Enter(CActor giver, CAbility ability)
        {
            var old = new Scope { Giver = s_Giver, Ability = s_Ability, Set = false };
            if (giver != null && GivesPlague(ability))
            {
                s_Giver = giver;
                s_Ability = ability;
                old.Set = true;
            }
            return old;
        }

        internal static void Leave(Scope old)
        {
            if (!old.Set) return;
            s_Giver = old.Giver;
            s_Ability = old.Ability;
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.ApplyToActor))]
        [HarmonyPrefix]
        private static void ApplyEnter(CAbility __instance, out Scope __state) => __state = Enter(__instance.TargetingActor, __instance);

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.ApplyToActor))]
        [HarmonyFinalizer]
        private static void ApplyLeave(Scope __state) => Leave(__state);

        [HarmonyPatch(typeof(CAbilityAttack), "AttackActor")]
        [HarmonyPrefix]
        private static void AttackEnter(CActor targetingActor, CAttackSummary.TargetSummary attackingTargetSummary, out Scope __state) =>
            __state = Enter(targetingActor, attackingTargetSummary?.AttackAbilityWithOverrides);

        [HarmonyPatch(typeof(CAbilityAttack), "AttackActor")]
        [HarmonyFinalizer]
        private static void AttackLeave(Scope __state) => Leave(__state);

        [HarmonyPatch(typeof(CAbilityDamage), nameof(CAbilityDamage.Perform))]
        [HarmonyPrefix]
        private static void DamageEnter(CAbilityDamage __instance, out Scope __state) => __state = Enter(__instance.TargetingActor, __instance);

        [HarmonyPatch(typeof(CAbilityDamage), nameof(CAbilityDamage.Perform))]
        [HarmonyFinalizer]
        private static void DamageLeave(Scope __state) => Leave(__state);

        [HarmonyPatch(typeof(CActor), nameof(CActor.CharacterHasResource))]
        [HarmonyPrefix]
        private static bool GiverHasSupply(CActor __instance, string resourceID, ref bool __result)
        {
            if (resourceID != Plague || s_Giver == null || !ReferenceEquals(__instance, s_Giver)) return true;
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.RemoveCharacterResource))]
        [HarmonyPrefix]
        private static bool GiverKeepsTokens(CActor __instance, string resourceID) =>
            resourceID != Plague || s_Giver == null || !ReferenceEquals(__instance, s_Giver);

        // ---- placing a token: the 12-token pool ----

        [HarmonyPatch(typeof(CActor), nameof(CActor.AddCharacterResource))]
        [HarmonyPrefix]
        private static bool Placing(CActor __instance, string resourceID, int amountToAdd)
        {
            if (resourceID != Plague || s_Inside) return true;
            int before = -1;
            try
            {
                before = __instance.GetCharacterResourceAmount(Plague);
                int card = CardOf(s_Ability);
                for (int i = 0; i < Math.Max(1, amountToAdd); i++)
                    Give(s_Giver, __instance, card, "ability");
            }
            catch (Exception e)
            {
                if (GHClasses.Reported.Add("GHPlague place")) Plugin.Log.LogError("GHPlague (place): " + e);
                // nothing placed yet: let the engine place the token itself rather than lose it
                try { if (before >= 0 && before < 3 && __instance.GetCharacterResourceAmount(Plague) == before) return true; } catch { }
            }
            return false;
        }

        /// <summary>One Plague token from the class supply onto <paramref name="target"/>, keeping the 12-token pool.</summary>
        internal static bool Give(CActor giver, CActor target, int card, string source)
        {
            if (target == null) return false;
            int has = target.GetCharacterResourceAmount(Plague);
            if (has >= 3)
            {
                Plugin.Log.LogInfo($"GHRule PlagueGive card {card} {NameOf(target)} already has 3 Plague: no token placed ({source})");
                return false;
            }
            int placed = Placed();
            if (placed >= Pool) MakeRoom(target, card, placed);
            s_Inside = true;
            try { target.AddCharacterResource(Plague, 1); }
            finally { s_Inside = false; }
            Plugin.Log.LogInfo($"GHRule PlagueGive card {card} {(giver != null ? NameOf(giver) : "?")} gives {NameOf(target)} Plague ({target.GetCharacterResourceAmount(Plague)} on it, {Placed()}/{Pool} placed; {source})");
            return true;
        }

        private static void MakeRoom(CActor target, int card, int placed)
        {
            var candidates = ScenarioManager.Scenario.AllAliveActors
                .Where(x => !(x is CPlayerActor) && x.GetCharacterResourceAmount(Plague) > 0)
                .OrderBy(x => x is CEnemyActor e ? e.StandeeID : 0).ToList();
            if (candidates.Count == 0) return;
            CActor chosen = candidates[0];
            string how = "the only enemy with Plague";
            if (candidates.Count > 1)
            {
                CActor asker = Plagueherald();
                how = "automatic (no player to ask)";
                chosen = candidates.OrderByDescending(x => x.GetCharacterResourceAmount(Plague)).First();
                if (asker != null && ScenarioRuleClient.IsWorkerThread)
                {
                    how = "picked by the player";
                    bool picked = false;
                    for (int round = 0; round < 3 && !picked; round++)
                    {
                        foreach (CActor c in candidates)
                        {
                            if (Ask(asker, true, "FHMODS_PLAGUE_POOL", "FHMODS_PLAGUE_REMOVE", "FHMODS_PLAGUE_NEXT",
                                    NameOf(target), NameOf(c), c.GetCharacterResourceAmount(Plague)))
                            {
                                chosen = c;
                                picked = true;
                                break;
                            }
                        }
                    }
                    if (!picked) { chosen = candidates[candidates.Count - 1]; how = "the last one offered (every offer declined three times)"; }
                }
            }
            chosen.RemoveCharacterResource(Plague, 1, updateWorldSpaceUI: true);
            Plugin.Log.LogInfo($"GHRule PlaguePool card {card} {placed} Plague placed: one removed from {NameOf(chosen)} ({how}) before {NameOf(target)} gains one");
        }

        // ---- our text ----

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_PLAGUE_POOL"] = "All 12 Plague tokens are placed. Before {0} gains Plague, remove one placed token. Remove one from {1} ({2} Plague)?",
            ["FHMODS_PLAGUE_REMOVE"] = "Remove this one",
            ["FHMODS_PLAGUE_NEXT"] = "Next enemy",
            ["FHMODS_PLAGUE_YES"] = "Yes",
            ["FHMODS_PLAGUE_NO"] = "No",
            ["FHMODS_PLAGUE_FESTER"] = "Festering Sores: {0} died with Plague. Discard Festering Sores to have two enemies within range 3 of its hex gain Plague ({1} enemies in range)?",
            ["FHMODS_PLAGUE_SACRIFICE"] = "Unwilling Sacrifice: remove one more Plague from {0} to have one more adjacent enemy suffer damage 1 and gain Plague? ({1} removed so far, up to {2})",
            ["FHMODS_PLAGUE_SACRIFICE_YES"] = "Remove one",
            ["FHMODS_PLAGUE_SACRIFICE_NO"] = "Done",
            ["FHMODS_PLAGUE_INFECTION"] = "Compounding Infection: {0} drew a Curse. Discard Compounding Infection to have it gain Curse, Curse, Plague, Plague?",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_PLAGUE_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }

    /// <summary>The push / pull / fear / teleport target loops are give sites too (GHPlague's give scope).</summary>
    [HarmonyPatch]
    internal static class GHPlagueGiveLoops
    {
        [HarmonyTargetMethods]
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[]
        {
            AccessTools.Method(typeof(CAbilityPush), "CheckNextTarget"),
            AccessTools.Method(typeof(CAbilityPull), "CheckNextTarget"),
            AccessTools.Method(typeof(CAbilityFear), "CheckNextTarget"),
            AccessTools.Method(typeof(CAbilityTeleport2), "CheckNextTarget"),
        };

        [HarmonyPrefix]
        private static void LoopEnter(CAbility __instance, out GHPlague.Scope __state) => __state = GHPlague.Enter(__instance.TargetingActor, __instance);

        [HarmonyFinalizer]
        private static void LoopLeave(GHPlague.Scope __state) => GHPlague.Leave(__state);
    }

    /// <summary>
    /// "For each Plague" (GHPerPlague), on abilities marked MiscVars GH2E_PerPlague:
    ///   - a Damage ability (Virulent Strain bottom, Rot Maggots bottom, Unwilling Sacrifice top, Wilt and Wither top):
    ///     each target suffers the printed damage once per Plague it has (the per-target damage is set when the engine
    ///     builds the target's damage summary, before the damage is dealt);
    ///   - an attack or move aura on enemies (Blistering Buboes bottom, Creeping Lethargy top, Festering Sores bottom):
    ///     the printed -X is applied once per Plague the attacking / moving enemy has when it attacks or moves.
    /// Logs "GHRule PerPlague card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHPerPlague
    {
        internal const string Marker = "GH2E_PerPlague";

        /// <summary>Plague on a damage ability's current target before the damage (Unwilling Sacrifice's "even if this
        /// ability kills it"), by ability.</summary>
        internal static readonly Dictionary<Guid, (CActor target, int plague, Point hex)> LastTarget =
            new Dictionary<Guid, (CActor, int, Point)>();

        [HarmonyPatch(typeof(CAbilityDamage), "UpdateDamageSummary")]
        [HarmonyPostfix]
        private static void PerTargetDamage(CAbilityDamage __instance, bool useCurrentTargets)
        {
            try
            {
                if (!useCurrentTargets || !GHPlague.Marked(__instance, Marker)) return;
                var summary = __instance.CurrentDamagingTargetSummary;
                CActor target = summary?.ActorToAttack;
                if (target == null || summary.AttackAbilityWithOverrides == null) return;
                int n = target.GetCharacterResourceAmount(GHPlague.Plague);
                if (GHPlague.Marked(__instance, "GH2E_UnwillingSacrifice"))
                {
                    if (LastTarget.Count > 64) LastTarget.Clear();   // abilities that never ended (reload, rewind)
                    LastTarget[__instance.ID] = (target, n, target.ArrayIndex);
                }
                int printed = __instance.Strength;
                summary.AttackAbilityWithOverrides.Strength += printed * (n - 1);
                Plugin.Log.LogInfo($"GHRule PerPlague card {GHPlague.CardOf(__instance)} {GHPlague.NameOf(target)} has {n} Plague: damage {printed} x {n} = {summary.AttackAbilityWithOverrides.Strength}");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("GHPerPlague (damage)")) Plugin.Log.LogError("GHPerPlague (damage): " + e); }
        }

        private static string _lastLogged;

        private static int Scale(CBespokeBehaviour b, CAbility ability, int value, string what)
        {
            if (value == 0 || ability?.TargetingActor == null || !GHPlague.Marked(b?.ActiveBonus?.Ability, Marker)) return value;
            int n = ability.TargetingActor.GetCharacterResourceAmount(GHPlague.Plague);
            int scaled = value * n;
            // the engine asks for the bonus many times per attack (previews, summaries): one line per figure and ability
            string key = $"{b.ActiveBonus.ID}/{ability.TargetingActor.ActorGuid}/{ability.ID}/{scaled}";
            if (key == _lastLogged) return scaled;
            _lastLogged = key;
            Plugin.Log.LogInfo($"GHRule PerPlague card {b.ActiveBonus.BaseCard?.ID ?? 0} {GHPlague.NameOf(ability.TargetingActor)} {what}: {value} x {n} Plague = {scaled}");
            return scaled;
        }

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), nameof(CAttackActiveBonus_BuffAttack.ReferenceStrength))]
        [HarmonyPostfix]
        private static void AttackAura(CAttackActiveBonus_BuffAttack __instance, CAbility ability, ref int __result)
        {
            try { __result = Scale(__instance, ability, __result, "attacks"); }
            catch (Exception e) { if (GHClasses.Reported.Add("GHPerPlague (attack aura)")) Plugin.Log.LogError("GHPerPlague (attack aura): " + e); }
        }

        [HarmonyPatch(typeof(CMoveActiveBonus_BuffMove), nameof(CMoveActiveBonus_BuffMove.ReferenceStrength))]
        [HarmonyPostfix]
        private static void MoveAura(CMoveActiveBonus_BuffMove __instance, CAbility ability, ref int __result)
        {
            try { __result = Scale(__instance, ability, __result, "moves"); }
            catch (Exception e) { if (GHClasses.Reported.Add("GHPerPlague (move aura)")) Plugin.Log.LogError("GHPerPlague (move aura): " + e); }
        }
    }

    /// <summary>
    /// Plague added to an ally's attack by an attack bonus marked MiscVars GH2E_GivePlague (Virulent Strain top: "On the
    /// next three attacks performed by one of your allies that has Poison, add +1 Attack, Plague"): when the bonus is
    /// used on an attack (its use slot advances), the attack's target gains one Plague from the class supply.
    /// Logs "GHRule PlagueByAlly card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHPlagueByAlly
    {
        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), nameof(CAttackActiveBonus_BuffAttack.OnAttacking))]
        [HarmonyPrefix]
        private static void Before(CAttackActiveBonus_BuffAttack __instance, out int __state)
        {
            __state = -1;
            try
            {
                if (GHPlague.Marked(__instance.ActiveBonus?.Ability, "GH2E_GivePlague")) __state = __instance.ActiveBonus.Remaining;
            }
            catch { }
        }

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), nameof(CAttackActiveBonus_BuffAttack.OnAttacking))]
        [HarmonyPostfix]
        private static void After(CAttackActiveBonus_BuffAttack __instance, CAbilityAttack attackAbility, CActor target, int __state)
        {
            try
            {
                if (__state < 0 || target == null) return;
                CActiveBonus bonus = __instance.ActiveBonus;
                // a use slot spent on this attack (a bonus with no slot left is only finished by the engine: not a use)
                bool used = __state > 0 && bonus.Remaining < __state;
                if (!used) return;
                int card = bonus.BaseCard?.ID ?? 0;
                Plugin.Log.LogInfo($"GHRule PlagueByAlly card {card} {GHPlague.NameOf(attackAbility?.TargetingActor)}'s attack on {GHPlague.NameOf(target)} adds Plague");
                GHPlague.Give(attackAbility?.TargetingActor, target, card, "ally's attack bonus");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("GHPlagueByAlly")) Plugin.Log.LogError("GHPlagueByAlly: " + e); }
        }
    }

    /// <summary>
    /// Festering Sores top (MiscVars GH2E_FesteringSores on the DuringActionAbilityOnDeath bonus): "When an enemy that has
    /// Plague dies, you may discard this card to have two enemies within Range 3 of the hex where it died gain Plague."
    /// The Plagueherald's player is asked (no enemy within range 3 of that hex: not asked, the card stays); on yes the
    /// triggered Plague ability's targets are the enemies within range 3 of the death hex (no line of sight needed: a
    /// standalone Plague, FAQ 8.11), on no the trigger is skipped and the card stays in play.
    /// Logs "GHRule FesteringSores card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHFesteringSores
    {
        [HarmonyPatch(typeof(CDuringActionAbilityActiveBonus_TriggerAbilityOnDeath), nameof(CDuringActionAbilityActiveBonus_TriggerAbilityOnDeath.OnDeath))]
        [HarmonyPrefix]
        private static bool Dying(CDuringActionAbilityActiveBonus_TriggerAbilityOnDeath __instance, CActor actor, CAbilityAddActiveBonus ___AbilityAddActiveBonus)
        {
            try
            {
                CActiveBonus bonus = __instance.ActiveBonus;
                if (bonus == null || actor == null || !GHPlague.Marked(bonus.Ability, "GH2E_FesteringSores")) return true;
                if (!bonus.IsActiveBonusToggledAndNotRestricted(actor) || !bonus.RequirementsMet(actor)) return true;
                int card = bonus.BaseCard?.ID ?? 0;
                CActor me = bonus.Caster ?? bonus.Actor;
                var inRange = GameState.GetAllEnemiesInRange(me, actor.ArrayIndex, 3)
                    .Where(x => x != null && x != actor && !x.IsDead && ScenarioManager.Scenario.HasActor(x)).Distinct().ToList();
                if (inRange.Count == 0)
                {
                    Plugin.Log.LogInfo($"GHRule FesteringSores card {card} {GHPlague.NameOf(actor)} died: no enemy within range 3 of its hex, card stays");
                    return false;
                }
                if (!GHPlague.Ask(me, true, "FHMODS_PLAGUE_FESTER", "FHMODS_PLAGUE_YES", "FHMODS_PLAGUE_NO", GHPlague.NameOf(actor), inRange.Count))
                {
                    Plugin.Log.LogInfo($"GHRule FesteringSores card {card} {GHPlague.NameOf(actor)} died: declined, card stays");
                    return false;
                }
                CAbility add = ___AbilityAddActiveBonus?.AddAbility;
                if (add != null)
                {
                    add.Targeting = CAbility.EAbilityTargeting.Custom;
                    add.TargetingVars = new List<(string, TValue)>
                    {
                        ("AddValidActorsInRange", new TValue(inRange)),
                        ("AddTilesInRange", new TValue(inRange.Select(x => x.Tile).ToList())),
                    };
                }
                Plugin.Log.LogInfo($"GHRule FesteringSores card {card} {GHPlague.NameOf(actor)} died at {actor.ArrayIndex}: used, {inRange.Count} enemies within range 3 of that hex may gain Plague");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("GHFesteringSores")) Plugin.Log.LogError("GHFesteringSores: " + e); }
            return true;
        }
    }

    /// <summary>
    /// Unwilling Sacrifice top: "One enemy suffers Damage 2 for each Plague it has (GHPerPlague). You may remove any number
    /// of Plague from that enemy, even if this ability kills it, to have that number of different enemies adjacent to it
    /// suffer Damage 1 and gain Plague." The spread ability is the damage ability's sub ability marked MiscVars
    /// GH2E_PlagueSpreadTemplate; it is taken off the card when the card is read and, after the marked damage ability
    /// (GH2E_UnwillingSacrifice) ends, the player chooses how many tokens to remove (one confirmation per token, up to the
    /// tokens it had before the damage and the enemies adjacent to its hex); the tokens leave the enemy (if it lives) and
    /// the spread is stacked with that many targets among the adjacent enemies. Logs "GHRule UnwillingSacrifice card N".
    /// </summary>
    [HarmonyPatch]
    internal static class GHUnwillingSacrifice
    {
        private static readonly Dictionary<int, CAbility> Templates = new Dictionary<int, CAbility>();
        private static CAbility _lastEnded;

        [HarmonyTargetMethod]
        private static System.Reflection.MethodBase TargetMethod() => typeof(ScenarioRuleLibrary.YML.AbilityData)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .First(m => m.Name == "GetAction" && m.GetParameters().Any(p => p.ParameterType == typeof(CAction).MakeByRefType()));

        [HarmonyPostfix]
        private static void Read(int cardID, ref CAction action)
        {
            try
            {
                if (action?.Abilities == null || cardID < 20000 || cardID >= 40000) return;
                foreach (CAbility a in action.Abilities)
                {
                    CAbility marked = a?.SubAbilities?.FirstOrDefault(s => GHPlague.Marked(s, "GH2E_PlagueSpreadTemplate"));
                    if (marked == null) continue;
                    a.SubAbilities.Remove(marked);
                    Templates[cardID] = marked;
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("GHUnwillingSacrifice (read)")) Plugin.Log.LogError("GHUnwillingSacrifice (read): " + e); }
        }

        internal static void Ended(CAbility ability)
        {
            try
            {
                if (!(ability is CAbilityDamage) || ReferenceEquals(ability, _lastEnded) || !GHPlague.Marked(ability, "GH2E_UnwillingSacrifice")) return;
                _lastEnded = ability;
                int card = GHPlague.CardOf(ability);
                if (!GHPerPlague.LastTarget.TryGetValue(ability.ID, out var hit)) return;
                GHPerPlague.LastTarget.Remove(ability.ID);
                if (!Templates.TryGetValue(card, out CAbility template))
                {
                    Plugin.Log.LogWarning($"GHUnwillingSacrifice: card {card} has no spread template");
                    return;
                }
                CActor me = ability.TargetingActor;
                CActor first = hit.target;
                var adjacent = ScenarioManager.Scenario.AllAliveActors
                    .Where(x => x != null && x != first && !x.IsDead && CustomAbility.IsEnemy(me, x) && Adjacent(first, hit.hex, x))
                    .Distinct().ToList();
                int max = Math.Min(hit.plague, adjacent.Count);
                if (max <= 0)
                {
                    Plugin.Log.LogInfo($"GHRule UnwillingSacrifice card {card} {GHPlague.NameOf(first)} had {hit.plague} Plague, {adjacent.Count} adjacent enemies: no spread");
                    return;
                }
                int removed = 0;
                while (removed < max && GHPlague.Ask(me, false, "FHMODS_PLAGUE_SACRIFICE", "FHMODS_PLAGUE_SACRIFICE_YES", "FHMODS_PLAGUE_SACRIFICE_NO",
                                                     GHPlague.NameOf(first), removed, max))
                    removed++;
                if (removed == 0)
                {
                    Plugin.Log.LogInfo($"GHRule UnwillingSacrifice card {card} no Plague removed from {GHPlague.NameOf(first)}: no spread");
                    return;
                }
                bool alive = !first.IsDead && ScenarioManager.Scenario.HasActor(first);
                if (alive)
                {
                    int take = Math.Min(removed, first.GetCharacterResourceAmount(GHPlague.Plague));
                    if (take > 0) first.RemoveCharacterResource(GHPlague.Plague, take, updateWorldSpaceUI: true);
                }
                if (!(PhaseManager.CurrentPhase is CPhaseAction phase)) return;
                CAbility copy = CAbility.CopyAbility(template, generateNewID: true);
                copy.NumberTargets = removed;
                copy.OriginalTargetCount = removed;
                copy.Targeting = CAbility.EAbilityTargeting.Custom;
                copy.TargetingVars = new List<(string, TValue)>
                {
                    ("AddValidActorsInRange", new TValue(adjacent)),
                    ("AddTilesInRange", new TValue(adjacent.Select(x => x.Tile).ToList())),
                };
                phase.StackNextAbilities(new List<CAbility> { copy }, me, killAfter: false, stackToNextCurrent: true, copyCurrentActionID: true);
                Plugin.Log.LogInfo($"GHRule UnwillingSacrifice card {card} {removed} Plague removed from {GHPlague.NameOf(first)} ({(alive ? "alive" : "killed")}): {removed} of {adjacent.Count} adjacent enemies suffer damage 1 and gain Plague");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("GHUnwillingSacrifice (ended)")) Plugin.Log.LogError("GHUnwillingSacrifice (ended): " + e); }
        }

        private static bool Adjacent(CActor first, Point hex, CActor other)
        {
            // the hex it occupied when it was damaged (it may have died since)
            return ScenarioManager.IsTileAdjacent(hex.X, hex.Y, other.ArrayIndex.X, other.ArrayIndex.Y);
        }

    }

    [HarmonyPatch]
    internal static class GHUnwillingSacrificeEnded
    {
        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void AbilityEnded(CAbility __instance) => GHUnwillingSacrifice.Ended(__instance);
    }

    /// <summary>
    /// Wilt and Wither bottom: "During this and your next turn, double all damage that enemies that have Plague suffer."
    /// The card plays a persistent bonus marked MiscVars GH2E_WiltAndWither (end-of-turn trigger with two use slots, XP 1
    /// each: it ends at the end of your next turn); while the Plagueherald holds it, any damage an enemy with Plague
    /// suffers (from any source) is doubled. Logs "GHRule WiltAndWither card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHWiltAndWither
    {
        [HarmonyPatch(typeof(CActor), nameof(CActor.Damaged))]
        [HarmonyPrefix]
        private static void Doubling(CActor __instance, ref int strength, CAbility damagingAbility)
        {
            try
            {
                if (strength <= 0 || __instance is CPlayerActor || __instance.GetCharacterResourceAmount(GHPlague.Plague) <= 0) return;
                CPlayerActor ph = GHPlague.Plagueherald();
                if (ph == null || !CustomAbility.IsEnemy(ph, __instance)) return;
                CActiveBonus bonus = CActiveBonus.FindApplicableActiveBonuses(ph)
                    .FirstOrDefault(b => b != null && b.Actor == ph && GHPlague.Marked(b.Ability, "GH2E_WiltAndWither"));
                if (bonus == null) return;
                int before = strength;
                strength *= 2;
                // GameState.ActorBeenDamaged counted the undoubled damage before this call: the damage the ability dealt
                // (DamageInflictedByAbility: Xorn's Embrace's "Heal X, X = damage dealt") and the action's/turn's damage
                // (FinishActorDamaged adds min(health, damage) afterwards) get the doubled half too
                if (damagingAbility != null)
                {
                    damagingAbility.DamageInflictedByAbility += before;
                    damagingAbility.DamageInflictedByAbilityOnLastTarget += before;
                }
                int health = __instance.Health;
                int extra = Math.Max(0, Math.Min(health, strength) - Math.Min(health, before));
                if (extra > 0) GameState.UpdateDamageInflictedThisTurn(extra, damagingAbility?.AbilityType ?? CAbility.EAbilityType.None);
                Plugin.Log.LogInfo($"GHRule WiltAndWither card {bonus.BaseCard?.ID ?? 0} {GHPlague.NameOf(__instance)} has Plague: damage {before} doubled to {strength} (damage dealt counted {strength})");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("GHWiltAndWither")) Plugin.Log.LogError("GHWiltAndWither: " + e); }
        }
    }

    /// <summary>
    /// Compounding Infection top: "Whenever an enemy draws a Curse during its attack, you may discard this card to have
    /// that enemy gain Curse, Curse, Plague, Plague." The card plays Frosthaven's curse-drawn bonus
    /// (DamageOnCurseModApplied, the trigger site in CAbilityAttack) marked MiscVars GH2E_CompoundingInfection, one use:
    /// instead of its damage, the Plagueherald's player is asked; on yes the attacker gains two Curses (two curse cards
    /// in its modifier deck) and two Plague and the bonus ends (the card goes to the discard pile); on no it stays.
    /// Logs "GHRule CompoundingInfection card N ...".
    /// The trigger site runs inside CAbilityAttack's foreach over CharacterClassManager.FindAllActiveBonuses(): ending the
    /// bonus there is safe only because GHBonusSnapshot turns that enumeration into a snapshot list (it crashed with
    /// "Collection was modified" before, bench 2026-11-03).
    /// </summary>
    [HarmonyPatch]
    internal static class GHCompoundingInfection
    {
        [HarmonyPatch(typeof(CDamageActiveBonus_DamageOnCurseModApplied), nameof(CDamageActiveBonus_DamageOnCurseModApplied.TriggerAbility))]
        [HarmonyPrefix]
        private static bool CurseDrawn(CDamageActiveBonus_DamageOnCurseModApplied __instance, CActor actorToDamage)
        {
            try
            {
                CActiveBonus bonus = __instance.ActiveBonus;
                if (bonus == null || !GHPlague.Marked(bonus.Ability, "GH2E_CompoundingInfection")) return true;
                if (actorToDamage == null || bonus.Finished() || bonus.Finishing()) return false;
                CActor me = bonus.Caster ?? bonus.Actor;
                if (!bonus.Ability.ActiveBonusData.Filter.IsValidTarget(actorToDamage, bonus.Actor, false, false, false)) return false;
                int card = bonus.BaseCard?.ID ?? 0;
                if (!GHPlague.Ask(me, true, "FHMODS_PLAGUE_INFECTION", "FHMODS_PLAGUE_YES", "FHMODS_PLAGUE_NO", GHPlague.NameOf(actorToDamage)))
                {
                    Plugin.Log.LogInfo($"GHRule CompoundingInfection card {card} {GHPlague.NameOf(actorToDamage)} drew a Curse: declined, card stays");
                    return false;
                }
                for (int i = 0; i < 2; i++)
                    actorToDamage.ApplyCondition(me, CCondition.ENegativeCondition.Curse, 0, EConditionDecTrigger.Never, "", canGoOverCurseLimit: false);
                for (int i = 0; i < 2; i++)
                    GHPlague.Give(me, actorToDamage, card, "Compounding Infection");
                __instance.OnBehaviourTriggered();
                if (bonus.HasTracker)
                {
                    bonus.UpdateXPTracker();
                    bonus.FinishIfNoRemaining();
                }
                Plugin.Log.LogInfo($"GHRule CompoundingInfection card {card} {GHPlague.NameOf(actorToDamage)} drew a Curse: gains Curse, Curse, Plague, Plague; card discarded");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("GHCompoundingInfection")) Plugin.Log.LogError("GHCompoundingInfection: " + e); }
            return false;
        }
    }
}
