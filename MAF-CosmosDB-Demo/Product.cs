namespace CosmosVectorDemo;

/// <summary>A catalog document. Embedding stays null until the vectorization step runs.</summary>
public class Product
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public int WeightGrams { get; set; }
    public double Rating { get; set; }
    public string Description { get; set; } = "";
    public List<string> Features { get; set; } = [];
    public string Warranty { get; set; } = "";
    public float[]? Embedding { get; set; }

    /// <summary>The text sent to the embedding model. Everything here becomes searchable by meaning.</summary>
    public string ToEmbeddingText() =>
        $"""
        Product: {Name}
        Category: {Category}
        Description: {Description}
        Features: {string.Join(", ", Features)}
        Warranty: {Warranty}
        """;
}

/// <summary>A product returned by a search. Score is the VectorDistance result (null for keyword search).</summary>
public class SearchHit : Product
{
    public double? Score { get; set; }
}
