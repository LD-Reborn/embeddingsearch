FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /build
COPY . .
RUN dotnet restore ./Server.csproj
RUN dotnet publish ./Server.csproj -c Release -o /output

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /output .
ENV ASPNETCORE_ENVIRONMENT Docker
EXPOSE 5146
ENTRYPOINT [ "./Server" ]