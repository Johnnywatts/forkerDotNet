using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Demo.FileDropper;

/// <summary>
/// Service for orchestrating file movement from Reservoir to Input directory.
/// Provides various modes for demonstrating different file processing scenarios.
/// </summary>
public class FileDropperService
{
    private readonly ILogger<FileDropperService> _logger;
    private readonly string _reservoirPath = @"C:\ForkerDemo\Reservoir";
    private readonly string _inputPath = @"C:\ForkerDemo\Input";

    public FileDropperService(ILogger<FileDropperService> logger)
    {
        _logger = logger;
        EnsureDirectoriesExist();
    }

    /// <summary>
    /// Interactive mode - user selects files manually.
    /// </summary>
    public async Task RunInteractiveModeAsync()
    {
        AnsiConsole.MarkupLine("[bold blue]Interactive File Dropping Mode[/]");
        AnsiConsole.MarkupLine("Select files to move from Reservoir to Input directory");
        AnsiConsole.WriteLine();

        while (true)
        {
            var availableFiles = GetAvailableFiles();
            if (!availableFiles.Any())
            {
                AnsiConsole.MarkupLine("[red]No files available in Reservoir directory[/]");
                AnsiConsole.MarkupLine($"[yellow]Place medical imaging files in: {_reservoirPath}[/]");
                break;
            }

            var choices = availableFiles.Select(f => Path.GetFileName(f)).ToList();
            choices.Add("Exit");

            var selectedFile = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]Select file to drop:[/]")
                    .AddChoices(choices));

            if (selectedFile == "Exit")
                break;

            var sourceFile = availableFiles.First(f => Path.GetFileName(f) == selectedFile);
            await DropFileAsync(sourceFile, selectedFile);

            await Task.Delay(1000); // Brief pause to see the result
        }
    }

    /// <summary>
    /// Automatic mode - drops files at timed intervals.
    /// </summary>
    public async Task RunAutomaticModeAsync()
    {
        AnsiConsole.MarkupLine("[bold blue]Automatic File Dropping Mode[/]");

        var interval = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Enter interval between drops (seconds):[/]")
                .DefaultValue(30)
                .ValidationErrorMessage("[red]Please enter a valid number[/]"));

        var maxFiles = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Maximum files to drop (0 for unlimited):[/]")
                .DefaultValue(10)
                .ValidationErrorMessage("[red]Please enter a valid number[/]"));

        AnsiConsole.MarkupLine($"[yellow]Dropping files every {interval} seconds...[/]");
        AnsiConsole.MarkupLine("[gray]Press Ctrl+C to stop[/]");

        var filesDropped = 0;
        var availableFiles = GetAvailableFiles().ToList();

        if (!availableFiles.Any())
        {
            AnsiConsole.MarkupLine("[red]No files available in Reservoir directory[/]");
            return;
        }

        try
        {
            while (maxFiles == 0 || filesDropped < maxFiles)
            {
                if (filesDropped >= availableFiles.Count)
                {
                    AnsiConsole.MarkupLine("[yellow]All available files have been dropped[/]");
                    break;
                }

                var sourceFile = availableFiles[filesDropped % availableFiles.Count];
                var fileName = Path.GetFileName(sourceFile);

                await DropFileAsync(sourceFile, $"{filesDropped + 1:D3}_{fileName}");
                filesDropped++;

                AnsiConsole.MarkupLine($"[green]Files dropped: {filesDropped}[/]");

                await Task.Delay(interval * 1000);
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Automatic dropping stopped[/]");
        }

        AnsiConsole.MarkupLine($"[blue]Total files dropped: {filesDropped}[/]");
    }

    /// <summary>
    /// Batch mode - drops multiple files simultaneously.
    /// </summary>
    public async Task RunBatchModeAsync()
    {
        AnsiConsole.MarkupLine("[bold blue]Batch File Dropping Mode[/]");

        var availableFiles = GetAvailableFiles().ToList();
        if (!availableFiles.Any())
        {
            AnsiConsole.MarkupLine("[red]No files available in Reservoir directory[/]");
            return;
        }

        var batchSize = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Number of files to drop simultaneously:[/]")
                .DefaultValue(3)
                .ValidationErrorMessage("[red]Please enter a valid number[/]"));

        batchSize = Math.Min(batchSize, availableFiles.Count);

        AnsiConsole.MarkupLine($"[yellow]Dropping {batchSize} files simultaneously...[/]");

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Dropping files[/]", maxValue: batchSize);

                var dropTasks = new List<Task>();

                for (int i = 0; i < batchSize; i++)
                {
                    var sourceFile = availableFiles[i];
                    var fileName = Path.GetFileName(sourceFile);
                    var targetFileName = $"batch_{i + 1:D2}_{fileName}";

                    dropTasks.Add(Task.Run(async () =>
                    {
                        await DropFileAsync(sourceFile, targetFileName);
                        task.Increment(1);
                    }));
                }

                await Task.WhenAll(dropTasks);
            });

        AnsiConsole.MarkupLine($"[green]Successfully dropped {batchSize} files simultaneously[/]");
    }

    /// <summary>
    /// Stress test mode - rapid file drops to test race conditions.
    /// </summary>
    public async Task RunStressTestModeAsync()
    {
        AnsiConsole.MarkupLine("[bold red]Stress Test Mode - Race Condition Testing[/]");
        AnsiConsole.MarkupLine("[yellow]This will rapidly drop files to test system resilience[/]");

        var confirm = AnsiConsole.Confirm("[red]Are you sure you want to proceed?[/]");
        if (!confirm)
        {
            return;
        }

        var availableFiles = GetAvailableFiles().ToList();
        if (!availableFiles.Any())
        {
            AnsiConsole.MarkupLine("[red]No files available in Reservoir directory[/]");
            return;
        }

        var fileCount = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Number of files for stress test:[/]")
                .DefaultValue(10)
                .ValidationErrorMessage("[red]Please enter a valid number[/]"));

        var delayMs = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Delay between drops (milliseconds):[/]")
                .DefaultValue(100)
                .ValidationErrorMessage("[red]Please enter a valid number[/]"));

        AnsiConsole.MarkupLine($"[red]STRESS TEST: Dropping {fileCount} files with {delayMs}ms delay[/]");

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[red]Stress testing[/]", maxValue: fileCount);

                for (int i = 0; i < fileCount; i++)
                {
                    var sourceFile = availableFiles[i % availableFiles.Count];
                    var fileName = Path.GetFileName(sourceFile);
                    var targetFileName = $"stress_{i + 1:D3}_{DateTime.Now:HHmmss}_{fileName}";

                    await DropFileAsync(sourceFile, targetFileName);
                    task.Increment(1);

                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs);
                    }
                }
            });

        AnsiConsole.MarkupLine($"[green]Stress test completed: {fileCount} files dropped[/]");
    }

    private async Task DropFileAsync(string sourceFile, string targetFileName)
    {
        try
        {
            var targetPath = Path.Combine(_inputPath, targetFileName);

            // Copy file to input directory
            File.Copy(sourceFile, targetPath, overwrite: true);

            var fileInfo = new FileInfo(sourceFile);
            var sizeMB = fileInfo.Length / 1024.0 / 1024.0;

            _logger.LogInformation("Dropped file: {FileName} ({SizeMB:F1} MB)", targetFileName, sizeMB);
            AnsiConsole.MarkupLine($"[green]✓[/] Dropped: [cyan]{targetFileName}[/] ({sizeMB:F1} MB)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to drop file: {FileName}", targetFileName);
            AnsiConsole.MarkupLine($"[red]✗[/] Failed to drop: [red]{targetFileName}[/] - {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private IEnumerable<string> GetAvailableFiles()
    {
        if (!Directory.Exists(_reservoirPath))
        {
            return Enumerable.Empty<string>();
        }

        try
        {
            return Directory.GetFiles(_reservoirPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => IsValidMedicalFile(f))
                .OrderBy(f => f);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading reservoir directory");
            return Enumerable.Empty<string>();
        }
    }

    private static bool IsValidMedicalFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".svs" or ".tiff" or ".tif" or ".ndpi" or ".scn" or ".vms";
    }

    private void EnsureDirectoriesExist()
    {
        try
        {
            Directory.CreateDirectory(_reservoirPath);
            Directory.CreateDirectory(_inputPath);

            _logger.LogInformation("Initialized directories - Reservoir: {Reservoir}, Input: {Input}",
                _reservoirPath, _inputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create directories");
            throw;
        }
    }
}