using System;
using System.Collections.Generic;
using System.Linq;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The bonuses a character's own cards hold, wherever they act. CActor.ActiveBonuses (CBaseCard.GetActiveBonusesForActor)
    /// lists an aura only under the figures in its range: an aura on "your summons" (Soultether's Living Night, Imposed
    /// Will) or on "all allies" / "enemies" (Soothsinger songs) is never in the caster's own list. Read from the caster's
    /// active area and this round's cards instead (as GHWildfury.CardBonus / GHNightshroud.CardBonus do), keeping the
    /// bonuses the caster cast or holds. Unfinished bonuses only. Not a rule: no GHRule line.
    /// </summary>
    internal static class GHCasterBonuses
    {
        internal static List<CActiveBonus> Of(CActor caster)
        {
            var list = new List<CActiveBonus>();
            try
            {
                CCharacterClass cls = (caster as CPlayerActor)?.CharacterClass;
                if (cls == null) return list;
                foreach (CBaseCard c in cls.ActivatedCards.Cast<CBaseCard>().Concat(cls.RoundAbilityCards))
                {
                    if (c?.ActiveBonuses == null) continue;
                    foreach (CActiveBonus b in c.ActiveBonuses)
                        if (b != null && (b.Caster == caster || b.Actor == caster) && !b.Finished() && !b.Finishing() && !list.Contains(b))
                            list.Add(b);
                }
            }
            catch (Exception) { }
            return list;
        }

        /// <summary>An unfinished bonus of card <paramref name="cardId"/> the caster cast, aura or not, or null.</summary>
        internal static CActiveBonus OfCard(CActor caster, int cardId, Func<CActiveBonus, bool> where = null) =>
            Of(caster).FirstOrDefault(b => b.BaseCard?.ID == cardId && (where == null || where(b)));
    }
}
