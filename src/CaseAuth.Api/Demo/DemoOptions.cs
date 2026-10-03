namespace CaseAuth.Api.Demo;

public class DemoOptions
{
    public const string SectionName = "Demo";

    // Folder holding personas.json and specimens/. When unset, the API looks for a `demo`
    // folder in its content root and each parent directory, which finds the repo's demo/
    // folder under `dotnet run` and in the test host.
    public string? DataPath { get; set; }
}
