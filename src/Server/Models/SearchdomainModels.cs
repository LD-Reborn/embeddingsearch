using MySqlX.XDevAPI.Common;

namespace Server.Models;
public readonly struct ResultItem(float score, string name)
{
    public readonly float Score = score;
    public readonly string Name = name;
}

public struct DateTimedSearchResult
{
    public List<DateTime> accessTimes;
    public SearchResult Results;
}

public readonly struct SearchResult(List<ResultItem> results)
{
    public readonly List<ResultItem> Results = results;
}