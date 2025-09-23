using Serilog;
using Spectre.Console;
using System.Diagnostics;
using System.Text;

namespace Demo.Tools;

/// <summary>
/// Utility tools for ForkerDotNet clinical demonstrations.
/// Provides corruption injection, external access simulation, and service control.
/// </summary>
public class ToolRunner
{
    private readonly string _demoPath = @"C:\ForkerDemo";
    private readonly ILogger _logger = Log.ForContext<ToolRunner>();

    public async Task RunCorruptionInjectorAsync()
    {
        AnsiConsole.MarkupLine("[bold red]Corruption Injector Tool[/]");
        AnsiConsole.MarkupLine("[yellow]Deliberately corrupts files to test quarantine system[/]");
        AnsiConsole.WriteLine();

        var inputPath = Path.Combine(_demoPath, "Input");
        var files = Directory.GetFiles(inputPath).ToList();

        if (!files.Any())
        {
            AnsiConsole.MarkupLine("[red]No files found in Input directory[/]");
            return;
        }

        var selectedFile = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[red]Select file to corrupt:[/]")
                .AddChoices(files.Select(f => Path.GetFileName(f)!)));

        var fullPath = files.First(f => Path.GetFileName(f) == selectedFile);

        var corruptionType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[red]Select corruption type:[/]")
                .AddChoices(
                    "Modify content - Change file data",
                    "Truncate file - Remove end of file",
                    "Single bit flip - Minimal corruption",
                    "Header corruption - Corrupt file header"));

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[red]Corrupting file[/]");

                switch (corruptionType)
                {
                    case "Modify content - Change file data":
                        await ModifyFileContentAsync(fullPath);
                        break;
                    case "Truncate file - Remove end of file":
                        await TruncateFileAsync(fullPath);
                        break;
                    case "Single bit flip - Minimal corruption":
                        await FlipRandomBitAsync(fullPath);
                        break;
                    case "Header corruption - Corrupt file header":
                        await CorruptFileHeaderAsync(fullPath);
                        break;
                }

                task.Increment(100);
                await Task.Delay(1000);
            });

        AnsiConsole.MarkupLine($"[red]✓ File corrupted: {selectedFile}[/]");
        AnsiConsole.MarkupLine("[yellow]Watch dashboard for quarantine event[/]");
    }

    public async Task RunExternalAccessSimulatorAsync()
    {
        AnsiConsole.MarkupLine("[bold blue]External Access Simulator[/]");
        AnsiConsole.MarkupLine("[yellow]Simulates external tools accessing files during processing[/]");
        AnsiConsole.WriteLine();

        var destAPath = Path.Combine(_demoPath, "DestinationA");
        var files = Directory.GetFiles(destAPath).ToList();

        if (!files.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No files in DestinationA - waiting for files to appear...[/]");

            // Wait for files to appear
            while (!files.Any())
            {
                await Task.Delay(2000);
                files = Directory.GetFiles(destAPath).ToList();
                if (files.Any()) break;
            }
        }

        AnsiConsole.MarkupLine($"[green]Found {files.Count} files in DestinationA[/]");

        var duration = AnsiConsole.Prompt(
            new TextPrompt<int>("[blue]How long to hold files open (seconds)?[/]")
                .DefaultValue(30)
                .ValidationErrorMessage("[red]Please enter a valid number[/]"));

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[blue]Simulating external access[/]", maxValue: duration);

                var fileStreams = new List<FileStream>();

                try
                {
                    // Open files for reading (simulating external monitoring tools)
                    foreach (var file in files.Take(3)) // Limit to first 3 files
                    {
                        try
                        {
                            var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                            fileStreams.Add(stream);
                            _logger.Information("Opened file for external access: {File}", Path.GetFileName(file));
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Could not open file: {File}", file);
                        }
                    }

                    AnsiConsole.MarkupLine($"[blue]Holding {fileStreams.Count} files open...[/]");

                    // Hold files open for specified duration
                    for (int i = 0; i < duration; i++)
                    {
                        await Task.Delay(1000);
                        task.Increment(1);
                    }
                }
                finally
                {
                    // Clean up file streams
                    foreach (var stream in fileStreams)
                    {
                        stream.Dispose();
                    }
                }
            });

        AnsiConsole.MarkupLine("[green]✓ External access simulation complete[/]");
        AnsiConsole.MarkupLine("[yellow]System should continue processing normally[/]");
    }

    public async Task RunServiceControllerAsync()
    {
        AnsiConsole.MarkupLine("[bold green]Service Controller[/]");
        AnsiConsole.MarkupLine("[yellow]Controls ForkerDotNet Windows service for demo scenarios[/]");
        AnsiConsole.WriteLine();

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Select service action:[/]")
                .AddChoices(
                    "Check Status - View current service status",
                    "Stop Service - Simulate service failure",
                    "Start Service - Demonstrate recovery",
                    "Restart Service - Complete restart cycle"));

        switch (action)
        {
            case "Check Status - View current service status":
                await CheckServiceStatusAsync();
                break;
            case "Stop Service - Simulate service failure":
                await StopForkerServiceAsync();
                break;
            case "Start Service - Demonstrate recovery":
                await StartForkerServiceAsync();
                break;
            case "Restart Service - Complete restart cycle":
                await RestartForkerServiceAsync();
                break;
        }
    }

    public async Task RunRaceConditionTriggerAsync()
    {
        AnsiConsole.MarkupLine("[bold red]Race Condition Trigger[/]");
        AnsiConsole.MarkupLine("[yellow]Creates simultaneous operations to test race condition handling[/]");
        AnsiConsole.WriteLine();

        var reservoirPath = Path.Combine(_demoPath, "Reservoir");
        var inputPath = Path.Combine(_demoPath, "Input");
        var files = Directory.GetFiles(reservoirPath).ToList();

        if (!files.Any())
        {
            AnsiConsole.MarkupLine("[red]No files in Reservoir for race condition test[/]");
            return;
        }

        var fileCount = AnsiConsole.Prompt(
            new TextPrompt<int>("[red]Number of files to drop simultaneously:[/]")
                .DefaultValue(5)
                .ValidationErrorMessage("[red]Please enter a valid number[/]"));

        fileCount = Math.Min(fileCount, files.Count);

        AnsiConsole.MarkupLine($"[red]Triggering race condition with {fileCount} files...[/]");

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[red]Creating race condition[/]", maxValue: fileCount);

                var dropTasks = new List<Task>();

                for (int i = 0; i < fileCount; i++)
                {
                    var sourceFile = files[i];
                    var targetFile = Path.Combine(inputPath, $"race_{i:D2}_{DateTime.Now:HHmmss}_{Path.GetFileName(sourceFile)}");

                    dropTasks.Add(Task.Run(() =>
                    {
                        File.Copy(sourceFile, targetFile, true);
                        task.Increment(1);
                        _logger.Information("Race condition file dropped: {File}", Path.GetFileName(targetFile));
                    }));
                }

                await Task.WhenAll(dropTasks);
            });

        AnsiConsole.MarkupLine("[red]✓ Race condition triggered[/]");
        AnsiConsole.MarkupLine("[yellow]Monitor dashboard for concurrent processing[/]");
    }

    public async Task RunArchiveCleanerAsync()
    {
        AnsiConsole.MarkupLine("[bold yellow]Archive Cleaner Simulator[/]");
        AnsiConsole.MarkupLine("[yellow]Simulates 24-hour archive cleanup process[/]");
        AnsiConsole.WriteLine();

        var archivePath = Path.Combine(_demoPath, "Archive");
        var files = Directory.GetFiles(archivePath).ToList();

        if (!files.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No files in Archive directory[/]");
            return;
        }

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Select cleanup action:[/]")
                .AddChoices(
                    "Simulate cleanup - Move files to simulated cleanup",
                    "Fast-forward demo - Immediate cleanup for demo",
                    "Show archive status - Display file ages"));

        switch (action)
        {
            case "Show archive status - Display file ages":
                ShowArchiveStatus(files);
                break;
            case "Fast-forward demo - Immediate cleanup for demo":
                await FastForwardCleanupAsync(files);
                break;
            case "Simulate cleanup - Move files to simulated cleanup":
                await SimulateCleanupAsync(files);
                break;
        }
    }

    private async Task ModifyFileContentAsync(string filePath)
    {
        var content = await File.ReadAllBytesAsync(filePath);
        if (content.Length > 100)
        {
            // Modify bytes in the middle of the file
            var random = new Random();
            for (int i = 0; i < 10; i++)
            {
                var position = random.Next(50, content.Length - 50);
                content[position] = (byte)random.Next(0, 255);
            }
            await File.WriteAllBytesAsync(filePath, content);
        }
    }

    private async Task TruncateFileAsync(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var newSize = fileInfo.Length / 2; // Cut file in half

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write);
        stream.SetLength(newSize);
        await stream.FlushAsync();
    }

    private async Task FlipRandomBitAsync(string filePath)
    {
        var content = await File.ReadAllBytesAsync(filePath);
        if (content.Length > 10)
        {
            var random = new Random();
            var position = random.Next(10, content.Length - 10);
            var bitPosition = random.Next(0, 8);
            content[position] ^= (byte)(1 << bitPosition); // Flip one bit
            await File.WriteAllBytesAsync(filePath, content);
        }
    }

    private async Task CorruptFileHeaderAsync(string filePath)
    {
        var content = await File.ReadAllBytesAsync(filePath);
        if (content.Length > 20)
        {
            // Corrupt first 10 bytes (typical header area)
            var random = new Random();
            for (int i = 0; i < 10; i++)
            {
                content[i] = (byte)random.Next(0, 255);
            }
            await File.WriteAllBytesAsync(filePath, content);
        }
    }

    private async Task CheckServiceStatusAsync()
    {
        AnsiConsole.MarkupLine("[blue]Checking ForkerDotNet service status...[/]");
        // This would check actual service status in a real implementation
        AnsiConsole.MarkupLine("[yellow]Note: Manual service status check required[/]");
        await Task.Delay(1000);
    }

    private async Task StopForkerServiceAsync()
    {
        AnsiConsole.MarkupLine("[red]Stopping ForkerDotNet service...[/]");
        AnsiConsole.MarkupLine("[yellow]Use: sc stop ForkerDotNet (or similar)[/]");
        await Task.Delay(1000);
    }

    private async Task StartForkerServiceAsync()
    {
        AnsiConsole.MarkupLine("[green]Starting ForkerDotNet service...[/]");
        AnsiConsole.MarkupLine("[yellow]Use: sc start ForkerDotNet (or similar)[/]");
        await Task.Delay(1000);
    }

    private async Task RestartForkerServiceAsync()
    {
        await StopForkerServiceAsync();
        await Task.Delay(2000);
        await StartForkerServiceAsync();
    }

    private void ShowArchiveStatus(List<string> files)
    {
        var table = new Table()
            .BorderColor(Color.Yellow)
            .AddColumn("File")
            .AddColumn("Size")
            .AddColumn("Age")
            .AddColumn("Cleanup Status");

        foreach (var file in files.Take(10)) // Show first 10 files
        {
            var fileInfo = new FileInfo(file);
            var age = DateTime.Now - fileInfo.CreationTime;
            var status = age.TotalHours > 24 ? "[red]Ready for cleanup[/]" : "[green]Retained[/]";

            table.AddRow(
                Path.GetFileName(file),
                $"{fileInfo.Length / 1024 / 1024:F1} MB",
                $"{age.TotalHours:F1} hours",
                status);
        }

        AnsiConsole.Write(table);
    }

    private async Task FastForwardCleanupAsync(List<string> files)
    {
        AnsiConsole.MarkupLine("[yellow]Fast-forward cleanup for demo purposes...[/]");

        var cleanupDir = Path.Combine(_demoPath, "CleanedUp");
        Directory.CreateDirectory(cleanupDir);

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[yellow]Cleaning up archive[/]", maxValue: files.Count);

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var cleanupPath = Path.Combine(cleanupDir, fileName);
                    File.Move(file, cleanupPath);
                    task.Increment(1);
                    await Task.Delay(100);
                }
            });

        AnsiConsole.MarkupLine($"[green]✓ Moved {files.Count} files to cleanup directory[/]");
    }

    private async Task SimulateCleanupAsync(List<string> files)
    {
        AnsiConsole.MarkupLine("[yellow]Simulating gradual cleanup process...[/]");
        // This would implement a more realistic cleanup simulation
        await Task.Delay(1000);
    }
}