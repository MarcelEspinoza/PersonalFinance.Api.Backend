# ---------------------------------------------------------------------------------
# 1. Etapa de Compilación (BUILD Stage)
# ---------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copiar solo csproj y restaurar paquetes para aprovechar caching
COPY PersonalFinance.Api.csproj ./
RUN dotnet restore

# Copiar el resto del código y publicar
COPY . .
# Publicar la aplicación en la carpeta /app/out
RUN dotnet publish PersonalFinance.Api.csproj -c Release -o out

# ---------------------------------------------------------------------------------
# 2. Etapa de Ejecución (FINAL Stage)
# ---------------------------------------------------------------------------------
# Solo una declaración FROM para el runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# ** LÍNEA CLAVE PARA CORREGIR EL ERROR DE CLOUD RUN **
# Le dice a Kestrel que escuche en todas las interfaces (http://+) y use la variable $PORT (típicamente 8080).
ENV ASPNETCORE_URLS=http://+:$PORT

# Copiar la app publicada desde build
COPY --from=build /app/out .

# Crear carpeta para keys si la vas a usar
RUN mkdir -p /app/keys

# Exponer el puerto de Cloud Run (Aunque Kestrel usa $PORT, es buena práctica)
EXPOSE 8080

# Comando de inicio
CMD ["dotnet", "PersonalFinance.Api.dll"]