using System.Diagnostics;

namespace MelonPatcher.Core;

public interface IToolRunner
{
    Task RunAsync(string executable, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken);
}

public sealed class ToolRunner : IToolRunner
{
    public async Task RunAsync(string executable, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (environment != null)
            foreach (var pair in environment) process.StartInfo.Environment[pair.Key] = pair.Value;
        if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        var output = await stdout;
        var errors = await stderr;
        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(errors) ? output : errors;
            if (details.Length > 4000) details = details[^4000..];
            // Never echo a tool's environment values into the GUI or logs.
            if (environment != null)
                foreach (var secret in environment.Values.Where(value => !string.IsNullOrEmpty(value)))
                    details = details.Replace(secret, "[redacted]", StringComparison.Ordinal);
            throw new InvalidOperationException($"{Path.GetFileName(executable)} failed (exit {process.ExitCode}):\n{details}");
        }
    }
}
