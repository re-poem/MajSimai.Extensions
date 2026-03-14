using System;

namespace MajSimai.Extensions.Checker;

public enum Severity
{
    Info,
    Warning, 
    Error
}

/// <summary>
/// 快速修复建议
/// </summary>
/// <param name="Patch">补丁字符串：以'+'开头表示插入，'-'开头表示删除，'~'开头表示替换</param>
/// <param name="Position">补丁应用的位置</param>
public record class QuickFix(
    string Patch, TextPosition Position);

/// <summary>
/// 诊断信息
/// </summary>
/// <param name="Severity">严重程度</param>
/// <param name="Message">错误/警告消息</param>
/// <param name="Detail">详细说明</param>
/// <param name="Position">错误位置</param>
/// <param name="QuickFix">可选的快速修复建议</param>
public record class Diagnostic(
    Severity Severity,
    string Message, string Detail,
    TextPosition Position,
    QuickFix? QuickFix = null);

