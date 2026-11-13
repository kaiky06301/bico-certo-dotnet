# Bico Certo - Contratacoes API (.NET 8)
# Build a partir da raiz do repositorio:
#   docker build -f dockerfiles/Dockerfile -t bicocerto-contratacoes .

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/BicoCerto.Contratacoes.Api/BicoCerto.Contratacoes.Api.csproj src/BicoCerto.Contratacoes.Api/
RUN dotnet restore src/BicoCerto.Contratacoes.Api/BicoCerto.Contratacoes.Api.csproj
COPY src/ src/
RUN dotnet publish src/BicoCerto.Contratacoes.Api/BicoCerto.Contratacoes.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
# usuario sem privilegios de root
RUN adduser --disabled-password --gecos "" bicocerto && mkdir -p /app/data && chown -R bicocerto /app
USER bicocerto
COPY --from=build --chown=bicocerto /app/publish .
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/app/data/contratacoes.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "BicoCerto.Contratacoes.Api.dll"]
