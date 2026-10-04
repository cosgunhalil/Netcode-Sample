using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NetcodeSample.Editor
{
    /// <summary>The parts of <c>tickwise compare</c>'s report the comparison window shows.</summary>
    public sealed class CompareReport
    {
        public bool IsIdentical { get; set; }

        public long ComparedTicks { get; set; } = -1;

        public long FirstDivergenceTick { get; set; } = -1;

        public long LastAgreementTick { get; set; } = -1;

        /// <summary>The first dump tick at or after the divergence, which Tickwise suggests diffing at; -1 if none.</summary>
        public long SuggestedDumpTick { get; set; } = -1;

        public string Verdict { get; set; } = string.Empty;
    }

    /// <summary>One line of <c>tickwise diff</c>: a field that differs between the two dumps at a tick.</summary>
    public sealed class DiffEntry
    {
        public long Tick { get; set; }

        /// <summary>structural, exact or drift.</summary>
        public string Kind { get; set; }

        public string Path { get; set; }

        public string First { get; set; }

        public string Second { get; set; }

        /// <summary>Anything after the two values, e.g. the delta of a float.</summary>
        public string Note { get; set; }
    }

    /// <summary>Parses the tickwise CLI's text output (it has no machine-readable mode).</summary>
    public static class TickwiseOutput
    {
        private static readonly Regex s_verdict = new(@"^\s*verdict\s+(.+)$", RegexOptions.Multiline);
        private static readonly Regex s_identical = new(@"identical over (\d+) compared tick");
        private static readonly Regex s_divergence = new(@"first divergence at tick (\d+)");
        private static readonly Regex s_lastAgreement = new(@"last agreement at tick (\d+)");
        private static readonly Regex s_suggestedDump = new(@"tickwise diff .+ --at (\d+)");
        private static readonly Regex s_dumpTicks = new(@"state dumps\s+.*at ticks ([0-9, ]+)");
        private static readonly Regex s_tickHeader = new(@"^tick (\d+)\s");
        private static readonly Regex s_entry = new(@"^\s{2}(structural|exact|drift)\s+(\S+): (.*)$");
        private static readonly Regex s_versus = new(@"^(.*?) versus (.*?)(?:, (delta .*))?$");

        public static CompareReport ParseCompare(string output)
        {
            CompareReport report = new();
            Match verdict = s_verdict.Match(output);
            if (verdict.Success)
            {
                report.Verdict = verdict.Groups[1].Value.Trim();
            }

            Match identical = s_identical.Match(output);
            if (identical.Success)
            {
                report.IsIdentical = true;
                report.ComparedTicks = long.Parse(identical.Groups[1].Value);
            }

            report.FirstDivergenceTick = ParseLong(s_divergence, output);
            report.LastAgreementTick = ParseLong(s_lastAgreement, output);
            report.SuggestedDumpTick = ParseLong(s_suggestedDump, output);
            return report;
        }

        /// <summary>The dump ticks from <c>tickwise inspect</c>, in order.</summary>
        public static List<long> ParseDumpTicks(string inspectOutput)
        {
            List<long> ticks = new();
            Match match = s_dumpTicks.Match(inspectOutput);
            if (!match.Success)
            {
                return ticks;
            }

            foreach (string part in match.Groups[1].Value.Split(','))
            {
                if (long.TryParse(part.Trim(), out long tick))
                {
                    ticks.Add(tick);
                }
            }

            return ticks;
        }

        public static List<DiffEntry> ParseDiff(string output)
        {
            List<DiffEntry> entries = new();
            long tick = -1;
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                Match header = s_tickHeader.Match(trimmed);
                if (header.Success)
                {
                    tick = long.Parse(header.Groups[1].Value);
                    continue;
                }

                Match entry = s_entry.Match(trimmed);
                if (!entry.Success)
                {
                    continue;
                }

                DiffEntry diff = new()
                {
                    Tick = tick,
                    Kind = entry.Groups[1].Value,
                    Path = entry.Groups[2].Value,
                    Note = string.Empty,
                };

                Match versus = s_versus.Match(entry.Groups[3].Value);
                if (versus.Success)
                {
                    diff.First = versus.Groups[1].Value;
                    diff.Second = versus.Groups[2].Value;
                    diff.Note = versus.Groups[3].Value;
                }
                else
                {
                    diff.First = entry.Groups[3].Value;
                    diff.Second = string.Empty;
                }

                entries.Add(diff);
            }

            return entries;
        }

        private static long ParseLong(Regex pattern, string text)
        {
            Match match = pattern.Match(text);
            return match.Success ? long.Parse(match.Groups[1].Value) : -1;
        }
    }
}
