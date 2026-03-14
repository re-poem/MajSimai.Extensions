using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MajSimai.Extensions.Converter;

public static class Simai2FCPXML
{
    public static void Convert(string fumen, string savePath = "timeline.fcpxml", double offset = 0, int fps = 60)
    {
        Convert(SimaiParser.ParseChart(fumen).NoteTimings.ToArray().ToList(), savePath, offset, fps);
    }
    public static void Convert(List<SimaiTimingPoint> noteList, string savePath = "timeline.fcpxml", double offset = 0, int fps = 60)
    {
        var fcpxml = new FcpxmlBuilder(fps: fps);

        if (offset != 0)
        {
            fcpxml.AddClip("offset", offset);
        }

        for (int i = 0; i < noteList.Count - 1; i++)
        {
            var duration = noteList[i+1].Timing - noteList[i].Timing;
            fcpxml.AddClip(duration);
        }

        fcpxml.Save(savePath);
    }
}

// love from ChatGPT
internal class FcpxmlBuilder(
    string assetPath = "",
    int width = 1920,
    int height = 1080,
    int fps = 60)
{
    private readonly List<long> durations = new();
    private readonly List<string> names = new();

    // =========================
    // 添加片段
    // =========================

    public void AddClip(string name, double seconds)
    {
        if (seconds <= 0)
            return;

        long frames = (long)Math.Round(seconds * fps);

        if (frames <= 0)
            frames = 1;

        names.Add(name);
        durations.Add(frames);
    }

    public void AddClip(double seconds)
    {
        AddClip($"seg{names.Count:D5}", seconds);
    }

    // =========================
    // 保存
    // =========================

    public void Save(string path)
    {
        long totalFrames = 0;

        foreach (var f in durations)
            totalFrames += f;

        var doc = new XDocument(

            new XDeclaration("1.0", "UTF-8", null),

            new XElement("fcpxml",
                new XAttribute("version", "1.10"),

                BuildResources(totalFrames),

                new XElement("library",
                    new XElement("event",
                        new XAttribute("name", "AutoEdit"),

                        new XElement("project",
                            new XAttribute("name", "Timeline"),

                            new XElement("sequence",
                                new XAttribute("format", "r1"),
                                new XAttribute("duration",
                                    Frames(totalFrames)
                                ),

                                BuildSpine()
                            )
                        )
                    )
                )
            )
        );

        doc.Save(path);
    }

    // =========================
    // resources
    // =========================

    private XElement BuildResources(long totalFrames)
    {
        return new XElement("resources",

            new XElement("format",
                new XAttribute("id", "r1"),
                new XAttribute("frameDuration",
                    $"1/{fps}s"
                ),
                new XAttribute("width", width),
                new XAttribute("height", height)
            ),

            new XElement("asset",
                new XAttribute("id", "r2"),
                new XAttribute("name", "source"),

                new XAttribute("src",
                    new Uri(assetPath).AbsoluteUri
                ),

                new XAttribute("duration",
                    Frames(totalFrames)
                ),

                new XAttribute("hasVideo", "1")
            )
        );
    }

    // =========================
    // spine
    // =========================

    private XElement BuildSpine()
    {
        var spine = new XElement("spine");

        long timeline = 0;

        for (int i = 0; i < durations.Count; i++)
        {
            long d = durations[i];

            spine.Add(

                new XElement("asset-clip",

                    new XAttribute("name",
                        names[i]
                    ),

                    new XAttribute("ref", "r2"),

                    new XAttribute("offset",
                        Frames(timeline)
                    ),

                    new XAttribute("start",
                        Frames(timeline)
                    ),

                    new XAttribute("duration",
                        Frames(d)
                    )
                )
            );

            timeline += d;
        }

        return spine;
    }

    // =========================
    // helpers
    // =========================

    private string Frames(long f)
    {
        return $"{f}/{fps}s";
    }
}