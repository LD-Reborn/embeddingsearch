# -----------------------
# Build stage
# -----------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ./src/Indexer/ ./Indexer/
COPY ./src/Shared/ ./Shared/
COPY ./src/Client/ ./Client/

RUN dotnet publish ./Indexer/Indexer.csproj -c Release -o /out /p:UseAppHost=false

# -----------------------
# Runtime stage
# -----------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Only runtime dependencies
RUN apt-get update && apt-get install -y software-properties-common \
    && add-apt-repository ppa:deadsnakes/ppa \
    && apt-get update \
    && apt-get install -y python3.14 python3.14-venv python3.14-dev

COPY --from=build /out ./
COPY ./src/Indexer/Scripts ./Scripts

ENV ASPNETCORE_ENVIRONMENT=Docker
ENV PYTHONIOENCODING=utf8

EXPOSE 5210
ENTRYPOINT ["dotnet", "Indexer.dll"]