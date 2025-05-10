# Using the CLI
Before anything follow these steps:
1. Enter the project's `src` directory (used as the working directory in all examples)
2. Build the project: `dotnet build`
All user-defined parameters are denoted using the `$` symbol. I.e. `$mysql_ip` means: replace this with your MySQL IP address or set it as a local variable in your terminal session.

## Database
### Create or check
`src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --database --create [--setup]`

Without the `--setup` parameter a "dry-run" is performed. I.e. no actions are taken. Only the database is checked for read access and that all tables exist.

## Searchdomain
### Create a searchdomain
`src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --searchdomain --create -s $searchdomain_name`

Creates the searchdomain as specified under `$searchdomain_name`

### List searchdomains
`src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --searchdomain --list`

List all searchdomains

### Update searchdomain
`src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --searchdomain --update -s $searchdomain_name [-n $searchdomain_newname] [-S $searchdomain_newsettings]`

Set a new name and/or update the settings for the searchdomain.

### Delete searchdomain
`src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --searchdomain --delete -s $searchdomain_name`

Deletes a searchdomain and its corresponding entites.

## Entity
### Create / Index entity
`src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --entity --index -o $ollama_URL -s $searchdomain_name -e $entities_as_JSON`
Creates the entities using the json string as specified under $entities_as_JSON

Example:
- Linux: `src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --entity --index -o $ollama_URL -s $searchdomain_name -e '[{"name": "myfile.txt", "probmethod": "weighted_average", "searchdomain": "mysearchdomain", "attributes": {"mimetype": "text-plain"}, "datapoints": [{"name": "text", "text": "this is the full text", "probmethod_embedding": "weighted_average", "model": ["bge-m3", "nomic-embed-text", "paraphrase-multilingual"]}, {"name": "filepath", "text": "/home/myuser/myfile.txt", "probmethod_embedding": "weighted_average", "model": ["bge-m3", "nomic-embed-text", "paraphrase-multilingual"]}]}]'`
- Powershell: `src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --entity --index -o $ollama_URL -s $searchdomain_name -e '[{\"name\": \"myfile.txt\", \"probmethod\": \"weighted_average\", \"searchdomain\": \"mysearchdomain\", \"attributes\": {\"mimetype\": \"text-plain\"}, \"datapoints\": [{\"name\": \"text\", \"text\": \"this is the full text\", \"probmethod_embedding\": \"weighted_average\", \"model\": [\"bge-m3\", \"nomic-embed-text\", \"paraphrase-multilingual\"]}, {\"name\": \"filepath\", \"text\": \"\/home\/myuser\/myfile.txt\", \"probmethod_embedding\": \"weighted_average\", \"model\": [\"bge-m3\", \"nomic-embed-text\", \"paraphrase-multilingual\"]}]}]'`

Only the json:
```json
[
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
]
```
### Evaluate query (i.e. "search"; that what you're here for)
`src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --entity --evaluate -o $ollama_URL -s $searchdomain_name -q $query_string [-n $max_results]`

Executes a search using the specified query string and outputs the results.

### List entities
`src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --entity --list -s $searchdomain_name`

Lists all entities in that domain (together with its attributes and datapoints and probmethod)

### Delete entity
`src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --entity --remove -s $searchdomain_name -n $entity_name`

Deletes the entity specified by `$entity_name`.