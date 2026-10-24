using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FH.Characters;
using FHGUI;
using FHGUI.DTO;
using FHGUI.Modules;
using FHGUI.States;
using FHRL;
using GLOOM;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// The outpost command behind the character sheet's look button (GHLooks): sets one character's look. Frosthaven has
    /// no skins (CharacterState has no field for one, and its screens pass none), so the look is a marker string in
    /// CampaignState.LootedChestTreasureTableIDs ("FHMODS:Look:&lt;character guid&gt;=ALT"), the inert, synced spot the
    /// masteries and Boneshaper count use: changed only inside this host-checked command, so every copy on every PC has
    /// it, and a player who joins later receives it with the state. No marker: the class's own look.
    /// </summary>
    public sealed class LookCommands
    {
        public const string CommandPath = "FrosthavenMods.SetLook";

        private static readonly MethodInfo BeginResult = AccessTools.Method(typeof(CampaignController), "BeginResult");
        private static readonly MethodInfo EndResult = AccessTools.Method(typeof(CampaignController), "EndResult");
        private static readonly Regex SkinName = new Regex("^[A-Za-z0-9_]{1,16}$");

        private readonly MultiplayerGameController _game;

        public LookCommands(MultiplayerGameController game) { _game = game; }

        /// <summary>Give <paramref name="who"/> the look <paramref name="skin"/> ("" for the class's own). Reads campaign
        /// state only, so every copy reaches the same verdict.</summary>
        [MultiplayerCommand(MultiplayerCommandExecType.Everybody)]
        public CampaignController.Result SetLook(CharacterID who, string skin)
        {
            CampaignController campaign = _game.CampaignController;
            string copy = CoopLog.Copy(_game);
            if (!campaign.CharacterController.TryGetCharacter(who, out CharacterState c))
            {
                CoopLog.Write($"LOOK REJECTED on={copy}: unknown character");
                throw new FHGameException("Look: unknown character.");
            }
            skin = skin ?? "";
            if (skin.Length > 0 && !SkinName.IsMatch(skin))
            {
                CoopLog.Write($"LOOK REJECTED on={copy}: bad look name");
                throw new FHGameException("Look: bad look name.");
            }
            BeginResult.Invoke(campaign, null);
            List<string> markers = campaign.State.LootedChestTreasureTableIDs;
            string prefix = GHLooks.Marker + c.ID.ID + "=";
            markers.RemoveAll(s => s != null && s.StartsWith(prefix, StringComparison.Ordinal));
            if (skin.Length > 0) markers.Add(prefix + skin);
            var result = (CampaignController.Result)EndResult.Invoke(campaign, null);
            CoopLog.Write($"LOOK APPLIED on={copy} {GiveRules.Name(c)}: {(skin.Length > 0 ? skin : "own look")}");
            return result;
        }
    }

    /// <summary>
    /// A Gloomhaven class's alternative look (the Brute's Boneguard), chosen per character with a button on the
    /// character sheet. Frosthaven never shows its inherited skin selector (that window, UIAdventurePartyAssemblyWindow,
    /// is Gloomhaven's) and its own screens pass no skin, so the look is applied where the model is asked for:
    /// <list type="bullet">
    /// <item>the character sheet's 3D model (FHCharacterSelectionScene): the character the sheet was opened for;</item>
    /// <item>everywhere else (the party scene, scenarios): the party member of that class (a party never has two).</item>
    /// </list>
    /// </summary>
    [HarmonyPatch]
    internal static class GHLooks
    {
        internal const string Marker = "FHMODS:Look:";
        private const string ButtonName = "FrosthavenMods_Look";
        private const float Scale = 0.7f;

        private static readonly ConditionalWeakTable<MultiplayerGameController, LookCommands> Handlers = new ConditionalWeakTable<MultiplayerGameController, LookCommands>();
        private static readonly MethodInfo Command = typeof(LookCommands).GetMethod(nameof(LookCommands.SetLook));

        /// <summary>The character whose sheet is opening (set around OutpostSceneController.OpenCharacterScreen).</summary>
        private static CharacterID _sheet;

        // ------------------------------------------------------------------ looks

        /// <summary>The character's look: a skin name, or "" for the class's own.</summary>
        internal static string LookOf(CampaignState state, CharacterID id)
        {
            string prefix = Marker + id.ID + "=";
            string marker = state?.LootedChestTreasureTableIDs?.FirstOrDefault(s => s != null && s.StartsWith(prefix, StringComparison.Ordinal));
            return marker == null ? "" : marker.Substring(prefix.Length);
        }

        private static ECharacter ModelOf(CharacterState c) => CrossReference.ClassDefToECharacterModel(c.ClassDef);

        /// <summary>The look of the party member playing <paramref name="model"/> ("" if none, or no campaign).</summary>
        internal static string PartyLook(ECharacter model)
        {
            try
            {
                CampaignState state = GHClassesRule.LiveState;
                CharacterState c = state?.PartyCharacters?.FirstOrDefault(p => p != null && ModelOf(p) == model);
                return c == null ? "" : LookOf(state, c.ID);
            }
            catch (Exception) { return ""; }
        }

        /// <summary>The looks <paramref name="model"/> has besides its own.</summary>
        private static List<string> Looks(ECharacter model) => GHModel.EntryFor(model)?.Skins.Keys.ToList() ?? new List<string>();

        private static string LookName(ECharacter model, string skin) =>
            skin.Length == 0 ? LocalizationManager.GetTranslation(model.ToString()) is string own && own.Length > 0 && own != model.ToString() ? own : model.ToString()
                             : LocalizationManager.GetTranslation($"{model}_SKIN_{skin}");

        // ------------------------------------------------------------------ the 3D model

        [HarmonyPatch(typeof(OutpostSceneController), nameof(OutpostSceneController.OpenCharacterScreen), new[] { typeof(CharacterDataDTO) })]
        [HarmonyPrefix]
        private static void SheetOpening(CharacterDataDTO character) => _sheet = character?.CharacterID;

        [HarmonyPatch(typeof(OutpostSceneController), nameof(OutpostSceneController.OpenCharacterScreen), new[] { typeof(CharacterState) })]
        [HarmonyPrefix]
        private static void SheetOpeningState(CharacterState character) => _sheet = character?.ID;

        [HarmonyPatch(typeof(OutpostSceneController), nameof(OutpostSceneController.OpenCharacterScreen), new[] { typeof(CharacterDataDTO) })]
        [HarmonyPostfix]
        private static void SheetOpened() => _sheet = null;

        [HarmonyPatch(typeof(OutpostSceneController), nameof(OutpostSceneController.OpenCharacterScreen), new[] { typeof(CharacterState) })]
        [HarmonyPostfix]
        private static void SheetOpenedState() => _sheet = null;

        /// <summary>The outpost's 3D displays pass no skin: give a Gloomhaven class's model the look it should wear. The
        /// models are kept per class and skin, so a different look is a different model. "" asks for the class's own
        /// look explicitly (GHClasses.Prefab reads null as "the party member's").</summary>
        [HarmonyPatch(typeof(Character3DDisplayManager), nameof(Character3DDisplayManager.Display))]
        [HarmonyPrefix]
        private static void Display(Component request, ECharacter character, ref string skin)
        {
            if (skin != null || character == ECharacter.None || !GHClasses.IsGloomhavenClass(character.ToString(), out _)) return;
            try
            {
                if (request != null && request.GetComponentInParent<FHCharacterSelectionScene>() != null)
                {
                    // the character sheet (or the class screen of a new character: no sheet, the class's own look)
                    CampaignState state = GHClassesRule.LiveState;
                    CharacterState c = null;
                    if (_sheet != null && state != null) c = state.AllCharacters.FirstOrDefault(x => x != null && _sheet.Equals(x.ID));
                    skin = c != null && ModelOf(c) == character ? LookOf(state, c.ID) : "";
                }
                else skin = PartyLook(character);
            }
            catch (Exception e)
            {
                skin = "";
                Plugin.Log.LogWarning("Gloomhaven looks: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ the button

        /// <summary>A "Skin: …" button under the character's name at the bottom of the sheet (above the tab icons), for a
        /// character of a class with another look, shown to the player who controls them, outside scenarios. Each click
        /// moves to the next look.</summary>
        [HarmonyPatch(typeof(EditCharacterViewState), nameof(EditCharacterViewState.OnShow))]
        [HarmonyPostfix]
        private static void SheetShown(EditCharacterViewState __instance)
        {
            GiveResourcesPatches.Safe("show the skin button", () =>
            {
                FHMapView view = __instance.View;
                CharacterPreviewModule preview = view == null ? null : view.Modules.Characters.CharacterPreview;
                if (preview == null) return;
                Transform button = (preview.transform.Find("CharacterPreviewModule_Slot") ?? preview.transform).Find(ButtonName);
                CharacterState c = SheetCharacter(view);
                List<string> looks = c == null ? null : Looks(ModelOf(c));
                bool show = looks != null && looks.Count > 0 && view.GameState?.ActiveScenario == null &&
                            (view.IsSoloGame || view.PlayerCharacters.Contains(c));
                if (!show) { button?.gameObject.SetActive(false); return; }
                if (button == null) button = Create(view, preview);
                if (button == null) return;
                button.gameObject.SetActive(true);
                button.GetComponent<FHButton>()?.SetNonLocalizedText(string.Format(Text.Get("FHMODS_LOOK"), LookName(ModelOf(c), LookOf(view.GameState, c.ID))));
            });
        }

        /// <summary>Every screen that uses the details module clears it first; the button goes with it.</summary>
        [HarmonyPatch(typeof(CharacterDetailsScreenModule), nameof(CharacterDetailsScreenModule.ClearAll))]
        [HarmonyPostfix]
        private static void Cleared()
        {
            FHMapView view = Singleton<FHMapChoreographer>.InstanceFast?.View;
            CharacterPreviewModule preview = view == null ? null : view.Modules.Characters.CharacterPreview;
            if (preview != null) (preview.transform.Find("CharacterPreviewModule_Slot") ?? preview.transform).Find(ButtonName)?.gameObject.SetActive(false);
        }

        private static CharacterState SheetCharacter(FHMapView view)
        {
            CharacterState c = view.SelectedCharacter;
            return c != null && c.PartyState != CharacterPartyStateType.CreationProcess ? c : null;
        }

        /// <summary>The sheet's preview slot (CharacterPreviewModule_Slot, 600x850) ends at the name: its "Name" text
        /// hangs from the slot's bottom edge, top 19 units above it, 54 tall, so it ends 35 below. The button is pinned
        /// under that, centred, in the gap above the bottom tab icons (button 40 tall at 0.7 scale).</summary>
        private static Transform Create(FHMapView view, CharacterPreviewModule preview)
        {
            UiDump.Once("Character sheet preview (name)", preview.transform);
            Transform slot = preview.transform.Find("CharacterPreviewModule_Slot") ?? preview.transform;
            FHButton button = ButtonCopy.Make(view, slot, ButtonName, "", OnClick);
            if (button == null) return null;
            var rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -54f);
            rt.sizeDelta = new Vector2(Mathf.Max(rt.sizeDelta.x * 1.9f, 330f), rt.sizeDelta.y);   // "Skin: Boneguard" on one line
            rt.localScale = new Vector3(Scale, Scale, 1f);
            foreach (TMPro.TMP_Text label in button.GetComponentsInChildren<TMPro.TMP_Text>(true)) label.enableWordWrapping = false;
            button.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            button.transform.SetAsLastSibling();   // drawn over the slot's backgrounds
            return button.transform;
        }

        private static void OnClick()
        {
            FHMapView view = Singleton<FHMapChoreographer>.InstanceFast?.View;
            CharacterState c = view == null ? null : SheetCharacter(view);
            if (c == null) return;
            if (!view.IsSoloGame && !view.PlayerCharacters.Contains(c))
            {
                GiveResourcesPatches.Message(view, "You can only change the look of characters you control.");
                return;
            }
            ECharacter model = ModelOf(c);
            List<string> looks = new List<string> { "" }.Concat(Looks(model)).ToList();
            string next = looks[(looks.IndexOf(LookOf(view.GameState, c.ID)) + 1) % looks.Count];
            CharacterID id = c.ID;
            FHMultiplayerClient.Promise promise = view.Choreographer.ExecuteCommand(LookCommands.CommandPath, id, next);
            promise.OnComplete(() => GiveResourcesPatches.Safe("show the new look", () => Refresh(view, id, model)));
            promise.OnFailed(() => GiveResourcesPatches.Message(view, "The look could not be changed."));
            if (view.IsSoloGame) Refresh(view, id, model);   // solo: ran synchronously
        }

        /// <summary>Show the sheet's model and button again with the character's look. The scene keeps its model while
        /// the class is the same, so it is reopened by force.</summary>
        private static void Refresh(FHMapView view, CharacterID id, ECharacter model)
        {
            _sheet = id;
            try { view.OutpostSceneController.OpenCharacterScreen(model, force: true); }
            finally { _sheet = null; }
            view.RefreshView();
        }

        // ------------------------------------------------------------------ command resolution (see GiveResourcesPatches.ResolveCommand)

        [HarmonyPatch(typeof(MultiplayerGameController), "GetExectionContext")]
        [HarmonyPrefix]
        private static bool ResolveCommand(MultiplayerGameController __instance, string path, ref ValueTuple<object, MethodInfo> __result)
        {
            if (path != LookCommands.CommandPath) return true;
            __result = (Handlers.GetValue(__instance, g => new LookCommands(g)), Command);
            return false;
        }
    }
}
