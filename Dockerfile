# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish TradingPlatform.csproj -c Release -o /app/out

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
# Render route vers le port 10000 par défaut en Docker
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000
COPY --from=build /app/out .
ENTRYPOINT ["dotnet","TradingPlatform.dll"]
