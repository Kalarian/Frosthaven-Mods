using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FFSNet;
using FFSNet.Backend;
using HarmonyLib;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// What this PC has of the Gloomhaven classes content pack, in the one-line form the two sides of a co-op join
    /// exchange: "FHMODS1 plugin=0.6.9 pack=gloomhaven/1/0123456789ab" (the pack's name, version and the first twelve
    /// characters of its data hash, GHClasses.Pack.Stamp) or "FHMODS1 plugin=0.6.9 pack=-". The pack only counts when
    /// the game really loaded its classes into the rules (GHClasses.PackClasses). A vanilla game sends "" and no
    /// custom data, which parse as null.
    /// </summary>
    internal sealed class PackManifest
    {
        public const string Wire = "FHMODS1";

        public string PluginVersion;
        public string Pack;         // null: no pack loaded
        public int Version;
        public string Sha;          // first 12 hex characters of data_sha

        public bool HasPack => Pack != null;
        public string PackLabel => HasPack ? $"{Pack} v{Version} ({Sha})" : "none";
        public bool SamePackAs(PackManifest other) =>
            other != null && HasPack && other.HasPack && Pack == other.Pack && Version == other.Version && Sha == other.Sha;

        private static PackManifest _local;

        /// <summary>This PC's manifest, read once the rules are loaded (they are long before any join).</summary>
        public static PackManifest Local
        {
            get
            {
                if (_local != null) return _local;
                var m = new PackManifest { PluginVersion = Plugin.Version };
                try
                {
                    List<ScenarioRuleLibrary.CCharacterClass> loaded = GHClasses.PackClasses();
                    var models = new HashSet<string>(loaded.Select(c => c.CharacterModel.ToString()), StringComparer.OrdinalIgnoreCase);
                    GHClasses.Pack pack = GHClasses.Packs.FirstOrDefault(p => p.Classes.Any(models.Contains)) ?? GHClasses.Packs.FirstOrDefault();
                    if (pack != null && loaded.Count > 0)   // installed but not loaded counts as no pack
                    {
                        m.Pack = pack.Name;
                        m.Version = pack.Version;
                        string sha = (pack.DataSha ?? "").ToLowerInvariant();
                        m.Sha = sha.Substring(0, Math.Min(12, sha.Length));
                    }
                    if (ScenarioRuleLibrary.CharacterClassManager.Classes != null) _local = m;   // rules loaded: the answer is final
                }
                catch (Exception e) { Plugin.Log.LogWarning("Pack gate: could not read this PC's pack: " + e.Message); }
                return m;
            }
        }

        public string ToWire()
        {
            string wire = $"{Wire} plugin={PluginVersion ?? "?"} pack={(HasPack ? $"{Pack}/{Version}/{Sha}" : "-")}";
            return Regex.Replace(wire, "[^\\x20-\\x7e]", "?");   // ASCII only: UserToken writes it with Encoding.ASCII
        }

        /// <summary>Null when the text isn't ours (a vanilla peer, or a mod too old to announce itself).</summary>
        public static PackManifest Parse(string wire)
        {
            if (string.IsNullOrEmpty(wire) || !wire.StartsWith(Wire + " ", StringComparison.Ordinal)) return null;
            var m = new PackManifest();
            foreach (string part in wire.Split(' ').Skip(1))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq), val = part.Substring(eq + 1);
                if (key == "plugin") m.PluginVersion = val;
                else if (key == "pack" && val != "-")
                {
                    string[] p = val.Split('/');
                    if (p.Length == 3 && int.TryParse(p[1], out int v)) { m.Pack = p[0]; m.Version = v; m.Sha = p[2]; }
                }
            }
            return m;
        }

        public static PackManifest Parse(byte[] customData) =>
            customData == null || customData.Length == 0 ? null : Parse(Encoding.ASCII.GetString(customData));
    }

    /// <summary>
    /// Co-op gate for the Gloomhaven classes pack: a campaign with the "Gloomhaven classes" rule on can only be joined by
    /// a PC with the same pack (name, version, data hash), refused with a clear message instead of the KeyNotFound the
    /// game would throw later (a character's class, the unlocked classes).
    ///
    /// How (read in the game's code):
    ///  - A joining client sends a UserToken with an unused CurrentMod string ("" in vanilla). We put our manifest there
    ///    (Announce). The host reads it in GHNetworkCallbacks.ConnectRequest, where the game refuses mismatched game
    ///    versions (HostDecides).
    ///  - The host answers with a GameToken carrying the campaign rules and an unused CustomData array. We put the host's
    ///    manifest there (HostAnnounces). The client reads both in GHClientCallbacks.Connected, the earliest moment it
    ///    knows the host's rules and before anything is downloaded, and backs out the way the game itself backs out of
    ///    a bad session (ClientGate).
    ///  - A refusal at ConnectRequest reaches the client as a bare disconnect (the backend never sends the error token):
    ///    its join window just resets, so only clients that can't show themselves a message (no mod at all) are refused
    ///    there, and the host gets a toast naming them. A modded client is let through to the GameToken and refuses
    ///    itself with the full text; if it asks for game data anyway, the host disconnects it (NoDataForFlagged).
    /// </summary>
    [HarmonyPatch]
    internal static class PackGate
    {
        internal enum Mismatch { None, HostNoMod, HostNoPack, ClientNoMod, ClientNoPack, VersionDiffers }

        private sealed class Verdict
        {
            public string Name;
            public PackManifest Client;   // null: no mod
            public Mismatch Why;
        }

        private const string Title = "Gloomhaven classes";
        private static readonly MethodInfo SetCurrentMod = AccessTools.PropertySetter(typeof(UserToken), nameof(UserToken.CurrentMod));
        private static readonly MethodInfo SetCustomData = AccessTools.PropertySetter(typeof(CustomDataToken), nameof(CustomDataToken.CustomData));

        /// <summary>Host only: what each PC that asked to join announced, by platform player id.</summary>
        private static readonly Dictionary<string, Verdict> Verdicts = new Dictionary<string, Verdict>();
        private static string _pendingModal;
        private static int _lastPollFrame = -1;

        // ---------------------------------------------------------------- the one comparison both sides run

        internal static Mismatch Check(ERulesFlag rules, PackManifest host, PackManifest client)
        {
            if ((rules & ModRules.GloomhavenClasses) == 0) return Mismatch.None;
            if (host == null) return Mismatch.HostNoMod;
            if (!host.HasPack) return Mismatch.HostNoPack;
            if (client == null) return Mismatch.ClientNoMod;
            if (!client.HasPack) return Mismatch.ClientNoPack;
            return host.SamePackAs(client) ? Mismatch.None : Mismatch.VersionDiffers;
        }

        /// <summary>The reason in words: for the host reading about player <paramref name="name"/>, or for the client
        /// reading about itself.</summary>
        internal static string Describe(Mismatch why, string name, PackManifest host, PackManifest client, bool forHost)
        {
            string need = host != null && host.HasPack ? host.PackLabel : "the Gloomhaven classes pack";
            switch (why)
            {
                case Mismatch.HostNoMod:
                    return "The host's campaign has 'Gloomhaven classes' on, but the host isn't running the mod pack. Ask them to install it or to turn the rule off.";
                case Mismatch.HostNoPack:
                    return forHost
                        ? "This campaign has 'Gloomhaven classes' on, but this PC has no Gloomhaven classes pack loaded (see the mod guide), so nobody can join it."
                        : "The host's campaign has 'Gloomhaven classes' on, but the host has no Gloomhaven classes pack loaded. Ask them to install it.";
                case Mismatch.ClientNoMod:
                    return $"{name} can't join: this campaign has 'Gloomhaven classes' on and they aren't running the mod pack with {need}. They were refused; their game only shows the join window again, so tell them.";
                case Mismatch.ClientNoPack:
                    return forHost
                        ? $"{name} has the mod but not the Gloomhaven classes pack ({need}). Their game explains it to them and leaves."
                        : $"This campaign uses the Gloomhaven classes pack ({need}) and this PC has none. Install it (see the mod guide), restart the game and join again.";
                case Mismatch.VersionDiffers:
                    return forHost
                        ? $"{name}'s Gloomhaven classes pack ({client?.PackLabel}) differs from yours ({host?.PackLabel}). Both PCs need the same pack. Their game explains it to them and leaves."
                        : $"Your Gloomhaven classes pack ({client?.PackLabel}) differs from the host's ({host?.PackLabel}). Both PCs need the same pack: rebuild it with the same mod version on both (see the mod guide), restart and join again.";
                default:
                    return null;
            }
        }

        // ---------------------------------------------------------------- client -> host: UserToken.CurrentMod

        /// <summary>The client's UserToken is written once, when its link to the host is up. The rules are loaded by then.</summary>
        [HarmonyPatch(typeof(UserToken), nameof(UserToken.Write))]
        [HarmonyPrefix]
        private static void Announce(UserToken __instance)
        {
            try
            {
                if (__instance != FFSNetwork.CurrentUserToken) return;
                string wire = PackManifest.Local.ToWire();
                SetCurrentMod.Invoke(__instance, new object[] { wire });
                CoopLog.Write("PACK announced to the host: " + wire);
            }
            catch (Exception e) { Plugin.Log.LogError("Pack gate: could not announce the pack: " + e); }
        }

        // ---------------------------------------------------------------- host decides (where the game checks versions)

        [HarmonyPatch(typeof(GHNetworkCallbacks), nameof(GHNetworkCallbacks.ConnectRequest))]
        [HarmonyPrefix]
        private static bool HostDecides(NetworkBackendConnectRequest request)
        {
            try
            {
                UserToken token = request?.UserToken;
                if (token == null) return true;   // the game refuses it (invalid user data)
                PackManifest client = PackManifest.Parse(token.CurrentMod);
                Mismatch why = Check(ModRules.Current, PackManifest.Local, client);
                Verdicts[token.PlatformPlayerID ?? ""] = new Verdict { Name = token.Username, Client = client, Why = why };
                CoopLog.Write($"PACK join request from '{token.Username}': announced '{token.CurrentMod}', mine '{PackManifest.Local.ToWire()}', rules {(int)ModRules.Current:X} -> {why}");
                if (why == Mismatch.None) return true;
                Notify(Describe(why, token.Username, PackManifest.Local, client, forHost: true));
                if (client == null)
                {
                    // No mod on that PC: it can't show itself a reason, so refuse the way the game refuses a wrong
                    // version (the backend only closes the connection; the error code never reaches the client).
                    request.Refuse(new ConnectionErrorToken(ConnectionErrorCode.ModsDoNotMatch));
                    return false;
                }
                // Modded but mismatched: let the game accept it so our GameToken reaches it; it refuses itself with the
                // full text (ClientGate), and the host never serves it game data (NoDataForFlagged).
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Pack gate: the host check failed, letting the game decide: " + e);
                return true;
            }
        }

        /// <summary>A flagged client that asks for the save (or anything else) is disconnected instead of served.</summary>
        [HarmonyPatch(typeof(GHHostCallbacks), nameof(GHHostCallbacks.OnEvent), new[] { typeof(GameDataRequestToken), typeof(NetworkBackendPlayer) })]
        [HarmonyPrefix]
        private static bool NoDataForFlagged(GameDataRequestToken request, NetworkBackendPlayer raisedBy)
        {
            try
            {
                if (raisedBy == null || !Verdicts.TryGetValue(raisedBy.PlatformID ?? "", out Verdict v) || v.Why == Mismatch.None) return true;
                CoopLog.Write($"PACK refused data request {(DataActionType)request.DataActionID} from '{raisedBy.Username}' ({v.Why}): disconnecting them");
                FFSNetwork.Disconnect(raisedBy);
                return false;
            }
            catch (Exception e) { Plugin.Log.LogError("Pack gate: the data request check failed: " + e); return true; }
        }

        [HarmonyPatch(typeof(GHHostCallbacks), nameof(GHHostCallbacks.Disconnected))]
        [HarmonyPostfix]
        private static void Forget(NetworkBackendPlayer player)
        {
            if (player?.PlatformID != null) Verdicts.Remove(player.PlatformID);
        }

        /// <summary>A new session: what earlier sessions' players announced is history.</summary>
        [HarmonyPatch(typeof(GHNetworkCallbacks), nameof(GHNetworkCallbacks.StartDone))]
        [HarmonyPostfix]
        private static void NewSession()
        {
            if (FFSNetwork.IsHost) Verdicts.Clear();
        }

        // ---------------------------------------------------------------- host -> client: GameToken.CustomData

        /// <summary>The game sends its GameToken (rules, save name...) to every accepted client; ours also carries the
        /// host's manifest.</summary>
        [HarmonyPatch(typeof(GHNetworkCallbacks), nameof(GHNetworkCallbacks.GenerateGameToken))]
        [HarmonyPostfix]
        private static void HostAnnounces(ref GameToken __result)
        {
            try
            {
                if (__result == null) return;
                SetCustomData.Invoke(__result, new object[] { Encoding.ASCII.GetBytes(PackManifest.Local.ToWire()) });
            }
            catch (Exception e) { Plugin.Log.LogError("Pack gate: could not add the manifest to the game token: " + e); }
        }

        // ---------------------------------------------------------------- the client's gate

        /// <summary>Earliest moment the client knows the host's campaign rules; nothing has been downloaded yet. Connected
        /// is async void: this patches its start, and returning false means its body never runs (no save download).</summary>
        [HarmonyPatch(typeof(GHClientCallbacks), nameof(GHClientCallbacks.Connected))]
        [HarmonyPrefix]
        private static bool ClientGate(GameToken gameToken)
        {
            ERulesFlag rules = 0;
            try
            {
                if (gameToken == null) return true;   // the game handles it (invalid session data)
                rules = (ERulesFlag)gameToken.CampaignRules;
                PackManifest host = PackManifest.Parse(gameToken.CustomData);
                PackManifest me = PackManifest.Local;
                Mismatch why = Check(rules, host, me);
                CoopLog.Write($"PACK join gate: host rules {(int)rules:X}, host pack {host?.PackLabel ?? "no mod"}, mine {me.PackLabel} -> {why}");
                if (why == Mismatch.None) return true;
                Refuse(Describe(why, "you", host, me, forHost: false));
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Pack gate: the client check failed: " + e);
                if ((rules & ModRules.GloomhavenClasses) == 0) return true;   // nothing to gate: join
                Refuse("This campaign uses the Gloomhaven classes pack and your game could not check its own copy (see the BepInEx log). Not joining.");
                return false;
            }
        }

        /// <summary>The game's own way out of a bad session, from Connected itself: a failed-connection event, then
        /// shutdown. The join window resets; our box carries the reason.</summary>
        private static void Refuse(string text)
        {
            FFSNetwork.QueueNetworkEvent(NetworkBackendEvent.Type.GameConnectionFailed, ConnectionErrorCode.ModsDoNotMatch);
            FFSNetwork.Shutdown();
            Modal(text);
        }

        // ---------------------------------------------------------------- mid-session: the rule is switched on

        /// <summary>Host: why the rule can't be turned on right now (null if it can): every connected PC must have this pack.</summary>
        internal static string WhyCantTurnOn()
        {
            PackManifest mine = PackManifest.Local;
            if (!mine.HasPack) return Describe(Mismatch.HostNoPack, null, mine, null, forHost: true);
            if (!FFSNetwork.IsOnline || !FFSNetwork.IsHost) return null;
            if (FFSNet.PlayerRegistry.JoiningPlayers.Count > 0 || FFSNet.PlayerRegistry.ConnectingUsers.Count > 0)
                return "Someone is joining: try again in a moment.";
            foreach (NetworkBackendPlayer p in (FFSNetwork.Players ?? Enumerable.Empty<NetworkBackendPlayer>()).ToList())
            {
                if (p == null || p == FFSNetwork.CurrentPlayer) continue;
                Verdicts.TryGetValue(p.PlatformID ?? "", out Verdict v);
                Mismatch why = Check(ModRules.GloomhavenClasses, mine, v?.Client);
                if (why != Mismatch.None) return Describe(why, p.Username, mine, v?.Client, forHost: true);
            }
            return null;
        }

        /// <summary>Client: the host turned the rule on while this PC has no pack (the host's own switch refuses that; this
        /// is the fallback). Leave before anything of the pack is used; the message waits for the main menu.</summary>
        [HarmonyPatch(typeof(HouseRulesSettings), nameof(HouseRulesSettings.ProxySetHouseRuleSettings))]
        [HarmonyPostfix]
        private static void RuleArrived()
        {
            try
            {
                if (!FFSNetwork.IsOnline || FFSNetwork.IsHost || !ModRules.IsOn(ModRules.GloomhavenClasses) || PackManifest.Local.HasPack) return;
                CoopLog.Write("PACK rule turned on by the host but this PC has no pack: leaving the session");
                _pendingModal = Describe(Mismatch.ClientNoPack, "you", null, PackManifest.Local, forHost: false);
                FFSNetwork.Shutdown();   // takes a client in a game back to the main menu
            }
            catch (Exception e) { Plugin.Log.LogError("Pack gate: the rule receipt check failed: " + e); }
        }

        [HarmonyPatch(typeof(InputFHManager), "LateUpdate")]
        [HarmonyPostfix]
        private static void Poll()
        {
            if (_pendingModal == null || Time.frameCount == _lastPollFrame) return;
            _lastPollFrame = Time.frameCount;
            try
            {
                if (SaveData.Instance?.Global == null || SaveData.Instance.Global.CurrentGameState != EGameState.None) return;
                if (SceneController.Instance == null || SceneController.Instance.IsLoading || FFSNetwork.IsShuttingDown) return;
                if (Singleton<UIConfirmationBoxManager>.InstanceFast == null) return;
                string text = _pendingModal;
                _pendingModal = null;
                Modal(text);
            }
            catch (Exception e) { _pendingModal = null; Plugin.Log.LogWarning("Pack gate: could not show the message: " + e.Message); }
        }

        // ---------------------------------------------------------------- UI

        /// <summary>The game's generic one-button box (the join window uses it for "connection with X lost").</summary>
        internal static void Modal(string text)
        {
            try
            {
                UIConfirmationBoxManager box = Singleton<UIConfirmationBoxManager>.InstanceFast;
                if (box == null) { Plugin.Log.LogWarning("Pack gate: no box to show the message in: " + text); return; }
                box.ShowGenericCancelConfirmation(Title, text, UIInfoTools.Instance.warningColor, "GUI_CONTINUE");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Pack gate: could not show the message: " + e.Message); }
        }

        /// <summary>Host-side toast (the game's own multiplayer notification channel).</summary>
        private static void Notify(string text)
        {
            try
            {
                UINotificationManager n = Singleton<UINotificationManager>.InstanceFast;
                if (n == null) return;
                n.ShowNotification("GUI_MULTIPLAYER", text, UIInfoTools.Instance.defaultNotificationDuration * 2f, "FHModsPackGate", "MULTIPLAYER");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Pack gate: could not show the notification: " + e.Message); }
        }
    }
}
