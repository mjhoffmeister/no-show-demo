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

# Runtime stage - use runtime image (not aspnet) for console app
FROM mcr.microsoft.com/dotnet/runtime:10.0-preview AS runtime
WORKDIR /app

# Use built-in non-root user (app) available in .NET images
USER app

# Copy published application
COPY --from=publish /app/publish .

# Entry point
ENTRYPOINT ["dotnet", "NoShowPredictor.Agent.dll"]
