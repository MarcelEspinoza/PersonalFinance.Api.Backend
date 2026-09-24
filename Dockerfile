# ---------------------------------------------------------------------------------
# 1. Etapa de Compilación (BUILD Stage)
# ---------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copiar solo los csproj y restaurar paquetes para aprovechar caching.
# El dominio vive en su propio proyecto, así que también hay que copiarlo
# antes del restore o este falla al resolver la ProjectReference.
COPY PersonalFinance.Api.csproj ./
COPY src/PersonalFinance.Domain/PersonalFinance.Domain.csproj src/PersonalFinance.Domain/
RUN dotnet restore PersonalFinance.Api.csproj

# Copiar el resto del código y publicar
COPY . . 
RUN dotnet publish PersonalFinance.Api.csproj -c Release -o out

# ---------------------------------------------------------------------------------
# 2. Etapa de Ejecución (FINAL Stage)
# ---------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copiar la app publicada desde build
COPY --from=build /app/out ./

# Crear carpeta para keys si la vas a usar
RUN mkdir -p /app/keys

# Exponer otro puerto interno para backend (por ejemplo 5000)
EXPOSE 5000

# Forzar Kestrel a escuchar en 0.0.0.0:5000
CMD ["dotnet", "PersonalFinance.Api.dll", "--urls", "http://0.0.0.0:5000"]
