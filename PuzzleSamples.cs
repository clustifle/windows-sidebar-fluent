namespace Sidebar;

internal sealed record PuzzleSample(string Id, string Name, string FileName);
internal static class PuzzleSamples
{
    public static IReadOnlyList<PuzzleSample> All { get; } = new PuzzleSample[]
    {
        new("Lakeside", "Lakeside", "PuzzleLandscape.svg"), new("Forest", "Forest", "PuzzleForest.svg"),
        new("Coast", "Coast", "PuzzleCoast.svg"), new("Mountains", "Mountains", "PuzzleMountains.svg"),
        new("Meadow", "Meadow", "PuzzleMeadow.svg"), new("Waterfall", "Waterfall", "PuzzleWaterfall.svg")
    };
    public static PuzzleSample Find(string? id) => All.FirstOrDefault(sample => string.Equals(sample.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    public static PuzzleSample Different(string id)
    { var choices = All.Where(sample => sample.Id != Find(id).Id).ToArray(); return choices[Random.Shared.Next(choices.Length)]; }
}
