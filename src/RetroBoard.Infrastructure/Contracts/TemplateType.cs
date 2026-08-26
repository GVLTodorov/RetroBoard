namespace RetroBoard.Contracts;

/// <summary>Wire-side mirror of <see cref="Domain.Templates.TemplateType"/> — kept separate so the
/// Client never needs a reference to the Domain layer.</summary>
public enum TemplateType
{
    WentWellDidntWork,
    StartStopContinue,
    MadSadGlad,
    FourLs,
}
