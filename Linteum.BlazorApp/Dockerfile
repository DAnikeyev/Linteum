# Use ASP.NET runtime as base image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
# Expose the Blazor app port from .env (default 8090)
ARG BLAZOR_CONTAINER_PORT=8090
EXPOSE ${BLAZOR_CONTAINER_PORT}

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Linteum.BlazorApp/Linteum.BlazorApp.csproj", "Linteum.BlazorApp/"]
RUN dotnet restore "Linteum.BlazorApp/Linteum.BlazorApp.csproj"
COPY . .
WORKDIR "/src/Linteum.BlazorApp"
RUN dotnet build "Linteum.BlazorApp.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Linteum.BlazorApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Linteum.BlazorApp.dll"]