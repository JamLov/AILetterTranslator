# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY shared/LetterTranslation.Shared.csproj shared/
COPY backend/LetterTranslation.Api.csproj backend/
RUN dotnet restore backend/LetterTranslation.Api.csproj

COPY shared/ shared/
COPY backend/ backend/
RUN dotnet publish backend/LetterTranslation.Api.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "LetterTranslation.Api.dll"]
