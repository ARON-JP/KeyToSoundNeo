using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace KeyToSoundNeo;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyToSoundNeo";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the current-user startup registry key.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the application executable path.");
        var command = $"\"{executable}\"";
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName))
                throw new InvalidOperationException("Could not determine the application assembly path.");
            var assembly = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
            if (!File.Exists(assembly))
                throw new FileNotFoundException("Could not locate the application assembly.", assembly);
            command += $" \"{assembly}\"";
        }
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }
}
