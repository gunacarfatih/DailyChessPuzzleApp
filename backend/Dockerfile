
# 1. Aşama: .NET 10 SDK ile projeyi derle
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Proje dosyalarını kopyala
COPY DailyChessPuzzleApi/DailyChessPuzzleApi.csproj DailyChessPuzzleApi/
COPY ReusableCaching/ReusableCaching.csproj ReusableCaching/

# NuGet paketlerini yükle
RUN dotnet restore DailyChessPuzzleApi/DailyChessPuzzleApi.csproj

# Kaynak kodlarını kopyala
COPY DailyChessPuzzleApi/ DailyChessPuzzleApi/
COPY ReusableCaching/ ReusableCaching/

# API'yi yayınla
RUN dotnet publish DailyChessPuzzleApi/DailyChessPuzzleApi.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# 2. Aşama: Yalnızca çalışma zamanı
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "DailyChessPuzzleApi.dll"]

