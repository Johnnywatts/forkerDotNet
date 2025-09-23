using Demo.Tools;
using Serilog;
using Spectre.Console;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/tools-.txt", rollingInterval: Serilog.RollingInterval.Day)
    .CreateLogger();

try
{
    AnsiConsole.Write(
        new FigletText("Demo Tools")
            .Color(Color.Red));

    AnsiConsole.MarkupLine("[bold red]ForkerDotNet Demo Tools[/]");
    AnsiConsole.MarkupLine("[yellow]Utilities for corruption injection, race conditions, and service control[/]");
    AnsiConsole.WriteLine();

    var tool = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[red]Select demo tool:[/]")
            .AddChoices(
                "Corruption Injector - Deliberately corrupt files",
                "External Access Simulator - Test file locking resilience",
                "Service Controller - Start/stop ForkerDotNet service",
                "Race Condition Trigger - Multiple simultaneous operations",
                "Archive Cleaner - Simulate 24-hour cleanup",
                "Exit"));

    if (tool == "Exit")
    {
        AnsiConsole.MarkupLine("[yellow]Exiting...[/]");
        return 0;
    }

    var toolRunner = new ToolRunner();

    switch (tool)
    {
        case "Corruption Injector - Deliberately corrupt files":
            await toolRunner.RunCorruptionInjectorAsync();
            break;
        case "External Access Simulator - Test file locking resilience":
            await toolRunner.RunExternalAccessSimulatorAsync();
            break;
        case "Service Controller - Start/stop ForkerDotNet service":
            await toolRunner.RunServiceControllerAsync();
            break;
        case "Race Condition Trigger - Multiple simultaneous operations":
            await toolRunner.RunRaceConditionTriggerAsync();
            break;
        case "Archive Cleaner - Simulate 24-hour cleanup":
            await toolRunner.RunArchiveCleanerAsync();
            break;
    }

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Demo Tools failed");
    AnsiConsole.WriteException(ex);
    return 1;
}
finally
{
    Log.CloseAndFlush();
}