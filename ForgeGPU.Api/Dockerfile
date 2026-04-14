FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ForgeGPU.sln ./
COPY ForgeGPU.Api/ForgeGPU.Api.csproj ForgeGPU.Api/
COPY ForgeGPU.Core/ForgeGPU.Core.csproj ForgeGPU.Core/
COPY ForgeGPU.Infrastructure/ForgeGPU.Infrastructure.csproj ForgeGPU.Infrastructure/
RUN dotnet restore ForgeGPU.sln

COPY . .
RUN dotnet publish ForgeGPU.Api/ForgeGPU.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ForgeGPU.Api.dll"]
