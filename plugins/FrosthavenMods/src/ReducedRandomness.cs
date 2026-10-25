using System;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// "Reduced randomness": Gloomhaven digital's house rule, which Frosthaven still carries whole but never switches on.
    /// Its data is in Base.ruleset (the decks ReducedRandomness and MonsterReducedRandomnessDeck: the null and the x2
    /// replaced by a -2 and a +2 that both reshuffle; Reduced_Randomness_Curse -2 and _Bless +2), and the scenario rules
    /// read StateShared.EHouseRulesFlag.ReducedRandomness from ScenarioManager.HouseRulesSettings when they build a
    /// character's base deck (CharacterYMLData.AttackModifierDeck), a monster deck (CMonsterAttackModifierDeck.LoadCardPool)
    /// and a bless or curse (AttackModifiersYML.CreateBless/CreateCurse). Only Gloomhaven's map state ever set that flag;
    /// Frosthaven's campaign never calls it, so it stays None. This sets it from the campaign rule (the game's own
    /// ERulesFlag.ReducedRandomness bit, hidden on its rules screen) just before each of those reads, on every PC (the rule
    /// is part of the synced campaign rules, and locked during online scenarios).
    ///
    /// Within a scenario the value is the one the scenario was loaded with, so a solo player flipping the switch mid-fight
    /// changes nothing until the next scenario (the decks were built already; blesses and curses must match them). Perks
    /// work unchanged: those that remove a -2 take the plain one and leave the reshuffling -2, as they leave the null in
    /// the standard deck; a perk removing a card the deck lacks does nothing (CCharacterClass.ApplyPerksToList).
    /// </summary>
    [HarmonyPatch]
    internal static class ReducedRandomness
    {
        private static bool? _scenario;   // the rule as the current scenario was loaded with; null outside one

        private static bool Wanted => _scenario ?? ModRules.IsOn(ModRules.ReducedRandomness);

        /// <summary>Puts the flag into the scenario rules' house rules (and takes it out), keeping any other bits.</summary>
        internal static void Sync()
        {
            try
            {
                StateShared.EHouseRulesFlag now = ScenarioManager.HouseRulesSettings;
                StateShared.EHouseRulesFlag next = Wanted
                    ? now | StateShared.EHouseRulesFlag.ReducedRandomness
                    : now & ~StateShared.EHouseRulesFlag.ReducedRandomness;
                if (next != now) ScenarioManager.HouseRulesSettings = next;
            }
            catch (Exception e) { Report(e); }
        }

        [HarmonyPatch(typeof(ScenarioManager), nameof(ScenarioManager.Load))]
        [HarmonyPrefix]
        private static void ScenarioLoaded()
        {
            _scenario = ModRules.IsOn(ModRules.ReducedRandomness);
            Sync();
            if (_scenario == true) Plugin.Log.LogInfo("Reduced randomness: on for this scenario");
        }

        [HarmonyPatch(typeof(ScenarioManager), nameof(ScenarioManager.Unload))]
        [HarmonyPostfix]
        private static void ScenarioUnloaded()
        {
            _scenario = null;
            Sync();
        }

        // every place that reads the flag
        [HarmonyPatch(typeof(CCharacterClass), nameof(CCharacterClass.Reset))]
        [HarmonyPrefix]
        private static void BeforeCharacterDeck() => Sync();

        [HarmonyPatch(typeof(CMonsterAttackModifierDeck), nameof(CMonsterAttackModifierDeck.LoadCardPool))]
        [HarmonyPrefix]
        private static void BeforeMonsterDeck() => Sync();

        [HarmonyPatch(typeof(AttackModifiersYML), nameof(AttackModifiersYML.CreateBless))]
        [HarmonyPrefix]
        private static void BeforeBless() => Sync();

        [HarmonyPatch(typeof(AttackModifiersYML), nameof(AttackModifiersYML.CreateCurse))]
        [HarmonyPrefix]
        private static void BeforeCurse() => Sync();

        private static bool _reported;

        private static void Report(Exception e)
        {
            if (_reported) return;
            _reported = true;
            Plugin.Log.LogError("Reduced randomness: could not set the scenario house rule: " + e);
        }
    }
}
