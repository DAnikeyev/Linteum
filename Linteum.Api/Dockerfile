# Use ASP\.NET runtime as base image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
RUN apt-get update && apt-get install -y libgssapi-krb5-2 libkrb5-3 libk5crypto3 libcom-err2 && rm -rf /var/lib/apt/lists/*
# Expose the API port from .env (default 8080)
ARG API_CONTAINER_PORT=8080
EXPOSE ${API_CONTAINER_PORT}

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Linteum.Api/Linteum.Api.csproj", "Linteum.Api/"]
RUN dotnet restore "Linteum.Api/Linteum.Api.csproj"
COPY . .
WORKDIR "/src/Linteum.Api"
RUN dotnet build "Linteum.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Linteum.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Linteum.Api.dll"]