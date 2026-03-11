# Giai đoạn chạy (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
EXPOSE 443

# Giai đoạn Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy file project và restore (Anh kiểm tra lại tên file .csproj cho đúng nhé)
COPY ["VuaRauAPI.csproj", "./"]
RUN dotnet restore "VuaRauAPI.csproj"

# Copy toàn bộ code và build
COPY . .
RUN dotnet build "VuaRauAPI.csproj" -c Release -o /app/build

# Giai đoạn Publish
FROM build AS publish
RUN dotnet publish "VuaRauAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Giai đoạn cuối: Chạy ứng dụng
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VuaRauAPI.dll"]