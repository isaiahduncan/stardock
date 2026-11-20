/// <summary>The result of an edit that is checked against the version the editor saw.</summary>
public enum UpdateOutcome
{
    Updated,

    /// <summary>Someone else changed the document after the editor loaded it, so nothing was saved.</summary>
    Conflict,

    NotFound,
}
