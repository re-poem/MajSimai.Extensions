using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MajSimai.Extensions.Checker;

/// <summary>
/// Simai谱面语法检查器
/// </summary>
public static class SimaiChecker
{
    private static readonly string[] SLIDE_TYPES = ["qq", "pp", "q", "p", "w", "z", "s", "V", "v", "<", ">", "^", "-"];
    private static readonly char[] SENSOR_TYPES = ['A', 'B', 'C', 'D', 'E'];

    /// <summary>
    /// 检查Simai谱面语法
    /// </summary>
    /// <param name="fumen">谱面字符串</param>
    /// <returns>诊断信息列表</returns>
    public static IReadOnlyList<Diagnostic> Check(string fumen)
    {
        var context = new CheckerContext(fumen);
        var segments = SplitIntoSegments(fumen);

        foreach (var segment in segments)
        {
            CheckSegment(context, segment, fumen);
        }

        CheckChartTermination(context, segments, fumen);

        return context.Diagnostics;
    }

    private static List<ChartSegment> SplitIntoSegments(string fumen)
    {
        var segments = new List<ChartSegment>();
        var currentStart = 0;
        var pos = TextPosition.Start;
        var inComment = false;

        for (var i = 0; i < fumen.Length; i++)
        {
            var c = fumen[i];

            if (inComment)
            {
                if (c == '\n')
                {
                    inComment = false;
                    currentStart = i + 1;
                    pos = pos.Advance(c);
                }
                continue;
            }

            if (c == '|' && i + 1 < fumen.Length && fumen[i + 1] == '|')
            {
                if (i > currentStart)
                {
                    segments.Add(new ChartSegment(fumen[currentStart..i], pos, i - currentStart));
                }
                inComment = true;
                i++;
                pos = pos.Advance('|').Advance('|');
                currentStart = i + 1;
                continue;
            }

            if (c == ',')
            {
                if (i > currentStart)
                {
                    segments.Add(new ChartSegment(fumen[currentStart..i], pos, i - currentStart));
                }
                pos = pos.Advance(c);
                currentStart = i + 1;
            }
            else
            {
                pos = pos.Advance(c);
            }
        }

        if (currentStart < fumen.Length)
        {
            segments.Add(new ChartSegment(fumen[currentStart..], pos, fumen.Length - currentStart));
        }

        return segments;
    }

    private static void CheckSegment(CheckerContext context, ChartSegment segment, string fumen)
    {
        if (string.IsNullOrWhiteSpace(segment.Content)) return;

        var content = segment.Content;
        var startPos = segment.StartPosition;

        if (content == "E")
        {
            return;
        }

        // HSpeed 必须在 segment 开头
        if (content.StartsWith("<HS*"))
        {
            content = CheckHSpeedSyntax(context, content, startPos);
        }
        else
        {
            var hspeedElsewhere = content.IndexOf("<HS*");
            if (hspeedElsewhere != -1)
            {
                context.AddError(
                    "HSpeed must be at segment start",
                    "HSpeed definition must appear at the beginning of a segment, before any notes",
                    startPos.Advance(content[..hspeedElsewhere])
                );
            }
        }

        // BPM 必须在 segment 开头（HSpeed 之后）
        var noteStart = 0;
        if (content.StartsWith('('))
        {
            var bpmEnd = content.IndexOf(')');
            CheckBpmDefinition(context, content, startPos);
            if (bpmEnd == -1) return;
            noteStart = bpmEnd + 1;
        }
        else
        {
            var bpmElsewhere = content.IndexOf('(');
            if (bpmElsewhere != -1)
            {
                context.AddError(
                    "BPM must be at segment start",
                    "BPM definition must appear at the beginning of a segment, before any notes",
                    startPos.Advance(content[..bpmElsewhere])
                );
            }
        }

        // 拍号必须在 BPM 之后（或 segment 开头）
        if (noteStart < content.Length && content[noteStart..].StartsWith('{'))
        {
            var localBeatEnd = content[noteStart..].IndexOf('}');
            CheckBeatDefinition(context, content[noteStart..], startPos.Advance(content[..noteStart]));
            if (localBeatEnd == -1) return;
            noteStart += localBeatEnd + 1;
        }
        else
        {
            var beatElsewhere = content.IndexOf('{');
            if (beatElsewhere != -1 && beatElsewhere >= noteStart)
            {
                context.AddError(
                    "Beat definition must be at segment start",
                    "Beat definition must appear after BPM (if any), before any notes",
                    startPos.Advance(content[..beatElsewhere])
                );
            }
        }

        // 没有note
        if (noteStart >= content.Length) return;
        var noteContent = content[noteStart..];
        if (string.IsNullOrEmpty(noteContent)) return;

        var noteStartPos = startPos.Advance(content[..noteStart]);
        CheckNoteGroup(context, noteContent, noteStartPos, fumen);
    }

    private static string CheckHSpeedSyntax(CheckerContext context, string content, TextPosition startPos)
    {
        var hspeedEnd = content.IndexOf('>');
        if (hspeedEnd == -1)
        {
            context.AddError(
                "HSpeed definition not closed",
                "HSpeed must be enclosed in angle brackets, e.g., <HS*1.5>",
                startPos
            );
            return content;
        }

        var hspeedContent = content[4..hspeedEnd];
        if (string.IsNullOrEmpty(hspeedContent))
        {
            context.AddError(
                "Empty HSpeed value",
                "HSpeed value cannot be empty",
                startPos.Advance("<HS*")
            );
            return content[(hspeedEnd + 1)..];
        }

        if (!double.TryParse(hspeedContent, out var hspeed) || hspeed <= 0)
        {
            context.AddError(
                $"Invalid HSpeed value: '{hspeedContent}'",
                "HSpeed must be a positive number",
                startPos.Advance("<HS*")
            );
        }

        return content[(hspeedEnd + 1)..];
    }

    private static void CheckBpmDefinition(CheckerContext context, string content, TextPosition startPos)
    {
        var closeIndex = content.IndexOf(')');
        if (closeIndex == -1)
        {
            context.AddError(
                "BPM definition not closed",
                "BPM must be enclosed in parentheses, e.g., (120)",
                startPos,
                new QuickFix(")", startPos.Advance(content))
            );
            return;
        }

        var bpmContent = content[1..closeIndex];
        if (string.IsNullOrEmpty(bpmContent))
        {
            context.AddError(
                "Empty BPM definition",
                "BPM value cannot be empty",
                startPos
            );
            return;
        }

        if (!double.TryParse(bpmContent, out var bpm) || bpm <= 0)
        {
            context.AddError(
                $"Invalid BPM value: '{bpmContent}'",
                "BPM must be a positive number",
                startPos.Advance("(")
            );
        }
    }

    private static void CheckBeatDefinition(CheckerContext context, string content, TextPosition startPos)
    {
        var closeIndex = content.IndexOf('}');
        if (closeIndex == -1)
        {
            context.AddError(
                "Beat definition not closed",
                "Beat must be enclosed in braces, e.g., {4} or {#0.5}",
                startPos,
                new QuickFix("}", startPos.Advance(content))
            );
            return;
        }

        var beatContent = content[1..closeIndex];
        if (string.IsNullOrEmpty(beatContent))
        {
            context.AddError(
                "Empty beat definition",
                "Beat value cannot be empty",
                startPos
            );
            return;
        }

        if (beatContent.StartsWith('#'))
        {
            var timeValue = beatContent[1..];
            if (!double.TryParse(timeValue, out var time) || time <= 0)
            {
                context.AddError(
                    $"Invalid absolute time value: '{timeValue}'",
                    "Absolute time must be a positive number (in seconds)",
                    startPos.Advance("{#")
                );
            }
        }
        else
        {
            if (!int.TryParse(beatContent, out var beat) || beat <= 0)
            {
                context.AddError(
                    $"Invalid beat value: '{beatContent}'",
                    "Beat must be a positive integer, e.g., {4}, {8}, {16}",
                    startPos.Advance("{")
                );
            }
        }
    }

    private static void CheckNoteGroup(CheckerContext context, string content, TextPosition startPos, string fumen)
    {
        var notes = SplitByEach(content);

        foreach (var note in notes)
        {
            if (string.IsNullOrEmpty(note.Content))
            {
                context.AddError(
                    "Empty note in EACH group",
                    "EACH groups cannot contain empty notes",
                    startPos.Advance(content[..note.StartIndex])
                );
                continue;
            }
            CheckSingleNote(context, note.Content, startPos.Advance(content[..note.StartIndex]), fumen);
        }
    }

    private static List<(string Content, int StartIndex)> SplitByEach(string content)
    {
        var result = new List<(string, int)>();
        var currentStart = 0;

        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '/')
            {
                if (i > currentStart)
                    result.Add((content[currentStart..i], currentStart));
                currentStart = i + 1;
            }
            else if (content[i] == '`')
            {
                if (i > currentStart)
                    result.Add((content[currentStart..i], currentStart));
                currentStart = i + 1;
            }
        }

        if (currentStart < content.Length)
            result.Add((content[currentStart..], currentStart));

        return result;
    }

    private static void CheckSingleNote(CheckerContext context, string content, TextPosition startPos, string fumen)
    {
        if (string.IsNullOrEmpty(content)) return;

        if (IsTouchNote(content, out var sensorType, out var sensorIndex, out var hasFirework, out var isHold))
        {
            CheckTouchNote(context, content, startPos, sensorType, sensorIndex, hasFirework, isHold);
            return;
        }

        if (char.IsDigit(content[0]))
        {
            CheckButtonNote(context, content, startPos, fumen);
            return;
        }

        context.AddError(
            $"Invalid note: '{content}'",
            "Note must start with a button number (1-8) or sensor type (A-E, C)",
            startPos
        );
    }

    private static bool IsTouchNote(string content, out char sensorType, out int? sensorIndex, out bool hasFirework, out bool isHold)
    {
        sensorType = '\0';
        sensorIndex = null;
        hasFirework = false;
        isHold = false;

        if (string.IsNullOrEmpty(content)) return false;

        var c = char.ToUpperInvariant(content[0]);
        if (!SENSOR_TYPES.Contains(c) && c != 'C') return false;

        sensorType = c;

        if (content.Length == 1)
        {
            return c == 'C';
        }

        var idx = 1;
        if (content.Length > idx && char.IsDigit(content[idx]))
        {
            sensorIndex = content[idx] - '0';
            idx++;
        }

        if (content.Length > idx)
        {
            var remaining = content[idx..];
            isHold = remaining.Contains('h');
            hasFirework = remaining.Contains('f');
        }

        return true;
    }

    private static void CheckTouchNote(CheckerContext context, string content, TextPosition startPos,
        char sensorType, int? sensorIndex, bool hasFirework, bool isHold)
    {
        if (sensorType == 'C')
        {
            if (sensorIndex.HasValue && sensorIndex.Value != 1 && sensorIndex.Value != 2)
            {
                context.AddError(
                    $"Invalid C sensor index: {sensorIndex.Value}",
                    "C sensor can only have index 1 or 2 (or no index)",
                    startPos
                );
            }
        }
        else
        {
            if (!sensorIndex.HasValue || sensorIndex.Value < 1 || sensorIndex.Value > 8)
            {
                context.AddError(
                    $"Invalid sensor index for {sensorType}",
                    "Sensor index must be between 1 and 8",
                    startPos
                );
            }
        }

        var validChars = new HashSet<char> { 'f', 'h', 'x' };
        var idx = 1;
        if (sensorIndex.HasValue) idx++;

        var hasDuration = false;
        var durationStart = 0;
        var durationEnd = 0;

        for (var i = idx; i < content.Length; i++)
        {
            var c = char.ToLowerInvariant(content[i]);

            if (c == '[')
            {
                hasDuration = true;
                durationStart = i;
                var closeIdx = content.IndexOf(']', i);
                if (closeIdx == -1)
                {
                    context.AddError(
                        "Duration not closed for touch hold",
                        "Duration must be enclosed in brackets, e.g., Ch[4:3]",
                        startPos.Advance(content[..i])
                    );
                    return;
                }
                durationEnd = closeIdx;
                i = closeIdx;
                continue;
            }

            if (!validChars.Contains(c))
            {
                context.AddError(
                    $"Invalid character in touch note: '{content[i]}'",
                    "Touch notes can only contain 'f' (firework), 'h' (hold), or 'x' (EX) modifiers",
                    startPos.Advance(content[..i])
                );
            }
        }

        if (isHold && hasDuration)
        {
            var duration = content[(durationStart + 1)..durationEnd];
            ValidateDuration(context, content, startPos, duration, durationStart, "TOUCH HOLD");
        }
        else if (hasDuration && !isHold)
        {
            context.AddWarning(
                "Duration specified for non-hold touch note",
                "Duration is only meaningful for touch hold notes",
                startPos.Advance(content[..durationStart])
            );
        }
    }

    private static void CheckButtonNote(CheckerContext context, string content, TextPosition startPos, string fumen)
    {
        var firstDigit = content[0] - '0';
        if (firstDigit < 1 || firstDigit > 8)
        {
            context.AddError(
                $"Invalid button position: {firstDigit}",
                "Button position must be between 1 and 8",
                startPos
            );
            return;
        }

        if (content.Length == 1)
        {
            return;
        }

        if (char.IsDigit(content[1]) && content.Length == 2)
        {
            var secondDigit = content[1] - '0';
            if (secondDigit < 1 || secondDigit > 8)
            {
                context.AddError(
                    $"Invalid button position: {secondDigit}",
                    "Button position must be between 1 and 8",
                    startPos.Advance(content[0].ToString())
                );
            }
            return;
        }

        var noteInfo = ParseNoteInfo(content);
        ValidateNoteInfo(context, content, startPos, noteInfo);
        ValidateSlideChain(context, content, startPos, noteInfo);
    }

    private static void ValidateSlideChain(CheckerContext context, string content, TextPosition startPos, NoteInfo noteInfo)
    {
        if (noteInfo.Slides.Count == 0) return;

        var hasSameStartPoint = content.Contains('*');
        var hasConnectedSlides = noteInfo.Slides.Count > 1 && !hasSameStartPoint;

        if (hasConnectedSlides)
        {
            var previousEnd = noteInfo.StartPosition;
            var allHaveDuration = true;
            var noneHaveDuration = true;

            for (var i = 0; i < noteInfo.Slides.Count; i++)
            {
                var slide = noteInfo.Slides[i];

                if (slide.EndPosition == null) continue;

                if (i > 0 && slide.StartPosition != previousEnd)
                {
                    context.AddError(
                        $"Connected slide chain broken at segment {i + 1}",
                        $"Each connected slide must start where the previous one ended. Expected start at {previousEnd}, but slide starts at {slide.StartPosition}",
                        startPos.Advance(content[..slide.StartIndex])
                    );
                }

                if (slide.Duration != null)
                {
                    noneHaveDuration = false;
                }
                else
                {
                    allHaveDuration = false;
                }

                previousEnd = slide.EndPosition.Value;
            }

            if (!allHaveDuration && !noneHaveDuration)
            {
                context.AddError(
                    "Inconsistent duration specification in connected slide",
                    "Either all segments must have individual durations, or only the last segment should have a duration",
                    startPos
                );
            }

            if (noteInfo.Slides.Count(s => s.IsBreak) > 1)
            {
                context.AddWarning(
                    "Multiple BREAK markers in connected slide",
                    "Only the last segment of a connected slide should have the BREAK marker 'b'",
                    startPos
                );
            }
        }

        if (hasSameStartPoint)
        {
            var slideParts = content.Split('*');
            var expectedStart = noteInfo.StartPosition;

            foreach (var part in slideParts.Skip(1))
            {
                if (string.IsNullOrEmpty(part)) continue;

                var partInfo = ParseNoteInfo(expectedStart.ToString() + part);
                if (partInfo.Slides.Count > 0)
                {
                    var slide = partInfo.Slides[0];
                    if (slide.Duration == null)
                    {
                        context.AddError(
                            "Same-start-point slide missing duration",
                            "Each slide in a same-start-point group must have its own duration",
                            startPos
                        );
                    }
                }
            }
        }
    }

    private static NoteInfo ParseNoteInfo(string content)
    {
        var info = new NoteInfo();
        var idx = 0;

        if (char.IsDigit(content[0]))
        {
            info.StartPosition = content[0] - '0';
            idx = 1;
        }

        while (idx < content.Length)
        {
            var c = content[idx];

            if (c == 'h') { info.IsHold = true; idx++; continue; }
            if (c == 'b') { info.IsBreak = true; idx++; continue; }
            if (c == 'x') { info.IsEx = true; idx++; continue; }
            if (c == 'm') { info.IsMine = true; idx++; continue; }
            if (c == '$')
            {
                info.HasStar = true;
                if (idx + 1 < content.Length && content[idx + 1] == '$')
                {
                    info.HasDoubleStar = true;
                    idx += 2;
                }
                else
                {
                    idx++;
                }
                continue;
            }
            if (c == '@') { info.NoStar = true; idx++; continue; }
            if (c == '?') { info.FadeSlide = true; idx++; continue; }
            if (c == '!') { info.NoFadeSlide = true; idx++; continue; }

            if (c == '[')
            {
                var closeIdx = content.IndexOf(']', idx);
                if (closeIdx != -1)
                {
                    info.Duration = content[(idx + 1)..closeIdx];
                    info.DurationStart = idx;
                    info.DurationEnd = closeIdx;
                    idx = closeIdx + 1;
                }
                else
                {
                    info.Duration = content[(idx + 1)..];
                    info.DurationStart = idx;
                    info.DurationEnd = content.Length - 1;
                    idx = content.Length;
                }
                continue;
            }

            if (c == '*')
            {
                info.HasSameStartPointSlides = true;
                idx++;
                continue;
            }

            var slideMatch = TryMatchSlide(content, idx, info.StartPosition);
            if (slideMatch != null)
            {
                info.Slides.Add(slideMatch);
                idx = slideMatch.EndIndex;
                continue;
            }

            info.UnknownChars.Add((c, idx));
            idx++;
        }

        return info;
    }

    private static SlideInfo? TryMatchSlide(string content, int startIdx, int noteStartPosition)
    {
        var idx = startIdx;
        var slide = new SlideInfo { StartIndex = idx, StartPosition = noteStartPosition };

        foreach (var slideType in SLIDE_TYPES.OrderByDescending(s => s.Length))
        {
            if (idx + slideType.Length <= content.Length &&
                content[idx..(idx + slideType.Length)] == slideType)
            {
                slide.SlideType = slideType;
                idx += slideType.Length;
                break;
            }
        }

        if (slide.SlideType == null) return null;

        if (slide.SlideType == "V")
        {
            if (idx >= content.Length || !char.IsDigit(content[idx]))
                return slide;
            slide.FlexionPoint = content[idx] - '0';
            idx++;
        }

        if (idx < content.Length && char.IsDigit(content[idx]))
        {
            slide.EndPosition = content[idx] - '0';
            idx++;
        }

        if (idx < content.Length && content[idx] == '[')
        {
            var closeIdx = content.IndexOf(']', idx);
            if (closeIdx != -1)
            {
                slide.Duration = content[(idx + 1)..closeIdx];
                slide.DurationStart = idx;
                slide.DurationEnd = closeIdx;
                idx = closeIdx + 1;
            }
        }

        if (idx < content.Length && content[idx] == 'b')
        {
            slide.IsBreak = true;
            idx++;
        }

        slide.EndIndex = idx;
        return slide;
    }

    private static void ValidateNoteInfo(CheckerContext context, string content, TextPosition startPos, NoteInfo info)
    {
        foreach (var (c, idx) in info.UnknownChars)
        {
            context.AddError(
                $"Unknown character in note: '{c}'",
                $"Character '{c}' is not a valid note modifier or slide type",
                startPos.Advance(content[..idx])
            );
        }

        if (info.IsHold && info.Slides.Count > 0)
        {
            context.AddError(
                "Note cannot be both HOLD and SLIDE",
                "A note can only be one type: TAP, HOLD, or SLIDE",
                startPos
            );
        }

        if (info.HasStar && info.NoStar)
        {
            context.AddWarning(
                "Conflicting star modifiers: '$' and '@'",
                "Using both '$' (force star) and '@' (no star) is contradictory",
                startPos
            );
        }

        if (info.FadeSlide && info.NoFadeSlide)
        {
            context.AddWarning(
                "Conflicting slide fade modifiers: '?' and '!'",
                "Using both '?' (fade in) and '!' (no fade) is contradictory",
                startPos
            );
        }

        if (info.HasStar && info.Slides.Count > 0)
        {
            context.AddWarning(
                "Redundant star modifier '$' on SLIDE",
                "SLIDE notes automatically have a star shape; '$' is redundant here",
                startPos
            );
        }

        if (info.NoStar && info.Slides.Count == 0)
        {
            context.AddWarning(
                "Invalid '@' modifier on non-SLIDE note",
                "The '@' modifier (no star) is only meaningful for SLIDE notes",
                startPos
            );
        }

        if (info.FadeSlide && info.Slides.Count == 0)
        {
            context.AddWarning(
                "Invalid '?' modifier on non-SLIDE note",
                "The '?' modifier (fade slide) is only meaningful for SLIDE notes",
                startPos
            );
        }

        if (info.NoFadeSlide && info.Slides.Count == 0)
        {
            context.AddWarning(
                "Invalid '!' modifier on non-SLIDE note",
                "The '!' modifier (no fade slide) is only meaningful for SLIDE notes",
                startPos
            );
        }

        if (info.IsHold)
        {
            if (info.Duration == null)
            {
                // 纯 HOLD (如 "2h") 允许无时长，但有修饰符时必须有时长
                var hasModifiers = info.IsBreak || info.IsEx || info.IsMine || 
                                   info.HasStar || info.HasDoubleStar || info.NoStar ||
                                   info.FadeSlide || info.NoFadeSlide;
                if (hasModifiers)
                {
                    context.AddError(
                        "HOLD with modifiers must have duration",
                        "HOLD notes with modifiers (b, x, m, $, @, ?, !) must specify a duration, e.g., 2hb[4:1]",
                        startPos
                    );
                }
            }
            else
            {
                ValidateDuration(context, content, startPos, info.Duration, info.DurationStart, "HOLD");
            }
        }

        foreach (var slide in info.Slides)
        {
            ValidateSlide(context, content, startPos, info, slide);
        }

        if (!info.IsHold && info.Slides.Count == 0 && info.Duration != null)
        {
            context.AddWarning(
                "Duration specified for non-HOLD/SLIDE note",
                "Duration is only meaningful for HOLD and SLIDE notes",
                startPos.Advance(content[..info.DurationStart])
            );
        }
    }

    private static void ValidateDuration(CheckerContext context, string content, TextPosition startPos,
        string duration, int durationStart, string noteType)
    {
        if (string.IsNullOrEmpty(duration))
        {
            context.AddError(
                $"Empty duration for {noteType}",
                "Duration cannot be empty",
                startPos.Advance(content[..durationStart])
            );
            return;
        }

        var hashCount = duration.Count(c => c == '#');
        var colonCount = duration.Count(c => c == ':');

        if (hashCount >= 2)
        {
            context.AddError(
                $"Invalid duration format: '{duration}'",
                $"{noteType} does not support '##' format. Use 'division:beats', '#seconds', or 'BPM#division:beats'",
                startPos.Advance(content[..(durationStart + 1)])
            );
            return;
        }

        if (hashCount == 0 && colonCount == 0)
        {
            if (!double.TryParse(duration, out var val) || val <= 0)
            {
                context.AddError(
                    $"Invalid duration: '{duration}'",
                    "Duration must be a positive number or use format like '8:1' or '#1.5'",
                    startPos.Advance(content[..(durationStart + 1)])
                );
            }
        }
        else if (hashCount == 0 && colonCount == 1)
        {
            var parts = duration.Split(':');
            if (parts.Length != 2)
            {
                context.AddError(
                    $"Invalid duration format: '{duration}'",
                    "Duration format should be 'division:beats', e.g., '4:2' means 2 beats at quarter note division",
                    startPos.Advance(content[..(durationStart + 1)])
                );
                return;
            }

            if (!int.TryParse(parts[0], out var division) || division <= 0)
            {
                context.AddError(
                    $"Invalid division: '{parts[0]}'",
                    "Division must be a positive integer (e.g., 4 for quarter note, 8 for eighth note)",
                    startPos.Advance(content[..(durationStart + 1)])
                );
            }

            if (!int.TryParse(parts[1], out var beats) || beats < 0)
            {
                context.AddError(
                    $"Invalid beat count: '{parts[1]}'",
                    "Beat count must be a non-negative integer",
                    startPos.Advance(content[..(durationStart + 1 + parts[0].Length + 1)])
                );
            }
        }
        else if (hashCount == 1 && !duration.StartsWith('#'))
        {
            var parts = duration.Split('#');
            if (parts.Length != 2)
            {
                context.AddError(
                    $"Invalid duration format: '{duration}'",
                    "Duration format should be 'BPM#division:count' or 'BPM#seconds'",
                    startPos.Advance(content[..(durationStart + 1)])
                );
                return;
            }

            if (!double.TryParse(parts[0], out var bpm) || bpm <= 0)
            {
                context.AddError(
                    $"Invalid BPM: '{parts[0]}'",
                    "BPM must be a positive number",
                    startPos.Advance(content[..(durationStart + 1)])
                );
                return;
            }

            ValidateDurationPart(context, content, startPos, parts[1], durationStart + 1 + parts[0].Length + 1);
        }
        else if (hashCount == 1 && duration.StartsWith('#'))
        {
            var timeValue = duration[1..];
            if (!double.TryParse(timeValue, out var time) || time <= 0)
            {
                context.AddError(
                    $"Invalid absolute time: '{timeValue}'",
                    "Absolute time must be a positive number (in seconds)",
                    startPos.Advance(content[..(durationStart + 2)])
                );
            }
        }
        else
        {
            context.AddError(
                $"Invalid duration format: '{duration}'",
                "Too many '#' characters in duration specification",
                startPos.Advance(content[..(durationStart + 1)])
            );
        }
    }

    private static void ValidateDurationPart(CheckerContext context, string content, TextPosition startPos,
        string part, int offset)
    {
        if (part.Contains(':'))
        {
            var subParts = part.Split(':');
            if (subParts.Length != 2)
            {
                context.AddError(
                    $"Invalid note ratio: '{part}'",
                    "Note ratio should be 'length:count', e.g., '8:1'",
                    startPos.Advance(content[..offset])
                );
                return;
            }

            if (!int.TryParse(subParts[0], out var div) || div <= 0)
            {
                context.AddError(
                    $"Invalid division: '{subParts[0]}'",
                    "Division must be a positive integer",
                    startPos.Advance(content[..offset])
                );
            }

            if (!int.TryParse(subParts[1], out var count) || count < 0)
            {
                context.AddError(
                    $"Invalid count: '{subParts[1]}'",
                    "Count must be a non-negative integer",
                    startPos.Advance(content[..(offset + subParts[0].Length + 1)])
                );
            }
        }
        else
        {
            if (!double.TryParse(part, out var time) || time <= 0)
            {
                context.AddError(
                    $"Invalid duration: '{part}'",
                    "Duration must be a positive number",
                    startPos.Advance(content[..offset])
                );
            }
        }
    }

    private static void ValidateSlideDuration(CheckerContext context, string content, TextPosition startPos,
        string duration, int durationStart)
    {
        if (string.IsNullOrEmpty(duration))
        {
            context.AddError(
                "Empty duration for SLIDE",
                "Duration cannot be empty",
                startPos.Advance(content[..durationStart])
            );
            return;
        }

        var hashCount = duration.Count(c => c == '#');

        // SLIDE 支持 startTime##moveTime 格式（两个 # 表示开始时间和移动时间）
        if (hashCount == 2)
        {
            var parts = duration.Split('#');
            if (parts.Length != 3)
            {
                context.AddError(
                    $"Invalid duration format: '{duration}'",
                    "SLIDE duration format should be 'startTime##moveTime', e.g., '1.5##4:1' means start at 1.5s, move for 1 beat of quarter note",
                    startPos.Advance(content[..(durationStart + 1)])
                );
                return;
            }

            // 验证开始时间（必须是绝对时间）
            ValidateAbsoluteTime(context, content, startPos, parts[0], durationStart + 1);
            // 验证移动时间（可以是任何有效格式）
            ValidateSimpleDuration(context, content, startPos, parts[2], durationStart + 1 + parts[0].Length + 2);
        }
        else if (hashCount > 2)
        {
            context.AddError(
                $"Invalid duration format: '{duration}'",
                "SLIDE duration supports 'startTime##moveTime' format with exactly 2 '#' characters, or standard duration formats",
                startPos.Advance(content[..(durationStart + 1)])
            );
        }
        else
        {
            // 单个时长（只有移动时间）
            ValidateSimpleDuration(context, content, startPos, duration, durationStart + 1);
        }
    }

    private static void ValidateAbsoluteTime(CheckerContext context, string content, TextPosition startPos,
        string timeStr, int offset)
    {
        if (!double.TryParse(timeStr, out var time) || time <= 0)
        {
            context.AddError(
                $"Invalid start time: '{timeStr}'",
                "Start time must be a positive number (in seconds)",
                startPos.Advance(content[..offset])
            );
        }
    }

    private static void ValidateSimpleDuration(CheckerContext context, string content, TextPosition startPos,
        string duration, int offset)
    {
        var hashCount = duration.Count(c => c == '#');
        var colonCount = duration.Count(c => c == ':');

        if (hashCount == 0 && colonCount == 0)
        {
            if (!double.TryParse(duration, out var val) || val <= 0)
            {
                context.AddError(
                    $"Invalid duration: '{duration}'",
                    "Duration must be a positive number or use format like '8:1' or '#1.5'",
                    startPos.Advance(content[..offset])
                );
            }
        }
        else if (hashCount == 0 && colonCount == 1)
        {
            var parts = duration.Split(':');
            if (parts.Length != 2)
            {
                context.AddError(
                    $"Invalid duration format: '{duration}'",
                    "Duration format should be 'division:beats', e.g., '4:2' means 2 beats at quarter note division",
                    startPos.Advance(content[..offset])
                );
                return;
            }

            if (!int.TryParse(parts[0], out var division) || division <= 0)
            {
                context.AddError(
                    $"Invalid division: '{parts[0]}'",
                    "Division must be a positive integer (e.g., 4 for quarter note, 8 for eighth note)",
                    startPos.Advance(content[..offset])
                );
            }

            if (!int.TryParse(parts[1], out var beats) || beats < 0)
            {
                context.AddError(
                    $"Invalid beat count: '{parts[1]}'",
                    "Beat count must be a non-negative integer",
                    startPos.Advance(content[..(offset + parts[0].Length + 1)])
                );
            }
        }
        else if (hashCount == 1 && !duration.StartsWith('#'))
        {
            var parts = duration.Split('#');
            if (parts.Length != 2)
            {
                context.AddError(
                    $"Invalid duration format: '{duration}'",
                    "Duration format should be 'BPM#division:count' or 'BPM#seconds'",
                    startPos.Advance(content[..offset])
                );
                return;
            }

            if (!double.TryParse(parts[0], out var bpm) || bpm <= 0)
            {
                context.AddError(
                    $"Invalid BPM: '{parts[0]}'",
                    "BPM must be a positive number",
                    startPos.Advance(content[..offset])
                );
                return;
            }

            ValidateSimpleDurationPart(context, content, startPos, parts[1], offset + 1 + parts[0].Length + 1);
        }
        else if (hashCount == 1 && duration.StartsWith('#'))
        {
            var timeValue = duration[1..];
            if (!double.TryParse(timeValue, out var time) || time <= 0)
            {
                context.AddError(
                    $"Invalid absolute time: '{timeValue}'",
                    "Absolute time must be a positive number (in seconds)",
                    startPos.Advance(content[..(offset + 1)])
                );
            }
        }
        else
        {
            context.AddError(
                $"Invalid duration format: '{duration}'",
                "Too many '#' characters in duration specification",
                startPos.Advance(content[..offset])
            );
        }
    }

    private static void ValidateSimpleDurationPart(CheckerContext context, string content, TextPosition startPos,
        string part, int offset)
    {
        if (part.Contains(':'))
        {
            var subParts = part.Split(':');
            if (subParts.Length != 2)
            {
                context.AddError(
                    $"Invalid duration ratio: '{part}'",
                    "Duration ratio should be 'division:beats', e.g., '8:1'",
                    startPos.Advance(content[..offset])
                );
                return;
            }

            if (!int.TryParse(subParts[0], out var div) || div <= 0)
            {
                context.AddError(
                    $"Invalid division: '{subParts[0]}'",
                    "Division must be a positive integer",
                    startPos.Advance(content[..offset])
                );
            }

            if (!int.TryParse(subParts[1], out var count) || count < 0)
            {
                context.AddError(
                    $"Invalid count: '{subParts[1]}'",
                    "Count must be a non-negative integer",
                    startPos.Advance(content[..(offset + subParts[0].Length + 1)])
                );
            }
        }
        else
        {
            if (!double.TryParse(part, out var time) || time <= 0)
            {
                context.AddError(
                    $"Invalid duration: '{part}'",
                    "Duration must be a positive number",
                    startPos.Advance(content[..offset])
                );
            }
        }
    }

    private static void ValidateSlide(CheckerContext context, string content, TextPosition startPos,
        NoteInfo noteInfo, SlideInfo slide)
    {
        if (slide.EndPosition == null)
        {
            context.AddError(
                $"Slide missing end position",
                $"Slide type '{slide.SlideType}' requires an end position (button 1-8)",
                startPos.Advance(content[..slide.StartIndex])
            );
            return;
        }

        if (slide.EndPosition < 1 || slide.EndPosition > 8)
        {
            context.AddError(
                $"Invalid slide end position: {slide.EndPosition}",
                "End position must be between 1 and 8",
                startPos.Advance(content[..(slide.StartIndex + slide.SlideType!.Length)])
            );
            return;
        }

        var startPosValue = noteInfo.StartPosition;

        if (!IsValidSlidePath(slide.SlideType!, startPosValue, slide.EndPosition.Value, slide.FlexionPoint))
        {
            var detail = GetSlidePathErrorDetail(slide.SlideType!, startPosValue, slide.EndPosition.Value, slide.FlexionPoint);
            context.AddError(
                $"Invalid slide path: {startPosValue}{slide.SlideType}{slide.FlexionPoint}{slide.EndPosition}",
                detail,
                startPos.Advance(content[..slide.StartIndex])
            );
        }

        if (slide.Duration == null && noteInfo.Duration == null)
        {
            context.AddError(
                "Slide missing duration",
                "Slide must have a duration specified, e.g., [8:1] or [#1.5]",
                startPos.Advance(content[..slide.EndIndex])
            );
        }
        else if (slide.Duration != null)
        {
            ValidateSlideDuration(context, content, startPos, slide.Duration, slide.DurationStart);
        }
    }

    private static bool IsValidSlidePath(string slideType, int start, int end, int? flexionPoint)
    {
        if (start == end) return false;

        var interval = GetPointInterval(start, end);

        return slideType switch
        {
            "-" => interval >= 2,
            "^" => interval is not (0 or 4),
            "v" => interval is not (0 or 4),
            "<" => true,
            ">" => true,
            "V" => flexionPoint.HasValue && GetPointInterval(start, flexionPoint.Value) == 2 && GetPointInterval(flexionPoint.Value, end) >= 2,
            "p" => true,
            "q" => true,
            "pp" => IsOpposite(start, end),
            "qq" => IsOpposite(start, end),
            "s" => IsOpposite(start, end),
            "z" => IsOpposite(start, end),
            "w" => IsOpposite(start, end),
            _ => true
        };
    }

    private static string GetSlidePathErrorDetail(string slideType, int start, int end, int? flexionPoint)
    {
        return slideType switch
        {
            "-" => "Straight slide requires start and end positions to be at least 2 buttons apart",
            "^" or "v" => "This slide type cannot connect adjacent buttons or opposite buttons",
            "V" => flexionPoint == null
                ? "V-shaped slide requires a flexion point, e.g., 1V35"
                : "V-shaped slide requires flexion point to be exactly 2 buttons from start, and end to be at least 2 buttons from flexion point",
            "pp" or "qq" or "s" or "z" or "w" => "This slide type requires start and end positions to be opposite (diagonally across)",
            _ => "Invalid slide path"
        };
    }

    private static int GetPointInterval(int a, int b)
    {
        var angleA = GetButtonAngle(a);
        var angleB = GetButtonAngle(b);
        var diff = Math.Abs(angleA - angleB);
        return Math.Min(diff / 45, 8 - diff / 45);
    }

    private static int GetButtonAngle(int button)
    {
        return button switch
        {
            8 => 0,
            1 => 45,
            2 => 90,
            3 => 135,
            4 => 180,
            5 => 225,
            6 => 270,
            7 => 315,
            _ => 0
        };
    }

    private static bool IsOpposite(int a, int b)
    {
        return GetPointInterval(a, b) == 4;
    }

    private static void CheckChartTermination(CheckerContext context, List<ChartSegment> segments, string fumen)
    {
        var nonEmptySegments = segments.Where(s => s.Content != "," && !string.IsNullOrWhiteSpace(s.Content)).ToList();

        if (nonEmptySegments.Count == 0) return;

        var lastSegment = nonEmptySegments.Last();

        if (lastSegment.Content != "E")
        {
            context.AddWarning(
                "Chart not terminated with 'E'",
                "Simai charts should end with 'E' to mark the end of the chart",
                lastSegment.StartPosition,
                new QuickFix(",E", lastSegment.StartPosition.Advance(lastSegment.Content))
            );
        }
    }

    private record ChartSegment(string Content, TextPosition StartPosition, int Length);

    private class NoteInfo
    {
        public int StartPosition { get; set; }
        public bool IsHold { get; set; }
        public bool IsBreak { get; set; }
        public bool IsEx { get; set; }
        public bool IsMine { get; set; }
        public bool HasStar { get; set; }
        public bool HasDoubleStar { get; set; }
        public bool NoStar { get; set; }
        public bool FadeSlide { get; set; }
        public bool NoFadeSlide { get; set; }
        public bool HasSameStartPointSlides { get; set; }
        public string? Duration { get; set; }
        public int DurationStart { get; set; }
        public int DurationEnd { get; set; }
        public List<SlideInfo> Slides { get; set; } = new();
        public List<(char C, int Index)> UnknownChars { get; set; } = new();
    }

    private class SlideInfo
    {
        public string? SlideType { get; set; }
        public int StartPosition { get; set; }
        public int? EndPosition { get; set; }
        public int? FlexionPoint { get; set; }
        public string? Duration { get; set; }
        public int DurationStart { get; set; }
        public int DurationEnd { get; set; }
        public bool IsBreak { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
    }

    private class CheckerContext
    {
        public string Source { get; }
        public List<Diagnostic> Diagnostics { get; } = new();

        public CheckerContext(string source)
        {
            Source = source;
        }

        public void AddError(string message, string detail, TextPosition start, QuickFix? fix = null)
        {
            Diagnostics.Add(new Diagnostic(Severity.Error, message, detail, start, fix));
        }

        public void AddWarning(string message, string detail, TextPosition start, QuickFix? fix = null)
        {
            Diagnostics.Add(new Diagnostic(Severity.Warning, message, detail, start, fix));
        }

        public void AddInfo(string message, string detail, TextPosition start, QuickFix? fix = null)
        {
            Diagnostics.Add(new Diagnostic(Severity.Info, message, detail, start, fix));
        }
    }
}
