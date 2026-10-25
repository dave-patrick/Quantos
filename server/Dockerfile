FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Quantos.Core/ src/Quantos.Core/
COPY src/Quantos.Server/ src/Quantos.Server/
RUN dotnet publish src/Quantos.Server -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/ .
ENV ASPNETCORE_URLS=http://+:8080 QUANTOS_SERVER_DATA=/data
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Quantos.Server.dll"]
