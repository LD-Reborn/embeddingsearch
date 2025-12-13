namespace Server.Exceptions;
public class SearchdomainNotFoundException(string searchdomainName) : Exception($"Searchdomain with name {searchdomainName} not found.") { }
public class SearchdomainAlreadyExistsException(string searchdomainName) : Exception($"Searchdomain with name {searchdomainName} already exists.") { }