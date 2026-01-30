# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["src/NoShowPredictor.Agent/NoShowPredictor.Agent.csproj", "src/NoShowPredictor.Agent/"]
RUN dotnet restore "src/NoShowPredictor.Agent/NoShowPredictor.Agent.csproj"

# Copy source files and build
COPY . .
WORKDIR "/src/src/NoShowPredictor.Agent"
RUN dotnet build "NoShowPredictor.Agent.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "NoShowPredictor.Agent.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Create non-root user for security
RUN adduser --disabled-password --gecos "" appuser
USER appuser

# Copy published application
COPY --from=publish /app/publish .

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "NoShowPredictor.Agent.dll"]
