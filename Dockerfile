# Giai đoạn chạy (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
EXPOSE 443

# Giai đoạn Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# SỬA Ở ĐÂY: Trỏ đúng vào thư mục VuaRauAPI của anh
COPY ["VuaRauAPI/VuaRauAPI.csproj", "VuaRauAPI/"]
RUN dotnet restore "VuaRauAPI/VuaRauAPI.csproj"

# Copy toàn bộ code
COPY . .
WORKDIR "/src/VuaRauAPI"
RUN dotnet build "VuaRauAPI.csproj" -c Release -o /app/build

# Giai đoạn Publish
FROM build AS publish
RUN dotnet publish "VuaRauAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Giai đoạn cuối
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VuaRauAPI.dll"]