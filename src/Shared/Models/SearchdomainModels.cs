
using System.Text.Json.Serialization;

namespace Shared.Models;
public readonly struct ResultItem(float score, string name)
{
    [JsonPropertyName("Score")]
    public readonly float Score { get; } = score;
    [JsonPropertyName("Name")]
    public readonly string Name { get; } = name;
}

public struct DateTimedSearchResult(DateTime dateTime, List<ResultItem> results)
{
    [JsonPropertyName("AccessDateTimes")]
    public List<DateTime> AccessDateTimes { get; set; } = [dateTime];
    [JsonPropertyName("Results")]
    public List<ResultItem> Results { get; set; } = results;
}