# RRHHNovedades.Web — imagen para Azure Container Apps (estándar Espert: compute = Container Apps).
# Multi-stage: empaqueta el runtime .NET 10 en la imagen, así no dependemos de que el host
# tenga la versión exacta (clave porque .NET 10 todavía es nuevo).

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore primero (capa cacheable) — copiamos solo los .csproj.
COPY src/RRHHNovedades.Web/RRHHNovedades.Web.csproj src/RRHHNovedades.Web/
RUN dotnet restore src/RRHHNovedades.Web/RRHHNovedades.Web.csproj

# Resto del código y publish.
COPY . .
RUN dotnet publish src/RRHHNovedades.Web/RRHHNovedades.Web.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Npgsql intenta negociar GSSAPI/Kerberos al conectar; sin esta lib loguea un error (igual cae a
# password). La agregamos para que el log quede limpio y la conexión no pierda tiempo negociando.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Container Apps rutea al puerto del ingress; escuchamos HTTP en 8080 (TLS lo termina el ingress).
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1
EXPOSE 8080

# Usuario no-root (buena práctica; la imagen aspnet trae el usuario "app").
USER app

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RRHHNovedades.Web.dll"]
