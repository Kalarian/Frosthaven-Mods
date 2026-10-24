using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClockStone;
using Newtonsoft.Json.Linq;
using ScenarioRuleLibrary;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// A Gloomhaven class's own sounds. Both games play sounds through the same audio toolkit (ClockStone's
    /// AudioController): an animator state's behaviour names an audio event, the controller plays one of the event's
    /// clips. tools/gh_sound.py exports the class's clips from the player's Gloomhaven into the pack
    /// (sounds\&lt;Model&gt;\*.wav, and sounds.json: event -> clips), and gh_model.py records which event each animator
    /// state played in Gloomhaven. Here the clips are loaded once (16-bit PCM WAV, read directly: no decoder, no wait)
    /// and registered in Frosthaven's AudioController under their Gloomhaven event names, as copies of one of the
    /// stand-in's events (so they play in the same category, at the same distance and volume settings); GHModel then
    /// points the model's animator states at them. An event that isn't registered is never set, so the state keeps its
    /// stand-in sound.
    /// </summary>
    internal static class GHSound
    {
        private const string LikeEvent = "Drifter_Melee_Basic";   // a stand-in event the class's events copy their settings from

        private static readonly Dictionary<ECharacter, Dictionary<string, AudioClip[]>> Loaded = new Dictionary<ECharacter, Dictionary<string, AudioClip[]>>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>True when <paramref name="audioEvent"/> can be played (registered by us, or the game's own).</summary>
        internal static bool Playable(string audioEvent)
        {
            if (string.IsNullOrEmpty(audioEvent)) return false;
            try { return AudioController.GetAudioItem(audioEvent) != null; }
            catch (Exception) { return false; }   // no AudioController yet
        }

        /// <summary>Registers the class's events with the current AudioController (it can be rebuilt between scenes,
        /// so this checks every time; the clips are loaded once). Quiet when the pack has no sounds.</summary>
        internal static void Ensure(ECharacter model, string packFolder)
        {
            Dictionary<string, AudioClip[]> events = Clips(model, packFolder);
            if (events.Count == 0) return;
            AudioItem like;
            try { like = AudioController.GetAudioItem(LikeEvent); }
            catch (Exception) { return; }
            if (like?.category == null)
            {
                if (Reported.Add("no like")) Plugin.Log.LogWarning($"Gloomhaven sounds: the game has no {LikeEvent} sound to copy settings from; {model} keeps the stand-in's sounds");
                return;
            }
            int added = 0;
            foreach (KeyValuePair<string, AudioClip[]> ev in events)
            {
                if (AudioController.GetAudioItem(ev.Key) != null) continue;
                var item = new AudioItem(like)
                {
                    Name = ev.Key,
                    subItems = ev.Value.Select(c => new AudioSubItem { Clip = c, Volume = 1f }).ToArray(),
                    // the copy constructor leaves these null, and playing reads stopEvents unchecked
                    stopEvents = new AudioStopEvent[0],
                    blockEvents = new AudioStopEvent[0],
                };
                AudioController.AddToCategory(like.category, item);
                added++;
            }
            if (added > 0 && Reported.Add("registered " + model))
                Plugin.Log.LogInfo($"Gloomhaven sounds: {model}'s {added} Gloomhaven sound events registered ({events.Values.Sum(v => v.Length)} clips)");
        }

        private static Dictionary<string, AudioClip[]> Clips(ECharacter model, string packFolder)
        {
            if (Loaded.TryGetValue(model, out Dictionary<string, AudioClip[]> cached)) return cached;
            var events = new Dictionary<string, AudioClip[]>(StringComparer.Ordinal);
            string folder = packFolder == null ? null : Path.Combine(packFolder, "sounds", model.ToString());
            string manifest = folder == null ? null : Path.Combine(folder, "sounds.json");
            try
            {
                if (manifest != null && File.Exists(manifest))
                {
                    var clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
                    foreach (JProperty ev in JObject.Parse(File.ReadAllText(manifest)).Properties())
                    {
                        AudioClip[] list = ev.Value.Select(f => (string)f).Where(f => !string.IsNullOrEmpty(f))
                            .Select(f => clips.TryGetValue(f, out AudioClip c) ? c : clips[f] = Wav(Path.Combine(folder, f)))
                            .Where(c => c != null).ToArray();
                        if (list.Length > 0) events[ev.Name] = list;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Gloomhaven sounds: could not read {manifest}: {e.Message}"); }
            Loaded[model] = events;
            return events;
        }

        /// <summary>A 16-bit PCM WAV file as an AudioClip (mono or more channels, any rate), or null.</summary>
        private static AudioClip Wav(string file)
        {
            try
            {
                byte[] b = File.ReadAllBytes(file);
                if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') throw new FormatException("not a RIFF file");
                int channels = 0, rate = 0, bits = 0, dataAt = -1, dataLength = 0;
                for (int at = 12; at + 8 <= b.Length;)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, at, 4);
                    int size = BitConverter.ToInt32(b, at + 4);
                    if (id == "fmt ")
                    {
                        channels = BitConverter.ToInt16(b, at + 10);
                        rate = BitConverter.ToInt32(b, at + 12);
                        bits = BitConverter.ToInt16(b, at + 22);
                    }
                    else if (id == "data")
                    {
                        dataAt = at + 8;
                        dataLength = Math.Min(size, b.Length - dataAt);
                        break;
                    }
                    at += 8 + size + (size & 1);
                }
                if (bits != 16 || channels < 1 || rate <= 0 || dataAt < 0) throw new FormatException($"unsupported WAV ({bits}-bit, {channels} channels)");
                int count = dataLength / 2;
                var samples = new float[count];
                for (int i = 0; i < count; i++) samples[i] = BitConverter.ToInt16(b, dataAt + i * 2) / 32768f;
                AudioClip clip = AudioClip.Create(Path.GetFileNameWithoutExtension(file), count / channels, channels, rate, false);
                clip.SetData(samples, 0);
                clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return clip;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Gloomhaven sounds: {file}: {e.Message}");
                return null;
            }
        }
    }
}
