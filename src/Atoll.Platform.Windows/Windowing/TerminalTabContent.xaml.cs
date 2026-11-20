using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// A minimal terminal tab: type a command, press Enter, see its output. Mirrors
/// the original SwiftTerm terminal tab. Commands run through cmd.exe from a
/// tracked working directory (so <c>cd</c> persists); this executes whatever the
/// app's own user types, nothing more.
/// </summary>
public partial class TerminalTabContent : UserControl
{
    private string _cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private bool _running;

    public TerminalTabContent()
    {
        InitializeComponent();
        Loaded += (_, _) => InputBox.Focus();
    }

    private async void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _running) return;
        var command = InputBox.Text.Trim();
        InputBox.Clear();
        if (command.Length == 0) return;

        if (command is "clear" or "cls")
        {
            OutputText.Text = string.Empty;
            return;
        }

        Append($"\n› {command}\n");

        // Handle `cd` in-process so the working directory persists between runs.
        if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) || command == "cd")
        {
            ChangeDirectory(command.Length > 2 ? command[3..].Trim().Trim('"') : "");
            return;
        }

        _running = true;
        try
        {
            var output = await RunAsync(command, _cwd);
            Append(output.Length == 0 ? "\n" : output.TrimEnd() + "\n");
        }
        catch (Exception ex)
        {
            Append($"[error] {ex.Message}\n");
        }
        finally
        {
            _running = false;
        }
    }

    private void ChangeDirectory(string target)
    {
        if (string.IsNullOrEmpty(target)) { Append(_cwd + "\n"); return; }
        try
        {
            var resolved = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(_cwd, target));
            if (Directory.Exists(resolved)) _cwd = resolved;
            else Append($"The system cannot find the path specified: {target}\n");
        }
        catch (Exception ex)
        {
            Append($"[error] {ex.Message}\n");
        }
    }

    private static Task<string> RunAsync(string command, string cwd) => Task.Run(() =>
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + command,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var proc = Process.Start(psi);
        if (proc is null) return "[error] failed to start cmd.exe\n";

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(15000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return stdout + stderr + "\n[timed out after 15s]\n";
        }
        return stdout + stderr;
    });

    private void Append(string text)
    {
        const int maxChars = 8000;
        var combined = OutputText.Text + text;
        if (combined.Length > maxChars) combined = combined[^maxChars..];
        OutputText.Text = combined;
        OutputScroller.ScrollToEnd();
    }

    private void OnClear(object sender, RoutedEventArgs e) => OutputText.Text = string.Empty;
}
