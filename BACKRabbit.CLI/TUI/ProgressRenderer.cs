using BACKRabbit.Firmware;
using Spectre.Console;

namespace BACKRabbit.CLI.TUI;

/// <summary>
/// Wraps Spectre.Console progress display and forwards progress
/// to the FirmwareSourcer's IProgress interface.
/// </summary>
public class ProgressRenderer : IProgress<FirmwareDownloadProgress>
{
    private readonly ProgressTask _task;

    public ProgressRenderer(ProgressTask task)
    {
        _task = task;
    }

    public void Report(FirmwareDownloadProgress value)
    {
        _task.Description = $"[green]{value.Phase}[/]";
        _task.Value = value.PercentComplete;
    }

    /// <summary>
    /// Create a progress context and run the action with a renderer.
    /// </summary>
    public static async Task<T> RunWithProgressAsync<T>(
        string description,
        Func<ProgressRenderer, Task<T>> action)
    {
        return await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[green]{description}[/]", maxValue: 1.0);
                var renderer = new ProgressRenderer(task);
                return await action(renderer);
            });
    }
}