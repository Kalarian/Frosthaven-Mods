using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FHGUI;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// Measures the codex when it is slow to open. Opening a class's page in the codex (from the codex itself, or the
    /// class screens' "Class Mechanics: View") froze the game for 5 to 14 seconds in one frame with the Gloomhaven
    /// classes, and the decompiled code alone doesn't say which step costs it. This times the codex's open and topic
    /// switch and the steps inside it: the hero page's parts, each card the page takes from the card pool, a card
    /// generated or laid out on the way, and our card dressing (the skin hook). When a whole open or switch takes longer
    /// than <see cref="SlowMs"/>, one log line breaks the time down; otherwise nothing is logged. Measurement only: it
    /// changes nothing in the game, so it has no house-rule switch.
    /// </summary>
    [HarmonyPatch]
    internal static class CodexTiming
    {
        private const double SlowMs = 400;
        private static readonly Dictionary<string, (double ms, int n)> Parts = new Dictionary<string, (double, int)>();
        private static readonly Stopwatch Total = new Stopwatch();
        private static int _depth;
        private static string _topic;

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        // ---- the whole open or topic switch

        [HarmonyPatch(typeof(FHUICodex), nameof(FHUICodex.Show))]
        [HarmonyPrefix]
        private static void ShowStart(string topicId) => Begin(topicId);

        [HarmonyPatch(typeof(FHUICodex), nameof(FHUICodex.Show))]
        [HarmonyFinalizer]
        private static Exception ShowEnd(Exception __exception) { End("open"); return __exception; }

        [HarmonyPatch(typeof(FHUICodex), "SelectTopic")]
        [HarmonyPrefix]
        private static void TopicStart(string topicId) => Begin(topicId);

        [HarmonyPatch(typeof(FHUICodex), "SelectTopic")]
        [HarmonyFinalizer]
        private static Exception TopicEnd(Exception __exception) { End("topic"); return __exception; }

        private static void Begin(string topicId)
        {
            if (_depth++ > 0) return;
            Parts.Clear();
            _topic = topicId;
            Total.Restart();
        }

        private static void End(string what)
        {
            if (_depth == 0 || --_depth > 0) return;
            Total.Stop();
            double total = Total.Elapsed.TotalMilliseconds;
            if (total < SlowMs) return;
            string parts = string.Join(", ", Parts.OrderByDescending(p => p.Value.ms)
                .Select(p => $"{p.Key} {p.Value.ms:0} ms" + (p.Value.n > 1 ? $" ({p.Value.n}x)" : "")));
            Plugin.Log.LogInfo($"Codex timing: {what} '{_topic ?? "default"}' took {total:0} ms: {(parts.Length > 0 ? parts : "no measured step")}");
        }

        // ---- the steps inside it (counted only while an open or switch is being timed)

        private static void Start(out long __state) => __state = _depth > 0 ? Stopwatch.GetTimestamp() : 0;

        private static void Stop(string part, long started)
        {
            if (started == 0 || _depth == 0) return;
            double ms = Ms(Stopwatch.GetTimestamp() - started);
            Parts.TryGetValue(part, out var p);
            Parts[part] = (p.ms + ms, p.n + 1);
        }

        [HarmonyPatch(typeof(FHUICodexHeroEntry), nameof(FHUICodexHeroEntry.Setup))]
        [HarmonyPrefix, HarmonyPriority(Priority.First)] private static void SetupA(out long __state) => Start(out __state);
        [HarmonyPatch(typeof(FHUICodexHeroEntry), nameof(FHUICodexHeroEntry.Setup))]
        [HarmonyPostfix, HarmonyPriority(Priority.Last)] private static void SetupB(long __state) => Stop("hero page", __state);

        [HarmonyPatch(typeof(FHUICodexHeroEntry), "PopulateStats")]
        [HarmonyPrefix, HarmonyPriority(Priority.First)] private static void StatsA(out long __state) => Start(out __state);
        [HarmonyPatch(typeof(FHUICodexHeroEntry), "PopulateStats")]
        [HarmonyPostfix, HarmonyPriority(Priority.Last)] private static void StatsB(long __state) => Stop("page stats", __state);

        [HarmonyPatch(typeof(FHUICodexHeroEntry), "PopulateDeck")]
        [HarmonyPrefix, HarmonyPriority(Priority.First)] private static void DeckA(out long __state) => Start(out __state);
        [HarmonyPatch(typeof(FHUICodexHeroEntry), "PopulateDeck")]
        [HarmonyPostfix, HarmonyPriority(Priority.Last)] private static void DeckB(long __state) => Stop("page cards", __state);

        [HarmonyPatch(typeof(ObjectPool), nameof(ObjectPool.SpawnCard))]
        [HarmonyPrefix, HarmonyPriority(Priority.First)] private static void SpawnA(out long __state) => Start(out __state);
        [HarmonyPatch(typeof(ObjectPool), nameof(ObjectPool.SpawnCard))]
        [HarmonyPostfix, HarmonyPriority(Priority.Last)] private static void SpawnB(long __state) => Stop("card from pool", __state);

        [HarmonyPatch(typeof(PersistentData), nameof(PersistentData.CreateAbilityCard))]
        [HarmonyPrefix, HarmonyPriority(Priority.First)] private static void CreateA(out long __state) => Start(out __state);
        [HarmonyPatch(typeof(PersistentData), nameof(PersistentData.CreateAbilityCard))]
        [HarmonyPostfix, HarmonyPriority(Priority.Last)] private static void CreateB(long __state) => Stop("card created", __state);

        [HarmonyPatch(typeof(AbilityCardUI), nameof(AbilityCardUI.MakeAbilityCard))]
        [HarmonyPrefix, HarmonyPriority(Priority.First)] private static void LayoutA(out long __state) => Start(out __state);
        [HarmonyPatch(typeof(AbilityCardUI), nameof(AbilityCardUI.MakeAbilityCard))]
        [HarmonyPostfix, HarmonyPriority(Priority.Last)] private static void LayoutB(long __state) => Stop("card laid out", __state);

        [HarmonyPatch(typeof(AbilityCardUI), "SetSkin", typeof(string), typeof(string))]
        [HarmonyPrefix, HarmonyPriority(Priority.First)] private static void SkinA(out long __state) => Start(out __state);
        [HarmonyPatch(typeof(AbilityCardUI), "SetSkin", typeof(string), typeof(string))]
        [HarmonyPostfix, HarmonyPriority(Priority.Last)] private static void SkinB(long __state) => Stop("card skin and dressing", __state);
    }
}
