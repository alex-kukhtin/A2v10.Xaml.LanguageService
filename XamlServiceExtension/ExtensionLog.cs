// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.IO;

namespace XamlServiceExtension;

internal class ExtensionLog
{
    // Provider diagnostic log — %TEMP%\xaml-lsp-extension.log.
    private static readonly String LogPath =
        Path.Combine(Path.GetTempPath(), "xaml-lsp-extension.log");

    public void Log(String message)
    {
        try
        {
            //File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch 
        { 
            /* logging must not crash the provider */
        }
    }
}
