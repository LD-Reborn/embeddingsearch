using Shared.Models;

namespace Server.Exceptions;

public class ProbMethodNotFoundException(ProbMethodEnum probMethod) : Exception($"Unknown probMethod name {probMethod}") { }

public class SimilarityMethodNotFoundException(SimilarityMethodEnum similarityMethod) : Exception($"Unknown similarityMethod name \"{similarityMethod}\"") { }

public class JSONPathSelectionException(string path, string testedContent) : Exception($"Unable to select tokens using JSONPath {path} for string: {testedContent}.") { }