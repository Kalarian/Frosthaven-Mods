using System.Collections.Generic;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// A sub-ability under a sub-ability: the 2nd edition Plagueherald's Mass Extinction top ("whenever this ability kills
    /// an enemy, add +2 Attack and +1 target": Attack 4, on a kill Attack 6, on a kill Attack 8, each step under the one
    /// before it) and Path of the Pox bottom (Plague spreading to an enemy next to the last one, four steps deep). When an
    /// action starts, CPhaseAction's constructor queues the action's abilities and their direct sub-abilities only, so the
    /// third step and beyond never ran (harness 2026-11-02: never started in any play). After the game's own list is built,
    /// every pack card sub-ability's own non-inline sub-abilities are queued right after it, depth first, each with its real
    /// parent (the kill check and "next to the parent's targets" read the step before, as the card means). Inline subs (a
    /// push riding an attack) stay with the ability that runs them. Only pack cards (IDs 20000-39999) are touched: no
    /// Frosthaven card nests a non-inline sub this way, and without the content packs this is inert.
    /// </summary>
    [HarmonyPatch]
    internal static class GHNestedSubAbilities
    {
        private static bool _reported;

        [HarmonyPatch(typeof(CPhaseAction), MethodType.Constructor)]
        [HarmonyPostfix]
        private static void After(CPhaseAction __instance)
        {
            List<CPhaseAction.CPhaseAbility> list = __instance?.RemainingPhaseAbilities;
            if (list == null) return;
            int added = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CPhaseAction.CPhaseAbility pa = list[i];
                CAbility ab = pa?.m_Ability;
                if (ab == null || ab.ParentAbility == null || ab.SubAbilities == null || ab.SubAbilities.Count == 0) continue;
                if (pa.m_BaseCard == null || pa.m_BaseCard.ID < 20000 || pa.m_BaseCard.ID >= 40000) continue;
                int at = i + 1;
                foreach (CAbility sub in ab.SubAbilities)
                {
                    if (sub == null || sub.IsInlineSubAbility || sub.OnDeath) continue;
                    if (list.Exists(x => x?.m_Ability == sub)) continue;
                    sub.ProcessIfDead = ab.ProcessIfDead;
                    sub.ParentAbility = ab;
                    list.Insert(at++, new CPhaseAction.CPhaseAbility(sub, pa.TargetingActor, pa.m_BaseCard, pa.ActionID));
                    added++;
                }
                // (the loop goes on from i + 1: the step just queued is looked at next, so a deeper chain unfolds in order)
            }
            if (added > 0 && !_reported)
            {
                _reported = true;
                Plugin.Log.LogInfo($"Gloomhaven nested sub-abilities: {added} queued after their parent step");
            }
        }
    }
}
