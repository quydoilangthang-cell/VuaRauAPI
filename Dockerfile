# Giai đoạn chạy (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
# Ép App chạy cổng 80 để khớp với Render
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
EXPOSE 443

# Giai đoạn Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# SỬA TẠI ĐÂY: Copy mọi file .csproj tìm thấy vào thư mục hiện tại
COPY *.csproj ./
RUN dotnet restore

# Copy toàn bộ code còn lại
COPY . .

# Build dự án (dùng dấu . để tự hiểu file dự án trong thư mục hiện tại)
RUN dotnet build -c Release -o /app/build

# Giai đoạn Publish
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Giai đoạn cuối: Chạy ứng dụng
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Anh lưu ý: Phải đảm bảo tên file DLL đúng là VuaRauAPI.dll
ENTRYPOINT ["dotnet", "VuaRauAPI.dll"]