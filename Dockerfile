FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Battleship.Shared/Battleship.Shared.csproj src/Battleship.Shared/
COPY src/Battleship.Server/Battleship.Server.csproj src/Battleship.Server/
COPY src/Battleship.Web/Battleship.Web.csproj src/Battleship.Web/

RUN dotnet restore src/Battleship.Server/Battleship.Server.csproj

COPY src/ src/

RUN dotnet publish src/Battleship.Web/Battleship.Web.csproj -c Release -o /out/web
RUN dotnet publish src/Battleship.Server/Battleship.Server.csproj -c Release -o /out/server /p:UseAppHost=false
RUN mkdir -p /out/server/wwwroot && cp -R /out/web/wwwroot/. /out/server/wwwroot/ \
    && test -f /out/server/wwwroot/index.html \
    && test -f /out/server/wwwroot/Battleship.Web.styles.css \
    && test -f /out/server/wwwroot/_framework/blazor.webassembly.js \
    && test -f /out/server/wwwroot/_framework/dotnet.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

COPY --from=build /out/server ./

USER $APP_UID

ENTRYPOINT ["dotnet", "Battleship.Server.dll"]
