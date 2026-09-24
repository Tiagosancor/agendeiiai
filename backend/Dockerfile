# syntax=docker/dockerfile:1

# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Plataforma.slnx ./
COPY src/Plataforma.Dominio/Plataforma.Dominio.csproj src/Plataforma.Dominio/
COPY src/Plataforma.Aplicacao/Plataforma.Aplicacao.csproj src/Plataforma.Aplicacao/
COPY src/Plataforma.Infraestrutura/Plataforma.Infraestrutura.csproj src/Plataforma.Infraestrutura/
COPY src/Plataforma.Api/Plataforma.Api.csproj src/Plataforma.Api/
COPY tests/Plataforma.Testes.Unidade/Plataforma.Testes.Unidade.csproj tests/Plataforma.Testes.Unidade/
COPY tests/Plataforma.Testes.Integracao/Plataforma.Testes.Integracao.csproj tests/Plataforma.Testes.Integracao/
RUN dotnet restore src/Plataforma.Api/Plataforma.Api.csproj

COPY . .
RUN dotnet publish src/Plataforma.Api/Plataforma.Api.csproj -c Release -o /app --no-restore

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Alpine não traz o banco de fusos horários IANA por padrão — sem isso,
# TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo") (seção 8.2.6) explode em
# runtime mesmo funcionando em dev (Windows/a maioria das distros Linux já tem tzdata).
# krb5-libs: o Npgsql tenta carregar o GSSAPI ao conectar e, sem ele, polui o log com
# "Cannot load library libgssapi_krb5.so.2" (inofensivo — não usamos Kerberos).
RUN apk add --no-cache tzdata krb5-libs

# Usuário sem privilégios (seção 8.5.1) — a imagem base já traz o usuário "app" (uid 64198) no Alpine.
USER app

COPY --from=build --chown=app:app /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Plataforma.Api.dll"]
