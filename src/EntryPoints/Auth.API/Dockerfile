FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["nuget.config", "."]
COPY ["src/EntryPoints/Auth.API/Auth.API.csproj", "src/EntryPoints/Auth.API/"]
COPY ["src/Auth.Application/Auth.Application.csproj", "src/Auth.Application/"]
COPY ["src/Auth.Infra/Auth.Infra.csproj", "src/Auth.Infra/"]
COPY ["src/Auth.Domain/Auth.Domain.csproj", "src/Auth.Domain/"]
COPY ["src/Infra/Infra.csproj", "src/Infra/"]
COPY ["src/Application/Application.csproj", "src/Application/"]
COPY ["src/Domain/Domain.csproj", "src/Domain/"]
COPY ["src/SharedKernel/SharedKernel.csproj", "src/SharedKernel/"]
RUN dotnet restore "src/EntryPoints/Auth.API/Auth.API.csproj"
COPY . .
WORKDIR "/src/src/EntryPoints/Auth.API"
RUN dotnet build "./Auth.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Auth.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN chown -R $APP_UID:$APP_UID /app
USER $APP_UID
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Auth.API.dll"]
