using System.Collections.Concurrent;

namespace Server;

public class Entity(Dictionary<string, string> attributes, Probmethods.ProbMethodDelegate probMethod, string probMethodName, ConcurrentBag<Datapoint> datapoints, string name, string searchdomain)
{
    public Dictionary<string, string> Attributes = attributes;
    public Probmethods.ProbMethodDelegate ProbMethod = probMethod;
    public string ProbMethodName = probMethodName;
    public ConcurrentBag<Datapoint> Datapoints = datapoints;
    public int Id;
    public string Name = name;
    public string Searchdomain = searchdomain;
}