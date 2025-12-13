namespace Server.Exceptions;

public class ProbMethodNotFoundException(string probMethod) : Exception($"Unknown probMethod name {probMethod}") { }

public class SimilarityMethodNotFoundException(string similarityMethod) : Exception($"Unknown similarityMethod name \"{similarityMethod}\"") { }

public class JSONPathSelectionException(string path, string testedContent) : Exception($"Unable to select tokens using JSONPath {path} for string: {testedContent}.") { }