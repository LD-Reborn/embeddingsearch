namespace server.Models
{
    public class SearchdomainListResults
    {
        public required List<string> Searchdomains { get; set; }
    }

    public class SearchdomainCreateResults
    {
        public required int Id { get; set; }
    }

    public class SearchdomainUpdateResults
    {
        public required bool Success { get; set; }
    }

    public class SearchdomainDeleteResults
    {
        public required bool Success { get; set; }
        public required int DeletedEntities { get; set; }
    }

}
