FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["ProviderStudio.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
RUN mkdir -p /data
COPY --from=build /app .
EXPOSE 5700
ENTRYPOINT ["dotnet", "ProviderStudio.dll"]
