FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/EntryPoints/CronJobs/CronJobs.csproj", "src/EntryPoints/CronJobs/"]
COPY ["src/Infra/Infra.csproj", "src/Infra/"]
COPY ["src/Application/Application.csproj", "src/Application/"]
COPY ["src/Domain/Domain.csproj", "src/Domain/"]
COPY ["src/SharedKernel/SharedKernel.csproj", "src/SharedKernel/"]
RUN dotnet restore "src/EntryPoints/CronJobs/CronJobs.csproj"
COPY . .
WORKDIR "/src/src/EntryPoints/CronJobs"
RUN dotnet build "./CronJobs.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./CronJobs.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "CronJobs.dll"]
