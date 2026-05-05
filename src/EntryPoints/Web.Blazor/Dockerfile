FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/EntryPoints/Web.Blazor/Web.Blazor.csproj", "src/EntryPoints/Web.Blazor/"]
COPY ["src/Infra/Infra.csproj", "src/Infra/"]
COPY ["src/Application/Application.csproj", "src/Application/"]
COPY ["src/Domain/Domain.csproj", "src/Domain/"]
COPY ["src/SharedKernel/SharedKernel.csproj", "src/SharedKernel/"]
RUN dotnet restore "src/EntryPoints/Web.Blazor/Web.Blazor.csproj"
COPY . .
WORKDIR "/src/src/EntryPoints/Web.Blazor"
RUN dotnet build "./Web.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Web.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN chown -R $APP_UID:$APP_UID /app
USER $APP_UID
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Web.Blazor.dll"]
