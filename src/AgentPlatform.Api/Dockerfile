FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/AgentPlatform.Api/AgentPlatform.Api.csproj", "src/AgentPlatform.Api/"]
COPY ["src/AgentPlatform.Core/AgentPlatform.Core.csproj", "src/AgentPlatform.Core/"]
RUN dotnet restore "src/AgentPlatform.Api/AgentPlatform.Api.csproj"

COPY . .
RUN dotnet publish "src/AgentPlatform.Api/AgentPlatform.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Root olmayan kullanıcı ile çalıştır (güvenlik ilkesi)
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AgentPlatform.Api.dll"]
