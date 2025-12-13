using System.Text.Json.Serialization;

namespace Shared.Models;


public class EntityQueryResults
{
    [JsonPropertyName("Results")]
    public required List<EntityQueryResult> Results { get; set; }
}

public class EntityQueryResult
{
    [JsonPropertyName("Name")]
    public required string Name { get; set; }
    [JsonPropertyName("Value")]
    public float Value { get; set; }
}

public class EntityIndexResult
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
    [JsonPropertyName("Message")]
    public string? Message { get; set; }
}

public class EntityListResults
{
    [JsonPropertyName("Results")]
    public required List<EntityListResult> Results { get; set; }
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

public class EntityListResult
{
    [JsonPropertyName("Name")]
    public required string Name { get; set; }
    [JsonPropertyName("Attributes")]
    public required List<AttributeResult> Attributes { get; set; }
    [JsonPropertyName("Datapoints")]
    public required List<DatapointResult> Datapoints { get; set; }
}

public class AttributeResult
{
    [JsonPropertyName("Name")]
    public required string Name { get; set; }
    [JsonPropertyName("Value")]
    public required string Value { get; set; }
}

public class DatapointResult
{
    [JsonPropertyName("Name")]
    public required string Name { get; set; }
    [JsonPropertyName("ProbMethod")]
    public required string ProbMethod { get; set; }
    [JsonPropertyName("SimilarityMethod")]
    public required string SimilarityMethod { get; set; }
    [JsonPropertyName("Embeddings")]
    public required List<EmbeddingResult>? Embeddings { get; set; } 
}

public class EmbeddingResult
{
    [JsonPropertyName("Model")]
    public required string Model { get; set; }
    [JsonPropertyName("Embeddings")]
    public required float[] Embeddings { get; set; }
}

public class EntityDeleteResults
{
    [JsonPropertyName("Success")]
    public required bool Success { get; set; }
}

