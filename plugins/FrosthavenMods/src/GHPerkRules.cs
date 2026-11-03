using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// "Rule fired" lines for the 2nd edition perks and perk modifier cards that Frosthaven's own engine runs from the
    /// pack's data (no plugin code acts for them, so nothing logged them and the perk bench couldn't prove them). Only
    /// logging: no effect is changed. Inert without the 2e pack (the perk IDs below and the AMC_GH2E_ card names exist
    /// only there).
    ///   - Engine-run modifier abilities on pack cards (AMC_GH2E_...): every ability kind CAbilityAttack.
    ///     ProcessModifierAbilities runs itself (Attack, Heal, ControlActor, Infuse, Push, ...; the kinds it can't run go
    ///     through GHModifierAbilities.cs, which logs them) logs "GHRule ModifierAbility card 0 {card} {kind} (engine)"
    ///     when the engine queues it, and an Attack / ControlActor from such a card logs again as it applies to its
    ///     target ("... applied to X"): Second Arrow (Doomstalker202, Attack 2 Range 5), Pack Hunt (Doomstalker204, the
    ///     grant to a summon adjacent to the target).
    ///   - Data-only perks (table Perks): "GHRule {PerkName} card 0 perk {Perk Name}: ..." where the engine uses them:
    ///       rest perks (ExecuteAbilitiesAfterLongRest / ShortRest): when the rest queues their abilities;
    ///       room perks (ExecuteAbilitiesAfterRoomRevealed): when a room is revealed;
    ///       active-bonus perks: when the bonus is used (CActiveBonus.ActiveBonusUsed, which every restrict/trigger
    ///         path calls, or a behaviour's OnBehaviourTriggered); Lying Low when monster focus reads its initiative;
    ///         Winged when a move reads its Flying;
    ///       Unfazed (IgnoreNegativeScenarioEffects): when a negative scenario effect is ignored for the character;
    ///       Thick Skin / Hardened / Well Equipped / Well Armored (IgnoreMinusOneItemEffects): when the deck is built
    ///         and an equipped item's -1 cards are left out.
    ///     At most one line per perk, character and round (a move-buff is read many times a move).
    /// </summary>
    [HarmonyPatch]
    internal static class GHPerkRules
    {
        // the 2e perks no other plugin code logs: ID -> English name (the perk bench matches the squashed name)
        private static readonly Dictionary<string, string> Perks = new Dictionary<string, string>
        {
            ["Berserker208"] = "Thick Skin", ["Berserker209"] = "Rapid Recovery",
            ["Bruiser09"] = "Hardened", ["Bruiser10"] = "Scavenger",
            ["Cragheart207"] = "Thick Skin", ["Cragheart208"] = "Boulder Leap", ["Cragheart209"] = "Earthen Rest", ["Cragheart210"] = "Earthquakes",
            ["Doomstalker208"] = "Foresight", ["Doomstalker209"] = "Restless Doom",
            ["Elementalist211"] = "Restful Flux",
            ["Mindthief209"] = "Lying Low", ["Mindthief210"] = "Restless Mind",
            ["Nightshroud208"] = "Unfazed", ["Nightshroud209"] = "Restful Night", ["Nightshroud210"] = "Rift Opener", ["Nightshroud211"] = "Hexed Retribution",
            ["Plagueherald209"] = "Unfazed", ["Plagueherald210"] = "Winged",
            ["Quartermaster208"] = "Well Equipped", ["Quartermaster211"] = "Resupply",
            ["SilentKnife08"] = "Parting Gift", ["SilentKnife10"] = "Cloak of Invisibility",
            ["Soultether10"] = "Unfazed", ["Soultether11"] = "Restful Guard", ["Soultether12"] = "Summoner's Call", ["Soultether13"] = "Phase Out",
            ["Spellweaver207"] = "Unfazed", ["Spellweaver208"] = "Vanishing Ward", ["Spellweaver210"] = "Arcane Blessing",
            ["Sunkeeper207"] = "Unfazed", ["Sunkeeper208"] = "Well Armored", ["Sunkeeper209"] = "Vigilant Entry", ["Sunkeeper210"] = "Restful Dawn",
            ["Tinkerer210"] = "Rejuvenating Vapor",
        };

        private static readonly HashSet<string> Logged = new HashSet<string>();
        private static object loggedScenario;

        private static string RuleName(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray());

        private static int Round()
        {
            try { return ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0; } catch { return 0; }
        }

        /// <summary>One line per perk, character and round (key extra: a finer key, e.g. the scenario modifier).</summary>
        private static void Log(PerksYMLData perk, CActor actor, string detail, string extra = "")
        {
            if (perk == null || !Perks.TryGetValue(perk.ID, out string name)) return;
            object scenario = ScenarioManager.Scenario;
            if (!ReferenceEquals(scenario, loggedScenario)) { Logged.Clear(); loggedScenario = scenario; }
            if (!Logged.Add($"{perk.ID}|{actor?.ActorGuid}|{Round()}|{extra}")) return;
            Plugin.Log.LogInfo($"GHRule {RuleName(name)} card 0 perk {name} ({perk.ID}): {GHPlague.NameOf(actor)} {detail}");
        }

        private static IEnumerable<PerksYMLData> TablePerks(CActor actor, EPerkFlag flag = EPerkFlag.None)
        {
            List<PerksYMLData> perks = (actor as CPlayerActor)?.CharacterClass?.Perks;
            if (perks == null) return Enumerable.Empty<PerksYMLData>();
            return perks.Where(p => p != null && p.ID != null && Perks.ContainsKey(p.ID) && (flag == EPerkFlag.None || (p.Flags & flag) != 0)).ToList();
        }

        private static void Report(string what, Exception e)
        {
            if (GHClasses.Reported.Add("perk rules " + what)) Plugin.Log.LogError("GHPerkRules (" + what + "): " + e);
        }

        // ================================================================== engine-run pack modifier abilities

        private static readonly System.Reflection.FieldInfo InlineChecked = AccessTools.Field(typeof(CAbilityAttack), "m_InlineModifiersChecked");

        private static readonly HashSet<CAbility.EAbilityType> RunAlways = new HashSet<CAbility.EAbilityType>
        {
            CAbility.EAbilityType.Consume, CAbility.EAbilityType.Infuse, CAbility.EAbilityType.ModeTransfer,
        };

        [HarmonyPatch(typeof(CAbilityAttack), "ProcessModifierAbilities")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        private static void EngineModifierAbilities(CAbilityAttack __instance, List<CAbilityAttack.AttackModifierAbilityInfo> modifierAbilities, CActor targetActor)
        {
            try
            {
                if (modifierAbilities == null || modifierAbilities.Count == 0 || __instance.TargetingActor == null || __instance.TargetingActor.IsMirrorActor()) return;
                bool inlineChecked = InlineChecked != null && (bool)InlineChecked.GetValue(__instance);
                foreach (CAbilityAttack.AttackModifierAbilityInfo info in modifierAbilities)
                {
                    if (!GHModifierAbilities.IsPackCard(info) || info.Ability == null) continue;
                    CAbility.EAbilityType kind = info.Ability.AbilityType;
                    if (inlineChecked && !RunAlways.Contains(kind)) continue; // the engine skips it on this pass
                    CActor performer = info.TargetingActor ?? __instance.TargetingActor;
                    Plugin.Log.LogInfo($"GHRule ModifierAbility card 0 {info.Card.Name} {kind} (engine) by {GHPlague.NameOf(performer)} (target {GHPlague.NameOf(targetActor)})");
                }
            }
            catch (Exception e) { Report("engine modifier abilities", e); }
        }

        private static CAbilityAttack.AttackModifierAbilityInfo PackModifierInfo(CAbility ability)
        {
            try
            {
                var info = ability?.VarsContext?.GetVar("AttackSummaryModInfo").GetObject<CAbilityAttack.AttackModifierAbilityInfo>();
                return GHModifierAbilities.IsPackCard(info) ? info : null;
            }
            catch { return null; }
        }

        [HarmonyPatch(typeof(CAbilityAttack), nameof(CAbilityAttack.ApplyToActor))]
        [HarmonyPostfix]
        private static void ModifierAttackApplied(CAbilityAttack __instance, CActor actor) => Applied(__instance, actor);

        [HarmonyPatch(typeof(CAbilityControlActor), nameof(CAbilityControlActor.ApplyToActor))]
        [HarmonyPostfix]
        private static void ModifierControlApplied(CAbilityControlActor __instance, CActor actor) => Applied(__instance, actor);

        private static void Applied(CAbility ability, CActor actor)
        {
            try
            {
                if (ability?.VarsContext == null) return;
                var info = PackModifierInfo(ability);
                if (info == null) return;
                Plugin.Log.LogInfo($"GHRule ModifierAbility card 0 {info.Card.Name} {ability.AbilityType} applied to {GHPlague.NameOf(actor)} by {GHPlague.NameOf(ability.TargetingActor)}");
            }
            catch (Exception e) { Report("modifier ability applied", e); }
        }

        // ================================================================== rest and room perks

        [HarmonyPatch(typeof(GameState), nameof(GameState.PlayerLongRested))]
        [HarmonyPrefix]
        private static void BeforeLongRest(CPlayerActor playerActor, out CPlayerActor __state)
        {
            __state = null;
            try { __state = playerActor ?? GameState.InternalCurrentActor as CPlayerActor; } catch { }
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.PlayerLongRested))]
        [HarmonyPostfix]
        private static void AfterLongRest(CPlayerActor __state)
        {
            try
            {
                if (__state == null) return;
                foreach (PerksYMLData p in TablePerks(__state, EPerkFlag.ExecuteAbilitiesAfterLongRest))
                {
                    var names = p.Abilities?.Where(a => a != null && a.MeetsStartAbilityRequirements(__state)).Select(a => a.AbilityType.ToString()).ToList();
                    if (names == null || names.Count == 0) Log(p, __state, "long rested; the perk's ability requirement isn't met (nothing to do)", "long");
                    else Log(p, __state, $"long rested: performs {string.Join(", ", names)}", "long");
                }
            }
            catch (Exception e) { Report("long rest", e); }
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.CheckExhaustAfterShortRest))]
        [HarmonyPostfix]
        private static void AfterShortRest(CPlayerActor playerActor, bool isShortRestingPlayer, bool __result)
        {
            try
            {
                if (!isShortRestingPlayer || __result || playerActor == null) return;
                foreach (PerksYMLData p in TablePerks(playerActor, EPerkFlag.ExecuteAbilitiesAfterShortRest))
                    Log(p, playerActor, $"short rested: performs {string.Join(", ", p.Abilities?.Where(a => a != null).Select(a => a.AbilityType.ToString()) ?? new string[0])}", "short");
            }
            catch (Exception e) { Report("short rest", e); }
        }

        private static readonly AccessTools.FieldRef<CMap, bool> Revealed = AccessTools.FieldRefAccess<CMap, bool>("m_ReveledInternal");

        [HarmonyPatch(typeof(CMap), nameof(CMap.Reveal))]
        [HarmonyPrefix]
        private static void BeforeReveal(CMap __instance, out bool __state)
        {
            __state = true;
            try { __state = Revealed(__instance); } catch { }
        }

        [HarmonyPatch(typeof(CMap), nameof(CMap.Reveal))]
        [HarmonyPostfix]
        private static void AfterReveal(CMap __instance, bool __state)
        {
            try
            {
                if (__state || ScenarioManager.Scenario?.AllPlayers == null) return;
                foreach (CPlayerActor player in ScenarioManager.Scenario.AllPlayers.Where(x => x != null && !x.IsDeadForObjectives))
                    foreach (PerksYMLData p in TablePerks(player, EPerkFlag.ExecuteAbilitiesAfterRoomRevealed))
                        Log(p, player, $"room {__instance.RoomName} revealed: its abilities start", "room " + __instance.MapGuid);
            }
            catch (Exception e) { Report("room revealed", e); }
        }

        // ================================================================== active-bonus perks

        /// <summary>The table perk whose ability made this bonus (the perk ability itself or a copy of it).</summary>
        private static PerksYMLData PerkOf(CActiveBonus bonus)
        {
            CAbility ab = bonus?.Ability;
            if (ab == null) return null;
            CActor owner = bonus.Caster as CPlayerActor ?? bonus.Actor as CPlayerActor;
            List<PerksYMLData> perks = (owner as CPlayerActor)?.CharacterClass?.Perks;
            if (perks == null || !perks.Any(p => p?.ID != null && Perks.ContainsKey(p.ID))) return null;
            foreach (PerksYMLData p in perks)
                if (p?.Abilities != null && p.Abilities.Any(a => a != null && (ReferenceEquals(a, ab) || a.ID == ab.ID)))
                    return p != null && Perks.ContainsKey(p.ID) ? p : null;
            // a fresh copy: the same name, kind and behaviours, and only one perk of the character fits
            var fits = perks.Where(p => p?.Abilities != null && p.Abilities.Any(a => a != null && a.Name == ab.Name && a.AbilityType == ab.AbilityType
                && (a.ActiveBonusData?.Behaviours ?? new List<CActiveBonus.EActiveBonusBehaviourType>()).SequenceEqual(ab.ActiveBonusData?.Behaviours ?? new List<CActiveBonus.EActiveBonusBehaviourType>()))).ToList();
            return fits.Count == 1 && Perks.ContainsKey(fits[0].ID) ? fits[0] : null;
        }

        private static void BonusUsed(CActiveBonus bonus, CActor actor, string how)
        {
            PerksYMLData p = PerkOf(bonus);
            if (p == null) return;
            Log(p, actor ?? bonus.Actor, $"{how} ({string.Join("/", bonus.Ability.ActiveBonusData?.Behaviours ?? new List<CActiveBonus.EActiveBonusBehaviourType>())})");
        }

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.ActiveBonusUsed))]
        [HarmonyPostfix]
        private static void AfterBonusUsed(CActiveBonus __instance, CActor actor)
        {
            try { BonusUsed(__instance, actor, "used the perk's bonus"); }
            catch (Exception e) { Report("bonus used", e); }
        }

        [HarmonyPatch(typeof(CBespokeBehaviour), nameof(CBespokeBehaviour.OnBehaviourTriggered))]
        [HarmonyPostfix]
        private static void AfterBehaviourTriggered(CBespokeBehaviour __instance)
        {
            try { BonusUsed(__instance?.ActiveBonus, null, "the perk's bonus triggered"); }
            catch (Exception e) { Report("behaviour triggered", e); }
        }

        [HarmonyPatch(typeof(CAdjustInitiativeActiveBonus_FocusInitiative), nameof(CAdjustInitiativeActiveBonus_FocusInitiative.SetFocusInitiative))]
        [HarmonyPostfix]
        private static void AfterFocusInitiative(CAdjustInitiativeActiveBonus_FocusInitiative __instance, int __result)
        {
            try
            {
                PerksYMLData p = PerkOf(__instance?.ActiveBonus);
                if (p != null) Log(p, __instance.ActiveBonus.Actor, $"counts as initiative {__result} when monsters pick their focus");
            }
            catch (Exception e) { Report("focus initiative", e); }
        }

        [HarmonyPatch(typeof(CMoveActiveBonus_BuffMove), nameof(CMoveActiveBonus_BuffMove.ReferenceFly))]
        [HarmonyPostfix]
        private static void AfterReferenceFly(CMoveActiveBonus_BuffMove __instance, CActor target, bool? __result)
        {
            try
            {
                if (__result != true) return;
                PerksYMLData p = PerkOf(__instance?.ActiveBonus);
                if (p != null) Log(p, target ?? __instance.ActiveBonus.Actor, "moves with Flying");
            }
            catch (Exception e) { Report("move fly", e); }
        }

        // ================================================================== flag perks

        [HarmonyPatch(typeof(CScenarioModifier), nameof(CScenarioModifier.IgnoreNegativeScenarioEffects))]
        [HarmonyPostfix]
        private static void AfterIgnoreScenarioEffect(CScenarioModifier __instance, CActor currentActor, bool __result)
        {
            try
            {
                if (!__result) return;
                foreach (PerksYMLData p in TablePerks(currentActor, EPerkFlag.IgnoreNegativeScenarioEffects))
                    Log(p, currentActor, $"ignores the negative scenario effect {__instance?.GetType().Name}", "sm " + __instance?.GetHashCode());
            }
            catch (Exception e) { Report("scenario effect", e); }
        }

        [HarmonyPatch(typeof(CCharacterClass), nameof(CCharacterClass.ApplyItemModifiersToList))]
        [HarmonyPostfix]
        private static void AfterItemModifiers(CCharacterClass __instance, List<CItem> items)
        {
            try
            {
                if (items == null || __instance?.Perks == null) return;
                PerksYMLData p = __instance.Perks.FirstOrDefault(x => x?.ID != null && Perks.ContainsKey(x.ID) && (x.Flags & EPerkFlag.IgnoreMinusOneItemEffects) != 0);
                if (p == null) return;
                int skipped = items.Where(i => i?.YMLData?.Data?.AdditionalModifiers != null)
                    .SelectMany(i => i.YMLData.Data.AdditionalModifiers).Where(kv => kv.Key?.MathModifier?.Trim() == "-1").Sum(kv => kv.Value);
                if (skipped <= 0) return;
                CPlayerActor actor = ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(x => ReferenceEquals(x?.CharacterClass, __instance));
                object scenario = ScenarioManager.Scenario;
                if (!ReferenceEquals(scenario, loggedScenario)) { Logged.Clear(); loggedScenario = scenario; }
                if (!Logged.Add($"{p.ID}|{__instance.ID}|items|{Round()}")) return;
                string name = Perks[p.ID];
                Plugin.Log.LogInfo($"GHRule {RuleName(name)} card 0 perk {name} ({p.ID}): {(actor != null ? GHPlague.NameOf(actor) : __instance.ID)} ignores {skipped} item -1 card(s)");
            }
            catch (Exception e) { Report("item modifiers", e); }
        }
    }
}
