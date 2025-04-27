namespace server.Models;


public class EntityQueryResults
{
    public required List<EntityQueryResult> Results { get; set; }
}

public class EntityQueryResult
{
    public required string Name { get; set; }
    public float Value { get; set; }
}

public class EntityIndexResult
{
    public required bool Success { get; set; }
}

public class EntityListResults
{
    public required List<EntityListResult> Results { get; set; }
}

public class EntityListResult
{
    public required string Name { get; set; }
    public required List<AttributeResult> Attributes { get; set; }
    public required List<DatapointResult> Datapoints { get; set; }
}

public class AttributeResult
{
    public required string Name { get; set; }
    public required string Value { get; set; }
}

public class DatapointResult
{
    public required string Name { get; set; }
    public required string ProbMethod { get; set; }
    public required List<EmbeddingResult>? Embeddings { get; set; } 
}

public class EmbeddingResult
{
    public required string Model { get; set; }
    public required float[] Embeddings { get; set; }
}

public class EntityDeleteResults
{
    public required bool Success { get; set; }
}

