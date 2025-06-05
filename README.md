# embeddingsearch
<img src="https://github.com/LD-Reborn/embeddingsearch/blob/main/logo.png" alt="Logo" width="100">

Embeddingsearch is a DotNet C# library that uses Embedding Similarity Search (similiarly to [Magna](https://github.com/yousef-rafat/Magna/tree/main)) to semantically compare a given input to a database of pre-processed entries.

This repository comes with
- a server (accessible via API calls & swagger)
- a clientside library
- a CLI module (deprecated)
- a scripting based indexer service that supports
  - Python
  - Golang (WIP)
  - Javascript (WIP)

# How to set up / use
## server
1. Install [ollama](https://ollama.com/download)
2. Pull a few models using ollama (e.g. `paraphrase-multilingual`, `bge-m3`, `mxbai-embed-large`, `nomic-embed-text`)
3. [Install the depencencies](docs/Server.md#installing-the-dependencies)
4. [Set up a local mysql database](docs/Server.md#mysql-database-setup)
5. [Set up the configuration](docs/Server.md#setup)
6. In `src/server` execute `dotnet build && dotnet run` to start the server
7. (optional) [Create a searchdomain using the web interface](docs/Server.md#accessing-the-api)
## client
1. Download the package and add it to your project (TODO: NuGet)
2. Create a new client by either:
    1. By injecting IConfiguration (e.g. `services.AddSingleton<Client>();`)
    2. By specifying the baseUri, apiKey, and searchdomain (e.g. `new Client.Client(baseUri, apiKey, searchdomain)`)
## indexer
1. [Install the dependencies](docs/Indexer.md#installing-the-dependencies)
2. [Set up the server](#server)
3. [Configure the indexer](docs/Indexer.md#configuration)
4. [Set up your indexing script(s)](docs/Indexer.md#scripting)
5. Run with `dotnet build && dotnet run` (Or `/usr/bin/dotnet build && /usr/bin/dotnet run`)
## CLI
Before anything follow these steps:
1. Enter the project's `src` directory (used as the working directory in all examples)
2. Build the project: `dotnet build`
All user-defined parameters are denoted using the `$` symbol. I.e. `$mysql_ip` means: replace this with your MySQL IP address or set it as a local variable in your terminal session.

All commands, parameters and examples are documented here: [docs/CLI.md](docs/CLI.md)
# Known issues
| Issue | Solution |
| --- | --- |
| Failed to load /usr/lib/dotnet/host/fxr/8.0.15/libhostfxr.so, error: /snap/core20/current/lib/x86_64-linux-gnu/libstdc++.so.6: version `GLIBCXX_3.4.29' not found (required by /usr/lib/dotnet/host/fxr/8.0.15/libhostfxr.so) | You likely installed dotnet via snap instead of apt. Try running the CLI using `/usr/bin/dotnet` instead of `dotnet`. |
| Unhandled exception. MySql.Data.MySqlClient.MySqlException (0x80004005): Invalid attempt to access a field before calling Read() | The searchdomain you entered does not exist |
| Unhandled exception. MySql.Data.MySqlClient.MySqlException (0x80004005): Authentication to host 'localhost' for user 'embeddingsearch' using method 'caching_sha2_password' failed with message: Access denied for user 'embeddingsearch'@'localhost' (using password: YES) | TBD |
| System.DllNotFoundException: Could not load libpython3.12.so with flags RTLD_NOW \| RTLD_GLOBAL: libpython3.12.so: cannot open shared object file: No such file or directory | Install python3.12-dev via apt |
# To-do
- (High priority) Add default indexer
  - Library
    - Processing:
      - Text / Markdown documents: file name, full text, paragraphs
      - Documents
        - PDF: file name, full text, headline?, paragraphs, images?
        - odt/docx: file name, full text, headline?, images?
        - msg/eml: file name, title, recipients, cc, text
      - Images: file name, OCR, image description?
      - Videos?
      - Presentations (Impress/Powerpoint): file name, full text, first slide title, titles, slide texts
      - Tables (Calc / Excel): file name, tab/page names?, full text (per tab/page)
      - Other? (TBD)
  - Server
    - ~~Scripting capability (Python; perhaps also lua)~~ (Done with the latest commits)
      - ~~Intended sourcing possibilities:~~
        - ~~Local/Remote files (CIFS, SMB, FTP)~~
        - ~~Database contents (MySQL, MSSQL)~~
        - ~~Web requests (E.g. manual crawling)~~
    - ~~Script call management (interval based & event based)~~
- Implement hash value to reduce wasteful re-indexing (Perhaps as a default property for an entity, set by the default indexer)
- Implement Healthz check
- Implement [ReaderWriterLock](https://learn.microsoft.com/en-us/dotnet/api/system.threading.readerwriterlockslim?view=net-9.0&redirectedfrom=MSDN) for entityCache to allow for multithreaded read access while retaining single-threaded write access.
- NuGet packaging and corresponding README documentation
- Add option for query result detail levels. e.g.:
  - Level 0: `{"Name": "...", "Value": 0.53}`
  - Level 1: `{"Name": "...", "Value": 0.53, "Datapoints": [{"Name": "title", "Value": 0.65}, {...}]}`
  - Level 2: `{"Name": "...", "Value": 0.53, "Datapoints": [{"Name": "title", "Value": 0.65, "Embeddings": [{"Model": "bge-m3", "Value": 0.87}, {...}]}, {...}]}`
- Add "Click-Through" result evaluation (For each entity: store a list of queries that led to the entity being chosen by the user. Then at query-time choose the best-fitting entry and maybe use it as another datapoint? Or use a separate weight function?)
- Reranker/Crossencoder/RAG (or anything else beyond initial retrieval) support
- Remove the CLI
- Improve error messaging for when retrieving a searchdomain fails.
- Remove the `id` collumns from the database tables where the table is actually identified (and should be unique by) the name, which should become the new primary key.
- Improve performance & latency (Create ready-to-go processes where each contain an n'th share of the entity cache, ready to perform a query. Prepare it after creating the entity cache.)
- Make the API server (and indexer, once it is done) a docker container

# Future features
- Support for other database types (MSSQL, SQLite)


# Community
<a href="https://discord.gg/MUKeZM3k"><img src="https://img.shields.io/badge/Join%20Discord-7289DA?style=flat&logo=discord&logoColor=whiteServer" alt="Discord"></img></a>