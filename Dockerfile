# Multi-stage Dockerfile for NovaGM
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["NovaGM/NovaGM.csproj", "NovaGM/"]
COPY ["Directory.Packages.props", "./"]

# Restore dependencies
RUN dotnet restore "NovaGM/NovaGM.csproj"

# Copy source code
COPY . .

# Build application
WORKDIR "/src/NovaGM"
RUN dotnet build "NovaGM.csproj" -c Release -o /app/build

# Publish application
FROM build AS publish
RUN dotnet publish "NovaGM.csproj" -c Release -o /app/publish --self-contained false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install required packages for LLamaSharp
RUN apt-get update && apt-get install -y \
    libc6-dev \
    && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=publish /app/publish .

# Create directories
RUN mkdir -p /app/llm /app/config /app/logs

# Set permissions
RUN chmod +x NovaGM

# Expose port
EXPOSE 5055

# Environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5055
ENV NOVAGM_GPU_LAYERS=0

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5055/health || exit 1

ENTRYPOINT ["./NovaGM"]