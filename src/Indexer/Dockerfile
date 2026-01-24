FROM ubuntu:25.10 AS ubuntu
WORKDIR /app
RUN apt-get update
RUN apt-get install -y python3.13 python3.13-venv python3.13-dev dotnet-sdk-10.0
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