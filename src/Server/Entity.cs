using System.Collections.Concurrent;

namespace Server;

public class Entity(Dictionary<string, string> attributes, Probmethods.probMethodDelegate probMethod, string probMethodName, ConcurrentBag<Datapoint> datapoints, string name, string searchdomain)
{
    public Dictionary<string, string> attributes = attributes;
    public Probmethods.probMethodDelegate probMethod = probMethod;
    public string probMethodName = probMethodName;
    public ConcurrentBag<Datapoint> datapoints = datapoints;
    public int id;
    public string name = name;
    public string searchdomain = searchdomain;
}