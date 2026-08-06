# Dockerfile for deploying Zalo.Net.Mcp Web Server to Cloud / VPS (24/7 ChatGPT Custom GPT Actions)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy full solution source
COPY . .

# Build and publish release binaries
RUN dotnet restore mcp/Zalo.Net.Mcp/Zalo.Net.Mcp.csproj
RUN dotnet publish mcp/Zalo.Net.Mcp/Zalo.Net.Mcp.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Expose HTTP port for ChatGPT OpenAPI Actions
ENV PORT=5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "Zalo.Net.Mcp.dll", "--web", "--port", "5000"]
