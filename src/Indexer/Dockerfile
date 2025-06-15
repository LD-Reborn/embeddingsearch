FROM ubuntu:24.04 AS ubuntu
WORKDIR /app
RUN apt-get update
RUN apt-get install -y python3.12 python3.12-venv python3.12-dev dotnet-sdk-8.0
RUN apt-get clean
COPY . /src/
ENV ASPNETCORE_ENVIRONMENT Docker
RUN rm /src/Server/appsettings*
RUN dotnet build /src/Indexer/Indexer.csproj
RUN dotnet publish /src/Indexer/Indexer.csproj -c Release -o /app
RUN cp -r /src/Indexer/Scripts /app/Scripts
RUN rm -r /src
ENV PYTHONIOENCODING=utf8
EXPOSE 5120
ENTRYPOINT ["./Indexer"]