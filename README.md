# embeddingsearch
<img src="docs/logo.png" alt="Logo" width="100">

embeddingsearch is a self-hosted semantic search server built on vector embeddings.

It lets you index and semantically search text using modern embedding models. It's designed to be flexible, extensible, and easy to use.

<img src="docs/ProjectOutline/ProjectOutlineDiagram.excalidraw.svg" alt="Logo">

## What embeddingsearch offers:
- Privacy and flexibility by allowing one to self-host everything, including:
  - Ollama
  - OpenAI-compatible APIs (like LocalAI)
- Astonishing accuracy when using multiple models for single indices
- Ease-of-use and ease-of-implementation
  - The server offers a front-end for management and status information, as well as a decorated swagger back-end
  - The indexer can also be self-hosted and serves as a host for executing indexing scripts
  - The client library can be used to develop your own client software that posts queries or creates indices
- Caching & persistency
  - Generating embeddings is expensive. So why not cache AND store them?
  - Query results can also be cached.
  - "Doesn't that eat a lot of precious RAM?" - My own testing showed: embeddings take up around 4200-5200 bytes each depending on the request string size. So around 4-5 GB per million cached embeddings.

This repository comes with a:
- Server
- Client library (C#)
- Scripting based indexer service that supports the use of
  - Python
  - CSharp (Roslyn - at-runtime evaluation)
  - CSharp (Reflection - compiled)
  - Lua (Planned)
  - Javascript (Planned)

# How to set up
## Server
(Docker also available! See [Docker installation](docs/Server.md#docker-installation))
1. Install the inferencing tool of your choice, (e.g. [ollama](https://ollama.com/download)) and pull a few models that support generating embeddings.
2. [Install the depencencies](docs/Server.md#installing-the-dependencies)
3. [Set up a mysql database](docs/Server.md#mysql-database-setup)
4. [Set up the configuration](docs/Server.md#configuration)
5. In `src/Server` execute `dotnet build && dotnet run` to start the server
6. (optional) Create a searchdomain using the web interface
## Indexer
(Docker now available! See [Docker installation](docs/Indexer.md#docker-installation))
1. [Install the dependencies](docs/Indexer.md#installing-the-dependencies)
2. [Configure the indexer](docs/Indexer.md#configuration)
3. [Set up your indexing script(s)](docs/Indexer.md#scripting)
4. In `src/Indexer` execute `dotnet build && dotnet run` to start the indexer
# Known issues
| Issue | Solution |
| --- | --- |
| System.DllNotFoundException: Could not load libpython3.13.so with flags RTLD_NOW \| RTLD_GLOBAL: libpython3.12.so: cannot open shared object file: No such file or directory | Install python3.13-dev via apt. Also: try running the indexer using `/usr/bin/dotnet` instead of `dotnet` (to make sure dotnet is not running as a snap) |

# Planned features and support
- Document processor with automatic chunking (e.g.: .md, .pdf, .docx, .xlsx, .png, .mp4)
- Indexer front-end
- Support for other database types (MSSQL, SQLite, PostgreSQL, MongoDB, Redis)

# Community
<a href="https://discord.gg/MUKeZM3k"><img src="https://img.shields.io/badge/Join%20Discord-7289DA?style=flat&logo=discord&logoColor=whiteServer" alt="Discord"></img></a>