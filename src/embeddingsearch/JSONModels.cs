namespace embeddingsearch;

public class JSONEntity
{
    public required string name { get; set; }
    public required string probmethod { get; set; }
    public required string searchdomain { get; set; }
    public required Dictionary<string, string> attributes { get; set; }
    public required JSONDatapoint[] datapoints { get; set; }
}

public class JSONDatapoint
{
    public required string name { get; set; }
    public required string text { get; set; }
    public required string probmethod_embedding { get; set; }
    public required string[] model { get; set; }
}