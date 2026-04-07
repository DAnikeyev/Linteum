# Use .NET runtime as base image (no ASP.NET needed for console app)
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Linteum.Bots/Linteum.Bots.csproj", "Linteum.Bots/"]
COPY ["Linteum.Shared/Linteum.Shared.csproj", "Linteum.Shared/"]
RUN dotnet restore "Linteum.Bots/Linteum.Bots.csproj"
COPY . .
WORKDIR "/src/Linteum.Bots"
RUN dotnet build "Linteum.Bots.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Linteum.Bots.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Linteum.Bots.dll"]

