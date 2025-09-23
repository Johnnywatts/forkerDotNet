using Demo.FileDropper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Spectre.Console;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/filedropper-.txt", rollingInterval: Serilog.RollingInterval.Day)
    .CreateLogger();

try
{
    AnsiConsole.Write(
        new FigletText("File Dropper")
            .Color(Color.Blue));

    AnsiConsole.MarkupLine("[bold green]ForkerDotNet Demo File Dropper[/]");
    AnsiConsole.MarkupLine("[yellow]Orchestrates file movement from Reservoir to Input directory[/]");
    AnsiConsole.WriteLine();

    var mode = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[green]Select file dropping mode:[/]")
            .AddChoices(
                "Interactive - Manual file selection",
                "Automatic - Timed file drops",
                "Batch - Drop multiple files",
                "Stress Test - Rapid file drops",
                "Exit"));

    if (mode == "Exit")
    {
        AnsiConsole.MarkupLine("[yellow]Exiting...[/]");
        return 0;
    }

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices(services =>
        {
            services.AddSingleton<FileDropperService>();
        })
        .Build();

    var dropperService = host.Services.GetRequiredService<FileDropperService>();

    switch (mode)
    {
        case "Interactive - Manual file selection":
            await dropperService.RunInteractiveModeAsync();
            break;
        case "Automatic - Timed file drops":
            await dropperService.RunAutomaticModeAsync();
            break;
        case "Batch - Drop multiple files":
            await dropperService.RunBatchModeAsync();
            break;
        case "Stress Test - Rapid file drops":
            await dropperService.RunStressTestModeAsync();
            break;
    }

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "File Dropper failed");
    AnsiConsole.WriteException(ex);
    return 1;
}
finally
{
    Log.CloseAndFlush();
}