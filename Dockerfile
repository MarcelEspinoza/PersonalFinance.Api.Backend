# Etapa de build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copiar solo csproj y restaurar paquetes para aprovechar caching
COPY *.csproj ./
RUN dotnet restore

# Copiar el resto del código y publicar
COPY . .
RUN dotnet publish PersonalFinance.Api.csproj -c Release -o out

# Etapa de runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Etapa de runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Copiar la app publicada desde build
COPY --from=build /app/out .

# Crear carpeta para keys si la vas a usar
RUN mkdir -p /app/keys

# 💡 AÑADIDO: Exponer el puerto de Cloud Run (8080 es el estándar)
EXPOSE 8080

# 💡 AÑADIDO: Comando de inicio (Asegúrate de que PersonalFinance.Api.dll es el nombre de tu DLL)
CMD ["dotnet", "PersonalFinance.Api.dll"]