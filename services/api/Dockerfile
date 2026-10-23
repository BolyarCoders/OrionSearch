FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY services/api/global.json services/api/OrionSearch.sln services/api/dotnet-tools.json ./services/api/
COPY services/api/src/ ./services/api/src/
COPY services/api/tests/ ./services/api/tests/
RUN dotnet restore services/api/OrionSearch.sln
RUN dotnet publish services/api/src/OrionSearch.Api/OrionSearch.Api.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "OrionSearch.Api.dll"]
