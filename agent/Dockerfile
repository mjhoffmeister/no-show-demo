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

# Create non-root user for security (using useradd for compatibility with Mariner/CBL)
RUN groupadd -r appgroup && useradd -r -g appgroup -s /usr/sbin/nologin appuser
USER appuser

# Copy published application
COPY --from=publish /app/publish .

# Expose port (Foundry hosted agents use 8088)
EXPOSE 8088

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8088
ENV ASPNETCORE_ENVIRONMENT=Production

# Entry point
ENTRYPOINT ["dotnet", "NoShowPredictor.Agent.dll"]
