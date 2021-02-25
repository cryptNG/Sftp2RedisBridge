#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
WORKDIR /app
FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["File2EventBridge/Sftp2RedisBridge.csproj", "File2EventBridge/"]
RUN dotnet restore "File2EventBridge/Sftp2RedisBridge.csproj"
COPY . .
WORKDIR "/src/File2EventBridge"
RUN dotnet build "Sftp2RedisBridge.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Sftp2RedisBridge.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sftp2RedisBridge.dll"]
