FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

COPY . .
WORKDIR /build/src/Kafdoc.Web
RUN dotnet publish -c release -o /app

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "aspnetapp.dll"]