using System;
using System.Collections.Generic;

/// Keeps a known Unity editor-service failure visible without treating it as a
/// robot exception. All other error and exception handling stays in each bench.
public static class Turbo4744BenchLog
{
    public static bool RecordEditorServiceError(
        string message,
        string stack,
        List<string> editorServiceErrors
    )
    {
        if (
            !message.StartsWith(
                "[Package Manager Window] Error while getting auth code: System.InvalidOperationException: Failed to call Unity ID to get auth code.",
                StringComparison.Ordinal
            ) || !stack.Contains("UnityEditor.AsyncHTTPClient:Done")
        )
            return false;
        if (editorServiceErrors.Count < 15)
            editorServiceErrors.Add(message + "\n" + stack);
        return true;
    }
}
