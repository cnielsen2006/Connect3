using System;
using System.Globalization;
using System.Text;
using Cascade.Gameplay;
using Cascade.Platform.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cascade.Persistence
{
    /// <summary>
    /// Reads and writes <c>save.json</c> (progress) and <c>settings.json</c> (player settings) through
    /// <see cref="IStorage"/> only (TRD §8). Writes are atomic (the storage writes a temp file and
    /// replaces). Loading never throws: a missing file gives defaults, an unreadable or malformed
    /// <c>save.json</c> is moved to <c>save.json.corrupt</c> and defaults are used, and
    /// <c>settings.json</c> is tolerant per field. Seeds are stored as decimal strings.
    /// </summary>
    public sealed class SaveService
    {
        public const string SaveFile = "save.json";
        public const string SettingsFile = "settings.json";
        public const string CorruptSuffix = ".corrupt";

        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        readonly IStorage _storage;
        readonly ILog? _log;

        public SaveService(IStorage storage, ILog? log = null)
        {
            _storage = storage;
            _log = log;
        }

        // -- progress --------------------------------------------------------------------------

        /// <summary>The saved progress, or defaults if there is none or it is corrupt (then moved aside).</summary>
        public SaveData LoadSave()
        {
            var raw = ReadJson(SaveFile);
            if (raw == null) return new SaveData();
            try { return ParseSave(raw); }
            catch (FormatException e)
            {
                _log?.Warn(SaveFile + " is invalid (" + e.Message + "); starting fresh");
                MoveAside(SaveFile);
                return new SaveData();
            }
        }

        /// <summary>Writes the progress atomically. Throws what the storage throws; the old file is untouched then.</summary>
        public void WriteSave(SaveData data) => _storage.WriteAtomic(SaveFile, Encode(SaveToJson(data)));

        /// <summary>Like <see cref="WriteSave"/> but returns false (and logs) instead of throwing.</summary>
        public bool TryWriteSave(SaveData data)
        {
            try { WriteSave(data); return true; }
            catch (Exception e)
            {
                _log?.Error("could not write " + SaveFile + ": " + e.Message);
                return false;
            }
        }

        // -- settings --------------------------------------------------------------------------

        /// <summary>The saved settings; every missing or invalid field keeps its default.</summary>
        public PlayerSettings LoadSettings()
        {
            var raw = ReadJson(SettingsFile);
            return raw == null ? new PlayerSettings() : ParseSettings(raw);
        }

        public void WriteSettings(PlayerSettings settings) => _storage.WriteAtomic(SettingsFile, Encode(SettingsToJson(settings)));

        /// <summary>Like <see cref="WriteSettings"/> but returns false (and logs) instead of throwing.</summary>
        public bool TryWriteSettings(PlayerSettings settings)
        {
            try { WriteSettings(settings); return true; }
            catch (Exception e)
            {
                _log?.Error("could not write " + SettingsFile + ": " + e.Message);
                return false;
            }
        }

        // -- save <-> JSON ---------------------------------------------------------------------

        static bool IsInt(JToken? t) => t != null && t.Type == JTokenType.Integer;

        static bool TryLong(JToken? t, out long value)
        {
            value = 0;
            if (!IsInt(t)) return false;
            object? v = ((JValue)t!).Value;
            if (v is long l) { value = l; return true; }
            return false; // a BigInteger: out of range
        }

        static SaveData ParseSave(JToken root)
        {
            if (!(root is JObject d)) throw new FormatException("save file is not an object");
            if (!TryLong(d["save_version"], out long version) || version < 1 || version > SaveData.CurrentSaveVersion)
                throw new FormatException("unsupported save_version");
            if (!TryLong(d["generator_version"], out long gen) || gen < int.MinValue || gen > int.MaxValue)
                throw new FormatException("bad generator_version");
            if (!TryLong(d["current_level"], out long level) || level < 1 || level > int.MaxValue)
                throw new FormatException("bad current_level");

            ulong? seed = null;
            var seedToken = d["current_seed"];
            if (seedToken != null && seedToken.Type != JTokenType.Null)
            {
                if (seedToken.Type == JTokenType.String)
                {
                    string text = (string)seedToken!;
                    if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
                        throw new FormatException("bad current_seed");
                    seed = parsed;
                }
                else if (seedToken.Type == JTokenType.Integer)
                {
                    // a JSON number (a plain number): the digits as text, so nothing passes through a double
                    string digits = ((JValue)seedToken).ToString(CultureInfo.InvariantCulture);
                    if (!ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
                        throw new FormatException("bad current_seed");
                    seed = parsed;
                }
                else throw new FormatException("bad current_seed");
            }

            double offset = 0.0;
            var offsetToken = d["adaptive_offset"];
            if (offsetToken != null)
            {
                if (offsetToken.Type != JTokenType.Integer && offsetToken.Type != JTokenType.Float)
                    throw new FormatException("bad adaptive_offset");
                offset = (double)offsetToken;
            }

            var stars = StarTable.Empty;
            var starsToken = d["stars"];
            if (starsToken != null)
            {
                if (!(starsToken is JObject starsObject)) throw new FormatException("bad stars");
                foreach (var p in starsObject.Properties())
                {
                    if (!TryLong(p.Value, out long s) || s < 0 || s > 3) throw new FormatException("bad stars value");
                    if (!int.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1)
                        throw new FormatException("bad stars key");
                    stars = stars.With(n, (int)s);
                }
            }

            return new SaveData
            {
                SaveVersion = (int)version,
                GeneratorVersion = (int)gen,
                CurrentLevel = (int)level,
                CurrentSeed = seed,
                Stars = stars,
                AdaptiveOffset = offset,
            };
        }

        static JObject SaveToJson(SaveData data)
        {
            var stars = new JObject();
            foreach (int level in data.Stars.Levels)
                stars[level.ToString(CultureInfo.InvariantCulture)] = data.Stars.Get(level);
            return new JObject
            {
                ["save_version"] = data.SaveVersion,
                ["generator_version"] = data.GeneratorVersion,
                ["current_level"] = data.CurrentLevel,
                ["current_seed"] = data.CurrentSeed.HasValue
                    ? new JValue(data.CurrentSeed.Value.ToString(CultureInfo.InvariantCulture))
                    : JValue.CreateNull(),
                ["stars"] = stars,
                ["adaptive_offset"] = data.AdaptiveOffset,
            };
        }

        // -- settings <-> JSON -----------------------------------------------------------------

        static PlayerSettings ParseSettings(JToken root)
        {
            var settings = new PlayerSettings();
            if (!(root is JObject d)) return settings;
            var sound = d["sound"];
            if (sound != null && sound.Type == JTokenType.Boolean) settings = settings with { Sound = (bool)sound };
            var adaptive = d["adaptive"];
            if (adaptive != null && adaptive.Type == JTokenType.Boolean) settings = settings with { Adaptive = (bool)adaptive };
            var speed = d["animation_speed"];
            if (speed != null && (speed.Type == JTokenType.Integer || speed.Type == JTokenType.Float))
            {
                double v = (double)speed;
                if (PlayerSettings.IsValidSpeed(v)) settings = settings with { AnimationSpeed = v };
            }
            var fps = d["target_fps"];
            if (fps != null && fps.Type == JTokenType.Integer && PlayerSettings.IsValidFps((int)fps)) settings = settings with { TargetFps = (int)fps };
            var skin = d["skin"];
            if (skin != null && skin.Type == JTokenType.String && PlayerSettings.IsValidSkin((string?)skin)) settings = settings with { Skin = (string)skin! };
            return settings;
        }

        static JObject SettingsToJson(PlayerSettings s) => new JObject
        {
            ["sound"] = s.Sound,
            ["animation_speed"] = s.AnimationSpeed,
            ["adaptive"] = s.Adaptive,
            ["target_fps"] = s.TargetFps,
            ["skin"] = s.Skin,
        };

        // -- file helpers ----------------------------------------------------------------------

        /// <summary>Parsed JSON, or null if the file is missing, unreadable or invalid (invalid files are moved aside).</summary>
        JToken? ReadJson(string name)
        {
            byte[]? bytes;
            try { bytes = _storage.TryRead(name); }
            catch (Exception e)
            {
                _log?.Warn("could not read " + name + ": " + e.Message);
                return null; // unreadable right now: do not destroy it
            }
            if (bytes == null) return null;
            string text;
            try { text = StrictUtf8.GetString(bytes).TrimStart('﻿'); }
            catch (ArgumentException)
            {
                _log?.Warn(name + " is not valid UTF-8; moving it aside");
                MoveAside(name);
                return null;
            }
            try
            {
                var token = JsonConvert.DeserializeObject<JToken>(text, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None, CheckAdditionalContent = true });
                if (token != null) return token;
                throw new JsonReaderException("empty document");
            }
            catch (JsonException e)
            {
                _log?.Warn(name + " is not valid JSON (" + e.Message + "); moving it aside");
                MoveAside(name);
                return null;
            }
        }

        void MoveAside(string name)
        {
            try { _storage.Move(name, name + CorruptSuffix); }
            catch (Exception e) { _log?.Warn("could not move " + name + " aside: " + e.Message); }
        }


        static byte[] Encode(JObject obj)
        {
            // two-space indent and LF line ends on every platform (Newtonsoft uses the OS newline)
            string text = obj.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
            return new UTF8Encoding(false).GetBytes(text);
        }
    }
}
