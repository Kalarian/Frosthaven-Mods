using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Safeguard, the Gloomhaven 2nd edition condition (2e rulebook): the next negative condition the figure would gain is
    /// prevented, then Safeguard is removed; it doesn't stack. Frosthaven has no such condition, so:
    ///
    /// - The token is the pack's character resource "GH2ESafeguard" (CharacterResources_Gloomhaven2e.yml, MaxAmount 1:
    ///   a second Safeguard adds nothing). A resource is part of the figure's saved state (ActorState.CharacterResources),
    ///   so it survives saves and every co-op PC holds the same one; the game shows it on the figure's status row (the
    ///   EffectsBar's character-resource effect: the resource's icon and its text as the tooltip), where the conditions sit.
    ///   It is not tied to the card: the card goes to the discard or lost pile as printed.
    /// - The cards give it with one shape (tools/gh2e/AUTHORING.md, Safeguard): a Ward ability tagged
    ///   <c>MiscVars: GH2ESafeguard: True</c>. Ward is only the carrier: it takes the targets, the range and the printed
    ///   diamond-plus enhancement mark (a Ward ability can hold one, so an enhancement adds its condition to the Safeguard's
    ///   targets); here the Ward token it would give is replaced by Safeguard. "Safeguard on the heal target" is that shape
    ///   as a sub-ability with UseParentTargets.
    /// - Prevention: CActor.ApplyCondition (negative) is the one door every negative condition goes through, a curse
    ///   added to the modifier deck included (a drawn curse isn't a gain). A holder that isn't immune to it (immunity
    ///   is checked first, as the game does) loses Safeguard instead of gaining the condition, and the game's own
    ///   "condition prevented" log line is sent. Not prevented: setting-up conditions of a scenario map (isMapCondition),
    ///   and the engine-only StopFlying.
    /// Inert without the 2nd edition pack: nothing else gives the resource, and the Ward conversion needs the pack's tag.
    /// Logs "GHRule Safeguard card {id} ..." when Safeguard is gained and when it prevents a condition.
    /// </summary>
    [HarmonyPatch]
    internal static class GHSafeguard
    {
        public const string Resource = "GH2ESafeguard";
        public const string Tag = "GH2ESafeguard";

        [ThreadStatic] private static CAbility _carrier;
        // which card gave each holder its Safeguard (for the log only; 0 after a reload)
        private static readonly Dictionary<CActor, int> GivenBy = new Dictionary<CActor, int>();

        public static bool Has(CActor actor) => actor?.CharacterResources?.Any(r => r.ID == Resource && r.Amount > 0) == true;

        private static bool Defined() => ScenarioRuleClient.SRLYML?.CharacterResources?.Any(r => r.ID == Resource) == true;

        private static int CardId(CAbility ability)
        {
            try { return ability?.GetAbilityBaseCard()?.ID ?? 0; }
            catch (Exception) { return 0; }
        }

        /// <summary>Gives the figure Safeguard (no second one). True if it was given.</summary>
        public static bool Give(CActor actor, int cardId, string how)
        {
            if (actor == null || actor.IsDead) return false;
            if (!Defined())
            {
                if (GHClasses.Reported.Add("safeguard undefined")) Plugin.Log.LogWarning("Safeguard: the pack's GH2ESafeguard resource is missing");
                return false;
            }
            bool had = Has(actor);
            if (!had) actor.AddCharacterResource(Resource, 1);
            GivenBy[actor] = cardId;
            Plugin.Log.LogInfo($"GHRule Safeguard card {cardId} {actor.GetPrefabName()} {(had ? "already has Safeguard (no stacking)" : "gains Safeguard")} ({how})");
            return !had;
        }

        // ---- giving: the Ward carrier

        [HarmonyPatch(typeof(CAbilityWard), nameof(CAbilityWard.ApplyToActor))]
        [HarmonyPrefix]
        private static void CarrierStart(CAbilityWard __instance, out CAbility __state)
        {
            __state = _carrier;
            _carrier = __instance.GetMiscVar(Tag).GetBool() ? __instance : null;
        }

        [HarmonyPatch(typeof(CAbilityWard), nameof(CAbilityWard.ApplyToActor))]
        [HarmonyFinalizer]
        private static Exception CarrierEnd(Exception __exception, CAbility __state)
        {
            _carrier = __state;
            return __exception;
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyCondition), new[] { typeof(CActor), typeof(CCondition.EPositiveCondition), typeof(int),
            typeof(EConditionDecTrigger), typeof(string), typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        private static bool WardToSafeguard(CActor __instance, CCondition.EPositiveCondition condition)
        {
            if (_carrier == null || condition != CCondition.EPositiveCondition.Ward) return true;
            try
            {
                CAbility carrier = _carrier;
                _carrier = null;   // one conversion per target (an enhancement's own conditions pass untouched)
                Give(__instance, CardId(carrier), "from " + carrier.Name);
            }
            catch (Exception e) { Plugin.Log.LogError("Safeguard: could not give it: " + e); }
            return false;
        }

        // ---- preventing

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyCondition), new[] { typeof(CActor), typeof(CCondition.ENegativeCondition), typeof(int),
            typeof(EConditionDecTrigger), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        private static bool Prevent(CActor __instance, CActor actorApplying, CCondition.ENegativeCondition condition, bool isMapCondition)
        {
            try
            {
                if (isMapCondition || condition == CCondition.ENegativeCondition.NA || condition == CCondition.ENegativeCondition.StopFlying
                    || condition == CCondition.ENegativeCondition.Disadvantage) return true;
                if (!Has(__instance)) return true;
                // immune: the game shows its immunity and nothing is gained, so Safeguard stays
                if (CActiveBonus.FindApplicableActiveBonuses(__instance, CAbility.EAbilityType.ImmunityTo).Any(b => b.Ability.HasCondition(condition))) return true;
                __instance.RemoveCharacterResource(Resource, 1, updateWorldSpaceUI: true);
                GivenBy.TryGetValue(__instance, out int card);
                GivenBy.Remove(__instance);
                ScenarioRuleClient.MessageHandler(new CPreventConditionCombatLog_MessageData(actorApplying ?? __instance,
                    CCondition.AbilityTypeFromNegativeCondition(condition), __instance));
                Plugin.Log.LogInfo($"GHRule Safeguard card {card} prevented {condition} on {__instance.GetPrefabName()} (from {actorApplying?.GetPrefabName() ?? "?"}); Safeguard removed");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Safeguard: prevention failed: " + e);
                return true;
            }
        }
    }
}
