FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /src

COPY src/OrionSearch.Api/OrionSearch.Api.csproj src/OrionSearch.Api/
COPY src/OrionSearch.Application/OrionSearch.Application.csproj src/OrionSearch.Application/
COPY src/OrionSearch.Domain/OrionSearch.Domain.csproj src/OrionSearch.Domain/
COPY src/OrionSearch.Infrastructure/OrionSearch.Infrastructure.csproj src/OrionSearch.Infrastructure/
RUN dotnet restore src/OrionSearch.Api/OrionSearch.Api.csproj

COPY src/ src/
RUN dotnet publish src/OrionSearch.Api/OrionSearch.Api.csproj \
    --configuration Release --no-restore --output /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /out ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrionSearch.Api.dll"]
