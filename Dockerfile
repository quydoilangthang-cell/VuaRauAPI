FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Chú ý: Trỏ vào thư mục con VuaRauAPI
COPY ["VuaRauAPI/VuaRauAPI.csproj", "VuaRauAPI/"]
RUN dotnet restore "VuaRauAPI/VuaRauAPI.csproj"

COPY . .
WORKDIR "/src/VuaRauAPI"
RUN dotnet build "VuaRauAPI.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "VuaRauAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VuaRauAPI.dll"]