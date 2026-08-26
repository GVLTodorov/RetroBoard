namespace RetroBoard.Domain.Templates;

/// <summary>The fixed column-title list behind each <see cref="TemplateType"/>, mirrors
/// PlanningPoker's <c>DeckCatalog</c>.</summary>
public static class TemplateCatalog
{
    public static IReadOnlyList<TemplateType> AllTypes { get; } = Enum.GetValues<TemplateType>();

    public static IReadOnlyList<string> Get(TemplateType type) => type switch
    {
        TemplateType.WentWellDidntWork => ["Went well", "Didn't go well", "Action items"],
        TemplateType.StartStopContinue => ["Start", "Stop", "Continue"],
        TemplateType.MadSadGlad => ["Mad", "Sad", "Glad"],
        TemplateType.FourLs => ["Liked", "Learned", "Lacked", "Longed for"],
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown template type."),
    };

    public static string GetDisplayName(TemplateType type) => type switch
    {
        TemplateType.WentWellDidntWork => "Went well / Didn't go well / Action items",
        TemplateType.StartStopContinue => "Start / Stop / Continue",
        TemplateType.MadSadGlad => "Mad / Sad / Glad",
        TemplateType.FourLs => "4Ls (Liked / Learned / Lacked / Longed for)",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown template type."),
    };
}
