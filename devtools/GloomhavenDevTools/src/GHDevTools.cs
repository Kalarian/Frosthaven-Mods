using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.Party;
using MapRuleLibrary.State;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;

namespace GloomhavenDevTools
{
    /// <summary>
    /// Testing shortcuts for Gloomhaven digital (the Steam game, app 780290; NOT Frosthaven) on the author's own
    /// machine: to play every Gloomhaven class at the top level in its own game and compare its cards, effects and
    /// sounds with the Frosthaven port. Not part of the mod pack (lives in devtools/, never packed). Solo games only,
    /// on the map screen of a campaign or guildmaster game:
    ///   Ctrl+F8   unlock every class, with the game's own debug action (DebugMenu.UnlockAllCharacters: one unlock
    ///             reward per class, as a retirement or a quest would grant it). Campaign: the classes can then be
    ///             created at the roster. Guildmaster: a mercenary of each class joins the guild. Saved.
    ///   Ctrl+F9   max out the party (the characters in the party slots): level 9 with the experience to match, every
    ///             card of the class owned (as a guildmaster character owns all cards of its level), perk points for
    ///             every perk still open, and 1000 gold. The hand and the perks are picked in the game's own screens
    ///             as usual. Saved. (Note: the recommended scenario level follows the party's levels.)
    ///   F1        the game's own developer menu (GameDevMode on; offline games; on the map or in a scenario):
    ///             spawn monsters, win or lose, items, XP, gold, perk checks, conditions, reveal rooms, and more. The
    ///             retail build ships the menu's prefab but its provider component destroys itself on Awake, so the
    ///             -debug launch option alone shows nothing. This plugin keeps the provider (or adds one), gives it the
    ///             menu prefab's address when it has none, and turns on the game's dev mode (what -debug sets). Dev mode
    ///             also enables the game's own keys: F2 hide the GUI, F3 pause, F4 free camera, F5 wall fade, F6 reset
    ///             the camera, F7 regenerate the scenery, PageUp/PageDown game speed, [ and ] rotate the camera.
    /// The keys are polled from postfixes on the game's own per-frame methods, not from this MonoBehaviour's Update
    /// (BepInEx's plugin object can't be relied on to keep updating; see FrosthavenDevTools).
    /// </summary>
    [BepInPlugin("gloomhaven.devtools", "Gloomhaven Dev Tools", "0.1.0")]
    public class GHDevTools : BaseUnityPlugin
    {
        private const int TopLevel = 9;
        private const int Gold = 1000;
        private const string MenuAddress = "DebugMenu";   // the menu prefab's address in the game's addressables catalog

        internal static ManualLogSource _log;
        private static ConfigEntry<bool> _devMode;
        private static int _lastFrame = -1;
        private static string _listeningVia;
        private static int _nextProviderTry;
        private static DebugMenuProvider _ourProvider;
        private static readonly FieldInfo ProviderInstance = AccessTools.Field(typeof(Singleton<DebugMenuProvider>), "_instance");
        private static readonly FieldInfo MenuReference = AccessTools.Field(typeof(DebugMenuProvider), "_referenceDebugMenu");

        private void Awake()
        {
            _log = Logger;
            _devMode = Config.Bind("General", "GameDevMode", true,
                "The game's own developer mode (what its -debug launch option sets): F1 opens the game's debug menu in offline games, " +
                "and its dev keys F2-F7, PageUp/PageDown and [ ] work. Off: only Ctrl+F8 and Ctrl+F9.");
            var harmony = new Harmony("gloomhaven.devtools");
            int hooks = 0;
            foreach (Type type in new[] { typeof(InputManager), typeof(Main) })
            {
                MethodInfo m = AccessTools.Method(type, "Update");
                if (m == null) { _log.LogWarning("Dev tools: no " + type.Name + ".Update to hook"); continue; }
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(GHDevTools), nameof(Poll)));
                hooks++;
            }
            if (_devMode.Value)
            {
                MethodInfo awake = AccessTools.DeclaredMethod(typeof(DebugMenuProvider), "Awake");
                if (awake != null && ProviderInstance != null)
                {
                    harmony.Patch(awake, prefix: new HarmonyMethod(typeof(GHDevTools), nameof(KeepProvider)));
                    Main.s_DevMode = true;
                }
                else _log.LogWarning("Dev tools: the game's DebugMenuProvider has changed; its F1 menu stays off");
            }
            _log.LogInfo($"Dev tools loaded ({hooks} frame hooks): Ctrl+F8 = unlock every class, Ctrl+F9 = max out the party" +
                         (Main.s_DevMode ? ", F1 = the game's debug menu (offline; map or scenario)" : ""));
        }

        /// <summary>Prefix on DebugMenuProvider.Awake. The retail build's Awake destroys the component; instead it is
        /// registered as the singleton (what the base class's Awake does) and, when it has no prefab reference, given
        /// the menu's address.</summary>
        private static bool KeepProvider(DebugMenuProvider __instance)
        {
            try
            {
                ProviderInstance.SetValue(null, __instance);
                if (MenuReference != null && string.IsNullOrEmpty((MenuReference.GetValue(__instance) as AssetReference)?.AssetGUID))
                    MenuReference.SetValue(__instance, new AssetReference(MenuAddress));
            }
            catch (Exception e) { _log.LogWarning("Dev tools: could not keep the game's debug menu provider: " + e.Message); }
            return false;
        }

        /// <summary>The game's scenes may or may not carry a DebugMenuProvider (and a scene's one goes with its scene);
        /// when none is registered, ours is (made once, kept across scenes).</summary>
        private static void EnsureProvider()
        {
            if (!Main.s_DevMode || Singleton<DebugMenuProvider>.Instance != null || Time.frameCount < _nextProviderTry) return;
            try
            {
                if (_ourProvider == null)
                {
                    var holder = new GameObject("GloomhavenDevTools debug menu provider");
                    UnityEngine.Object.DontDestroyOnLoad(holder);
                    _ourProvider = holder.AddComponent<DebugMenuProvider>();   // its Awake (KeepProvider) registers it
                }
                else ProviderInstance.SetValue(null, _ourProvider);
                if (Singleton<DebugMenuProvider>.Instance != null) _log.LogInfo("Dev tools: the game's debug menu is available: F1 (offline games; map or scenario)");
                else _log.LogWarning("Dev tools: the debug menu provider did not register; F1 stays off");
            }
            catch (Exception e) { _log.LogWarning("Dev tools: could not add the debug menu provider: " + e.Message); }
            _nextProviderTry = Time.frameCount + 600;
        }

        private static void Poll(MethodBase __originalMethod)
        {
            if (Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            if (_listeningVia == null)
            {
                _listeningVia = __originalMethod.DeclaringType?.Name;
                _log.LogInfo("Dev tools: listening for keys (via " + _listeningVia + ")");
            }
            EnsureProvider();
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.ctrlKey.isPressed) return;
            if (kb.f8Key.wasPressedThisFrame) Run("unlock every class", UnlockAllClasses);
            if (kb.f9Key.wasPressedThisFrame) Run("max out the party", MaxOutParty);
        }

        private static CMapState Map()
        {
            GlobalData global = SaveData.Instance != null ? SaveData.Instance.Global : null;
            CMapState map = AdventureState.MapState;
            if (global == null || map == null || global.CurrentGameState != EGameState.Map)
                throw new InvalidOperationException("only on the map screen of a campaign or guildmaster game (not the main menu, not inside a scenario)");
            return map;
        }

        /// <summary>Ctrl+F8: the game's own debug action, then a save.</summary>
        private static void UnlockAllClasses()
        {
            CMapState map = Map();
            var had = new HashSet<string>(map.MapParty.UnlockedCharacterIDs ?? new List<string>());
            DebugMenu.UnlockAllCharacters();
            List<string> gained = (map.MapParty.UnlockedCharacterIDs ?? new List<string>()).Where(id => !had.Contains(id)).ToList();
            SaveData.Instance.SaveCurrentAdventureData();
            _log.LogInfo(gained.Count == 0
                ? $"Dev tools: every class was already unlocked ({had.Count})"
                : $"Dev tools: {gained.Count} classes unlocked ({had.Count + gained.Count} of {CharacterClassManager.Classes.Count}): {string.Join(", ", gained)}" +
                  (map.IsCampaign ? ". Create characters of them at the roster." : ". A mercenary of each has joined the guild."));
        }

        /// <summary>Ctrl+F9: every character in the party slots to the top level, with every card of their class.</summary>
        private static void MaxOutParty()
        {
            CMapState map = Map();
            List<CMapCharacter> party = map.MapParty.SelectedCharacters.ToList();
            if (party.Count == 0) throw new InvalidOperationException("nobody is in the party slots");
            foreach (CMapCharacter c in party)
            {
                CCharacterClass cls = CharacterClassManager.Classes.SingleOrDefault(x => x.ID == c.CharacterID)
                                      ?? throw new InvalidOperationException($"{c.CharacterName}: no class {c.CharacterID}");
                List<int> xpTable = c.XPTable;
                int top = xpTable != null && xpTable.Count > 1 ? Math.Min(TopLevel, xpTable.Count - 1) : TopLevel;
                if (c.Level < top) c.SetCharacterLevel(top);   // the level first: the experience must not read as a level-up still owed
                int xp = c.GetXPThreshold(c.Level);
                if (xp != int.MaxValue && c.EXP < xp) c.GainEXP(xp - c.EXP, 1f);
                int perksOpen = c.Perks?.Count(p => !p.IsActive) ?? 0;
                if (c.PerkPoints < perksOpen) c.UpdatePerkPoints(perksOpen);
                int gained = 0;
                foreach (CAbilityCard card in cls.AbilityCardsPool.Where(k => !c.OwnedAbilityCardIDs.Contains(k.ID)).ToList())
                {
                    try { c.GainCard(card); gained++; }
                    catch (Exception e) { _log.LogWarning($"Dev tools: {c.CharacterName} could not gain {card.Name}: {e.Message}"); }
                }
                c.GetSmallItemMax();
                _log.LogInfo($"Dev tools: {c.CharacterName} the {c.CharacterID}: level {c.Level}, {c.EXP} XP, {c.PerkPoints} perk points to spend, " +
                             $"+{gained} cards ({c.OwnedAbilityCardIDs.Count} of {cls.AbilityCardsPool.Count} owned)");
            }
            DebugMenu.GainSelectedCharactersGold(Gold);
            map.CheckLockedContent();
            SaveData.Instance.SaveCurrentAdventureData();
        }

        private static void Run(string what, Action action)
        {
            if (FFSNetwork.IsOnline) { _log.LogWarning($"Dev tools: '{what}' is for solo games only; ignored."); return; }
            try
            {
                action();
                _log.LogInfo($"Dev tools: {what} done.");
            }
            catch (Exception e)
            {
                _log.LogWarning($"Dev tools: {what} failed: {(e.InnerException ?? e).Message}");
            }
        }
    }
}
