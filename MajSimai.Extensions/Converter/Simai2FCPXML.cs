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

// love from ChatGPT & Gemini
internal class FcpxmlBuilder(
    int width = 1920,
    int height = 1080,
    int fps = 60)
{
    private readonly List<long> durations = new();
    private readonly List<string> names = new();

    public void AddClip(string name, double seconds)
    {
        if (seconds <= 0) return;
        long frames = (long)Math.Round(seconds * fps);
        if (frames <= 0) frames = 1;

        names.Add(name);
        durations.Add(frames);
    }

    public void AddClip(double seconds)
    {
        AddClip($"seg{names.Count:D5}", seconds);
    }

    public void Save(string path)
    {
        long totalFrames = 0;
        foreach (var f in durations) totalFrames += f;

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("fcpxml",
                new XAttribute("version", "1.10"),

                // 资源部分：定义格式和基础文字插件
                new XElement("resources",
                    new XElement("format",
                        new XAttribute("id", "r1"),
                        new XAttribute("frameDuration", $"1/{fps}s"),
                        new XAttribute("width", width),
                        new XAttribute("height", height)
                    ),
                    // 使用内置的 Basic Title 插件作为占位符，不需要 src
                    new XElement("effect",
                        new XAttribute("id", "r2"),
                        new XAttribute("name", "Basic Title"),
                        new XAttribute("uid", ".../Titles.localized/Bumper:Glow.localized/Basic Title.localized/Basic Title.btitle")
                    )
                ),

                new XElement("library",
                    new XElement("event",
                        new XAttribute("name", "SimaiConvert"),
                        new XElement("project",
                            new XAttribute("name", "Timeline"),
                            new XElement("sequence",
                                new XAttribute("format", "r1"),
                                new XAttribute("duration", Frames(totalFrames)),
                                BuildSpine()
                            )
                        )
                    )
                )
            )
        );

        doc.Save(path);
    }

    private XElement BuildSpine()
    {
        var spine = new XElement("spine");
        long timeline = 0;

        for (int i = 0; i < durations.Count; i++)
        {
            long d = durations[i];

            // 使用 title 标签，它在 FCPX/达芬奇中表现为一个有名字的片段
            // 且不需要指向任何物理文件
            var title = new XElement("title",
                new XAttribute("name", names[i]),
                new XAttribute("ref", "r2"), // 引用上面的 Basic Title
                new XAttribute("offset", Frames(timeline)),
                new XAttribute("start", "0/1s"), // title 内部起始通常设为 0
                new XAttribute("duration", Frames(d)),
                // 内部文字结构，防止有些软件强制要求 text 节点
                new XElement("text",
                    new XElement("text-style",
                        new XAttribute("ref", "ts1"),
                        names[i]))
            );

            spine.Add(title);
            timeline += d;
        }

        return spine;
    }

    private string Frames(long f)
    {
        // FCPXML 的标准时间格式： 分子/分母s
        return $"{f}/{fps}s";
    }
}