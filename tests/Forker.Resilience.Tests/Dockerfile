# Dockerfile for ForkerDotNet multi-process race condition testing
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["src/Forker.Domain/Forker.Domain.csproj", "src/Forker.Domain/"]
COPY ["src/Forker.Infrastructure/Forker.Infrastructure.csproj", "src/Forker.Infrastructure/"]
COPY ["src/Forker.Service/Forker.Service.csproj", "src/Forker.Service/"]
COPY ["tests/Forker.Resilience.Tests/Forker.Resilience.Tests.csproj", "tests/Forker.Resilience.Tests/"]

RUN dotnet restore "src/Forker.Service/Forker.Service.csproj"
RUN dotnet restore "tests/Forker.Resilience.Tests/Forker.Resilience.Tests.csproj"

# Copy source code
COPY . .

# Build the applications
WORKDIR "/src/src/Forker.Service"
RUN dotnet build "Forker.Service.csproj" -c Release -o /app/build

WORKDIR "/src/tests/Forker.Resilience.Tests"
RUN dotnet build "Forker.Resilience.Tests.csproj" -c Release -o /app/test

FROM base AS final
WORKDIR /app

# Install required packages for multi-process testing
RUN apt-get update && apt-get install -y \
    procps \
    htop \
    stress \
    && rm -rf /var/lib/apt/lists/*

# Copy built applications
COPY --from=build /app/build .
COPY --from=build /app/test ./test

# Create shared directories for multi-process testing
RUN mkdir -p /shared/source /shared/targetA /shared/targetB /shared/temp

# Copy entrypoint script for multi-process testing
COPY tests/Forker.Resilience.Tests/docker-test-entrypoint.sh /app/docker-test-entrypoint.sh

RUN chmod +x /app/docker-test-entrypoint.sh

# Set environment variables
ENV DOTNET_ENVIRONMENT=Development
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["/app/docker-test-entrypoint.sh"]