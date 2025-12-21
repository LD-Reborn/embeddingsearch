namespace Server;

public class Entity(Dictionary<string, string> attributes, Probmethods.probMethodDelegate probMethod, string probMethodName, List<Datapoint> datapoints, string name)
{
    public Dictionary<string, string> attributes = attributes;
    public Probmethods.probMethodDelegate probMethod = probMethod;
    public string probMethodName = probMethodName;
    public List<Datapoint> datapoints = datapoints;
    public int id;
    public string name = name;
}