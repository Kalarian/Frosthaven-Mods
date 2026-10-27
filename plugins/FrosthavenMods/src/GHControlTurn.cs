using System.Collections.Generic;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven's "force an enemy to perform its turn" (the Scoundrel's Long Con bottom: ControlActor for one turn with
    /// no abilities of its own; the enemy performs its own monster card). Frosthaven's control ability drops every
    /// candidate target for which all of its commanded abilities are immune, and with no commanded abilities at all that
    /// test passes for everyone: the card could never target anything (found by the test harness, 2026-10-27: 16 plays,
    /// the game's valid list always empty). Frosthaven's own control cards always command abilities, so for them nothing
    /// changes; without the content pack no card has an empty list and this is inert.
    ///   - A control ability with no commanded abilities keeps the rest of the game's target checks (asleep enemies are
    ///     still refused, and everything the base ability refuses).
    /// </summary>
    [HarmonyPatch]
    internal static class GHControlTurn
    {
        private delegate void RemoveImmune(CAbility self, ref List<CActor> actorList, bool onlyCheck);

        // the base ability's own check, called directly (not the control ability's override, which would empty the list)
        private static readonly RemoveImmune BaseRemoveImmune = AccessTools.MethodDelegate<RemoveImmune>(
            AccessTools.Method(typeof(CAbility), nameof(CAbility.RemoveImmuneActorsFromList)), null, virtualCall: false);

        private static bool _reported;

        [HarmonyPatch(typeof(CAbilityControlActor), nameof(CAbilityControlActor.RemoveImmuneActorsFromList))]
        [HarmonyPrefix]
        private static bool KeepTargetsWithoutCommands(CAbilityControlActor __instance, ref List<CActor> actorList, bool onlyCheck)
        {
            var commanded = __instance.ControlActorData?.ControlAbilities;
            if (commanded == null || commanded.Count > 0 || actorList == null) return true;
            actorList.RemoveAll(x => x.Tokens.HasKey(CCondition.ENegativeCondition.Sleep));
            BaseRemoveImmune(__instance, ref actorList, onlyCheck);
            if (!_reported)
            {
                _reported = true;
                Plugin.Log.LogInfo($"Gloomhaven control: {__instance.Name} commands no abilities (the enemy performs its own turn): targets kept ({actorList.Count})");
            }
            return false;
        }
    }
}
