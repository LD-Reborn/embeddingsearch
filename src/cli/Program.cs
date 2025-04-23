using System.Drawing.Printing;
using embeddingsearch;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using CommandLine;
using cli;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using Org.BouncyCastle.Asn1.X509.Qualified;
using Microsoft.Identity.Client;
using System.Text.Json.Serialization;
using System.Text.Json;

// ParserSettings parserSettings = new()
// {
//     IgnoreUnknownArguments = true
// };

Parser parser = new(settings =>
{
    settings.HelpWriter = Console.Error;
    settings.IgnoreUnknownArguments = true;
});

int retval = 0;

parser.ParseArguments<OptionsCommand>(args).WithParsed<OptionsCommand>(opts =>
{
    if (opts.IsDatabase)
    {
        parser.ParseArguments<OptionsDatabase>(args).WithParsed<OptionsDatabase>(opts =>
        {
            Searchdomain searchdomain = GetSearchdomain("http://localhost", "", opts.IP, opts.Username, opts.Password, true); // http://localhost is merely a placeholder.
            
            Dictionary<string, dynamic> parameters = [];
            System.Data.Common.DbDataReader reader = searchdomain.ExecuteSQLCommand("show tables", parameters);
            bool hasTables = reader.Read();
            if (!hasTables)
            {
                reader.Close();
                Console.WriteLine("Your database has no tables.");
                if (opts.SetupDatabase)
                {
                    Console.WriteLine("Setting up tables.");
                    searchdomain.ExecuteSQLNonQuery("CREATE TABLE searchdomain (id int PRIMARY KEY auto_increment, name varchar(512), settings JSON);", parameters);
                    searchdomain.ExecuteSQLNonQuery("CREATE TABLE entity (id int PRIMARY KEY auto_increment, name varchar(512), probmethod varchar(128), id_searchdomain int, FOREIGN KEY (id_searchdomain) REFERENCES searchdomain(id));", parameters);
                    searchdomain.ExecuteSQLNonQuery("CREATE TABLE attribute (id int PRIMARY KEY auto_increment, id_entity int, attribute varchar(512), value longtext, FOREIGN KEY (id_entity) REFERENCES entity(id));", parameters);
                    searchdomain.ExecuteSQLNonQuery("CREATE TABLE datapoint (id int PRIMARY KEY auto_increment, name varchar(512), probmethod_embedding varchar(512), id_entity int, FOREIGN KEY (id_entity) REFERENCES entity(id));", parameters);
                    searchdomain.ExecuteSQLNonQuery("CREATE TABLE embedding (id int PRIMARY KEY auto_increment, id_datapoint int, model varchar(512), embedding blob, FOREIGN KEY (id_datapoint) REFERENCES datapoint(id));", parameters);
                    Console.WriteLine("Your database is ready to use.");
                } else
                {
                    Console.WriteLine("Add the parameter `--setup` if you want the tables to be created for you.");
                }
            } else
            {
                List<string> tables = ["attribute", "datapoint", "embedding", "entity", "searchdomain"];
                Console.WriteLine("Your database is read-accessible and has the following tables:");
                while (hasTables)
                {
                    string table = reader.GetString(0);
                    Console.WriteLine($" - {table}");
                    try
                    {
                        tables.Remove(table);
                    } catch (Exception) {}
                    hasTables = reader.Read();
                }
                if (tables.Count == 0)
                {
                    Console.WriteLine("It looks like all necessary tables are there.");
                }
                Console.WriteLine("There is no check in place (yet) as to whether each table is formatted correctly and the data is consistent. Also this does not test write access. Good luck.");
            }
        })
        .WithNotParsed<OptionsDatabase>(action =>
        {
            PrintErrorMissingParameters("database");
            retval = 1;
        });
    } else if (opts.IsSearchdomain)
    {
        parser.ParseArguments<OptionsSearchdomain>(args).WithParsed<OptionsSearchdomain>(opts =>
        {
            if (opts.IsCreate)
            {
                parser.ParseArguments<OptionsSearchdomainCreate>(args).WithParsed<OptionsSearchdomainCreate>(opts =>
                {
                    Searchdomain searchdomain = GetSearchdomain("http://localhost", "", opts.IP, opts.Username, opts.Password, true); // http://localhost is merely a placeholder. // TODO implement a cleaner workaround
                    int id = searchdomain.DatabaseInsertSearchdomain(opts.Searchdomain);
                    Console.WriteLine($"The searchdomain was created under the following ID: {id}");
                })
                .WithNotParsed<OptionsSearchdomainCreate>(action =>
                {
                    PrintErrorMissingParameters("searchdomain --create");
                    retval = 1;
                });
            } else if (opts.IsList)
            {
                parser.ParseArguments<OptionsSearchdomainList>(args).WithParsed<OptionsSearchdomainList>(opts =>
                {
                    Searchdomain searchdomain = GetSearchdomain("http://localhost", "", opts.IP, opts.Username, opts.Password, true);
                    System.Data.Common.DbDataReader search = searchdomain.ExecuteSQLCommand("SELECT name FROM searchdomain", []);
                    Console.WriteLine("Searchdomains:");
                    while (search.Read())
                    {
                        Console.WriteLine($" - {search.GetString(0)}");
                    }
                    search.Close();
                })
                .WithNotParsed<OptionsSearchdomainList>(action =>
                {
                    PrintErrorMissingParameters("searchdomain --list");
                    retval = 1;
                });
            } else if (opts.IsUpdate)
            {
                parser.ParseArguments<OptionsSearchdomainUpdate>(args).WithParsed<OptionsSearchdomainUpdate>(opts =>
                {
                    if (opts.Name is null && opts.Settings is null)
                    {
                        Console.WriteLine("Warning: You did not specify either a new name or new settings. This will run, but with no effects."); // TODO add settings so this actually does not have any effect
                    }
                    Searchdomain searchdomainDry = GetSearchdomain("http://localhost:11434", "", opts.IP, opts.Username, opts.Password, true);
                    var search = searchdomainDry.ExecuteSQLCommand("SELECT * FROM searchdomain where name = @name", new() {{"name", opts.Searchdomain}});
                    bool hasSearchdomain = search.Read();
                    search.Close();
                    if (hasSearchdomain)
                    {
                        Searchdomain searchdomain = GetSearchdomain("http://localhost:11434", opts.Searchdomain, opts.IP, opts.Username, opts.Password);
                        Dictionary<string, dynamic> parameters = new()
                        {
                            {"name", opts.Name ?? opts.Searchdomain},
                            {"settings", opts.Settings ?? "{}"}, // TODO add settings.
                            {"id", searchdomain.id}
                        };
                        searchdomain.ExecuteSQLNonQuery("UPDATE searchdomain set name = @name, settings = @settings WHERE id = @id", parameters);
                        Console.WriteLine("Updated the searchdomain.");
                    } else
                    {
                        Console.WriteLine("No searchdomain under this name found.");
                        retval = 1;
                    }
                })
                .WithNotParsed<OptionsSearchdomainUpdate>(action =>
                {
                    PrintErrorMissingParameters("searchdomain --list");
                    retval = 1;
                });
            } else if (opts.IsDelete)
            {
                parser.ParseArguments<OptionsSearchdomainDelete>(args).WithParsed<OptionsSearchdomainDelete>(opts =>
                {
                    Searchdomain searchdomain = GetSearchdomain("http://localhost:11434", opts.Searchdomain, opts.IP, opts.Username, opts.Password);
                    int counter = 0;
                    foreach (Entity entity in searchdomain.entityCache)
                    {
                        searchdomain.DatabaseRemoveEntity(entity.name);
                        counter += 1;
                    }
                    Console.WriteLine($"Number of entities deleted as part of deleting the searchdomain: {counter}");
                    searchdomain.ExecuteSQLNonQuery("DELETE FROM entity WHERE id_searchdomain = @id", new() {{"id", searchdomain.id}}); // Cleanup // TODO add rows affected
                    searchdomain.ExecuteSQLNonQuery("DELETE FROM searchdomain WHERE name = @name", new() {{"name", opts.Searchdomain}});
                    Console.WriteLine("Searchdomain has been successfully removed.");
                })
                .WithNotParsed<OptionsSearchdomainDelete>(action =>
                {
                    PrintErrorMissingParameters("searchdomain --list");
                    retval = 1;
                });
            }
        })
        .WithNotParsed<OptionsSearchdomain>(action =>
        {
            PrintErrorMissingParameters("searchdomain");
            retval = 1;            
        });
        
    } else if (opts.IsEntity)
    {
        parser.ParseArguments<OptionsEntity>(args).WithParsed<OptionsEntity>(opts =>
        {
            if (opts.IsEvaluate)
            {
                parser.ParseArguments<OptionsEntityEvaluate>(args).WithParsed<OptionsEntityEvaluate>(opts =>
                {
                    Console.WriteLine("The results:");
                    var search = Search(opts);
                    int max = opts.Num;
                    if (max > search.Count)
                    {
                        max = search.Count;
                    }
                    for (int i = 0; i < max; i++)
                    {
                        Console.WriteLine($"{search[i].Item1} {search[i].Item2}");
                    }
                })
                .WithNotParsed<OptionsEntityEvaluate>(action =>
                {
                    PrintErrorUndeterminedAction("entity");
                    retval = 1;
                });
            } else if (opts.IsIndex)
            {
                parser.ParseArguments<OptionsEntityIndex>(args).WithParsed<OptionsEntityIndex>(opts =>
                {
                    if (opts.EntityJSON is null)
                    {
                        opts.EntityJSON = Console.In.ReadToEnd();
                    }
                    Searchdomain searchdomain = GetSearchdomain(opts.OllamaURL, opts.Searchdomain, opts.IP, opts.Username, opts.Password);
                    try
                    {
                        if (opts.EntityJSON.StartsWith('[')) // multiple entities
                        {
                            List<JSONEntity>? jsonEntities = JsonSerializer.Deserialize<List<JSONEntity>?>(opts.EntityJSON);
                            if (jsonEntities is not null)
                            {
                                
                                List<Entity>? entities = searchdomain.EntitiesFromJSON(opts.EntityJSON);
                                if (entities is not null)
                                {
                                    Console.WriteLine("Successfully created/updated the entity");
                                } else
                                {
                                    Console.Error.WriteLine("Unable to create the entity using the provided JSON.");
                                    retval = 1;
                                }
                            }
                        } else
                        {
                            Entity? entity = searchdomain.EntityFromJSON(opts.EntityJSON);
                            if (entity is not null)
                            {
                                Console.WriteLine("Successfully created/updated the entity");
                            } else
                            {
                                Console.Error.WriteLine("Unable to create the entity using the provided JSON.");
                                retval = 1;
                            }
                        }
                    } catch (Exception e)
                    {
                        Console.Error.WriteLine($"Unable to create the entity using the provided JSON.\nException: {e}");
                        retval = 1;
                    }
                })
                .WithNotParsed<OptionsEntityIndex>(action =>
                {
                    PrintErrorMissingParameters("entity --index");
                    retval = 1;
                });
            } else if (opts.IsDelete)
            {
                parser.ParseArguments<OptionsEntityRemove>(args).WithParsed<OptionsEntityRemove>(opts =>
                {
                    Searchdomain searchdomain = GetSearchdomain("http://localhost:11434", opts.Searchdomain, opts.IP, opts.Username, opts.Password);
                    bool hasEntity = searchdomain.HasEntity(opts.Name);
                    if (hasEntity)
                    {
                        searchdomain.DatabaseRemoveEntity(opts.Name);
                    } else
                    {
                        Console.Error.WriteLine($"No entity with the name {opts.Name} has been found.");
                        retval = 1;
                    }
                })
                .WithNotParsed<OptionsEntityRemove>(action =>
                {
                    PrintErrorMissingParameters("entity --remove");
                    retval = 1;
                });
            } else if (opts.IsList)
            {
                parser.ParseArguments<OptionsEntityList>(args).WithParsed<OptionsEntityList>(opts =>
                {
                    Searchdomain searchdomain = GetSearchdomain("http://localhost:11434", opts.Searchdomain, opts.IP, opts.Username, opts.Password);
                    Console.WriteLine("Entities:");
                    foreach (Entity entity in searchdomain.entityCache)
                    {
                        Dictionary<string, string> datapointNames = [];
                        foreach (Datapoint datapoint in entity.datapoints)
                        {
                            datapointNames[datapoint.name] = datapoint.probMethod.Method.Name;
                        }
                        Console.WriteLine($"- {entity.name} | {JsonSerializer.Serialize(entity.attributes)} | {JsonSerializer.Serialize(datapointNames)}");
                    }
                })
                .WithNotParsed<OptionsEntityList>(action =>
                {
                    PrintErrorMissingParameters("entity --list");
                    retval = 1;
                });
            } else
            {
                PrintErrorUndeterminedAction("entity");
                retval = 1;
            }
        });

    } else
    {
        Console.Error.WriteLine($"Unable to parse {args[0]}. Needs to be \"database\", \"searchdomain\", or \"entity\".");
    }
});

return retval;

static List<(float, string)> Search(OptionsEntityEvaluate optionsEntityIndex)
{
    var searchdomain = GetSearchdomain(optionsEntityIndex.OllamaURL, optionsEntityIndex.Searchdomain, optionsEntityIndex.IP, optionsEntityIndex.Username, optionsEntityIndex.Password);
    List<(float, string)> results = searchdomain.Search(optionsEntityIndex.Query);
    return results;
}


static Searchdomain GetSearchdomain(string ollamaURL, string searchdomain, string ip, string username, string password, bool runEmpty = false)
{
    string connectionString = $"server={ip};database=embeddingsearch;uid={username};pwd={password};";
    // var ollamaConfig = new OllamaApiClient.Configuration
    // {
    //     Uri = new Uri(ollamaURL)
    // };
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(ollamaURL),
        Timeout = TimeSpan.FromSeconds(36000) //.MaxValue //FromSeconds(timeout)
    };
    var ollama = new OllamaApiClient(httpClient);
    return new Searchdomain(searchdomain, connectionString, ollama, "sqlserver", runEmpty);
}


static void PrintErrorUndeterminedAction(string prefix)
{
    PrintErrorReferToHelp("Unable to determine an action", prefix);
}

static void PrintErrorMissingParameters(string prefix)
{
    PrintErrorReferToHelp("Not all required parameters were specified", prefix);
}

static void PrintErrorReferToHelp(string text, string prefix) // TODO make this not static and set retval to not zero
{
    Console.Error.WriteLine($"{text}. Please use `{prefix} --help` for more info");
}
