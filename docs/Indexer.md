# Overview
The indexer by default
- runs on port 5210
- Uses Swagger UI in development mode (endpoint: `/swagger/index.html`)
- Ignores API keys when in development mode
- Uses Elmah error logging (endpoint: `/elmah`, local files: `~/logs`)
- Uses serilog logging (local files: `~/logs`)
- Uses HealthChecks (endpoint: `/healthz`)
## Docker installation
(On Linux you might need root privileges, thus use `sudo` where necessary)
1. Navigate to the `src` directory
2. Build the docker container: `docker build -t embeddingsearch-indexer -f Indexer/Dockerfile .`
3. Run the docker container: `docker run --net=host -t embeddingsearch-indexer` (the `-t` is optional, but you get more meaningful output. Or use `-d` to run it in the background)
## Installing the dependencies
## Ubuntu 24.04
1. Install the .NET SDK: `sudo apt update && sudo apt install dotnet-sdk-8.0 -y`
2. Install the python SDK: `sudo apt install python3 python3.12 python3.12-dev`
## Windows
Download the [.NET SDK](https://dotnet.microsoft.com/en-us/download) or follow these steps to use WSL:
1. Install Ubuntu in WSL (`wsl --install` and `wsl --install -d Ubuntu`)
2. Enter your WSL environment `wsl.exe` and configure it
3. Update via `sudo apt update && sudo apt upgrade -y && sudo snap refresh`
4. Continue here: [Ubuntu 24.04](#Ubuntu-24.04)
# Configuration
The configuration is located in `src/Indexer` and conforms to the [ASP.NET configuration design pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-9.0), i.e. `src/Indexer/appsettings.json` is the base configuration, and `/src/Indexer/appsettings.Development.json` overrides it.

If you plan to use multiple environments, create any `appsettings.{YourEnvironment}.json` (e.g. `Development`, `Staging`, `Prod`) and set the environment variable `DOTNET_ENVIRONMENT` accordingly on the target machine.
## Setup
If you just installed the server and want to configure it:
1. Open `src/Server/appsettings.Development.json`
2. If your search server is not on the same machine as the indexer, update "BaseUri" to reflect the URL to the server.
3. If your search server requires API keys, (i.e. it's operating outside of the "Development" environment) set `"ApiKey": "<your key here>"` beneath `"BaseUri"` in the `"Embeddingsearch"` section.
4. Create your own indexing script(s) in `src/Indexer/Scripts/` and configure their use as 
## Structure
```json
  "EmbeddingsearchIndexer": {
    "Worker":
    [ // This is a list; you can have as many "workers" as you want
      {
        "Name": "example",
        "Script": "Scripts/example.py",
        "Calls": [ // This is also a list. A worker may have multiple different calls.
          {
            "Type": "interval", // See: Call types
            "Interval": 60000
          }
        ]
      },
      {
        "Name": "secondWorker",
        /* ... */
      }
    ]
  }
```
## Call types
- `interval`
    - What does it do: The script gets called periodically based on the specified `Interval` parameter.
    - Parameters:
        - Interval (in milliseconds)
- `schedule` (WIP)
    - What does it do: The script gets called based on the provided schedule
    - Parameters: (WIP)
- `fileupdate` (WIP)
    - What does it do: The script gets called whenever a file is updated in the specified subdirectory
    - Parameters: (WIP)
# Scripting
## General
## probMethods
Probmethods are used to join the multiple similarity values from multiple models and multiple datapoints into one single result.

They need to be specified when constructing a datapoint or an entity (see: [src/Indexer/Scripts/example.py](/src/Indexer/Scripts/example.py) in method `index_files`)

Currently the following probMethods are implemented:
- "Mean"
- "HarmonicMean"
- "QuadraticMean"
- "GeometricMean"
- "ExtremeValuesEmphasisWeightedAverage" or "EVEWavg"
- "HighValueEmphasisWeightedAverage" or "HVEWAvg"
- "LowValueEmphasisWeightedAverage" or "LVEWAvg"
- "DictionaryWeightedAverage"

### Mean
Averages the values by accumulating the sums and dividing by the number of entries.

$\frac{1}{n} \sum_{i=1}^{n} x_i$

### HarmonicMean
Calculates the harmonic mean, but also avoids division by 0 issues
$$
\text{HarmonicMean}(L) = \begin{cases}0,
& \text{if } n_{nz} = 0 \\\left( \frac{n_{nz}}{\sum\limits_{x_i \in L,\ x_i \neq 0} \frac{1}{x_i}} \right) \cdot \left( \frac{n_{nz}}{n_T} \right), & \text{otherwise}
\end{cases}
$$
with
- $n_{nz}$ being the number of non-zero elements
- $n_T$ being the total number of elements
### QuadraticMean
Calculates the quadratic mean.
$$
\text{QuadraticMean}(L) = \sqrt{ \frac{1}{n} \sum_{i=1}^{n} x_i^2 }
$$
### GeometricMean
Calculates the geometric mean.
$$
\text{GeometricMean}(L) = \begin{cases}0, & \text{if } n = 0\\\left(\prod\limits_{i=1}^{n} x_i \right)^{\frac{1}{n}}, & \text{otherwise}\end{cases}
$$

### ExtremeValuesEmphasisWeightedAverage
aka. EVEWavg

Calculates a weighted average where values near 0 or 1 are weighted much more heavily.

A single `1` makes the whole function return 1, as it has "infinite" weight.
Similarly any `0` causes the function to return 0.

(If both a `0` and a `1` are present, the function returns 1)
$$
\text{EVEWA}(L) = \begin{cases}1, & \text{if } \exists, x_i = 1 \\0, & \text{if } \exists, x_i = 0 \\\frac{ \sum\limits_{i=1}^{n} \frac{x_i}{x_i(1 - x_i)} }{ \sum\limits_{i=1}^{n} \frac{1}{x_i(1 - x_i)} }, & \text{otherwise}\end{cases}
$$

### HighValueEmphasisWeightedAverage
aka. HVEWAvg

Calculates a weighted average where values near 1 are weighted much more heavily. Lower values are weighted less.

A single `1` makes the whole function return 1, as it has "infinite" weight.
A `0` has zero weight.
$$
\text{HVEWA}(L) = \begin{cases}1, & \text{if } \exists, x_i = 1 \\\frac{ \sum\limits_{i=1}^{n} \frac{x_i}{1 - x_i} }{ \sum\limits_{i=1}^{n} \frac{1}{1 - x_i} }, & \text{otherwise}\end{cases}
$$
### LowValueEmphasisWeightedAverage
aka. LVEWAvg

Calculates a weighted average where values near 0 are weighted much more heavily. Higher values are weighted less.

A single `0` makes the whole function return 0, as it has "infinite" weight.
A `1` has zero weight.
$$
\text{LVEWA}(L) = \begin{cases}1, & \text{if } \exists, x_i = 1 \\\frac{ n}{ \sum\limits_{i=1}^{n} \frac{1}{x_i} }, & \text{otherwise}\end{cases}
$$
### DictionaryWeightedAverage
Calculates a weighted average as specified by the user.

$$
\text{DWA}(L, D) = \frac{ \sum\limits_{i=1}^{n} w_i x_i }{ \sum\limits_{i=1}^{n} w_i }
$$

Where:
- $L = \{(k_1, x_1), (k_2, x_2), \dots, (k_n, x_n)\}$ is the list of key–value pairs
  - $x_i$ is the float value associated with key $k_i$
- $D : k_i \mapsto w_i$ is a dictionary mapping keys $k_i$ to weights $w_i \in \mathbb{R}$
$


e.g.:
```
probmethod_datapoint = "DictionaryWeightedAverage:{\"ollama:bge-m3\": 4, \"ollama:mxbai-embed-large\": 1}"
probmethod_entity = "DictionaryWeightedAverage:{\"title\": 2, \"filename\": 0.1, \"text\": 0.25}"
```

## Python
To ease scripting, tools.py contains all definitions of the .NET objects passed to the script. This includes attributes and methods.

These are not yet defined in a way that makes them 100% interactible with the Dotnet CLR, meaning some methods that require anything more than strings or other simple data types to be passed are not yet supported. (WIP)
### Required elements
Here is an overview of required elements by example:
```python
from tools import * # Import all tools that are provided for ease of scripting

def init(toolset: Toolset): # defining an init() function with 1 parameter is required.
    pass # Your code would go here.
    # DO NOT put a main loop here! Why?
    # This function prevents the application from initializing and maintains exclusive control over the GIL

def update(toolset: Toolset): # defining an update() function with 1 parameter is required.
    pass # Your code - including possibly a main loop - would go here.
```
### Using the toolset passed by the .NET CLR
The use of the toolset is laid out in good example by `src/Indexer/Scripts/example.py`.

Currently, `Toolset`, as provided by the IndexerService to the Python script, contains 3 elements:
1. (only for `update`, not `init`) `callbackInfos` - an object that provides all information regarding the callback. (e.g. what file was updated)
2. `client` - a .NET object that has the functions as described in `src/Indexer/Scripts/tools.py`. It's the client that - according to the configuration - communicates with the search server and executes the API calls.
3. `filePath` - the path to the script, as specified in the configuration
## Golang
TODO
## Javascript
TODO