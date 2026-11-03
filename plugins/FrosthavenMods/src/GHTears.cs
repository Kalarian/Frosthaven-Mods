using System;
using System.Collections.Generic;
using System.Linq;
using AStar;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// The Gloomhaven 2nd edition Nightshroud's Tears (class mat; Tears stay Frosthaven's Deathwalker shadows, the user's
    /// decision A5, owned by the Nightshroud who placed them) and the cards that read them. Only Nightshroud2ID heroes
    /// and her pack cards are touched: inert without the 2e pack.
    ///  - "You may place a maximum of two Tears at any time (you may remove one to replace it elsewhere)": the game caps
    ///    shadows at 5 and counts every shadow (CAbilityCreateShadow, PreCreateShadow). While a Nightshroud is in the
    ///    scenario that step counts only the placer's own: hers capped at 2, a Deathwalker's (anyone else's) at 5 without
    ///    her Tears. Over the cap, the game's own "remove extras" choice follows (any of hers, the new one included).
    ///  - "Whenever you enter a hex containing a Tear during an ability, you may remove the Tear to lose Invisible at the
    ///    end of the ability" (2e FAQ: removed at once, interrupting the move; Invisible goes at the end of the move or
    ///    teleport; a teleport must end on the Tear; a Tear placed under you counts only after you leave and come back):
    ///    each hex her own move enters (a jump: its last hex; a teleport: where it lands) that holds one of her Tears
    ///    stacks a RemoveShadow on that hex, interrupting the move (the game resumes it after); the player confirms or
    ///    skips it ("may"). Removed: Invisible goes just before her next ability starts, or at her turn's end. Offered
    ///    while she is Invisible or Prepare for the Kill is active (otherwise the removal would do nothing).
    ///  - "Whenever you remove a Tear" (Prepare for the Kill, card 32916; removals for the two-Tear limit count, the user's
    ///    default): every Tear of hers destroyed by her own ability is logged; with Prepare for the Kill active she is
    ///    offered Strengthen (self, skippable = "you may discard this card"); taken, the card's bonus ends (the card goes
    ///    to her discard) and all adjacent enemies suffer Damage 2.
    ///  - Tear hexes ("Some of your abilities have Tear hexes. When you perform the ability, you may place one Tear in
    ///    that hex"): the hex is a blue hex of the card's area (tag MiscVars GH2ETearHexArea; Banner Spear's ally-hex
    ///    flag, so it turns with the area). Such an area doesn't need an ally in its blue hex (the game's Banner Spear
    ///    rule) and doesn't affect a figure standing there. The card's Tear placement (tag GH2ETearHex) may place only in
    ///    the blue hex(es) of that area, and only when the area's ability was performed (2e FAQ: "you must perform the
    ///    main-line ability to place the Tear"). Shadowstrike's teleport (tag GH2ETearHexTeleport) may land only there.
    ///  - Push paths (Nightfall top, Voice of the Night bottom): a Nightshroud's push marks the pushed figure's temp vars
    ///    GH2EPushTear (true if it entered a hex with one of her Tears) and GH2EPushTearTiles (the first such hex) for
    ///    the card's riders.
    ///  - Black Knives top (card 32908): "+1 Attack to the second attack if the first attack kills an enemy".
    ///  - Swallowed by Night bottom (card 32927): "When this card is moved from your active area to your lost pile, you
    ///    become exhausted".
    /// Logs "GHRule {Tears|TearHex|TearRemoved|PrepareForTheKill|PushPathTear|BlackKnives|SwallowedByNight} card {id} ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHTears
    {
        public const string OwnTag = "GH2ENightshroudRule";       // abilities this plugin stacks
        private const string EnterTag = "GH2ETearEnterMove";        // value: the move ability's ID
        private const string PrepareTag = "GH2EPrepareForTheKill";
        private const string AreaTag = "GH2ETearHexArea", HexTag = "GH2ETearHex", TeleportTag = "GH2ETearHexTeleport";

        private static readonly Dictionary<string, string> PendingLose = new Dictionary<string, string>();   // hero guid -> move ID
        private static readonly HashSet<string> Offered = new HashSet<string>();
        private static readonly HashSet<string> PrepareOffered = new HashSet<string>();
        private static readonly HashSet<string> Removed = new HashSet<string>();
        private static readonly HashSet<string> PendingExhaust = new HashSet<string>();
        private static readonly HashSet<Guid> Logged = new HashSet<Guid>();
        [ThreadStatic] private static int _suppress;

        public static void Reset()
        {
            PendingLose.Clear(); Offered.Clear(); PrepareOffered.Clear(); Removed.Clear(); PendingExhaust.Clear(); Logged.Clear();
        }

        private static void Log(string rule, int card, string detail) => Plugin.Log.LogInfo($"GHRule {rule} card {card} {detail}");

        private static void Report(string what, Exception e)
        {
            if (GHClasses.Reported.Add("GHTears " + what)) Plugin.Log.LogError("Nightshroud Tears: " + what + ": " + e);
        }

        private static bool Tagged(CAbility a, string tag)
        {
            try { return a != null && a.GetMiscVar(tag).GetBool(); }
            catch (Exception) { return false; }
        }

        public static bool IsHerTear(CObjectProp p, CActor her) =>
            p != null && p.ObjectType == ScenarioManager.ObjectImportType.Shadow && her != null && p.OwnerGUID == her.ActorGuid
            && !(p is CObjectShadow s && s.WillBeRemoved);

        private static bool IsNightshroudTear(CObjectProp p) =>
            p != null && p.ObjectType == ScenarioManager.ObjectImportType.Shadow && p.OwnerGUID != null
            && ScenarioManager.Scenario.AllPlayers.Any(h => GHNightshroud.Is(h) && h.ActorGuid == p.OwnerGUID);

        private static void Stack(CActor her, CAbility ability, bool interrupt)
        {
            if (PhaseManager.CurrentPhaseAction == null) return;
            if (GameState.InternalCurrentActor != her) GameState.OverrideCurrentActorForOneAction(her);
            PhaseManager.CurrentPhaseAction.StackNextAbilities(new List<CAbility> { ability }, her, killAfter: false, stackToNextCurrent: true,
                copyCurrentActionID: false, interruptCurrent: interrupt);
        }

        // ------------------------------------------------------------------ the two-Tear limit

        [HarmonyPatch(typeof(CAbilityCreateShadow), nameof(CAbilityCreateShadow.Perform))]
        [HarmonyPrefix]
        private static bool Limit(CAbilityCreateShadow __instance, ref bool __result)
        {
            // the engine's own PreCreateShadow step re-enters Perform for the new state and returns false; that re-entry runs
            // outside the try below, so an engine exception in it is not swallowed into a second engine Perform of the
            // already-advanced state (the catch's "let the engine run" is only for a failure before any state change here)
            try
            {
                if (__instance.m_State != CAbilityCreateShadow.CreateShadowState.PreCreateShadow) return true;
                CActor actor = __instance.TargetingActor;
                if (actor == null || CMirrorDamageActiveBonus_MirrorAbilities.GetMirrorAbilityBehaviour() != null) return true;
                bool mine = GHNightshroud.Is(actor);
                if (!mine && !ScenarioManager.CurrentScenarioState.ShadowProps.Any(IsNightshroudTear)) return true;
                Func<CObjectProp, bool> counts = mine ? (Func<CObjectProp, bool>)(p => p.OwnerGUID == actor.ActorGuid) : p => !IsNightshroudTear(p);
                int cap = mine ? 2 : 5;
                List<CObjectProp> own = ScenarioManager.CurrentScenarioState.ShadowProps.Where(counts).ToList();
                ScenarioRuleClient.MessageHandler(new CRefreshWorldSpaceStarHexDisplay_MessageData());
                int leaving = own.Count(s => s is CObjectShadow cs && cs.WillBeRemoved);
                List<CTile> selected = __instance.TilesSelected;
                Traverse tr = Traverse.Create(__instance);
                if (own.Count + selected.Count - leaving <= cap)
                {
                    tr.Field("m_CanUndo").SetValue(false);
                    tr.Property("m_State").SetValue(CAbilityCreateShadow.CreateShadowState.CreatingShadow);
                }
                else
                {
                    List<CObjectProp> cancel = tr.Field("m_cancelShadows").GetValue<List<CObjectProp>>();
                    cancel.Clear();
                    foreach (CTile t in selected) cancel.Add(CAbilityCreateShadow.SpawnShadow(t, new CVector3(0f, 0f, 0f), 0f, __instance, actor));
                    List<CTile> tiles = ScenarioManager.CurrentScenarioState.ShadowProps
                        .Where(p => counts(p) && !(p is CObjectShadow cs && cs.WillBeRemoved)).Select(p => p.PropTile).ToList();
                    // the hexes this placement put Tears in; the game reuses TilesSelected for the removal choice, so a
                    // card reading it afterwards (Terror Blade's "not adjacent to each other") would see the removed Tears:
                    // restored when the ability ends (PlacedBack)
                    if (mine) { Placed.Remove(__instance); Placed.Add(__instance, selected.ToList()); }
                    tr.Field("_isRemovingShadows").SetValue(true);
                    if (__instance.TilesInRange == null) __instance.TilesInRange = new List<CTile>();
                    __instance.TilesInRange.Clear();
                    __instance.TilesInRange.AddRange(tiles);
                    selected.Clear();
                    __instance.NumberTargets = tiles.Count - cap;
                    tr.Field("m_NumberTargetsRemaining").SetValue(__instance.NumberTargets);
                    tr.Property("m_State").SetValue(CAbilityCreateShadow.CreateShadowState.RemoveShadowPositions);
                    if (mine) Log("Tears", GHNightshroud.CardId(__instance), $"two-Tear limit: {tiles.Count} Tears, choose {tiles.Count - cap} to remove");
                }
            }
            catch (Exception e)
            {
                Report("limit", e);
                return true;
            }
            __instance.Perform();
            __result = false;
            return false;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CAbilityCreateShadow, List<CTile>> Placed =
            new System.Runtime.CompilerServices.ConditionalWeakTable<CAbilityCreateShadow, List<CTile>>();

        [HarmonyPatch(typeof(CAbilityCreateShadow), nameof(CAbilityCreateShadow.AbilityEnded))]
        [HarmonyPostfix]
        private static void PlacedBack(CAbilityCreateShadow __instance)
        {
            try
            {
                if (!Placed.TryGetValue(__instance, out List<CTile> tiles)) return;
                Placed.Remove(__instance);
                if (!__instance.AbilityHasHappened || __instance.TilesSelected == null) return;
                __instance.TilesSelected.Clear();
                __instance.TilesSelected.AddRange(tiles);
                Log("Tears", GHNightshroud.CardId(__instance), $"{__instance.Name}: placed at {string.Join(" ", tiles.Select(t => t.m_ArrayIndex.ToString()))} (over the limit; the hexes the card reads)");
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud Tears: placed tiles: " + e); }
        }

        // ------------------------------------------------------------------ Enervating Wound bottom

        // "Whenever an enemy dies within range 2, you MAY discard this card to grant whoever killed that enemy: Heal 2,
        // self" (card 32909). The engine spends the card's one slot as soon as a death passes the bonus's requirements and
        // the Nightshroud performs the heal on the killer. Here the Nightshroud's player is asked first (the game's yes/no
        // box): no = the card stays for a later death; yes = the killer performs Heal 2 targeting itself (its own heal
        // bonuses apply) and the card's slot is used (it goes to the discard pile). Outside an action phase the engine's own
        // path runs (logged).
        public const int EnervatingWoundCard = 32909;

        [HarmonyPatch(typeof(CDuringActionAbilityActiveBonus_TriggerAbilityOnDeath), nameof(CDuringActionAbilityActiveBonus_TriggerAbilityOnDeath.OnDeath))]
        [HarmonyPrefix]
        private static bool EnervatingWound(CDuringActionAbilityActiveBonus_TriggerAbilityOnDeath __instance, CActor actor, CAbility causeOfDeathAbility)
        {
            try
            {
                CActiveBonus bonus = __instance.ActiveBonus;
                if (bonus?.BaseCard == null || bonus.BaseCard.ID != EnervatingWoundCard || bonus.Finished() || bonus.Finishing()) return true;
                CActor her = bonus.Caster;
                if (!GHNightshroud.Is(her) || !bonus.IsActiveBonusToggledAndNotRestricted(actor)) return true;
                CActor killer = causeOfDeathAbility?.TargetingActor;
                if (killer == null || killer.IsDead || !(killer == her || CActor.AreActorsAllied(her.Type, killer.Type, her, killer))) return false;
                if (killer.Health >= killer.MaxHealth)
                {
                    Log("EnervatingWound", EnervatingWoundCard, $"{actor.GetPrefabName()} died; the killer {killer.GetPrefabName()} has full hit points: not offered (the card stays)");
                    return false;
                }
                if (PhaseManager.CurrentPhaseAction == null)
                {
                    Log("EnervatingWound", EnervatingWoundCard, "outside an action phase: the engine's own trigger (the Nightshroud heals the killer)");
                    return true;
                }
                bool yes = GHPlague.Ask(her, false, "FHMODS_NS_ENERVATE", "FHMODS_NS_YES", "FHMODS_NS_NO", killer.GetPrefabName(), actor.GetPrefabName());
                if (!yes)
                {
                    Log("EnervatingWound", EnervatingWoundCard, $"{actor.GetPrefabName()} died: declined, the card stays active");
                    return false;
                }
                CAbility heal = CAbilityHeal.CreateDefaultHeal(2, 1, 1, isMonster: false, new CAbilityFilterContainer(CAbilityFilter.EFilterTargetType.Self));
                if (heal.MiscAbilityData == null) heal.MiscAbilityData = new AbilityData.MiscAbilityData();
                heal.MiscAbilityData.AutotriggerAbility = true;
                heal.MiscAbilityData.IgnoreStun = true;
                heal.SetCanSkip(canSkip: false);
                heal.CanUndoOverride = false;
                heal.SetMiscVar(OwnTag, new TValue(true));
                Stack(killer, heal, interrupt: true);
                bonus.UpdateXPTracker();
                bonus.FinishIfNoRemaining();
                Log("EnervatingWound", EnervatingWoundCard, $"{actor.GetPrefabName()} died: the card is discarded, {killer.GetPrefabName()} (the killer) performs Heal 2, self");
                return false;
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud: Enervating Wound: " + e); return true; }
        }

        private static readonly Dictionary<string, string> Text = new Dictionary<string, string>
        {
            ["FHMODS_NS_ENERVATE"] = "Enervating Wound: {1} died. Discard this card to grant {0} (who killed it): Heal 2, self?",
            ["FHMODS_NS_YES"] = "Discard it",
            ["FHMODS_NS_NO"] = "Keep it",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_NS_", StringComparison.Ordinal) && Text.TryGetValue(term, out string t)) __result = t;
        }

        // ------------------------------------------------------------------ Tear-only tests (not a Deathwalker's shadow)

        // Marked for Death bottom, Into the Night bottom, Terror Blade bottom (teleports) and Swallowed by Night top (the
        // kill's requirement) test "a hex containing a Tear" with CustomAbility.SteppedOnShadow, which any shadow passes (a
        // Deathwalker's too). Steps tagged GH2ETearOnly count only a Nightshroud's Tears; Terror Blade's teleport (tag
        // GH2ETearPlacedOnly) only the two Tears its own card placed this action ("Teleport to one of those hexes").
        private const string TearOnlyTag = "GH2ETearOnly", PlacedOnlyTag = "GH2ETearPlacedOnly";
        [ThreadStatic] private static int s_TearOnly;

        private static bool HasNightshroudTear(CTile t) => t != null && t.m_Props.Any(p => IsNightshroudTear(p) && !(p is CObjectShadow s && s.WillBeRemoved));

        [HarmonyPatch(typeof(CAbilityTeleport2), nameof(CAbilityTeleport2.GetTeleportTiles))]
        [HarmonyPostfix]
        private static void TeleportTearOnly(CAbilityTeleport2 __instance, ref List<CTile> __result)
        {
            try
            {
                if (__result == null || !Tagged(__instance, TearOnlyTag)) return;
                int before = __result.Count;
                List<CTile> keep = __result.Where(HasNightshroudTear).ToList();
                if (Tagged(__instance, PlacedOnlyTag))
                {
                    int card = GHNightshroud.CardId(__instance);
                    var placed = new HashSet<CTile>((PhaseManager.CurrentPhaseAction?.PreviousPhaseAbilities ?? new List<CPhaseAction.CPhaseAbility>())
                        .Select(x => x?.m_Ability).OfType<CAbilityCreateShadow>()
                        .Where(a => a.TargetingActor == __instance.TargetingActor && GHNightshroud.CardId(a) == card && a.AbilityHasHappened)
                        .SelectMany(a => a.TilesSelected ?? new List<CTile>()));
                    keep = keep.Where(placed.Contains).ToList();
                }
                __result = keep;
                if (Logged.Add(__instance.ID))
                    Log("Tears", GHNightshroud.CardId(__instance), $"{__instance.Name}: {keep.Count} of {before} hexes hold {(Tagged(__instance, PlacedOnlyTag) ? "a Tear this card placed" : "a Nightshroud's Tear")}");
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud Tears: Tear-only teleport: " + e); }
        }

        [HarmonyPatch(typeof(CAbilityRequirements), nameof(CAbilityRequirements.MeetsAbilityRequirements), new[] { typeof(CActor), typeof(CAbility), typeof(CVarsContext), typeof(CItem), typeof(CActiveBonus) })]
        [HarmonyPrefix]
        private static void TearOnlyStart(CAbility ability, out bool __state)
        {
            __state = Tagged(ability, TearOnlyTag);
            if (__state) s_TearOnly++;
        }

        [HarmonyPatch(typeof(CAbilityRequirements), nameof(CAbilityRequirements.MeetsAbilityRequirements), new[] { typeof(CActor), typeof(CAbility), typeof(CVarsContext), typeof(CItem), typeof(CActiveBonus) })]
        [HarmonyFinalizer]
        private static Exception TearOnlyEnd(Exception __exception, bool __state)
        {
            if (__state && s_TearOnly > 0) s_TearOnly--;
            return __exception;
        }

        [HarmonyPatch(typeof(CustomAbility), nameof(CustomAbility.SteppedOnShadow))]
        [HarmonyPostfix]
        private static void SteppedOnTear(CTile tile, ref bool __result)
        {
            if (s_TearOnly <= 0 || !__result) return;
            __result = HasNightshroudTear(tile);
        }

        // ------------------------------------------------------------------ entering a Tear

        [HarmonyPatch(typeof(CPlayerActor), "OnMoved")]
        [HarmonyPostfix]
        private static void Entered(CAbility moveAbility, CActor movedActor, bool finalMovement, int thisMoveHexes)
        {
            try
            {
                if (!GHNightshroud.Is(movedActor) || moveAbility == null || moveAbility.TargetingActor != movedActor || Tagged(moveAbility, OwnTag)) return;
                bool counts, interrupt;
                if (moveAbility is CAbilityMove move) { counts = move.Jump ? finalMovement : thisMoveHexes != 0; interrupt = true; }
                else if (moveAbility is CAbilityTeleport2 || moveAbility is CAbilityTeleport) { counts = finalMovement; interrupt = false; }
                else return;
                if (!counts) return;
                CTile tile = movedActor.Tile;
                if (tile == null || !tile.m_Props.Any(p => IsHerTear(p, movedActor))) return;
                if (!GHNightshroud.HasInvisible(movedActor) && GHNightshroud.CardBonus(movedActor, GHNightshroud.PrepareCard) == null) return;
                if (!Offered.Add(moveAbility.ID + ":" + tile.m_ArrayIndex)) return;
                CAbility remove = CAbility.CreateAbility(CAbility.EAbilityType.RemoveShadow, new CAbilityFilterContainer(CAbilityFilter.EFilterTargetType.Self),
                    isMonster: false, isTargetedAbility: false, strength: 0, range: 0, numberTargets: 1);
                remove.HelpBoxTooltipLocKey = "GH2E_Nightshroud2_TEAR_ENTER_HELP";
                remove.SetCanSkip(canSkip: true);
                remove.CanUndoOverride = false;
                remove.SetMiscVar(EnterTag, new TValue(moveAbility.ID.ToString()));
                remove.SetMiscVar(OwnTag, new TValue(true));
                Stack(movedActor, remove, interrupt);
                Log("Tears", GHNightshroud.CardId(moveAbility), $"class mat: entered a Tear at {tile.m_ArrayIndex} during {moveAbility.Name}: may remove it to lose Invisible");
            }
            catch (Exception e) { Report("enter", e); }
        }

        /// <summary>The end of the ability in which she removed a Tear by entering it: the next ability of hers (not the
        /// move's own resumed copy, not one this plugin stacked) or her turn's end.</summary>
        public static void EndOfAbilityPoint(CActor her, CAbility starting)
        {
            if (her == null || !PendingLose.TryGetValue(her.ActorGuid, out string moveId)) return;
            if (starting != null && (starting.ID.ToString() == moveId || Tagged(starting, OwnTag))) return;
            PendingLose.Remove(her.ActorGuid);
            if (!GHNightshroud.HasInvisible(her)) return;
            her.Tokens.RemovePositiveToken(CCondition.EPositiveCondition.Invisible);
            Log("Tears", 0, "class mat: Invisible lost at the end of the ability (a Tear was removed by entering it)");
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.Start))]
        [HarmonyPrefix]
        private static void BeforeStart(CAbility __instance, CActor targetingActor)
        {
            try
            {
                CActor her = targetingActor ?? __instance.TargetingActor;
                if (GHNightshroud.Is(her)) EndOfAbilityPoint(her, __instance);
            }
            catch (Exception e) { Report("ability start", e); }
        }

        // ------------------------------------------------------------------ removing a Tear

        [HarmonyPatch(typeof(CAbilityCreateShadow), nameof(CAbilityCreateShadow.Undo))]
        [HarmonyPrefix]
        private static void UndoStart() => _suppress++;

        [HarmonyPatch(typeof(CAbilityCreateShadow), nameof(CAbilityCreateShadow.Undo))]
        [HarmonyFinalizer]
        private static Exception UndoEnd(Exception __exception) { _suppress--; return __exception; }

        [HarmonyPatch(typeof(CAbilityCreateShadow), nameof(CAbilityCreateShadow.AbilityEnded))]
        [HarmonyPrefix]
        private static void EndStart(CAbilityCreateShadow __instance, out bool __state)
        {
            __state = !__instance.AbilityHasHappened;   // the game takes back the unplaced preview Tears: not a removal
            if (__state) _suppress++;
        }

        [HarmonyPatch(typeof(CAbilityCreateShadow), nameof(CAbilityCreateShadow.AbilityEnded))]
        [HarmonyFinalizer]
        private static Exception EndEnd(Exception __exception, bool __state) { if (__state) _suppress--; return __exception; }

        [HarmonyPatch(typeof(CObjectShadow), nameof(CObjectShadow.DestroyProp))]
        [HarmonyPostfix]
        private static void TearGone(CObjectShadow __instance, CAbility destroyedByAbility)
        {
            try
            {
                if (_suppress > 0 || __instance?.OwnerGUID == null || PhaseManager.CurrentPhaseAction == null) return;
                CPlayerActor her = GHNightshroud.Heroes().FirstOrDefault(h => h.ActorGuid == __instance.OwnerGUID);
                if (her == null || destroyedByAbility?.TargetingActor != her) return;
                if (!Removed.Add(__instance.PropGuid ?? (__instance.ArrayIndex.ToString() + destroyedByAbility.ID))) return;
                int card = GHNightshroud.CardId(destroyedByAbility);
                Log("TearRemoved", card, $"a Tear at {__instance.ArrayIndex} removed by {destroyedByAbility.Name}");
                if (destroyedByAbility.GetMiscVar(EnterTag).isValid && GHNightshroud.HasInvisible(her))
                    PendingLose[her.ActorGuid] = destroyedByAbility.GetMiscVar(EnterTag).GetString();
                OfferPrepare(her);
            }
            catch (Exception e) { Report("removal", e); }
        }

        private static void OfferPrepare(CPlayerActor her)
        {
            if (GHNightshroud.CardBonus(her, GHNightshroud.PrepareCard) == null || !PrepareOffered.Add(her.ActorGuid)) return;
            CAbility strengthen = CAbilityNullTargeting.CreateDefaultNullTargeting(0, 1, isMonster: false, "PowerUp",
                new CAbilityFilterContainer(CAbilityFilter.EFilterTargetType.Self),
                new List<CCondition.EPositiveCondition> { CCondition.EPositiveCondition.Strengthen }, null, "GH2EPrepareForTheKill");
            strengthen.HelpBoxTooltipLocKey = "GH2E_Nightshroud2_PREPARE_HELP";
            strengthen.SetCanSkip(canSkip: true);
            strengthen.CanUndoOverride = false;
            strengthen.SetMiscVar(PrepareTag, new TValue(true));
            strengthen.SetMiscVar(OwnTag, new TValue(true));
            Stack(her, strengthen, interrupt: false);
            Log("PrepareForTheKill", GHNightshroud.PrepareCard, "a Tear was removed: Strengthen and Damage 2 offered (may discard the card)");
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void OwnAbilityEnded(CAbility __instance)
        {
            try
            {
                if (!Tagged(__instance, PrepareTag)) return;
                CActor her = __instance.TargetingActor;
                if (her == null) return;
                PrepareOffered.Remove(her.ActorGuid);
                if (!__instance.AbilityHasHappened) { Log("PrepareForTheKill", GHNightshroud.PrepareCard, "declined: the card stays active"); return; }
                GHNightshroud.CardBonus(her, GHNightshroud.PrepareCard)?.Finish();
                CAbility dmg = CAbilityDamage.CreateDefaultDamage(2, 1, -1, isMonster: false, "Debuff", isMultipass: true,
                    new CAbilityFilterContainer(CAbilityFilter.EFilterTargetType.Enemy));
                if (dmg.MiscAbilityData == null) dmg.MiscAbilityData = new AbilityData.MiscAbilityData();
                dmg.MiscAbilityData.AutotriggerAbility = true;
                dmg.SetCanSkip(canSkip: false);
                dmg.SetMiscVar(OwnTag, new TValue(true));
                Stack(her, dmg, interrupt: false);
                Log("PrepareForTheKill", GHNightshroud.PrepareCard, "taken: Strengthen, the card is discarded, all adjacent enemies suffer Damage 2");
            }
            catch (Exception e) { Report("Prepare for the Kill", e); }
        }

        // ------------------------------------------------------------------ Tear hexes

        private static bool MainLinePerformed(CAbility area)
        {
            if (area.AbilityType == CAbility.EAbilityType.NullTargeting)
                return CustomAbility.ActorsOnRedHexes(area, false).Any(a => a != null && !a.IsDead
                    && area.AbilityFilter.IsValidTarget(a, area.TargetingActor, area.IsTargetedAbility, useTargetOriginalType: false, area.MiscAbilityData?.CanTargetInvisible));
            return area.AbilityHasHappened || (area.ActorsTargeted?.Count ?? 0) > 0;
        }

        /// <summary>The Tear hexes (the blue hexes) of the area ability the given ability of the same card belongs to,
        /// as placed this action; none if that ability wasn't performed.</summary>
        private static List<CTile> TearHexes(CAbility ability, string why)
        {
            var none = new List<CTile>();
            CPhaseAction pa = PhaseManager.CurrentPhaseAction;
            if (pa == null) return none;
            int card = GHNightshroud.CardId(ability);
            CAbility area = pa.PreviousPhaseAbilities.Select(x => x?.m_Ability)
                .LastOrDefault(a => a != null && Tagged(a, AreaTag) && a.TargetingActor == ability.TargetingActor && GHNightshroud.CardId(a) == card);
            if (area == null)
            {
                if (Logged.Add(ability.ID)) Log("TearHex", card, $"{why}: no area ability of this card was performed");
                return none;
            }
            if (!MainLinePerformed(area))
            {
                if (Logged.Add(ability.ID)) Log("TearHex", card, $"{why}: {area.Name} was not performed (FAQ: the main-line ability must be performed)");
                return none;
            }
            if (area.FlagsOfValidTiles == null) return none;
            List<CTile> tiles = area.FlagsOfValidTiles
                .Where(kv => (kv.Value & CAreaEffect.EHexFlag.Ally) != 0 && (kv.Value & CAreaEffect.EHexFlag.Enemy) == 0)
                .Select(kv => kv.Key).ToList();
            if (Logged.Add(ability.ID)) Log("TearHex", card, $"{why}: Tear hex(es) of {area.Name}: {string.Join(" ", tiles.Select(t => t.m_ArrayIndex.ToString()))}");
            return tiles;
        }

        [HarmonyPatch(typeof(SharedAbilityTargeting), nameof(SharedAbilityTargeting.GetValidActorsInRange), new[] { typeof(CAbility) })]
        [HarmonyPostfix]
        private static void TearHexTargets(CAbility ability)
        {
            try
            {
                if (!(ability is CAbilityCreateShadow) || !Tagged(ability, HexTag)) return;
                List<CTile> tiles = TearHexes(ability, "place a Tear");
                if (ability.TilesInRange == null) ability.TilesInRange = new List<CTile>();
                ability.TilesInRange.Clear();
                ability.TilesInRange.AddRange(tiles);
            }
            catch (Exception e) { Report("Tear hex", e); }
        }

        [HarmonyPatch(typeof(CAbilityTeleport2), nameof(CAbilityTeleport2.GetTeleportTiles))]
        [HarmonyPostfix]
        private static void TeleportToTearHex(CAbilityTeleport2 __instance, ref List<CTile> __result)
        {
            try
            {
                if (!Tagged(__instance, TeleportTag) || __result == null) return;
                List<CTile> hexes = TearHexes(__instance, "Teleport to the Tear hex");
                __result = __result.Where(hexes.Contains).ToList();
            }
            catch (Exception e) { Report("Tear hex teleport", e); }
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.RequiresAllyTargeting))]
        [HarmonyPostfix]
        private static void NoAllyNeeded(CAbility __instance, ref bool __result)
        {
            if (__result && Tagged(__instance, AreaTag)) __result = false;
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.ValidAlliesTargeted))]
        [HarmonyPostfix]
        private static void NoAllyNeeded2(CAbility __instance, ref bool __result)
        {
            if (!__result && Tagged(__instance, AreaTag)) __result = true;
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.UpdateAreaEffect))]
        [HarmonyPostfix]
        private static void TearHexNotAffected(CAbility __instance)
        {
            try
            {
                if (!Tagged(__instance, AreaTag)) return;
                Dictionary<CTile, CAreaEffect.EHexFlag> flags = __instance.FlagsOfValidTiles;
                if (flags == null) return;
                __instance.ValidTilesInAreaAffected?.RemoveAll(t => flags.TryGetValue(t, out CAreaEffect.EHexFlag f)
                    && (f & CAreaEffect.EHexFlag.Ally) != 0 && (f & CAreaEffect.EHexFlag.Enemy) == 0);
            }
            catch (Exception e) { Report("Tear hex area", e); }
        }

        // ------------------------------------------------------------------ push paths through Tears

        [HarmonyPatch(typeof(CAbilityPush), nameof(CAbilityPush.Perform))]
        [HarmonyPrefix]
        private static void PushPath(CAbilityPush __instance)
        {
            try
            {
                if (__instance.State != CAbilityPush.EPushState.ActorFinalizePush) return;
                CActor her = __instance.TargetingActor, target = __instance.CurrentTarget;
                if (!GHNightshroud.Is(her) || target == null || __instance.m_AbilityPath == null) return;
                if (Traverse.Create(__instance).Field("m_AbilityPathProgressIndex").GetValue<int>() != 0) return;
                CTile first = null;
                foreach (Point pt in __instance.m_AbilityPath)
                {
                    CTile t = ScenarioManager.Tiles[pt.X, pt.Y];
                    if (t != null && t.m_Props.Any(p => IsHerTear(p, her))) { first = t; break; }
                }
                target.SetTempVar("GH2EPushTear", new TValue(first != null));
                target.SetTempVar("GH2EPushTearTiles", new TValue(first == null ? new List<CTile>() : new List<CTile> { first }));
                if (first != null)
                    Log("PushPathTear", GHNightshroud.CardId(__instance), $"{target.GetPrefabName()} pushed into a Tear at {first.m_ArrayIndex}");
            }
            catch (Exception e) { Report("push path", e); }
        }

        /// <summary>Nightfall top's hazardous terrain damage (card step TTearDamage) on a figure pushed into a Tear: one rule
        /// line per figure it hits, so a bench can see the damage step ran (the card's own data does the damage).</summary>
        // (2026-11-03: CAbilityDamage.ApplyToActor only runs for damage buffs; the real damage goes through
        // GameState.ActorBeenDamaged with the step as damageAbility, so the line never came)
        [HarmonyPatch(typeof(GameState), nameof(GameState.ActorBeenDamaged),
            new[] { typeof(CActor), typeof(int), typeof(bool), typeof(GameState.DamageData), typeof(bool), typeof(CActor), typeof(CAbility), typeof(CAbility.EAbilityType),
                    typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(bool), typeof(int), typeof(bool) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal,
                    ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
        [HarmonyPostfix]
        private static void NightfallTearDamage(CActor actor, int strength, CAbility damageAbility)
        {
            try
            {
                if (damageAbility == null || actor == null || damageAbility.Name != "TTearDamage") return;
                if (!GHNightshroud.Is(damageAbility.TargetingActor) || GHNightshroud.CardId(damageAbility) != NightfallCard) return;
                Log("TearDamage", NightfallCard, $"{actor.GetPrefabName()} pushed into a Tear suffers hazardous terrain damage {strength}");
            }
            catch (Exception e) { Report("Nightfall damage", e); }
        }

        private const int NightfallCard = 32910;

        // ------------------------------------------------------------------ Black Knives

        [HarmonyPatch(typeof(CAbilityAttack), "AttackActor")]
        [HarmonyPrefix]
        private static void BlackKnives(CAbilityAttack ability, CAttackSummary.TargetSummary attackingTargetSummary, ref int modifiedStrength)
        {
            try
            {
                if (ability == null || attackingTargetSummary == null || GHNightshroud.CardId(ability) != GHNightshroud.BlackKnivesCard) return;
                if (!GHNightshroud.Is(ability.TargetingActor) || ability.ActorsTargeted == null || ability.ActorsTargeted.Count != 1) return;
                CActor first = ability.ActorsTargeted[0];
                if (first == null || !first.IsDead || first == attackingTargetSummary.ActorToAttack) return;
                int s = attackingTargetSummary.AttackAbilityWithOverrides?.ModifiedStrength() ?? ability.ModifiedStrength();
                int delta = attackingTargetSummary.CalcExpressionString(s + 1, 0, false, false) - attackingTargetSummary.CalcExpressionString(s, 0, false, false);
                if (delta < 0) delta = 0;
                modifiedStrength += delta;
                Log("BlackKnives", GHNightshroud.BlackKnivesCard, $"the first attack killed {first.GetPrefabName()}: +1 Attack on the second ({delta} after the modifier)");
            }
            catch (Exception e) { Report("Black Knives", e); }
        }

        // ------------------------------------------------------------------ Swallowed by Night: exhausted when it goes to the lost pile

        [HarmonyPatch(typeof(CCharacterClass), "MoveAbilityCardToPile")]
        [HarmonyPrefix]
        private static void PileBefore(CAbilityCard abilityCard, out CBaseCard.ECardPile __state)
        {
            __state = abilityCard?.CurrentCardPile ?? CBaseCard.ECardPile.None;
        }

        [HarmonyPatch(typeof(CCharacterClass), "MoveAbilityCardToPile")]
        [HarmonyPostfix]
        private static void PileAfter(CCharacterClass __instance, CAbilityCard abilityCard, CBaseCard.ECardPile __state)
        {
            try
            {
                if (abilityCard == null || abilityCard.ID != GHNightshroud.SwallowedByNightCard) return;
                if (__state != CBaseCard.ECardPile.Activated || abilityCard.CurrentCardPile != CBaseCard.ECardPile.Lost) return;
                CPlayerActor her = ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => p.CharacterClass == __instance);
                if (her == null || her.IsDead) return;
                if (GameState.InternalCurrentActor == her && PhaseManager.CurrentPhase is CPhaseAction)
                {
                    her.ExhaustAfterAction = true;
                    Log("SwallowedByNight", GHNightshroud.SwallowedByNightCard, "moved from the active area to the lost pile: exhausted after this action");
                }
                else
                {
                    PendingExhaust.Add(her.ActorGuid);
                    Log("SwallowedByNight", GHNightshroud.SwallowedByNightCard, "moved from the active area to the lost pile: exhausted at the next phase change (before the round's end checks)");
                }
            }
            catch (Exception e) { Report("Swallowed by Night", e); }
        }

        // The card reaches the lost pile outside her action at a round's end (its last end-of-round slot): she is exhausted
        // at the very next phase change (the Final Fight precedent, GHBerserker2e), so before the scenario's end-of-round
        // checks and the next round; the next exhaustion check stays as a fallback.
        [HarmonyPatch(typeof(GameState), nameof(GameState.NextPhase))]
        [HarmonyPrefix]
        private static void ExhaustAtPhaseChange() => ExhaustPending();

        [HarmonyPatch(typeof(CPhasePlayerExhausted), "OnNextStep")]
        [HarmonyPrefix]
        private static void ExhaustPending()
        {
            try
            {
                if (PendingExhaust.Count == 0) return;
                foreach (CPlayerActor her in (ScenarioManager.Scenario?.PlayerActors ?? new List<CPlayerActor>()).Where(p => p != null && PendingExhaust.Contains(p.ActorGuid) && !p.IsDead).ToList())
                {
                    bool asleep = her.Tokens.HasKey(CCondition.ENegativeCondition.Sleep);
                    GameState.KillActor(her, her, CActor.ECauseOfDeath.NoMoreCards, out bool _, null, asleep);
                    Log("SwallowedByNight", GHNightshroud.SwallowedByNightCard, "exhausted");
                }
                PendingExhaust.Clear();
            }
            catch (Exception e) { PendingExhaust.Clear(); Report("Swallowed by Night exhaustion", e); }
        }
    }
}
