namespace MajSimai.Extensions;

public static class SimaiChartExtensions
{
    public static bool IsDeluxeChart(this SimaiChart chart)
    {
        foreach (var noteTiming in chart.NoteTimings)
            foreach (var note in noteTiming.Notes)
                if (note.IsEx)                                                            // ex
                    return true;
                else if (note.Type == SimaiNoteType.Hold && note.IsBreak)                 // break hold
                    return true;
                else if (note.Type is SimaiNoteType.Touch or SimaiNoteType.TouchHold)     // touch
                    return true;
                else if (note.Type == SimaiNoteType.Slide)
                    if (note.IsSlideBreak)                                                //break slide
                        return true;
                    else                                                                  //festival slide
                        foreach (var c in new[] { "-", "^", "v", "<", ">", "V", "s", "z", "w", "qq", "pp" })
                            if (((note.RawContent!.Length - note.RawContent!.Replace(c, "").Length) / c.Length) > 1)
                                return true;

        return false;
    }
}
