using System;
using System.Diagnostics;
using System.IO;

namespace MajSimai.Extensions.MediaProcessor;

public static class TrackProcessor
{
    /// <summary>
    /// 提取音频并转换为 44100Hz 的 mp3
    /// </summary>
    public static void ExtractAudio(string ffmpegPath, string videoPath, string audioOutputPath)
    {
        if (!File.Exists(videoPath)) return;

        // -y: 覆盖, -vn: 禁用视频, -ar: 采样率, -q:a: 质量
        string args = $"-y -i \"{videoPath}\" -vn -ar 44100 -acodec libmp3lame -q:a 2 \"{audioOutputPath}\"";
        RunFFmpeg(ffmpegPath, args);
    }

    /// <summary>
    /// 调整单个媒体文件（视频或音频）的时间偏移，并覆盖原文件
    /// </summary>
    /// <param name="ffmpegPath">FFmpeg 路径</param>
    /// <param name="filePath">要处理的文件路径（视频或音频）</param>
    /// <param name="targetTime">目标时间</param>
    /// <param name="offset">当前实际偏移量</param>
    /// <param name="clone">是否使用静帧填充开头（否则使用黑帧）</param>
    public static void AdjustMediaTime(string ffmpegPath, string filePath, double targetTime, double offset, bool clone = false)
    {
        if (!File.Exists(filePath)) return;

        double diff = targetTime - offset;
        if (Math.Abs(diff) < 0.01) return; // 差距过小无需处理

        string ext = Path.GetExtension(filePath).ToLower();
        bool isAudio = ext == ".mp3" || ext == ".wav" || ext == ".ogg" || ext == ".flac";
        string audioCodec = (ext == ".mp3") ? "libmp3lame" : "aac";

        string tempPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"t_{Guid.NewGuid()}{ext}");
        string args;

        if (diff < 0)
        {
            // 情况1：裁剪头部
            double cut = Math.Abs(diff);
            if (isAudio)
                args = $"-y -i \"{filePath}\" -ss {cut} -c:a {audioCodec} \"{tempPath}\"";
            else
                args = $"-y -i \"{filePath}\" -ss {cut} -c:v libx264 -c:a {audioCodec} -preset superfast \"{tempPath}\"";
        }
        else
        {
            // 情况2：补齐头部
            double delayMs = diff * 1000;
            if (isAudio)
            {
                // 音频：仅使用 adelay
                args = $"-y -i \"{filePath}\" -af \"adelay={delayMs}:all=1\" -c:a {audioCodec} \"{tempPath}\"";
            }
            else
            {
                // 视频：tpad 补静帧，adelay 补静音
                if (clone)
                    args = $"-y -i \"{filePath}\" -filter_complex \"[0:v]tpad=start_duration={diff}:start_mode=clone[v];[0:a]adelay={delayMs}:all=1[a]\" -map \"[v]\" -map \"[a]\" -c:v libx264 -c:a {audioCodec} -preset superfast \"{tempPath}\"";
                else    
                    args = $"-y -i \"{filePath}\" -filter_complex \"[0:v]tpad=start_duration={diff}:start_mode=add[v];[0:a]adelay={delayMs}:all=1[a]\" -map \"[v]\" -map \"[a]\" -c:v libx264 -c:a {audioCodec} -preset superfast \"{tempPath}\"";
            }
        }

        try
        {
            RunFFmpeg(ffmpegPath, args);
            if (File.Exists(tempPath))
            {
                var rawPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"raw{ext}");
                if (File.Exists(rawPath))
                {
                    File.Delete(rawPath);
                }
                File.Move(filePath, rawPath);
                File.Move(tempPath, filePath);
            }
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    private static void RunFFmpeg(string ffmpegPath, string arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0) throw new Exception($"FFmpeg Error: {error}");
    }
}