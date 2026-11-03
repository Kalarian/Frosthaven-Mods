using System;
using System.Collections.Generic;
using System.Linq;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// In-scenario counts the 2nd edition plugins keep (mastery counts, "this round" states) saved with the scenario, so a
    /// save/reload, a rewind or a rejoin keeps them. Each count is a hidden character resource on the character it belongs
    /// to (GH2ECount1..GH2ECount10, tools/gh2e/shared/resources.yml, MaxAmount 999; the Tinkerer2SharedSupplies /
    /// GH2EAllSixElements precedent: a character resource is saved with the actor). The slots are per character, so each
    /// class numbers its own (the class file says which slot holds what). Without the 2nd edition pack the resource has no
    /// data and the engine's constructor throws: caught, the count then lives only in the caller's memory copy.
    /// </summary>
    internal static class GHSavedCounts
    {
        internal const int Slots = 10;
        private static string Id(int slot) => "GH2ECount" + slot;
        internal static string ResourceId(int slot) => Id(slot);

        /// <summary>The character with this actor guid, alive or exhausted, in the scenario being played.</summary>
        internal static CPlayerActor Player(string guid)
        {
            CScenario s = ScenarioManager.Scenario;
            if (s == null || string.IsNullOrEmpty(guid)) return null;
            return (s.PlayerActors ?? new List<CPlayerActor>()).Concat(s.ExhaustedPlayers ?? new List<CPlayerActor>())
                .FirstOrDefault(p => p != null && p.ActorGuid == guid);
        }

        internal static int Get(CActor a, int slot)
        {
            try { return a == null || slot < 1 || slot > Slots ? 0 : a.GetCharacterResourceAmount(Id(slot)); }
            catch { return 0; }
        }

        internal static int Get(string guid, int slot) => Get(Player(guid), slot);

        internal static bool Set(CActor a, int slot, int value)
        {
            if (a == null || slot < 1 || slot > Slots) return false;
            try
            {
                value = Math.Max(0, Math.Min(999, value));
                int now = a.GetCharacterResourceAmount(Id(slot));
                if (value > now) a.AddCharacterResource(Id(slot), value - now);
                else if (value < now) a.RemoveCharacterResource(Id(slot), now - value);
                return a.GetCharacterResourceAmount(Id(slot)) == value;
            }
            catch (Exception e)
            {
                if (GHClasses.Reported.Add("saved counts")) Plugin.Log.LogWarning("GHSavedCounts: the hidden count resource is missing (2nd edition pack not installed?); the count is kept in memory only: " + e.Message);
                return false;
            }
        }

        internal static int Add(CActor a, int slot, int delta = 1)
        {
            int v = Get(a, slot) + delta;
            Set(a, slot, v);
            return v;
        }
    }
}
