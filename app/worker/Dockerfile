# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY shared/LetterTranslation.Shared.csproj shared/
COPY worker/LetterTranslation.Worker.csproj worker/
RUN dotnet restore worker/LetterTranslation.Worker.csproj

COPY shared/ shared/
COPY worker/ worker/
RUN dotnet publish worker/LetterTranslation.Worker.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:10.0-preview
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LetterTranslation.Worker.dll"]
