# Overview
The server by default
- runs on port 5146
- Uses Swagger UI in development mode (`/swagger/index.html`)
- Ignores API keys when in development mode
- Uses Elmah error logging (endpoint: `/elmah`, local files: `~/logs`)
- Uses serilog logging (local files: `~/logs`)
- Uses HealthChecks (endpoint: `/healthz`)
# Installing the dependencies
## Ubuntu 24.04
1. Install the .NET SDK: `sudo apt update && sudo apt install dotnet-sdk-8.0 -y`
## Windows
Download the [.NET SDK](https://dotnet.microsoft.com/en-us/download) or follow these steps to use WSL:
1. Install Ubuntu in WSL (`wsl --install` and `wsl --install -d Ubuntu`)
2. Enter your WSL environment `wsl.exe` and configure it
3. Update via `sudo apt update && sudo apt upgrade -y && sudo snap refresh`
4. Continue here: [Ubuntu 24.04](#Ubuntu-24.04)

# MySQL database setup
1. Install the MySQL server:
- Linux/WSL: `sudo apt install mysql-server`
- Windows: [MySQL Community Server](https://dev.mysql.com/downloads/mysql/)
2. connect to it: `sudo mysql -u root` (Or from outside of WSL: `mysql -u root`)
3. Create the database:
`CREATE DATABASE embeddingsearch; use embeddingsearch;`
4. Create the user (replace "somepassword! with a secure password):
`CREATE USER 'embeddingsearch'@'%' identified by "somepassword!"; GRANT ALL ON embeddingsearch.* TO embeddingsearch; FLUSH PRIVILEGES;`

# Configuration
## Environments
The configuration is located in `src/Server/` and conforms to the [ASP.NET configuration design pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-9.0), i.e. `src/Server/appsettings.json` is the base configuration, and `/src/Server/appsettings.Development.json` overrides it.

If you plan to use multiple environments, create any `appsettings.{YourEnvironment}.json` (e.g. `Development`, `Staging`, `Prod`) and set the environment variable `DOTNET_ENVIRONMENT` accordingly on the target machine.
## Setup
If you just installed the server and want to configure it:
1. Open `src/Server/appsettings.Development.json`
2. Change the password in the "SQL" section (`pwd=<your password goes here>;`)
3. Check the "AiProviders" section. If your Ollama/LocalAI/etc. instance does not run locally, update the "baseURL" to point to the correct URL.
4. If you plan on using the server in production:
    1. Set the environment variable `DOTNET_ENVIRONMENT` to something that is not "Development". (e.g. "Prod")
    2. Rename the `appsettings.Development.json` - replace "Development" with whatever you chose. (e.g. "Prod")
    3. Set API keys in the "ApiKeys" section (generate keys using the `uuid` command on Linux)
## Structure
```json
  "Embeddingsearch": {
    "ConnectionStrings": {
      "SQL": "server=localhost;database=embeddingsearch;uid=embeddingsearch;pwd=somepassword!;"
    },
    "Elmah": {
      "AllowedHosts": [ // Specify which IP addresses can access /elmah
        "127.0.0.1",
        "::1",
        "172.17.0.1"
      ]
    },
    "AiProviders": {
      "ollama": { // Name of the provider. Used when defining models for a datapoint, e.g. "ollama:mxbai-embed-large"
        "handler": "ollama", // The type of API located at baseURL
        "baseURL": "http://localhost:11434" // Location of the API
      },
      "localAI": {
        "handler": "openai",
        "baseURL": "http://localhost:8080",
        "ApiKey": "Some API key here"
      }
    },
    "ApiKeys": ["Some UUID here", "Another UUID here"], // Restrict access in non-development environments to the server's API using your own generated API keys
    "UseHttpsRedirection": true // tbh I don't even know why this is still here. // TODO implement HttpsRedirection or remove this line
  }
```
## AiProviders
Each AI provider (Ollama/LocalAI/OpenAI/etc.) can be specified individually.

One can even specify multiple Ollama instances and name them however one pleases. E.g.:
```json
    "AiProviders": {
      "ollama_1": {
        "handler": "ollama",
        "baseURL": "http://x.x.x.x:11434",
      },
      "ollama_2": {
        "handler": "ollama",
        "baseURL": "http://y.y.y.y:11434",
      }
    }
```
### handler
Currently two handlers are implemented for embeddings generation:
- ollama
    - requests embeddings from `/api/embed`
- localai
    - requests embeddings from `/v1/embeddings`
### baseURL
Specified by `scheme://host:port`. E.g.: `"baseUrl": "http://localhost:11434"`

Any specified absolute path will be disregarded. (e.g. "http://x.x.x.x/any/subroute" -> "http://x.x.x.x/api/embed")
### ApiKey
- `ollama` currently does not support API keys. Specifying a key does not have any effect.
- `openai` implements the use of ApiKey. E.g. `"ApiKey": "Some API key here"`

# API
## Accessing the api
Once started, the server's API can be comfortably be viewed and manipulated via swagger.

By default it is accessible under: `http://localhost:5146/swagger/index.html`

To make an API request from within swagger:
1. Open one of the actions ("GET" / "POST")
2. Click the "Try it out" button. The input fields (if there are any for your action) should now be editable.
3. Fill in the necessary information
4. Click "Execute"
## Restricting access
API keys do **not** get checked in Development environment!

Set up a non-development environment as described in [Configuration>Setup](#setup) to enable API key authentication.