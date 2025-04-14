# embeddingsearch

Embeddingsearch is a DotNet C# library that uses Embedding Similarity Search (similiarly to [Magna](https://github.com/yousef-rafat/Magna/tree/main)) to semantically compare a given input to a database of pre-processed entries.

This repository comes with
- the library
- a ready-to-use CLI module
- a REST API server (WIP) for if you want to process the data on a remote server or make it available to other languages.

(Reranker support is planned, but its integration is not yet conceptualized.)
# How to set up
1. Install [ollama](https://ollama.com/download)
2. Pull a few models using ollama (e.g. `paraphrase-multilingual`, `bge-m3`, `mxbai-embed-large`, `nomic-embed-text`)
3. [Install the depencencies](#installing-the-dependencies)
4. [Set up a local mysql database](#mysql-database-setup)
5. (optional) [Create a searchdomain](#create-a-searchdomain)

# Installing the dependencies
## Ubuntu 24.04
1. `sudo apt update && sudo apt install dotnet-sdk-8.0 -y`
## Windows
Download the [.NET SDK](https://dotnet.microsoft.com/en-us/download) or follow these steps to use WSL:
1. Install Ubuntu in WSL (`wsl --install` and `wsl --install -d Ubuntu`)
2. Enter your WSL environment `wsl.exe` and configure it
3. Update via `sudo apt update && sudo apt upgrade -y && sudo snap refresh`
4. GOTO [Ubuntu 24.04](#Ubuntu-24.04)

# MySQL database setup
1. Install the MySQL server:
- Linux/WSL: `sudo apt install mysql-server`
- Windows: [MySQL Community Server](https://dev.mysql.com/downloads/mysql/)
2. connect to it: `sudo mysql -u root` (Or from outside of WSL: `mysql -u root`)
3. Create the database
`CREATE DATABASE embeddingsearch; use embeddingsearch;`
4. Create the user
`CREATE USER 'embeddingsearch'@'%' identified by "somepassword!"; GRANT ALL ON embeddingsearch.* TO embeddingsearch; FLUSH PRIVILEGES;`
5. Create the tables: `dotnet build` and `src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --database --setup`

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
`src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --entity --index -o $ollama_URL -s $searchdomain_name -e $entity_as_JSON`
Creates the entity using the json string as specified under $entity_as_JSON

Example:
- Linux: `src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --entity --index -o $ollama_URL -s $searchdomain_name -e '{"name": "myfile.txt", "probmethod": "weighted_average", "searchdomain": "mysearchdomain", "attributes": {"mimetype": "text-plain"}, "datapoints": [{"name": "text", "text": "this is the full text", "probmethod_embedding": "weighted_average", "model": ["bge-m3", "nomic-embed-text", "paraphrase-multilingual"]}, {"name": "filepath", "text": "/home/myuser/myfile.txt", "probmethod_embedding": "weighted_average", "model": ["bge-m3", "nomic-embed-text", "paraphrase-multilingual"]}]}'`
- Powershell: `src/cli/bin/Debug/net8.0/cli -h $mysql_ip -p $mysql_port -U $mysql_username -P $mysql_password --entity --index -o $ollama_URL -s $searchdomain_name -e '{\"name\": \"myfile.txt\", \"probmethod\": \"weighted_average\", \"searchdomain\": \"mysearchdomain\", \"attributes\": {\"mimetype\": \"text-plain\"}, \"datapoints\": [{\"name\": \"text\", \"text\": \"this is the full text\", \"probmethod_embedding\": \"weighted_average\", \"model\": [\"bge-m3\", \"nomic-embed-text\", \"paraphrase-multilingual\"]}, {\"name\": \"filepath\", \"text\": \"\/home\/myuser\/myfile.txt\", \"probmethod_embedding\": \"weighted_average\", \"model\": [\"bge-m3\", \"nomic-embed-text\", \"paraphrase-multilingual\"]}]}'`

Only the json:
```json
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

# Known issues
| Issue | Solution |
| --- | --- |
| Failed to load /usr/lib/dotnet/host/fxr/8.0.15/libhostfxr.so, error: /snap/core20/current/lib/x86_64-linux-gnu/libstdc++.so.6: version `GLIBCXX_3.4.29' not found (required by /usr/lib/dotnet/host/fxr/8.0.15/libhostfxr.so) | You likely installed dotnet via snap. Try using `/usr/bin/dotnet` instead of `dotnet`. |
| Unhandled exception. MySql.Data.MySqlClient.MySqlException (0x80004005): Invalid attempt to access a field before calling Read() | The searchdomain you entered does not exist |
| Unhandled exception. MySql.Data.MySqlClient.MySqlException (0x80004005): Authentication to host 'localhost' for user 'embeddingsearch' using method 'caching_sha2_password' failed with message: Access denied for user 'embeddingsearch'@'localhost' (using password: YES) | TBD |

# To-do
- Implement environment variable use
- fix the `--help` functionality
- Rename `cli` to something unique but still short, e.g. `escli`?
- Improve error messaging for when retrieving a searchdomain fails.
- Remove the `id` collumns from the database tables where the table is actually identified (and should be unique by) the name, which should become the new primary key.
- Implement the api server
- Improve performance & latency (Create ready-to-go processes where each contain an n'th share of the entity cache, ready to perform a query. Prepare it after creating the entity cache.)
- Write a Linux installer for the CLI tool
- Make the API server a docker container
- Maybe add a config such that one does not need to always specify the MySQL connection info

# Future features
- Support for other database types (TSQL, SQLite)
