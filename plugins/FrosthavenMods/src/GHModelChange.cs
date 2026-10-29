using System;
using System.Collections;
using Chronos;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// A Gloomhaven card that changes the hero's model (the Demolitionist's Mech Suit top: ChangeCharacterModel
    /// DemolitionistMech, and back when the card leaves play). The rules side is Frosthaven's own and works: the active
    /// bonus sets the actor's model override and sends ChangeCharacterModel, which plays the arrival clip. Gloomhaven swaps
    /// the model from a ChangeModelSMB on that animation state, which the ported hero's borrowed Drifter animator doesn't
    /// have (the user's manual test, 2026-10-29: the card worked, he never became the mech). So once the clip has played,
    /// the swap is done with Frosthaven's own ForcedChangeCharacterModel handler, which recycles the old figure and makes
    /// the one GetPrefabName now names (GHModel serves the mech's model like any pack model).
    /// </summary>
    [HarmonyPatch]
    internal static class GHModelChange
    {
        private const float MaxWait = 4f;   // game seconds: never longer than this behind the clip

        [HarmonyPatch(typeof(Choreographer), "Handle_ChangeCharacterModel")]
        [HarmonyPostfix]
        private static void Changed(CMessageData message)
        {
            try
            {
                CActor actor = (message as CChangeCharacterModel_MessageData)?.m_ActorToChange;
                Choreographer ch = Choreographer.s_Choreographer;
                if (actor == null || ch == null) return;
                GameObject go = ch.FindClientActor(actor);
                CharacterManager cm = go != null ? CharacterManager.GetCharacterManager(go) : null;
                string wanted = actor.GetPrefabName(), now = cm?.CharacterPrefab != null ? cm.CharacterPrefab.name : null;
                if (now == null || string.Equals(Strip(now), wanted, StringComparison.OrdinalIgnoreCase)) return;
                if (!Enum.TryParse(wanted, out ECharacter model) || GHModel.EntryFor(model) == null) return;   // Gloomhaven models only
                CoroutineHost.Get().StartCoroutine(SwapAfterClip(actor, go, wanted));
            }
            catch (Exception e) { Plugin.Log.LogError("Gloomhaven model change: " + e); }
        }

        private static string Strip(string name) => name.Replace("(Clone)", "").Trim();

        private static IEnumerator SwapAfterClip(CActor actor, GameObject go, string wanted)
        {
            yield return null;   // the clip has started
            Animator animator = go != null ? go.GetComponentInChildren<Animator>() : null;
            float wait = animator != null ? Mathf.Min(animator.GetCurrentAnimatorStateInfo(0).length, MaxWait) : 0.5f;
            for (float t = 0f; t < wait; t += Timekeeper.instance != null ? Timekeeper.instance.m_GlobalClock.deltaTime : Time.deltaTime)
                yield return null;
            try
            {
                Choreographer ch = Choreographer.s_Choreographer;
                if (ch == null || actor.IsDead) yield break;
                GameObject current = ch.FindClientActor(actor);
                CharacterManager cm = current != null ? CharacterManager.GetCharacterManager(current) : null;
                if (cm?.CharacterPrefab == null || !string.Equals(actor.GetPrefabName(), wanted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Strip(cm.CharacterPrefab.name), wanted, StringComparison.OrdinalIgnoreCase)) yield break;
                AccessTools.Method(typeof(Choreographer), "Handle_ForcedChangeCharacterModel").Invoke(ch, new object[] { new CForcedChangeCharacterModel_MessageData(actor) });
                Plugin.Log.LogInfo($"Gloomhaven model change: the hero is now {wanted}");
            }
            catch (Exception e) { Plugin.Log.LogError("Gloomhaven model change: " + e); }
        }
    }
}
