FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["nuget.config", "./"]
COPY ["src/SharedKernel/SharedKernel.csproj", "src/SharedKernel/"]
COPY ["src/Domain/Domain.csproj", "src/Domain/"]
COPY ["src/Application/Application.csproj", "src/Application/"]
COPY ["src/Infra/Infra.csproj", "src/Infra/"]
COPY ["src/EntryPoints/Gateway/Gateway.csproj", "src/EntryPoints/Gateway/"]

RUN dotnet restore "src/EntryPoints/Gateway/Gateway.csproj"

COPY src/SharedKernel/ src/SharedKernel/
COPY src/Domain/ src/Domain/
COPY src/Application/ src/Application/
COPY src/Infra/ src/Infra/
COPY src/EntryPoints/Gateway/ src/EntryPoints/Gateway/

WORKDIR "/src/src/EntryPoints/Gateway"
RUN dotnet build "./Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN chown -R 1654:1654 /app
USER 1654:1654
ENTRYPOINT ["dotnet", "Gateway.dll"]

HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=5 \
  CMD wget --quiet --tries=1 --spider http://localhost:8080/health/live || exit 1
