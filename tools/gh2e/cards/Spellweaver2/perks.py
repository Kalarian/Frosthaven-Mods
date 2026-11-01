# Spellweaver (Gloomhaven 2nd edition) perks, from the perk sheet "Orchid Spellweaver" (tools/gh2e/classes.json):
# 10 perks, 18 check marks. gh_crossover's table format: (checkboxes, name, description, {perk fields}).

PERKS = [
    (1, "Composure", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    (3, "Wild Weave", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=CreateWild>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Spellweaver2_Wild_P0]"}),
    # the conditional +2 needs the plugin (no attack modifier can ask whether the current action has the lost icon):
    # until then the card is a plain +1 with the rule printed on the perk
    (3, "Spent Power", 'Replace one <sprite name="0"> with one <sprite name="+1"> "Add +2 <sprite name=Attack> if drawn during an action with <sprite name=Lost>"',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_Spellweaver2_Lost_P1]"}),
    (2, "Hex", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Curse>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Curse_P1]"}),
    (2, "Thunderclap", 'Add one <sprite name="+1"> <sprite name=Stun>',
     {"CardsToAdd": "[AMC_Stun_P1]"}),
    (2, "Frostfire", 'Add one <sprite name="+2"> <sprite name=CreateMixed-FI>',
     {"CardsToAdd": "[AMC_GH2E_Spellweaver2_FireIce_P2]"}),
    (1, "Unfazed", 'Ignore scenario effects and remove one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_P0]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # Frosthaven's own "once each scenario, negate the damage" (Frozen Fist perk 11: a hidden, optional PreventFirstDamage
    # bonus with one use) that then performs Invisible + Stun on yourself (Metal Mosaic perk 10: the bonus's AbilityData,
    # TriggerActiveBonusAbilities). Verify on the bench.
    (1, "Vanishing Ward", 'Once each scenario, when you would suffer <sprite name=Damage>, you may gain <sprite name=Invisible> and <sprite name=Stun> to negate the <sprite name=Damage>',
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    APreventDamage:
        AutoTrigger: True
        SkipAnim: True
        Filter: Self
        PreventDamage: 1
        ActiveBonus:
            Duration: Persistent
            Behaviour: PreventFirstDamage
            XPTracker: [0]
            Hidden: True
            ToggleIsOptional: True
            ListLayoutOverride: [$PERK_Spellweaver2_08_Desc$, Positive_DamagePrevent]
            Vars:
                TriggerActiveBonusAbilities: CustomAbility.PerformActiveBonusAbility(TriggerBehaviour.ActiveBonus, thisContext)
            AbilityData:
                Filter: Self
                AutoTrigger: True
                NullTargeting: True
                Conditions: [Invisible, Stun]"""}),
    # NEEDS PLUGIN: the 1st edition's Etheric Bond is answered by the plugin for the 1st edition Reviving Ether; the 2e
    # card is 32401 (see NOTES.md, shared changes wanted)
    (1, "Etheric Bond", "Whenever you short rest, if <i>Reviving Ether</i> is in your discard pile, first <sprite name=Recover> it",
     {"Flags": "[Passive]"}),
    # two linked boxes: one perk costing two marks (as the Scoundrel's Cloak of Invisibility). Frosthaven's own lost-action
    # trigger (Geminate perk 7: DuringActionAbilityOnActionEnded + the performed half's CardPile), once per turn; the Bless
    # comes right after the lost action instead of at the end of the turn (NOTES.md)
    (1, "Arcane Blessing", "At the end of each of your turns during which you performed an action with <sprite name=Lost>, gain <sprite name=Bless>",
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    TActive:
        AutoTrigger: True
        AddActiveBonus:
            Bless: True
            Filter: Self
            AutoTrigger: True
            SkipAnim: True
            CanUndo: False
        ActiveBonus:
            Duration: Persistent
            Behaviour: DuringActionAbilityOnActionEnded
            Restriction: OncePerTurn
            Hidden: True
            Vars:
                LastPerformedAbility: TriggerActor.LastAbilityPerformed
                LostPile: LastPerformedAbility?.GetAbilityBaseCard()?.SelectedAction?.CardPile
                ValidateDidLoseCard: LostPile == "Lost" || LostPile == "PermanentlyLost\""""}),
]

MASTERIES = [
    # mastery 2 of the sheet; the 1st edition Spellweaver's Deep Reserves is the same rule and its plugin tag counts any
    # character's lost halves. Mastery 1 (consume both Fire and Ice in one turn, six times) has no tag yet (NOTES.md).
    ("Deep Reserves", "In a single scenario, perform four different <sprite name=Lost> actions twice each",
     """\
# the plugin's tag (GHMasteries.cs LossActionsRepeated): Value different card halves with a lost icon each performed
# Amount times
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_LossActionsRepeated]
        CharacterIDs:
            ActorID: Spellweaver2ID
        Value: 4
        Amount: 2"""),
]

NEW_CARDS = {
    "AMC_GH2E_Spellweaver2_Wild_P0": 'MathModifier: "+0"\nAbilities:\n    InfuseAbility:\n        Infuse: [Any]\n        ShowElementPicker: True\n',
    "AMC_GH2E_Spellweaver2_FireIce_P2": 'MathModifier: "+2"\nAbilities:\n    InfuseAbility:\n        Infuse: [Fire, Ice]\n        ShowElementPicker: True\n',
    # +1 now; the "+2 more during a lost action" needs the plugin (NOTES.md)
    "AMC_GH2E_Spellweaver2_Lost_P1": 'MathModifier: "+1"\n',
}

STRINGS = {}
