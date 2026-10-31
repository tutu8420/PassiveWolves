using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ChineseCanFly.PassiveWolves
{
    [BepInPlugin(Guid, "Passive Wolves", "2.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "ChineseCanFly.PassiveWolves";

        private static readonly HashSet<string> VanillaTames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Boar", "Boar_piggy", "Wolf", "Wolf_cub", "Lox", "Lox_calf",
            "Chicken", "Hen", "Asksvin", "Asksvin_calf"
        };

        private sealed class Entry
        {
            public string Prefab;
            public string Display;
            public float Alert;
            public float Chase;
            public bool Cleared;
        }

        private ConfigEntry<KeyboardShortcut> _hotkey;
        private ConfigEntry<bool> _startPassive;
        private ConfigEntry<float> _maxDistance;
        private ConfigEntry<float> _scanInterval;
        private ConfigEntry<bool> _vanilla;
        private ConfigEntry<string> _custom;
        private ConfigEntry<string> _only;
        private ConfigEntry<string> _never;
        private ConfigEntry<bool> _logPerf;
        private ConfigEntry<bool> _hudShow;
        private ConfigEntry<float> _hudX;
        private ConfigEntry<float> _hudY;
        private ConfigEntry<int> _hudFont;
        private ConfigEntry<bool> _hudPrefabNames;

        private static readonly Dictionary<MonsterAI, Entry> Tracked = new Dictionary<MonsterAI, Entry>();
        private static readonly List<MonsterAI> Drop = new List<MonsterAI>();
        private static readonly Dictionary<string, int> ScanCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> LastCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> DisplayCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly FieldInfo TargetCreature = typeof(MonsterAI).GetField("m_targetCreature", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TargetStatic = typeof(MonsterAI).GetField("m_targetStatic", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly List<string> CustomList = new List<string>();
        private static readonly List<string> OnlyList = new List<string>();
        private static readonly List<string> NeverList = new List<string>();
        private static string _customRaw;
        private static string _onlyRaw;
        private static string _neverRaw;

        private static Dictionary<string, string> _lang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _languageName = "English";

        private static bool _passive;
        private float _scanTimer;

        private double _perfTotalMs;
        private double _perfMaxMs;
        private int _perfScans;
        private int _perfLastCount;

        private string _hudText = "";
        private int _hudTotal;
        private GUIStyle _boxStyle;
        private GUIStyle _textStyle;
        private int _styleFontSize = -1;

        private void Awake()
        {
            LoadLanguage();

            _hotkey = Config.Bind("General", "Toggle Hotkey", new KeyboardShortcut(KeyCode.F9),
                new ConfigDescription(T("ToggleHotkey", "Key that switches the affected tamed creatures between fighting and passive.")));

            _startPassive = Config.Bind("General", "Start Passive", false,
                new ConfigDescription(T("StartPassive", "Default is off, so every tame fights normally until you press the hotkey.")));

            _scanInterval = Config.Bind("General", "Scan Interval", 1f,
                new ConfigDescription(T("ScanInterval", "Seconds between checks for newly tamed or newly loaded creatures."),
                    new AcceptableValueRange<float>(0.25f, 5f)));

            _maxDistance = Config.Bind("General", "Max Distance", 0f,
                new ConfigDescription(T("MaxDistance", "Calm radius (m): 0 = no limit, every tamed animal stays peaceful. A number = only animals inside the circle stay peaceful."),
                    new AcceptableValueRange<float>(0f, 500f)));

            _vanilla = Config.Bind("Scope", "Affect Vanilla Tames", true,
                new ConfigDescription(T("AffectVanilla", "Affect the game's own tameable animals: boar, wolf, lox, chicken and asksvin.")));

            _custom = Config.Bind("Scope", "Custom Mod Prefabs", "",
                new ConfigDescription(T("CustomPrefabs", "For animals added by other mods: prefab names, comma separated.")));

            _only = Config.Bind("Scope", "Only These Prefabs", "",
                new ConfigDescription(T("OnlyPrefabs", "Optional strict whitelist. When set, ONLY these are affected.")));

            _never = Config.Bind("Scope", "Never These Prefabs", "",
                new ConfigDescription(T("NeverPrefabs", "Comma separated prefab names that are never affected.")));

            _logPerf = Config.Bind("Diagnostics", "Log Performance", false,
                new ConfigDescription(T("LogPerformance", "Every 60 scans, write average and worst scan time to the log.")));

            _hudShow = Config.Bind("UI", "Show HUD", true,
                new ConfigDescription(T("HudShow", "Show the calm panel at the left edge while calm mode is on.")));

            _hudX = Config.Bind("UI", "HUD X (%)", 1f,
                new ConfigDescription(T("HudX", "Panel distance from the left edge, in percent of screen width."),
                    new AcceptableValueRange<float>(0f, 100f)));

            _hudY = Config.Bind("UI", "HUD Y (%)", 45f,
                new ConfigDescription(T("HudY", "Panel distance from the top edge, in percent of screen height."),
                    new AcceptableValueRange<float>(0f, 100f)));

            _hudFont = Config.Bind("UI", "Font Size", 16,
                new ConfigDescription(T("HudFont", "Panel text size in pixels."),
                    new AcceptableValueRange<int>(8, 40)));

            _hudPrefabNames = Config.Bind("UI", "Show Prefab Names", false,
                new ConfigDescription(T("HudPrefabNames", "Show raw prefab names instead of translated creature names.")));

            _passive = _startPassive.Value;
            Logger.LogInfo("Passive Wolves 2.1.0 loaded. Language: " + _languageName + ", hotkey " + _hotkey.Value);
        }

        private void Update()
        {
            try
            {
                if (_hotkey.Value.IsDown())
                {
                    SetPassive(!_passive, announce: true);
                }

                if (_passive)
                {
                    _scanTimer += Time.deltaTime;
                    float interval = Mathf.Max(0.25f, _scanInterval.Value);
                    if (_scanTimer >= interval)
                    {
                        _scanTimer = 0f;
                        Apply(announce: false);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Passive Wolves update failed: " + e);
            }
        }

        private void OnGUI()
        {
            if (!_passive || _hudTotal <= 0) return;
            if (_hudShow == null || !_hudShow.Value) return;

            try
            {
                int fontSize = _hudFont.Value;
                if (_boxStyle == null || _styleFontSize != fontSize)
                {
                    _boxStyle = new GUIStyle(GUI.skin.box);
                    _textStyle = new GUIStyle(GUI.skin.label);
                    _textStyle.fontSize = fontSize;
                    _textStyle.alignment = TextAnchor.UpperLeft;
                    _textStyle.wordWrap = false;
                    _styleFontSize = fontSize;
                }

                float x = Screen.width * Mathf.Clamp01(_hudX.Value / 100f);
                float y = Screen.height * Mathf.Clamp01(_hudY.Value / 100f);

                Vector2 size = _textStyle.CalcSize(new GUIContent(_hudText));
                Rect box = new Rect(x, y, size.x + 16f, size.y + 12f);
                GUI.Box(box, GUIContent.none, _boxStyle);
                GUI.Label(new Rect(box.x + 8f, box.y + 6f, size.x, size.y), _hudText, _textStyle);
            }
            catch
            {
                // never let UI errors break the game loop
            }
        }

        private void SetPassive(bool on, bool announce)
        {
            _passive = on;
            if (on)
            {
                Apply(announce);
            }
            else
            {
                Restore();
                if (announce) Announce(T("MsgOff", "Tames: fighting again"));
            }
        }

        private void Apply(bool announce)
        {
            long started = Stopwatch.GetTimestamp();
            Player player = Player.m_localPlayer;
            float maxDist = _maxDistance.Value;
            bool vanilla = _vanilla.Value;
            SyncLists();

            ScanCounts.Clear();
            int count = 0;
            List<Character> chars = Character.GetAllCharacters();
            for (int i = 0; i < chars.Count; i++)
            {
                Character c = chars[i];
                if (!Qualifies(c, player, maxDist, vanilla)) continue;

                MonsterAI ai = c.GetComponent<MonsterAI>();
                if (ai == null) continue;

                Entry e;
                if (!Tracked.TryGetValue(ai, out e))
                {
                    string prefab = PrefabName(c);
                    e = new Entry
                    {
                        Prefab = prefab,
                        // Vanilla animals use the game's own translation (Wolf -> 狼 on a Chinese client).
                        // Creatures added by other mods keep the prefab name exactly as typed in the config.
                        Display = VanillaTames.Contains(prefab) ? DisplayName(c) : prefab,
                        Alert = ai.m_alertRange,
                        Chase = ai.m_maxChaseDistance
                    };
                    Tracked[ai] = e;
                    DisplayCache[e.Prefab] = e.Display;
                }

                if (!e.Cleared || ai.m_alertRange != 0f || ai.m_maxChaseDistance != 0f)
                {
                    ai.m_alertRange = 0f;
                    ai.m_maxChaseDistance = 0f;
                    ClearTarget(ai);
                    e.Cleared = true;
                }

                int had;
                ScanCounts.TryGetValue(e.Prefab, out had);
                ScanCounts[e.Prefab] = had + 1;
                count++;
            }

            Drop.Clear();
            foreach (KeyValuePair<MonsterAI, Entry> kv in Tracked)
            {
                MonsterAI ai = kv.Key;
                bool keep = false;
                if (ai != null)
                {
                    Character c = ai.GetComponent<Character>();
                    keep = c != null && Qualifies(c, player, maxDist, vanilla);
                }
                if (keep) continue;

                if (ai != null)
                {
                    ai.m_alertRange = kv.Value.Alert;
                    ai.m_maxChaseDistance = kv.Value.Chase;
                }
                Drop.Add(ai);
            }
            for (int i = 0; i < Drop.Count; i++) Tracked.Remove(Drop[i]);

            if (_hudTotal != count || CountsChanged()) RebuildHud();

            if (announce) Announce(string.Format(T("MsgOn", "Tames: passive now ({0})"), count));

            RecordPerf((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency, count);
        }

        private bool CountsChanged()
        {
            if (ScanCounts.Count != LastCounts.Count) return true;
            foreach (KeyValuePair<string, int> kv in ScanCounts)
            {
                int old;
                if (!LastCounts.TryGetValue(kv.Key, out old) || old != kv.Value) return true;
            }
            return false;
        }

        private void RebuildHud()
        {
            LastCounts.Clear();
            List<KeyValuePair<string, int>> sorted = new List<KeyValuePair<string, int>>(ScanCounts.Count);
            foreach (KeyValuePair<string, int> kv in ScanCounts)
            {
                LastCounts[kv.Key] = kv.Value;
                sorted.Add(kv);
            }
            sorted.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });

            _hudTotal = 0;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < sorted.Count; i++)
            {
                string prefab = sorted[i].Key;
                int n = sorted[i].Value;
                _hudTotal += n;
                sb.Append(HudName(prefab)).Append(" x").Append(n).Append('\n');
            }

            string title = string.Format(T("HudTitle", "Calm {0}"), _hudTotal);
            if (sb.Length > 0) sb.Length -= 1;
            _hudText = title + "\n" + sb;
        }

        private string HudName(string prefab)
        {
            if (_hudPrefabNames.Value) return prefab;
            string name;
            if (DisplayCache.TryGetValue(prefab, out name)) return name;
            return prefab;
        }

        private static MethodInfo _localizeMethod;
        private static object _localizeInstance;

        private static string DisplayName(Character c)
        {
            try
            {
                string localized = Localize(c.m_name);
                if (!string.IsNullOrEmpty(localized) && localized != c.m_name) return localized;
            }
            catch
            {
                // fall back to the prefab name
            }
            return PrefabName(c);
        }

        // Resolved by reflection so the mod does not need a compile-time reference to the game's Localization class.
        private static string Localize(string token)
        {
            if (_localizeMethod == null && _localizeInstance == null)
            {
                Type t = FindLocalizationType();
                if (t != null)
                {
                    PropertyInfo[] props = t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int i = 0; i < props.Length; i++)
                    {
                        if (props[i].PropertyType != t) continue;
                        try { _localizeInstance = props[i].GetValue(null); } catch { }
                        if (_localizeInstance != null) break;
                    }
                    _localizeMethod = t.GetMethod("Localize", new[] { typeof(string) });
                }
            }
            if (_localizeMethod == null || _localizeInstance == null) return token;
            try
            {
                string result = (string)_localizeMethod.Invoke(_localizeInstance, new object[] { token });
                return string.IsNullOrEmpty(result) ? token : result;
            }
            catch
            {
                return token;
            }
        }

        private static Type FindLocalizationType()
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                Type[] types;
                try { types = asms[i].GetTypes(); }
                catch { continue; }
                for (int j = 0; j < types.Length; j++)
                {
                    if (types[j].Name == "Localization") return types[j];
                }
            }
            return null;
        }

        private void Restore()
        {
            foreach (KeyValuePair<MonsterAI, Entry> kv in Tracked)
            {
                MonsterAI ai = kv.Key;
                if (ai == null) continue;
                ai.m_alertRange = kv.Value.Alert;
                ai.m_maxChaseDistance = kv.Value.Chase;
            }
            Tracked.Clear();
            ScanCounts.Clear();
            LastCounts.Clear();
            _hudTotal = 0;
            _hudText = "";
        }

        private bool Qualifies(Character c, Player player, float maxDist, bool vanilla)
        {
            if (c == null || !c.IsTamed() || c.IsDead()) return false;

            string prefab = PrefabName(c);
            if (Contains(NeverList, prefab)) return false;

            if (OnlyList.Count > 0)
            {
                if (!Contains(OnlyList, prefab)) return false;
            }
            else
            {
                bool match = (vanilla && VanillaTames.Contains(prefab)) || Contains(CustomList, prefab);
                if (!match) return false;
            }

            if (maxDist > 0f)
            {
                if (player == null) return false;
                if (Vector3.SqrMagnitude(c.transform.position - player.transform.position) > maxDist * maxDist) return false;
            }
            return true;
        }

        private void SyncLists()
        {
            if (!string.Equals(_customRaw, _custom.Value, StringComparison.Ordinal))
            {
                _customRaw = _custom.Value;
                Rebuild(CustomList, _customRaw);
            }
            if (!string.Equals(_onlyRaw, _only.Value, StringComparison.Ordinal))
            {
                _onlyRaw = _only.Value;
                Rebuild(OnlyList, _onlyRaw);
            }
            if (!string.Equals(_neverRaw, _never.Value, StringComparison.Ordinal))
            {
                _neverRaw = _never.Value;
                Rebuild(NeverList, _neverRaw);
            }
        }

        private static void Rebuild(List<string> list, string raw)
        {
            list.Clear();
            if (string.IsNullOrEmpty(raw)) return;
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length > 0) list.Add(p);
            }
        }

        private static bool Contains(List<string> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string PrefabName(Character c)
        {
            string n = c.gameObject.name;
            if (n.EndsWith("(Clone)", StringComparison.Ordinal))
            {
                n = n.Substring(0, n.Length - 7);
            }
            return n.Trim();
        }

        private static void ClearTarget(MonsterAI ai)
        {
            try
            {
                if (TargetCreature != null) TargetCreature.SetValue(ai, null);
                if (TargetStatic != null) TargetStatic.SetValue(ai, null);
            }
            catch
            {
                // zeroed ranges already stop new targets
            }
        }

        private void RecordPerf(double ms, int count)
        {
            _perfScans++;
            _perfTotalMs += ms;
            _perfLastCount = count;
            if (ms > _perfMaxMs) _perfMaxMs = ms;

            if (_perfScans >= 60)
            {
                if (_logPerf != null && _logPerf.Value)
                {
                    Logger.LogInfo(string.Format(
                        "perf: {0} scans, avg {1:F3} ms, max {2:F3} ms, {3} tame(s) held, {4} tracked",
                        _perfScans, _perfTotalMs / _perfScans, _perfMaxMs, _perfLastCount, Tracked.Count));
                }
                _perfScans = 0;
                _perfTotalMs = 0.0;
                _perfMaxMs = 0.0;
            }
        }

        private void LoadLanguage()
        {
            try
            {
                string lang = PlayerPrefs.GetString("language", "English");
                _languageName = string.IsNullOrEmpty(lang) ? "English" : lang;

                string dir = Path.GetDirectoryName(Info.Location);
                string file = Path.Combine(dir, "PassiveWolves." + _languageName + ".yml");
                if (!File.Exists(file)) file = Path.Combine(dir, "PassiveWolves.English.yml");
                if (!File.Exists(file)) return;

                string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int sep = line.IndexOf(':');
                    if (sep <= 0) continue;
                    string key = line.Substring(0, sep).Trim();
                    string value = line.Substring(sep + 1).Trim();
                    if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    {
                        value = value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\n", "\n");
                    }
                    _lang[key] = value;
                }
            }
            catch
            {
                // missing translations simply fall back to the built-in English text
            }
        }

        private static string T(string key, string fallback)
        {
            string value;
            if (_lang.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)) return value;
            return fallback;
        }

        private static void Announce(string text)
        {
            Player p = Player.m_localPlayer;
            if (p != null)
            {
                p.Message(MessageHud.MessageType.Center, text);
            }
        }
    }
}



