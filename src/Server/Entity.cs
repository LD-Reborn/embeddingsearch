namespace Server;

public class Entity(Dictionary<string, string> attributes, Probmethods.probMethodDelegate probMethod, List<Datapoint> datapoints, string name)
{
    public Dictionary<string, string> attributes = attributes;
    public Probmethods.probMethodDelegate probMethod = probMethod;
    public List<Datapoint> datapoints = datapoints;
    public int id;
    public string name = name;
}