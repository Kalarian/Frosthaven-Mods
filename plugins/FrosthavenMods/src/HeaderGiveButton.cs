using System;
using System.Linq;
using FHGUI;
using FHRL;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// A "Give" button under the top bar's "NAME, LEVEL n" text: opens the Give resources window for the selected
    /// character (same checks as Ctrl+click on a portrait). The header has no text button of its own to copy, so the
    /// Give window's Give button (DistributionModule.DoneButton) is cloned, scaled down, and stripped of its keyboard
    /// shortcut. Shown only while the rule is on, outside scenarios, with a character selected. The header layout is
    /// logged once (UiDump) in case the button needs nudging.
    /// </summary>
    [HarmonyPatch]
    internal static class HeaderGiveButton
    {
        private const string ButtonName = "FrosthavenMods_HeaderGive";
        private const float Scale = 0.7f;

        [HarmonyPatch(typeof(MainHeaderModule), nameof(MainHeaderModule.Init))]
        [HarmonyPostfix]
        private static void AddButton(TMP_Text ____heroData)
        {
            GiveResourcesPatches.Safe("add the header Give button", () =>
            {
                if (____heroData == null) return;
                FHMapView view = Singleton<FHMapChoreographer>.InstanceFast?.View;
                if (view == null) return;
                UiDump.Once("Top bar hero text area", ____heroData.transform.parent);
                bool show = ModRules.IsOn(ModRules.GiveResources) && view.GameState?.ActiveScenario == null && view.SelectedCharacter != null;
                Transform existing = ____heroData.transform.Find(ButtonName);
                if (existing == null)
                {
                    if (!show) return;
                    existing = Create(view, ____heroData);
                    if (existing == null) return;
                }
                existing.gameObject.SetActive(show);
            });
        }

        private static Transform Create(FHMapView view, TMP_Text heroData)
        {
            var template = Traverse.Create(view.Modules.DistributionModule).Field("DoneButton").GetValue<FHButton>();
            if (template == null) { Plugin.Log.LogWarning("Give resources: no button to copy for the header"); return null; }
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, heroData.transform, false);
            go.name = ButtonName;
            // No keyboard shortcut (the Give window's button answers Confirm/Enter).
            // (HotkeyClickableScopeElement lives in DevLocker.GFrame.Input; matched by name to avoid the reference.)
            foreach (Component c in go.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "HotkeyClickableScopeElement").ToList())
                UnityEngine.Object.DestroyImmediate(c);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(0f, -4f);
            rt.localScale = new Vector3(Scale, Scale, 1f);

            var button = go.GetComponent<FHButton>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => GiveResourcesPatches.Safe("header Give", OnClick));
            button.interactable = true;
            button.SetNonLocalizedText(Text.Get("FHMODS_GIVE"));
            var group = go.GetComponent<CanvasGroup>();
            if (group != null) { group.alpha = 1f; group.interactable = true; group.blocksRaycasts = true; }
            return go.transform;
        }

        private static void OnClick()
        {
            FHMapView view = Singleton<FHMapChoreographer>.InstanceFast?.View;
            CharacterState giver = view?.SelectedCharacter;
            if (giver == null) return;
            if (!view.IsSoloGame && !view.PlayerCharacters.Contains(giver))
            {
                GiveResourcesPatches.Message(view, "You can only give from characters you control.");
                return;
            }
            GiveResourcesPatches.StartFrom(view, giver.ID);
        }
    }
}
