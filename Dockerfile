# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY . .
RUN dotnet publish -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# publish output
COPY --from=build /app/out .

# 🔥 IMPORTANT: ensure static files भी आएं
COPY --from=build /app/wwwroot ./wwwroot

ENTRYPOINT ["dotnet", "HauntedVoiceUniverse.dll"]