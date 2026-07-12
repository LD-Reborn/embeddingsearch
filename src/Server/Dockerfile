# Prepare build environment
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

# Copy projects
COPY ./src/Server/ ./src/Server/
COPY ./src/Shared/ ./src/Shared/

# Remove sensitive files
RUN rm ./src/Server/appsettings*.json
RUN rm ./src/Server/wwwroot/logs/* || true

# Build and publish projects
RUN dotnet restore src/Server/Server.csproj
RUN dotnet publish src/Server/Server.csproj -c Release -o /output

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /output .
ENV ASPNETCORE_ENVIRONMENT Docker

# Define port and entrypoint
EXPOSE 5146
ENTRYPOINT [ "./Server" ]