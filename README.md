# embeddingsearch

Embeddingsearch is a DotNet C# library that uses Embedding Similarity Search (similiarly to [Magna](https://github.com/yousef-rafat/Magna/tree/main)) to semantically compare a given input to a database of pre-processed entries.

This repository comes with
- a server
- a clientside library
- a ready-to-use CLI module
- an API for if you want to process the data on a remote server or make it available to other languages.

(Currently only initial retrieval is implemented.
Reranker support is planned, but its integration is not yet conceptualized.)
# How to set up
## server
1. Install [ollama](https://ollama.com/download)
2. Pull a few models using ollama (e.g. `paraphrase-multilingual`, `bge-m3`, `mxbai-embed-large`, `nomic-embed-text`)
3. [Install the depencencies](docs/Server.md#installing-the-dependencies)
4. [Set up a local mysql database](docs/Server.md#mysql-database-setup)
5. (optional) [Create a searchdomain](#create-a-searchdomain)
## indexer
TBD
## client
1. Download the package and add it to your project (TODO: NuGet)
2. Create a new client by either:
    1. By injecting IConfiguration (e.g. `services.AddSingleton<Client>();`)
    2. By specifying the baseUri, apiKey, and searchdomain (e.g. `new Client.Client(baseUri, apiKey, searchdomain)`)

# Known issues
| Issue | Solution |
| --- | --- |
| Failed to load /usr/lib/dotnet/host/fxr/8.0.15/libhostfxr.so, error: /snap/core20/current/lib/x86_64-linux-gnu/libstdc++.so.6: version `GLIBCXX_3.4.29' not found (required by /usr/lib/dotnet/host/fxr/8.0.15/libhostfxr.so) | You likely installed dotnet via snap instead of apt. Try running the CLI using `/usr/bin/dotnet` instead of `dotnet`. |
| Unhandled exception. MySql.Data.MySqlClient.MySqlException (0x80004005): Invalid attempt to access a field before calling Read() | The searchdomain you entered does not exist |
| Unhandled exception. MySql.Data.MySqlClient.MySqlException (0x80004005): Authentication to host 'localhost' for user 'embeddingsearch' using method 'caching_sha2_password' failed with message: Access denied for user 'embeddingsearch'@'localhost' (using password: YES) | TBD |

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
    - Scripting capability (Python; perhaps also lua)
      - Intended sourcing possibilities:
        - Local/Remote files (CIFS, SMB, FTP)
        - Database contents (MySQL, MSSQL)
        - Web requests (E.g. manual crawling)
    - Script call management (interval based & event based)
- NuGet packaging and according README documentation
- Add "Click-Through" result evaluation (For each entity: store a list of queries that led to the entity being chosen by the user. Then at query-time choose the best-fitting entry and maybe use it as another datapoint? Or use a separate weight function?)
- Reranker/Crossencoder/RAG (or anything else beyond initial retrieval) support
- Implement environment variable use in CLI
- fix the `--help` functionality
- Rename `cli` to something unique but still short, e.g. `escli`?
- Improve error messaging for when retrieving a searchdomain fails.
- Remove the `id` collumns from the database tables where the table is actually identified (and should be unique by) the name, which should become the new primary key.
- Improve performance & latency (Create ready-to-go processes where each contain an n'th share of the entity cache, ready to perform a query. Prepare it after creating the entity cache.)
- Write a Linux installer for the CLI tool
- Make the API server (and indexer, once it is done) a docker container

# Future features
- Support for other database types (MSSQL, SQLite)


# Community
<a href="https://discord.gg/MUKeZM3k"><img src="https://img.shields.io/badge/Join%20Discord-7289DA?style=flat&logo=discord&logoColor=whiteServer" alt="Discord"></img></a>