using Shared.Models;

namespace Server.Exceptions;

public class ProbMethodNotFoundException(ProbMethodEnum probMethod) : Exception($"Unknown probMethod name {probMethod}") { }

public class SimilarityMethodNotFoundException(SimilarityMethodEnum similarityMethod) : Exception($"Unknown similarityMethod name \"{similarityMethod}\"") { }
