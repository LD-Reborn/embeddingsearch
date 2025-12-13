namespace Server.Exceptions;

public class ProbMethodNotFoundException(string probMethod) : Exception($"Unknown probMethod name {probMethod}") { }

public class SimilarityMethodNotFoundException(string similarityMethod) : Exception($"Unknown similarityMethod name \"{similarityMethod}\"") { }