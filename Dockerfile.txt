# B??c 1: S? d?ng môi tr??ng ch?y .NET 8 (nh? và b?o m?t)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
# Render s? s? d?ng c?ng 80 theo m?c ??nh cho HTTP
EXPOSE 80
EXPOSE 443

# B??c 2: S? d?ng SDK ?? biên d?ch (Build) mã ngu?n
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Sao chép file d? án vào và khôi ph?c các th? vi?n (NuGet)
COPY ["VuaRauAPI.csproj", "."]
RUN dotnet restore "./VuaRauAPI.csproj"

# Sao chép toàn b? mã ngu?n còn l?i và biên d?ch
COPY . .
RUN dotnet build "VuaRauAPI.csproj" -c Release -o /app/build

# B??c 3: Xu?t b?n (Publish) ?ng d?ng ra th? m?c t?m
FROM build AS publish
RUN dotnet publish "VuaRauAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# B??c 4: ?óng gói s?n ph?m cu?i cùng ?? ch?y
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# L?nh ?? kh?i ch?y API khi Render b?t server lên
ENTRYPOINT ["dotnet", "VuaRauAPI.dll"]