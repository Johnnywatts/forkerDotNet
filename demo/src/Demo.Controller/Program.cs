using Demo.Controller;
using Serilog;
using Spectre.Console;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/controller-.txt", rollingInterval: Serilog.RollingInterval.Day)
    .CreateLogger();

try
{
    AnsiConsole.Write(
        new FigletText("Demo Controller")
            .Color(Color.Green));

    AnsiConsole.MarkupLine("[bold green]ForkerDotNet Clinical Demo Controller[/]");
    AnsiConsole.MarkupLine("[yellow]Master orchestrator for observable clinical demonstrations[/]");
    AnsiConsole.WriteLine();

    var demoOrchestrator = new DemoOrchestrator();

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[green]Select demonstration type:[/]")
            .AddChoices(
                "Quick Demo - 10 minute overview",
                "Full Clinical Demo - Complete 30 minute demonstration",
                "Setup Only - Prepare environment and instructions",
                "Race Condition Demo - Focused stress testing",
                "Recovery Demo - Failure and recovery scenarios",
                "Performance Demo - Throughput and resource validation",
                "Exit"));

    if (choice == "Exit")
    {
        AnsiConsole.MarkupLine("[yellow]Exiting...[/]");
        return 0;
    }

    await demoOrchestrator.InitializeAsync();

    switch (choice)
    {
        case "Quick Demo - 10 minute overview":
            await demoOrchestrator.RunQuickDemoAsync();
            break;
        case "Full Clinical Demo - Complete 30 minute demonstration":
            await demoOrchestrator.RunFullClinicalDemoAsync();
            break;
        case "Setup Only - Prepare environment and instructions":
            await demoOrchestrator.SetupEnvironmentOnlyAsync();
            break;
        case "Race Condition Demo - Focused stress testing":
            await demoOrchestrator.RunRaceConditionDemoAsync();
            break;
        case "Recovery Demo - Failure and recovery scenarios":
            await demoOrchestrator.RunRecoveryDemoAsync();
            break;
        case "Performance Demo - Throughput and resource validation":
            await demoOrchestrator.RunPerformanceDemoAsync();
            break;
    }

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Demo Controller failed");
    AnsiConsole.WriteException(ex);
    return 1;
}
finally
{
    Log.CloseAndFlush();
}