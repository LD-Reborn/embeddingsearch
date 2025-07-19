namespace Shared.Models;

public class JSONEntity
{
    public required string Name { get; set; }
    public required string Probmethod { get; set; }
    public required string Searchdomain { get; set; }
    public required Dictionary<string, string> Attributes { get; set; }
    public required JSONDatapoint[] Datapoints { get; set; }
}

public class JSONDatapoint
{
    public required string Name { get; set; }
    public required string Text { get; set; }
    public required string Probmethod_embedding { get; set; }
    public required string[] Model { get; set; }
}