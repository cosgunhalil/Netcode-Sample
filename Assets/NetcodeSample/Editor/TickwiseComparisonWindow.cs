using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NetcodeSample.Game;
using UnityEditor;
using UnityEngine;

namespace NetcodeSample.Editor
{
    /// <summary>
    /// Compares two Tickwise recordings with the tickwise CLI and shows the result: the verdict, the first divergent
    /// tick, and the differing fields at a dump tick in a sortable, filterable table.
    /// </summary>
    public sealed class TickwiseComparisonWindow : EditorWindow
    {
        private static readonly Regex s_unitIndex = new(@"^units\[(\d+)\]");

        private readonly List<Pair> _sessions = new();
        private readonly List<DiffEntry> _visibleEntries = new();
        private string[] _files = Array.Empty<string>();
        private bool _pickFiles;
        private int _sessionIndex;
        private int _firstIndex;
        private int _secondIndex = 1;

        private string _comparedFirst;
        private string _comparedSecond;
        private CompareReport _report;
        private string _compareOutput = string.Empty;
        private List<long> _sharedDumpTicks = new();
        private int _dumpIndex;

        private List<DiffEntry> _entries = new();
        private string _diffOutput = string.Empty;
        private string _diffSummary = string.Empty;
        private string _filter = string.Empty;
        private SortColumn _sortColumn = SortColumn.Field;
        private bool _sortAscending = true;
        private bool _showRawOutput;
        private Vector2 _tableScroll;
        private Vector2 _rawScroll;

        private enum SortColumn
        {
            Kind,
            Field,
            First,
            Second,
        }

        [MenuItem("Netcode Sample/Tickwise/Comparison Window")]
        public static void Open()
        {
            GetWindow<TickwiseComparisonWindow>("Tickwise Comparison").Show();
        }

        private void OnEnable()
        {
            RefreshFiles();
        }

        private void OnGUI()
        {
            DrawSourcePicker();
            if (_report == null)
            {
                return;
            }

            EditorGUILayout.Space();
            DrawVerdict();
            DrawDiffControls();
            if (_entries.Count > 0)
            {
                DrawTable();
            }

            DrawRawOutput();
        }

        private void DrawSourcePicker()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(TickwiseSupport.RecordingsFolder, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
                {
                    RefreshFiles();
                }

                if (GUILayout.Button("Open Folder", EditorStyles.toolbarButton))
                {
                    Directory.CreateDirectory(TickwiseSupport.RecordingsFolder);
                    EditorUtility.RevealInFinder(TickwiseSupport.RecordingsFolder);
                }
            }

            if (_files.Length < 2)
            {
                EditorGUILayout.HelpBox("Need at least two recordings. Play a match first; recordings go to the folder above.", MessageType.Info);
                return;
            }

            _pickFiles = GUILayout.Toolbar(_pickFiles ? 1 : 0, new[] { "Sessions", "Any two files" }) == 1;
            string first;
            string second;
            if (_pickFiles || _sessions.Count == 0)
            {
                string[] names = _files.Select(Path.GetFileName).ToArray();
                _firstIndex = Mathf.Clamp(EditorGUILayout.Popup("First", _firstIndex, names), 0, names.Length - 1);
                _secondIndex = Mathf.Clamp(EditorGUILayout.Popup("Second", _secondIndex, names), 0, names.Length - 1);
                first = _files[_firstIndex];
                second = _files[_secondIndex];
            }
            else
            {
                _sessionIndex = Mathf.Clamp(EditorGUILayout.Popup("Session", _sessionIndex, _sessions.Select(pair => pair.Label).ToArray()), 0, _sessions.Count - 1);
                first = _sessions[_sessionIndex].First;
                second = _sessions[_sessionIndex].Second;
                EditorGUILayout.LabelField(" ", $"{Path.GetFileName(first)}  vs  {Path.GetFileName(second)}", EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(first == second))
            {
                if (GUILayout.Button("Compare", GUILayout.Height(26)))
                {
                    Compare(first, second);
                }
            }
        }

        private void DrawVerdict()
        {
            if (_report.IsIdentical)
            {
                EditorGUILayout.HelpBox($"Identical over {_report.ComparedTicks} compared ticks.\n{_report.Verdict}", MessageType.Info);
            }
            else if (_report.FirstDivergenceTick >= 0)
            {
                string agreement = _report.LastAgreementTick >= 0 ? $", last agreement at tick {_report.LastAgreementTick}" : string.Empty;
                EditorGUILayout.HelpBox($"DIVERGED at tick {_report.FirstDivergenceTick}{agreement}.\n{_report.Verdict}", MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(string.IsNullOrEmpty(_report.Verdict) ? "No verdict; see the raw output below." : _report.Verdict, MessageType.Warning);
            }
        }

        private void DrawDiffControls()
        {
            if (_sharedDumpTicks.Count == 0)
            {
                EditorGUILayout.LabelField("No state dumps at a tick both recordings share.", EditorStyles.miniLabel);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                string[] labels = _sharedDumpTicks.Select(tick => tick == _report.SuggestedDumpTick ? $"{tick} (first dump after the divergence)" : tick.ToString()).ToArray();
                _dumpIndex = Mathf.Clamp(EditorGUILayout.Popup("Dump tick", _dumpIndex, labels), 0, labels.Length - 1);
                if (GUILayout.Button("Diff", GUILayout.Width(80)))
                {
                    Diff(_sharedDumpTicks[_dumpIndex]);
                }
            }

            if (!string.IsNullOrEmpty(_diffSummary))
            {
                EditorGUILayout.LabelField(_diffSummary, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawTable()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                string filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField);
                if (filter != _filter)
                {
                    _filter = filter;
                    UpdateVisibleEntries();
                }

                GUILayout.Label($"{_visibleEntries.Count} of {_entries.Count}", EditorStyles.miniLabel, GUILayout.Width(90));
            }

            float width = position.width - 30f;
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawHeader("Kind", SortColumn.Kind, width * 0.1f);
                DrawHeader("Field", SortColumn.Field, width * 0.36f);
                DrawHeader("First", SortColumn.First, width * 0.18f);
                DrawHeader("Second", SortColumn.Second, width * 0.18f);
                GUILayout.Label("Note", EditorStyles.miniBoldLabel, GUILayout.Width(width * 0.18f));
            }

            _tableScroll = EditorGUILayout.BeginScrollView(_tableScroll, GUILayout.MinHeight(160));
            foreach (DiffEntry entry in _visibleEntries)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color previous = GUI.contentColor;
                    GUI.contentColor = entry.Kind == "structural" ? new Color(1f, 0.45f, 0.45f) : entry.Kind == "drift" ? new Color(0.5f, 0.85f, 1f) : new Color(1f, 0.85f, 0.4f);
                    GUILayout.Label(entry.Kind, GUILayout.Width(width * 0.1f));
                    GUI.contentColor = previous;
                    EditorGUILayout.SelectableLabel(entry.Path, GUILayout.Width(width * 0.36f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.SelectableLabel(entry.First, GUILayout.Width(width * 0.18f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.SelectableLabel(entry.Second, GUILayout.Width(width * 0.18f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    GUILayout.Label(entry.Note, EditorStyles.miniLabel, GUILayout.Width(width * 0.18f));
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader(string label, SortColumn column, float width)
        {
            string arrow = _sortColumn == column ? (_sortAscending ? " ▲" : " ▼") : string.Empty;
            if (GUILayout.Button(label + arrow, EditorStyles.miniBoldLabel, GUILayout.Width(width)))
            {
                _sortAscending = _sortColumn != column || !_sortAscending;
                _sortColumn = column;
                UpdateVisibleEntries();
            }
        }

        private void DrawRawOutput()
        {
            _showRawOutput = EditorGUILayout.Foldout(_showRawOutput, "Raw tickwise output", true);
            if (_showRawOutput)
            {
                _rawScroll = EditorGUILayout.BeginScrollView(_rawScroll, GUILayout.MinHeight(120));
                EditorGUILayout.TextArea(_compareOutput + "\n" + _diffOutput, EditorStyles.textArea);
                EditorGUILayout.EndScrollView();
            }
        }

        private void RefreshFiles()
        {
            _files = Directory.Exists(TickwiseSupport.RecordingsFolder)
                ? new DirectoryInfo(TickwiseSupport.RecordingsFolder).GetFiles("*.rec").OrderByDescending(file => file.LastWriteTimeUtc).Select(file => file.FullName).ToArray()
                : Array.Empty<string>();

            // A session's red and blue recordings, and a recording with its self-check replay.
            _sessions.Clear();
            foreach (string file in _files)
            {
                string name = Path.GetFileName(file);
                string folder = Path.GetDirectoryName(file);
                if (name.EndsWith("-red.rec", StringComparison.OrdinalIgnoreCase))
                {
                    string stem = name.Substring(0, name.Length - "-red.rec".Length);
                    string blue = Path.Combine(folder, stem + "-blue.rec");
                    if (File.Exists(blue))
                    {
                        _sessions.Add(new Pair($"{stem}   (red / blue)", file, blue));
                    }
                }
                else if (!name.EndsWith(".replay.rec", StringComparison.OrdinalIgnoreCase))
                {
                    string replay = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + ".replay.rec");
                    if (File.Exists(replay))
                    {
                        _sessions.Add(new Pair($"{Path.GetFileNameWithoutExtension(name)}   (recording / self-check replay)", file, replay));
                    }
                }
            }

            _sessionIndex = 0;
            _firstIndex = 0;
            _secondIndex = Mathf.Min(1, Mathf.Max(0, _files.Length - 1));
        }

        private void Compare(string first, string second)
        {
            _comparedFirst = first;
            _comparedSecond = second;
            _entries = new List<DiffEntry>();
            _visibleEntries.Clear();
            _diffOutput = string.Empty;
            _diffSummary = string.Empty;

            TickwiseCli.TryRun($"compare {TickwiseCli.Quote(first)} {TickwiseCli.Quote(second)}", out _, out _compareOutput);
            _report = TickwiseOutput.ParseCompare(_compareOutput);

            TickwiseCli.TryRun($"inspect {TickwiseCli.Quote(first)}", out _, out string firstInspect);
            TickwiseCli.TryRun($"inspect {TickwiseCli.Quote(second)}", out _, out string secondInspect);
            HashSet<long> secondDumps = new(TickwiseOutput.ParseDumpTicks(secondInspect));
            _sharedDumpTicks = TickwiseOutput.ParseDumpTicks(firstInspect).Where(secondDumps.Contains).ToList();

            int suggested = _sharedDumpTicks.IndexOf(_report.SuggestedDumpTick);
            _dumpIndex = suggested >= 0 ? suggested : 0;

            // A divergence with a dump after it: go straight to the fields.
            if (suggested >= 0)
            {
                Diff(_report.SuggestedDumpTick);
            }
        }

        private void Diff(long tick)
        {
            TickwiseCli.TryRun($"diff {TickwiseCli.Quote(_comparedFirst)} {TickwiseCli.Quote(_comparedSecond)} --at {tick} --all --no-color", out _, out _diffOutput);
            _entries = TickwiseOutput.ParseDiff(_diffOutput);

            SortedSet<int> units = new();
            foreach (DiffEntry entry in _entries)
            {
                Match unit = s_unitIndex.Match(entry.Path);
                if (unit.Success)
                {
                    units.Add(int.Parse(unit.Groups[1].Value));
                }
            }

            int structural = _entries.Count(entry => entry.Kind == "structural");
            int exact = _entries.Count(entry => entry.Kind == "exact");
            int drift = _entries.Count(entry => entry.Kind == "drift");
            string unitList = units.Count == 0 ? "none" : string.Join(", ", units.Take(20)) + (units.Count > 20 ? $" and {units.Count - 20} more" : string.Empty);
            _diffSummary = _entries.Count == 0
                ? $"Tick {tick}: the dumps are identical."
                : $"Tick {tick}: {_entries.Count} differences ({structural} structural, {exact} exact, {drift} float drift). Unit slots involved: {unitList}.";
            UpdateVisibleEntries();
        }

        private void UpdateVisibleEntries()
        {
            _visibleEntries.Clear();
            foreach (DiffEntry entry in _entries)
            {
                if (string.IsNullOrEmpty(_filter) || entry.Path.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _visibleEntries.Add(entry);
                }
            }

            Comparison<DiffEntry> comparison = _sortColumn switch
            {
                SortColumn.Kind => (a, b) => string.CompareOrdinal(a.Kind, b.Kind),
                SortColumn.First => (a, b) => NaturalCompare(a.First, b.First),
                SortColumn.Second => (a, b) => NaturalCompare(a.Second, b.Second),
                _ => (a, b) => NaturalCompare(a.Path, b.Path),
            };

            _visibleEntries.Sort((a, b) => _sortAscending ? comparison(a, b) : comparison(b, a));
        }

        // Compares embedded numbers by value, so units[9] sorts before units[10].
        private static int NaturalCompare(string a, string b)
        {
            int i = 0;
            int j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int startA = i;
                    int startB = j;
                    while (i < a.Length && char.IsDigit(a[i]))
                    {
                        i++;
                    }

                    while (j < b.Length && char.IsDigit(b[j]))
                    {
                        j++;
                    }

                    string numberA = a.Substring(startA, i - startA).TrimStart('0');
                    string numberB = b.Substring(startB, j - startB).TrimStart('0');
                    int byLength = numberA.Length.CompareTo(numberB.Length);
                    if (byLength != 0)
                    {
                        return byLength;
                    }

                    int byDigits = string.CompareOrdinal(numberA, numberB);
                    if (byDigits != 0)
                    {
                        return byDigits;
                    }

                    continue;
                }

                if (a[i] != b[j])
                {
                    return a[i].CompareTo(b[j]);
                }

                i++;
                j++;
            }

            return (a.Length - i).CompareTo(b.Length - j);
        }

        private readonly struct Pair
        {
            public Pair(string label, string first, string second)
            {
                Label = label;
                First = first;
                Second = second;
            }

            public string Label { get; }

            public string First { get; }

            public string Second { get; }
        }
    }
}
