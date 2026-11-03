using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// 2nd edition rules that change a drawn attack modifier card, at the game's own rewrite step
    /// (GameState.ChangeModifierValues, where Frosthaven turns a card into its alternative or applies an attack's
    /// ReplaceModifiers; CActor.DrawAttackModifierCard calls it for every card drawn), plus the attack bonus that goes
    /// with one of them:
    ///   - Sawbones' Follow-up Care (AMC_GH2E_Sawbones2_FollowUp_P1): "+1, add +1 Attack if an ally has an active
    ///     Prescription": the +1 becomes +2 while another player figure holds an active bonus tagged GH2E_Prescription
    ///     (the class's Prescription abilities carry that misc var).
    ///   - Sunkeeper's Scales of Justice bottom (card 33324): "Whenever you consume Light during one of your attack
    ///     abilities, add +1 Attack, Pierce 1 to all the attacks and treat all null attack modifier cards drawn as 2x
    ///     instead": the card's persistent BuffAttack +1, Pierce 1 counts only on an attack of an action that consumes
    ///     Light (CBespokeBehaviour.IsValidTarget, which the bonus's strength, pierce and XP all ask), and in that attack
    ///     a drawn null is a 2x.
    /// Logs "GHRule FollowUpCare card 0 ..." and "GHRule ScalesOfJustice card 33324 ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHModifierRewrites
    {
        public const int ScalesOfJustice = 33324;
        private const string FollowUpCare = "AMC_GH2E_Sawbones2_FollowUp_P1";

        private static bool HasPrescription(CActor actor) =>
            actor?.ActiveBonuses?.Any(b => b?.Ability != null && b.Ability.GetMiscVar("GH2E_Prescription").GetBool()) == true;

        /// <summary>The current action consumes Light (a consume the player toggled on the card).</summary>
        public static bool LightConsumedThisAction()
        {
            try
            {
                return PhaseManager.CurrentPhaseAction?.ActionAugmentationsToConsume?.Any(a => a?.Elements != null
                    && a.Elements.Contains(ElementInfusionBoardManager.EElement.Light)) == true;
            }
            catch (Exception) { return false; }
        }

        private static CActiveBonus Scales(CActor actor) =>
            actor?.ActiveBonuses?.FirstOrDefault(b => b?.BaseCard != null && b.BaseCard.ID == ScalesOfJustice
                                                      && b.HasBehaviour(CActiveBonus.EActiveBonusBehaviourType.BuffAttack));

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
        private static void Rewrite(CActor actor, List<AttackModifierYMLData> modifiers, CAbilityAttack attackAbility, CActor currentTarget)
        {
            try
            {
                if (modifiers == null || modifiers.Count == 0) return;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    AttackModifierYMLData card = modifiers[i];
                    if (card == null) continue;
                    if (card.Name == FollowUpCare && card.MathModifier == "+1")
                    {
                        // the drawer's owner (a summon draws from its summoner's deck)
                        CActor owner = actor is CHeroSummonActor s && s.Summoner != null ? s.Summoner : actor;
                        CActor ally = ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => p != owner && !p.IsDead && HasPrescription(p));
                        if (ally != null)
                        {
                            modifiers[i] = Rewritten(card, "+2");
                            Plugin.Log.LogInfo($"GHRule FollowUpCare card 0 +1 counts as +2 ({ally.GetPrefabName()} has an active Prescription)");
                        }
                        continue;
                    }
                    if (card.MathModifier == "*0" && attackAbility?.TargetingActor == actor && Scales(actor) != null && LightConsumedThisAction())
                    {
                        modifiers[i] = Rewritten(card, "*2");
                        Plugin.Log.LogInfo($"GHRule ScalesOfJustice card {ScalesOfJustice} null counts as 2x ({actor.GetPrefabName()} consumed Light this attack)");
                    }
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("modifier rewrites")) Plugin.Log.LogError("Gloomhaven modifier rewrites: " + e); }
        }

        private static readonly HashSet<string> ScalesLogged = new HashSet<string>();

        /// <summary>Scales of Justice's +1 Attack, Pierce 1: only on attacks of an action that consumes Light.</summary>
        [HarmonyPatch(typeof(CBespokeBehaviour), "IsValidTarget")]
        [HarmonyPostfix]
        private static void ScalesNeedsLight(CBespokeBehaviour __instance, CAbility ability, ref bool __result)
        {
            try
            {
                if (!__result || !(__instance is CAttackActiveBonus_BuffAttack)) return;
                CActiveBonus bonus = __instance.ActiveBonus;
                if (bonus?.BaseCard == null || bonus.BaseCard.ID != ScalesOfJustice) return;
                if (!(ability is CAbilityAttack)) return;
                bool light = LightConsumedThisAction();
                __result = light;
                string key = ability.ID + " " + light;
                if (light && ScalesLogged.Add(key))
                {
                    if (ScalesLogged.Count > 500) { ScalesLogged.Clear(); ScalesLogged.Add(key); }
                    Plugin.Log.LogInfo($"GHRule ScalesOfJustice card {ScalesOfJustice} +1 Attack, Pierce 1 on this attack (Light consumed)");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("scales valid target")) Plugin.Log.LogError("GHModifierRewrites (Scales of Justice target): " + e); }
        }
    }
}
