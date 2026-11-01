namespace Sidebar;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class GadgetDefinition(string id, string category, string glyph, string description)
{
    public string Id { get; set; } = id;
    public string Category { get; set; } = category;
    public string Glyph { get; set; } = glyph;
    public string Description { get; set; } = description;
    public override string ToString() => Id;
}
internal static class GadgetCatalog
{
    internal static IReadOnlyList<GadgetDefinition> All { get; } = new GadgetDefinition[]
    {
        new("Clock", "Time", "\uE917", "Analog faces, digital time and time zones"),
        new("Calendar", "Time", "\uE787", "Month view with a fixed six-week layout"),
        new("Timer", "Time", "\uE916", "Countdown with pause and completion sound"),
        new("Notes", "Personal", "\uE70B", "Notes saved on this device"),
        new("Performance", "System", "\uE9D9", "CPU and memory usage"),
        new("Storage", "System", "\uEDA2", "Drive capacity and available space"),
        new("Slide show", "Media", "\uEB9F", "Pictures from a folder you choose"),
        new("Picture puzzle", "Media", "\uEA86", "Nature pictures and sliding tiles"),
        new("Feed headlines", "Information", "\uE428", "RSS and Atom articles with thumbnails"),
        new("Quick launch", "Launch", "\uE71D", "Your pinned apps and websites")
    };
}
