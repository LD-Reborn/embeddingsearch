using CommandLine;
namespace cli;

public class OptionsCommand
{
    [Option("database", Required = false, HelpText = "Do things related to the database")] // Create database / ensure it is set up correctly
    public bool IsDatabase { get; set; }

    [Option("searchdomain", Required = false, HelpText = "Execute CRUD on searchdomains")]
    public bool IsSearchdomain { get; set; }

    [Option("entity", Required = false, HelpText = "Execute CRUD on entities")]
    public bool IsEntity { get; set; }

    [Option('h', "host", Required = true, HelpText = "Host IP address (e.g. 192.168.0.75)")]
    public required string IP { get; set; }

    [Option('p', "port", Required = true, HelpText = "Host port (e.g. 3306)")]
    public required int Port { get; set; }

    [Option('U', "username", Required = true, HelpText = "Username for the MySQL database")]
    public required string Username { get; set; }

    [Option('P', "password", Required = true, HelpText = "Password for the MySQL database")]
    public required string Password { get; set; }

}

public class OptionsDatabase : OptionsCommand
{
    [Option("setup", Required = false, HelpText = "Ensure the database is set up correctly")]
    public bool SetupDatabase { get; set; }
}


public class OptionsSearchdomain : OptionsCommand
{
    [Option("create", Required = false, HelpText = "Create a searchdomain")]
    public bool IsCreate { get; set; }

    [Option("list", Required = false, HelpText = "Lists the searchdomains")]
    public bool IsList { get; set; }

    [Option("update", Required = false, HelpText = "Update a searchdomain (settings, name)")]
    public bool IsUpdate { get; set; }

    [Option("delete", Required = false, HelpText = "Delete a searchdomain")]
    public bool IsDelete { get; set; }
}


public class OptionsSearchdomainCreate : OptionsSearchdomain
{
    [Option('s', Required = true, HelpText = "Name of the searchdomain to create")]
    public required string Searchdomain { get; set; }
}

public class OptionsSearchdomainList : OptionsSearchdomain
{
    // The cleanest piece of code in this project
}

public class OptionsSearchdomainUpdate : OptionsSearchdomain
{
    [Option('s', Required = true, HelpText = "Name of the searchdomain to update")]
    public required string Searchdomain { get; set; }

    [Option('n', Required = false, HelpText = "New name to set")]
    public string? Name { get; set; }

    [Option('S', Required = false, HelpText = "New Settings (as json)")]
    public string? Settings { get; set; }
}

public class OptionsSearchdomainDelete : OptionsSearchdomain
{
    [Option('s', Required = true, HelpText = "Name of the searchdomain to delete")]
    public required string Searchdomain { get; set; }
}


public class OptionsEntity : OptionsCommand
{
    [Option("evaluate", Required = false, HelpText = "Evaluate a query")]
    public bool IsEvaluate { get; set; }

    [Option("index", Required = false, HelpText = "Create or update an entity from a JSON string")]
    public bool IsIndex { get; set; }

    [Option("remove", Required = false, HelpText = "Remove an entity")]
    public bool IsDelete { get; set; }

    [Option("list", Required = false, HelpText = "List all entities")]
    public bool IsList { get; set; }

}

public class OptionsEntityEvaluate : OptionsEntity
{
    [Option('s', Required = true, HelpText = "Searchdomain to be searched")]
    public required string Searchdomain { get; set; }
    
    [Option('q', "query", Required = true, HelpText = "Query string to evaluate the entities against")]
    public required string Query { get; set; }

    [Option('o', "ollama", Required = true, HelpText = "Ollama URL")]
    public required string OllamaURL { get; set; }

    [Option('n', "num", Required = false, HelpText = "(Maximum) number of results to output", Default = 5)]
    public int Num { get; set; }
}

public class OptionsEntityIndex : OptionsEntity // Example: -i -e {"name": "myfile.txt", "probmethod": "weighted_average", "searchdomain": "mysearchdomain", "attributes": {"mimetype": "text-plain"}, "datapoints": [{"name": "text", "text": "this is the full text", "probmethod_embedding": "weighted_average", "model": ["bge-m3", "nomic-embed-text", "paraphrase-multilingual"]}, {"name": "filepath", "text": "/home/myuser/myfile.txt", "probmethod_embedding": "weighted_average", "model": ["bge-m3", "nomic-embed-text", "paraphrase-multilingual"]}]}
{
    [Option('s', Required = true, HelpText = "Searchdomain the entity belongs to")]
    public required string Searchdomain { get; set; }
    
    [Option('e', Required = true, HelpText = "Entity (as JSON) to be inserted")]
    public required string EntityJSON { get; set; }
    /* Example for an entity:
    {
    "name": "myfile.txt",
    "probmethod": "weighted_average",
    "searchdomain": "mysearchdomain",
    "attributes": {
        "mimetype": "text-plain"
    },
    "datapoints": [
        {
        "name": "text",
        "text": "this is the full text",
        "probmethod_embedding": "weighted_average",
        "model": [
            "bge-m3",
            "nomic-embed-text",
            "paraphrase-multilingual"
        ]
        },
        {
        "name": "filepath",
        "text": "/home/myuser/myfile.txt",
        "probmethod_embedding": "weighted_average",
        "model": [
            "bge-m3",
            "nomic-embed-text",
            "paraphrase-multilingual"
        ]
        }
    ]
    }
    */
    
    [Option('o', "ollama", Required = true, HelpText = "Ollama URL")]
    public required string OllamaURL { get; set; }
}

public class OptionsEntityRemove : OptionsEntity
{
    [Option('s', Required = true, HelpText = "Searchdomain the entity belongs to")]
    public required string Searchdomain { get; set; }
    
    [Option('n', Required = true, HelpText = "Name of the entity")]
    public required string Name { get; set; }
}

public class OptionsEntityList : OptionsEntity
{
    [Option('s', Required = true, HelpText = "Searchdomain the entity belongs to")]
    public required string Searchdomain { get; set; }
}